"""Shared write-timing client for the installed tia-mcp global tool.

Python 3.11+, stdlib only, Windows. Replaces the per-PR copies of the HMI timing tooling.

  run <flags>            restore -> warm read -> workload, per case and run; run 0 is discarded
  validate <archive>     offline check of a finished archive (counts derive from cases x runs)
  self-test              run the offline unit tests (no server is started)

Run it as `python -I scripts/timing/measure_write_timing.py ...`; see
docs/development/measuring-write-timing.md. Covered by offline tests only.
"""

import argparse
import datetime
import gzip
import hashlib
import json
import os
import pathlib
import queue
import re
import statistics
import subprocess
import sys
import threading
import time

METRICS = ('rootMs', 'itemSumMs', 'remainderMs')
DEFAULT_SERVER_ARGS = ['--access-mode', 'full', '--max-item-chars', '1000000',
                       '--max-document-chars', '1000000', '--worker-timeout-minutes', '15']
DEFAULT_TOOL_PATH = '~/.dotnet/tools/tia-mcp.exe'


def parse_args(argv):
    parser = argparse.ArgumentParser(prog='measure_write_timing')
    sub = parser.add_subparsers(dest='command', required=True)
    run = sub.add_parser('run', help='measure a write workload against a disposable project')
    run.add_argument('--tool', default='hmi_write')
    run.add_argument('--contract-version', required=True)
    run.add_argument('--expected-version', required=True, help='installed tiamcpserver version')
    run.add_argument('--server-version', help='asserted serverInfo.version (default: derived from --expected-version)')
    run.add_argument('--tool-path', default=DEFAULT_TOOL_PATH)
    run.add_argument('--server-arg', action='append', help='repeatable; replaces the default server arguments')
    run.add_argument('--request-timeout', type=int, default=900)
    run.add_argument('--project', default=os.environ.get('HMI_TIMING_PROJECT'))
    run.add_argument('--project-sha256', default=os.environ.get('HMI_TIMING_PROJECT_SHA256'))
    run.add_argument('--portal-pid', type=int, help='Portal process id for the startup bind_project')
    run.add_argument('--workloads', help='JSON file: {case: tool arguments}')
    run.add_argument('--cases', help='comma-separated subset of workload cases (default: all)')
    run.add_argument('--runs', type=int, default=4, help='runs per case; run 0 is discarded')
    run.add_argument('--warm-read', help='JSON file {"name":..., "args":{...}} read before each workload')
    run.add_argument('--expect-inventory', help='JSON file the warm-read document must equal (default: first read)')
    run.add_argument('--stdin', action='store_true', help='read JSON commands from stdin instead of scheduling')
    run.add_argument('--out', required=True)
    validate = sub.add_parser('validate', help='offline check of a finished archive directory')
    validate.add_argument('archive')
    sub.add_parser('self-test', help='run the offline unit tests')
    args = parser.parse_args(argv)
    if args.command == 'run' and not args.server_arg:
        args.server_arg = list(DEFAULT_SERVER_ARGS)
    return args


# --- transport -------------------------------------------------------------------------------

def read_responses(stream, responses):
    try:
        for line in stream:
            responses.put(json.loads(line))
    except Exception as error:
        responses.put(error)
    finally:
        responses.put(None)


def await_response(responses, request_id, timeout):
    deadline = time.monotonic() + timeout
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError('JSON-RPC request exceeded its deadline')
        try:
            response = responses.get(timeout=remaining)
        except queue.Empty as error:
            raise TimeoutError('JSON-RPC request exceeded its deadline') from error
        if response is None:
            raise RuntimeError('Server stdout closed before response')
        if isinstance(response, Exception):
            raise response
        if response.get('id') == request_id:
            if 'error' in response:
                raise RuntimeError(response['error'])
            return response['result']


# --- checks ----------------------------------------------------------------------------------

def sha256_file(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def check_project(project, expected_hash):
    path = pathlib.Path(project)
    expected_hash = (expected_hash or '').lower()
    if not path.is_absolute() or path.suffix.lower() != '.ap21':
        raise ValueError('--project must be an absolute .ap21 path')
    if len(expected_hash) != 64 or any(char not in '0123456789abcdef' for char in expected_hash):
        raise ValueError('--project-sha256 must be 64 hex characters')
    if sha256_file(path) != expected_hash:
        raise ValueError('Project hash mismatch; refusing to start')
    return path


def derive_server_version(expected_version):
    match = re.match(r'\d+(?:\.\d+){0,3}', expected_version)
    if not match:
        raise ValueError('Cannot derive serverInfo.version; pass --server-version')
    parts = match.group(0).split('.')
    return '.'.join(parts + ['0'] * (4 - len(parts)))


def check_server_info(info, server_version):
    assert info == {'name': 'TiaMcpServer', 'version': server_version}, info


def audit_metrics(result, record, response_mtime, tool, contract_version):
    text = result['content'][0]['text']
    document = result['structuredContent']
    assert json.loads(text) == document, 'Canonical text differs'
    assert document['contractVersion'] == contract_version
    assert record['recordVersion'] == 2 and record['tool'] == tool
    assert record['accessMode'] == 'full'
    assert record['responseText'] == text
    expected_hash = 'sha256:' + hashlib.sha256(text.encode('utf-8')).hexdigest()
    assert record['responseHash'].lower() == expected_hash
    completion = datetime.datetime.fromisoformat(record['timestamp']).timestamp() + record['durationMs'] / 1000
    assert abs(response_mtime - completion) <= 5, 'Stale audit record'
    durations = [item['durationMs'] for item in record['items']]
    assert all(value is None or (type(value) in (int, float) and value >= 0) for value in durations)
    assert type(record['durationMs']) in (int, float) and record['durationMs'] >= 0
    item_sum = sum(value or 0 for value in durations)
    assert item_sum <= record['durationMs']
    return {'rootMs': record['durationMs'], 'itemSumMs': item_sum, 'remainderMs': record['durationMs'] - item_sum}


def validate_write(document, record, args):
    assert document['success'] and document['error'] is None
    count = len(args['operations'])
    identities = [(item['operationId'], item['operation']) for item in args['operations']]
    assert [(item['operationId'], item['operation']) for item in record['items']] == identities
    assert len(document['effects']) == count
    if args.get('dryRun', False):
        assert document['phase'] == record['phase'] == 'preview'
        assert document['batch'] is None and document['verification'] is None
        assert all(item['durationMs'] is None and item['status'] == 'skipped' for item in record['items'])
    else:
        assert document['phase'] == record['phase'] == 'applied'
        batch = document['batch']
        assert batch['operationCount'] == count
        assert batch['counts'] == {'failed': 0, 'omitted': 0, 'skipped': 0, 'succeeded': count}
        assert [(item['operationId'], item['operation']) for item in batch['operations']] == identities
        assert all(item['status'] == 'succeeded' for item in batch['operations'])
        assert all(item['status'] == 'succeeded' and item['durationMs'] is not None for item in record['items'])
        assert document['verification']['success']


# --- schedule and summary --------------------------------------------------------------------

def build_schedule(cases, runs):
    return [{'label': f'{case}-run{run}', 'case': case, 'run': run, 'discarded': run == 0}
            for run in range(runs) for case in cases]


def expected_counts(cases, runs, warm_read=True):
    writes = len(cases) * runs
    return {'writes': writes, 'reads': writes + 2 if warm_read else 0, 'projections': 3 * writes,
            'measured': len(cases) * (runs - 1), 'discarded': len(cases)}


def summarize(rows):
    """Median of each metric column independently, over non-discarded rows, per case."""
    cases = list(dict.fromkeys(row['case'] for row in rows))
    return {case: {key: statistics.median(row[key] for row in rows if row['case'] == case and not row['discarded'])
                   for key in METRICS} for case in cases}


# --- validate --------------------------------------------------------------------------------

def load_json(root, name):
    return json.loads((root / name).read_text(encoding='utf-8'))


def load_lines(root, name):
    for candidate, opener in ((name, open), (name + '.gz', gzip.open)):
        path = root / candidate
        if path.exists():
            with opener(path, 'rt', encoding='utf-8') as stream:
                return [json.loads(line) for line in stream if line.strip()]
    raise FileNotFoundError(name)


def require_count(what, actual, expected):
    if actual != expected:
        raise ValueError(f'{what}: expected {expected} from the schedule, found {actual}')


def validate_archive(archive):
    root = pathlib.Path(archive)
    summary = load_json(root, 'summary.json')
    tool, contract = summary['tool'], summary['contractVersion']
    cases, runs = summary['cases'], summary['runs']
    warm_read = summary.get('warmRead', True)
    counts = expected_counts(cases, runs, warm_read)
    schedule = build_schedule(cases, runs)
    workloads = load_json(root, 'workloads.json') if (root / 'workloads.json').exists() else None
    writes = load_lines(root, 'timing-writes.jsonl')
    require_count('writes', len(writes), counts['writes'])
    if [row['label'] for row in writes] != [row['label'] for row in schedule]:
        raise ValueError('write labels do not follow the schedule')
    rows = []
    for slot, capture in zip(schedule, writes):
        if workloads is not None:
            assert capture['request'] == {'name': tool, 'arguments': workloads[slot['case']]}, slot['label']
        assert capture['response']['isError'] is False
        metrics = audit_metrics(capture['response'], capture['audit'], capture['sourceResponseMtime'], tool, contract)
        validate_write(capture['response']['structuredContent'], capture['audit'],
                       capture['request']['arguments'])
        rows.append({'label': slot['label'], 'case': slot['case'], 'run': slot['run'],
                     'discarded': slot['discarded'], **metrics})
    assert rows == summary['rows'], 'Summary rows differ from the captures'
    assert summarize(rows) == summary['mediansMs'], 'Medians differ'
    require_count('measured samples', sum(not row['discarded'] for row in rows), counts['measured'])
    require_count('discarded samples', sum(row['discarded'] for row in rows), counts['discarded'])
    reads = load_lines(root, 'hmi-reads.jsonl') if warm_read else []
    require_count('reads', len(reads), counts['reads'])
    for capture in reads:
        response = capture['response']
        document = response['structuredContent']
        assert json.loads(response['content'][0]['text']) == document and document['success'] is True
        assert document == reads[0]['response']['structuredContent'], 'Inventory changed between reads'
    projections = load_json(root, 'lifecycle-status-projections.json')['captures']
    require_count('lifecycle projections', len(projections), counts['projections'])
    assert all(item['success'] is True for item in projections)
    return (f"PASS: {counts['writes']} write/audit pairs, {counts['reads']} reads, "
            f"{counts['projections']} projections, {counts['measured']} measured samples")


# --- run -------------------------------------------------------------------------------------

class Session:
    def __init__(self, args, project, workloads, out):
        self.args, self.project, self.workloads, self.out = args, project, workloads, out
        self.expected_hash = args.project_sha256.lower()
        self.writes, self.reads, self.projections, self.rows = [], [], [], []
        self.counter, self.bound, self.pending = 0, False, False
        self.warm = json.loads(pathlib.Path(args.warm_read).read_text(encoding='utf-8')) if args.warm_read else None
        self.inventory = (json.loads(pathlib.Path(args.expect_inventory).read_text(encoding='utf-8'))
                          if args.expect_inventory else None)
        self.stderr = (out / 'server-stderr.log').open('a', encoding='utf-8')
        command = [str(pathlib.Path(args.tool_path).expanduser()), *args.server_arg]
        self.server = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.stderr,
                                       text=True, encoding='utf-8')
        self.responses = queue.Queue()
        threading.Thread(target=read_responses, args=(self.server.stdout, self.responses), daemon=True).start()

    def rpc(self, method, params, notification=False):
        assert not self.pending, 'A request is already pending; do not replay'
        self.counter += 1
        request = {'jsonrpc': '2.0', 'method': method, 'params': params}
        if not notification:
            request['id'] = self.counter
        self.server.stdin.write(json.dumps(request) + '\n')
        self.server.stdin.flush()
        if notification:
            return None
        self.pending = True
        try:
            result = await_response(self.responses, self.counter, self.args.request_timeout)
        except TimeoutError:
            evidence = {'request': request, 'serverPid': self.server.pid, 'timeoutSeconds': self.args.request_timeout,
                        'outcome': 'unknown', 'automaticRestore': False, 'automaticReplay': False}
            (self.out / 'pending-call.json').write_text(json.dumps(evidence), encoding='utf-8')
            print(json.dumps({'timeout': True, 'serverPid': self.server.pid, 'requestId': self.counter,
                              'outcome': 'unknown; scheduling stopped; inspect before any retry'}), flush=True)
            raise
        except Exception:
            self.pending = False
            raise
        self.pending = False
        return result

    def find_audit(self, text):
        audit_dir = pathlib.Path(os.environ['LOCALAPPDATA']) / 'TiaMcpServer/audit'
        for audit in sorted(audit_dir.glob('writes-*.jsonl'), reverse=True)[:2]:
            for line in reversed(audit.read_text(encoding='utf-8-sig').splitlines()):
                candidate = json.loads(line)
                if candidate.get('tool') == self.args.tool and candidate.get('responseText') == text:
                    return candidate
        raise AssertionError('Matching audit record missing')

    def call(self, name, arguments, label):
        assert label and pathlib.Path(label).name == label and label not in ('.', '..')
        path, request_path = self.out / (label + '.json'), self.out / (label + '-request.json')
        if path.exists() or request_path.exists():
            raise RuntimeError('Capture already exists: ' + label)
        request_path.write_text(json.dumps({'name': name, 'arguments': arguments}, ensure_ascii=False), encoding='utf-8')
        started = time.monotonic()
        result = self.rpc('tools/call', {'name': name, 'arguments': arguments})
        path.write_text(json.dumps(result, ensure_ascii=False), encoding='utf-8')
        text = result['content'][0]['text']
        document = result.get('structuredContent') or json.loads(text)
        assert json.loads(text) == document, 'Canonical text differs'
        if name == 'bind_project' and document.get('success'):
            self.bound = True
        summary = {'label': label, 'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                   'seconds': round(time.monotonic() - started, 3), 'success': document.get('success'),
                   'error': document.get('error'), 'phase': document.get('phase'),
                   'counts': (document.get('batch') or {}).get('counts'), 'isError': result.get('isError')}
        record = None
        if name == self.args.tool:
            record = self.find_audit(text)
            self.append('audits.jsonl', {'label': label, 'record': record})
            summary.update(audit_metrics(result, record, path.stat().st_mtime, name, self.args.contract_version))
        self.append('measurements.jsonl', summary)
        print(json.dumps(summary, ensure_ascii=False), flush=True)
        assert document.get('success') is True and not result.get('isError', False), 'Tool failure captured; scheduling stopped'
        if name == self.args.tool:
            validate_write(document, record, arguments)
            self.writes.append({'label': label, 'request': {'name': name, 'arguments': arguments}, 'response': result,
                                'audit': record, 'sourceResponseMtime': path.stat().st_mtime,
                                'measurement': {key: summary[key] for key in METRICS}})
        return result, document

    def append(self, name, value):
        with (self.out / name).open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(value, ensure_ascii=False) + '\n')

    def restore(self, label):
        assert self.bound, 'Not bound; pass --portal-pid or send a bind_project command first'
        steps = (('close_project', {'saveBeforeClose': False}, '-close'),
                 ('open_project', {'projectPath': str(self.project)}, '-open'),
                 ('get_project_status', {}, '-status'))
        for name, arguments, suffix in steps:
            _, document = self.call(name, arguments, label + suffix)
            entry = {'label': label + suffix, 'tool': name, 'success': document.get('success'),
                     'requestArguments': arguments}
            if name == 'get_project_status':
                value = document['result']['value']
                assert value['isOpen'] is True and value['isModified'] is False
                assert pathlib.Path(value['path']).resolve() == self.project
                entry.update(isOpen=value['isOpen'], isModified=value['isModified'])
            self.projections.append(entry)
            assert sha256_file(self.project) == self.expected_hash, 'Project file changed'

    def warm_read(self, label):
        if self.warm is None:
            return
        result, document = self.call(self.warm['name'], self.warm.get('args', {}), label)
        if self.inventory is None:
            self.inventory = document
        assert document == self.inventory, 'Inventory differs from the expected inventory'
        self.reads.append({'label': label, 'response': result})

    def handshake(self):
        hello = self.rpc('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {},
                                       'clientInfo': {'name': 'write-timing', 'version': '1'}})
        self.rpc('notifications/initialized', {}, True)
        (self.out / 'initialize.json').write_text(json.dumps(hello, ensure_ascii=False), encoding='utf-8')
        check_server_info(hello['serverInfo'], self.args.server_version)
        provenance = {'installedVersion': self.args.expected_version, 'serverInfo': hello['serverInfo'],
                      'projectPath': str(self.project), 'projectSha256': self.expected_hash,
                      'serverArgs': self.args.server_arg, 'requestTimeoutSeconds': self.args.request_timeout,
                      'serverPid': self.server.pid}
        (self.out / 'provenance.json').write_text(json.dumps(provenance, ensure_ascii=False), encoding='utf-8')
        print(json.dumps({'ready': hello['serverInfo'], 'serverPid': self.server.pid}), flush=True)

    def schedule(self, cases):
        if self.args.portal_pid:
            self.call('bind_project', {'portalProcessId': self.args.portal_pid}, 'startup-bind')
        self.warm_read('startup-discarded-read')
        for slot in build_schedule(cases, self.args.runs):
            self.restore(slot['label'])
            _, _ = self.call(self.args.tool, self.workloads[slot['case']], slot['label'])
            metrics = self.writes[-1]['measurement']
            self.rows.append({'label': slot['label'], 'case': slot['case'], 'run': slot['run'],
                              'discarded': slot['discarded'], **metrics})
            self.warm_read(slot['label'] + '-warm')
        self.warm_read('timing-final-inventory')
        self.save(cases)

    def save(self, cases):
        def lines(name, items):
            (self.out / name).write_text(''.join(json.dumps(item, ensure_ascii=False) + '\n' for item in items), encoding='utf-8')
        lines('timing-writes.jsonl', self.writes)
        lines('hmi-reads.jsonl', self.reads)
        (self.out / 'lifecycle-status-projections.json').write_text(json.dumps({'captures': self.projections}), encoding='utf-8')
        (self.out / 'workloads.json').write_text(json.dumps({case: self.workloads[case] for case in cases}), encoding='utf-8')
        summary = {'tool': self.args.tool, 'contractVersion': self.args.contract_version, 'cases': cases,
                   'runs': self.args.runs, 'warmRead': self.warm is not None, 'rows': self.rows,
                   'mediansMs': summarize(self.rows)}
        (self.out / 'summary.json').write_text(json.dumps(summary), encoding='utf-8')
        print(json.dumps(summary['mediansMs'], sort_keys=True), flush=True)

    def stdin_loop(self):
        for line in sys.stdin:
            command = json.loads(line)
            if command.get('stop'):
                break
            if command.get('restore'):
                self.restore(command['label'])
            else:
                self.call(command['name'], command.get('args', {}), command['label'])

    def close(self):
        # An uncertain native call must be inspected before cleanup or another request.
        if not self.pending:
            self.server.stdin.close()
            try:
                self.server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.server.terminate()
        self.stderr.close()


def run(args):
    project = check_project(args.project, args.project_sha256).resolve()
    if args.server_version is None:
        args.server_version = derive_server_version(args.expected_version)
    if not args.workloads and not args.stdin:
        raise ValueError('--workloads is required unless --stdin is used')
    workloads = json.loads(pathlib.Path(args.workloads).read_text(encoding='utf-8')) if args.workloads else {}
    cases = args.cases.split(',') if args.cases else list(workloads)
    if not args.stdin:
        if args.runs < 2:
            raise ValueError('--runs must be at least 2 (run 0 is discarded)')
        if not args.portal_pid:
            raise ValueError('--portal-pid is required for the scheduler (startup bind_project)')
        missing = [case for case in cases if case not in workloads]
        if missing or not cases:
            raise ValueError('Unknown or empty cases: ' + ','.join(missing))
    listing = subprocess.run(['dotnet', 'tool', 'list', '--global'], check=True, capture_output=True, text=True).stdout
    if not any(line.split()[:2] == ['tiamcpserver', args.expected_version] for line in listing.splitlines()):
        raise ValueError('Unexpected installed version')
    out = pathlib.Path(args.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    session = Session(args, project, workloads, out)
    try:
        session.handshake()
        session.stdin_loop() if args.stdin else session.schedule(cases)
    finally:
        session.close()
    return 0


def main(argv=None):
    args = parse_args(sys.argv[1:] if argv is None else argv)
    if args.command == 'validate':
        print(validate_archive(args.archive))
        return 0
    if args.command == 'self-test':
        test = pathlib.Path(__file__).with_name('test_measure_write_timing.py')
        return subprocess.run([sys.executable, '-I', str(test)]).returncode
    return run(args)


if __name__ == '__main__':
    sys.exit(main())
