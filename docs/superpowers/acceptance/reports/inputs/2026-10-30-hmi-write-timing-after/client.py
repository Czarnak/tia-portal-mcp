"""Installed HMI timing client. Commands arrive as one JSON object per stdin line."""

import datetime
import hashlib
import json
import os
import pathlib
import queue
import subprocess
import sys
import threading
import time

ROOT = pathlib.Path(__file__).resolve().parent
EXPECTED_VERSION = '4.2.0-local.113.g31d1a44'
REQUEST_TIMEOUT = 900


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


def audit_metrics(result, record, response_mtime):
    text = result['content'][0]['text']
    document = result['structuredContent']
    assert json.loads(text) == document, 'Canonical text differs'
    assert document['contractVersion'] == '2.0'
    assert record['recordVersion'] == 2 and record['tool'] == 'hmi_write'
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
    return {'rootMs': record['durationMs'], 'itemSumMs': item_sum,
            'remainderMs': record['durationMs'] - item_sum}


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


def main():
    project = pathlib.Path(os.environ['HMI_TIMING_PROJECT']).resolve()
    expected_hash = os.environ['HMI_TIMING_PROJECT_SHA256'].lower()
    assert project.is_absolute() and project.suffix.lower() == '.ap21'
    assert len(expected_hash) == 64 and all(char in '0123456789abcdef' for char in expected_hash)
    assert hashlib.sha256(project.read_bytes()).hexdigest() == expected_hash
    tool_listing = subprocess.run(['dotnet', 'tool', 'list', '--global'], check=True, capture_output=True, text=True).stdout
    assert any(line.split()[:2] == ['tiamcpserver', EXPECTED_VERSION] for line in tool_listing.splitlines()), 'Unexpected installed version'
    stderr = (ROOT / 'server-stderr.log').open('a', encoding='utf-8')
    command = [str(pathlib.Path.home() / '.dotnet/tools/tia-mcp.exe'), '--access-mode', 'full',
               '--max-item-chars', '1000000', '--max-document-chars', '1000000', '--worker-timeout-minutes', '15']
    server = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr,
                              text=True, encoding='utf-8')
    responses = queue.Queue()
    threading.Thread(target=read_responses, args=(server.stdout, responses), daemon=True).start()
    counter = 0
    bound = False
    pending = False

    def rpc(method, params, notification=False):
        nonlocal counter, pending
        assert not pending, 'A request is already pending; do not replay'
        counter += 1
        request = {'jsonrpc': '2.0', 'method': method, 'params': params}
        if not notification:
            request['id'] = counter
        server.stdin.write(json.dumps(request) + '\n')
        server.stdin.flush()
        if notification:
            return None
        pending = True
        try:
            result = await_response(responses, counter, REQUEST_TIMEOUT)
        except TimeoutError:
            evidence = {'request': request, 'serverPid': server.pid, 'timeoutSeconds': REQUEST_TIMEOUT,
                        'outcome': 'unknown', 'automaticRestore': False, 'automaticReplay': False}
            (ROOT / 'pending-call.json').write_text(json.dumps(evidence), encoding='utf-8')
            print(json.dumps({'timeout': True, 'serverPid': server.pid, 'requestId': counter,
                              'outcome': 'unknown; scheduling stopped; inspect before any retry'}), flush=True)
            raise
        except Exception:
            pending = False
            raise
        pending = False
        return result

    def call(name, args, label):
        nonlocal bound
        assert label and pathlib.Path(label).name == label and label not in ('.', '..')
        path = ROOT / (label + '.json')
        request_path = ROOT / (label + '-request.json')
        if path.exists() or request_path.exists():
            raise RuntimeError('Capture already exists: ' + label)
        request_path.write_text(json.dumps({'name': name, 'arguments': args}, ensure_ascii=False), encoding='utf-8')
        started = time.monotonic()
        utc = datetime.datetime.now(datetime.timezone.utc).isoformat()
        result = rpc('tools/call', {'name': name, 'arguments': args})
        path.write_text(json.dumps(result, ensure_ascii=False), encoding='utf-8')
        document = result.get('structuredContent') or json.loads(result['content'][0]['text'])
        assert json.loads(result['content'][0]['text']) == document, 'Canonical text differs'
        if name == 'bind_project' and document.get('success'):
            bound = True
        summary = {'label': label, 'utc': utc, 'seconds': round(time.monotonic() - started, 3),
                   'success': document.get('success'), 'error': document.get('error'), 'phase': document.get('phase'),
                   'counts': (document.get('batch') or {}).get('counts'), 'isError': result.get('isError')}
        record = None
        if name == 'hmi_write':
            audit_dir = pathlib.Path(os.environ['LOCALAPPDATA']) / 'TiaMcpServer/audit'
            for audit in sorted(audit_dir.glob('writes-*.jsonl'), reverse=True)[:2]:
                for line in reversed(audit.read_text(encoding='utf-8-sig').splitlines()):
                    candidate = json.loads(line)
                    if candidate.get('tool') == name and candidate.get('responseText') == result['content'][0]['text']:
                        record = candidate
                        break
                if record is not None:
                    break
            assert record is not None, 'Matching audit record missing'
            with (ROOT / 'audits.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps({'label': label, 'record': record}, ensure_ascii=False) + '\n')
            summary.update(audit_metrics(result, record, path.stat().st_mtime))
            summary['items'] = [{'id': item.get('operationId'), 'status': item.get('status'),
                                 'failure': item.get('failure')} for item in (document.get('batch') or {}).get('operations', [])]
        with (ROOT / 'measurements.jsonl').open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(summary, ensure_ascii=False) + '\n')
        print(json.dumps(summary, ensure_ascii=False), flush=True)
        assert document.get('success') is True and not result.get('isError', False), 'Tool failure captured; scheduling stopped'
        if name == 'hmi_write':
            validate_write(document, record, args)
        return document

    def restore(label):
        assert bound
        call('close_project', {'saveBeforeClose': False}, label + '-close')
        call('open_project', {'projectPath': str(project)}, label + '-open')
        assert hashlib.sha256(project.read_bytes()).hexdigest() == expected_hash
        status = call('get_project_status', {}, label + '-status')
        value = status['result']['value']
        assert value['isOpen'] is True and value['isModified'] is False
        assert pathlib.Path(value['path']).resolve() == project

    try:
        hello = rpc('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {},
                                  'clientInfo': {'name': 'hmi-timing-baseline', 'version': '1'}})
        rpc('notifications/initialized', {}, True)
        (ROOT / 'initialize.json').write_text(json.dumps(hello, ensure_ascii=False), encoding='utf-8')
        assert hello['serverInfo'] == {'name': 'TiaMcpServer', 'version': '4.2.0.0'}, hello.get('serverInfo')
        provenance = {'installedVersion': EXPECTED_VERSION, 'serverInfo': hello['serverInfo'],
                      'projectPath': str(project), 'projectSha256': expected_hash, 'mode': 'full',
                      'maxItemChars': 1000000, 'maxDocumentChars': 1000000,
                      'workerTimeoutMinutes': 15, 'requestTimeoutSeconds': REQUEST_TIMEOUT, 'serverPid': server.pid}
        (ROOT / 'provenance.json').write_text(json.dumps(provenance, ensure_ascii=False), encoding='utf-8')
        print(json.dumps({'ready': hello['serverInfo'], 'installedVersion': EXPECTED_VERSION,
                          'projectSha256': expected_hash, 'serverPid': server.pid}), flush=True)
        for line in sys.stdin:
            command = json.loads(line)
            if command.get('stop'):
                break
            if command.get('restore'):
                restore(command['label'])
            else:
                call(command['name'], command.get('args', {}), command['label'])
    finally:
        # An uncertain native call must be inspected before cleanup or another request.
        if not pending:
            server.stdin.close()
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.terminate()
        stderr.close()


if __name__ == '__main__':
    main()
