"""Validate raw Stage 0 captures before summarizing or redacting an evidence archive."""

import argparse
import collections
import datetime
import gzip
import hashlib
import json
import pathlib
import re
import statistics

CASE = re.compile(r'^(T[1-7]|B[1-8]|P[1-9])(?:[-_]|$)')
TIMING = re.compile(r'^(T[1-7])-r([123])(?:-write)?$')
METRICS = ('rootMs', 'itemSumMs', 'remainderMs')


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def read_lines(path):
    return [json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines() if line.strip()] if path.exists() else []


def verify_pair(response, request, audit, response_mtime):
    text = response['content'][0]['text']
    document = response['structuredContent']
    assert json.loads(text) == document, 'Canonical text differs'
    assert audit['recordVersion'] == 2 and audit['accessMode'] == 'full'
    assert audit['tool'] == request['name'] and audit['responseText'] == text
    assert audit['responseHash'].lower() == 'sha256:' + hashlib.sha256(text.encode('utf-8')).hexdigest(), 'Audit hash differs'
    root_ms = audit['durationMs']
    assert type(root_ms) in (int, float) and root_ms >= 0
    end = datetime.datetime.fromisoformat(audit['timestamp']).timestamp() + root_ms / 1000
    assert abs(response_mtime - end) <= 5, 'Audit timestamp differs from raw capture'
    durations = [item['durationMs'] for item in audit['items']]
    assert all(value is None or (type(value) in (int, float) and value >= 0) for value in durations)
    if 'operations' in request['arguments']:
        assert [(item['operationId'], item['operation']) for item in audit['items']] == [
            (item['operationId'], item['operation']) for item in request['arguments']['operations']]
    if request['arguments'].get('dryRun') is True and document['success']:
        assert document['phase'] == audit['phase'] == 'preview' and all(value is None for value in durations)
    item_ms = sum(value or 0 for value in durations)
    assert item_ms <= root_ms
    return {'rootMs': root_ms, 'itemSumMs': item_ms, 'remainderMs': root_ms - item_ms}


def redact(value, replacements):
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            replaced_key = redact(key, replacements)
            assert replaced_key not in result, 'Redaction collapsed object keys'
            result[replaced_key] = redact(item, replacements)
        return result
    if isinstance(value, list):
        return [redact(item, replacements) for item in value]
    if not isinstance(value, str):
        return value
    if value.lstrip().startswith(('{', '[')):
        try:
            parsed = json.loads(value)
        except json.JSONDecodeError:
            pass
        else:
            return json.dumps(redact(parsed, replacements), ensure_ascii=False, separators=(',', ':'))
    for old, new in sorted(replacements.items(), key=lambda pair: len(pair[0]), reverse=True):
        assert old and isinstance(old, str) and isinstance(new, str), 'Invalid redaction mapping'
        value = value.replace(old, new)
    return value


def leaf_exceptions(notes):
    groups = collections.defaultdict(list)
    leaf_notes = []
    for note in notes:
        if not note.startswith('PROBE_EXCEPTION ') or note.startswith('PROBE_EXCEPTION top-level/no-leaf'):
            continue
        leaf_notes.append(note)
        body = note.split('\n', 1)[1] if '\n' in note else ''
        matches = list(re.finditer(r'(?m)^([A-Za-z_][\w.]*Exception): ?', body))
        for index, match in enumerate(matches):
            end = matches[index + 1].start() - 1 if index + 1 < len(matches) else len(body)
            groups[match.group(1)].append(body[match.end():end])
    return leaf_notes, [{'type': kind, 'count': len(messages), 'messageLengthMin': min(map(len, messages)),
                         'messageLengthMax': max(map(len, messages))} for kind, messages in sorted(groups.items())]


def file_changes(before, after):
    def flatten(snapshot):
        files, directories = {}, set()
        for root in snapshot['roots']:
            prefix = root['root'].rstrip('\\/')
            files.update({prefix + '/' + item['path']: (item['length'], item['sha256']) for item in root['files']})
            directories.update(prefix + '/' + name for name in root['directories'])
        return files, directories
    old_files, old_dirs = flatten(before)
    new_files, new_dirs = flatten(after)
    return {'addedFiles': sorted(new_files.keys() - old_files.keys()), 'removedFiles': sorted(old_files.keys() - new_files.keys()),
            'changedFiles': sorted(name for name in old_files.keys() & new_files.keys() if old_files[name] != new_files[name]),
            'addedDirectories': sorted(new_dirs - old_dirs), 'removedDirectories': sorted(old_dirs - new_dirs)}


def summarize(directories):
    samples, cases, snapshots, archive = {}, [], [], []
    seen_cases = set()
    verified_pairs = 0
    for session_index, directory in enumerate(map(pathlib.Path, directories)):
        verified_labels = set()
        audits = {}
        for row in read_lines(directory / 'audits.jsonl'):
            assert row['label'] not in audits, 'Duplicate audit label: ' + row['label']
            audits[row['label']] = row['record']
        measurements = read_lines(directory / 'measurements.jsonl')
        seen_measurements = set()
        for measurement in measurements:
            label = measurement['label']
            assert label not in seen_measurements, 'Duplicate measurement label: ' + label
            seen_measurements.add(label)
            match = TIMING.fullmatch(label)
            if not match:
                continue
            slot = (match.group(1), int(match.group(2)))
            assert slot not in samples, 'Duplicate timing sample: ' + str(slot)
            response_path = directory / (label + '.json')
            response = read_json(response_path)
            request = read_json(directory / (label + '-request.json'))
            assert request['name'] == 'vci_write', 'Timing sample is not vci_write'
            assert response['structuredContent']['success'] is True and not response.get('isError', False)
            metrics = verify_pair(response, request, audits[label], response_path.stat().st_mtime)
            assert metrics == {key: measurement[key] for key in METRICS}, 'Measurement differs from correlated audit'
            samples[slot] = {'label': label, **metrics}
            verified_pairs += 1
            verified_labels.add(label)
        for path in sorted(directory.glob('*.json')):
            if not CASE.match(path.stem):
                continue
            payload = read_json(path)
            source_operations = payload.get('arguments', {}).get('operations', []) if isinstance(payload, dict) else []
            source_operations += (payload.get('structuredContent', {}).get('batch') or {}).get('operations', []) if isinstance(payload, dict) else []
            full_source_export = any(item.get('operation') in ('get_block_content', 'get_type_content') for item in source_operations)
            paired_request = path.with_name(path.stem + '-request.json')
            source_request = payload if path.stem.endswith('-request') else read_json(paired_request) if paired_request.exists() else {}
            project_status = source_request.get('name') == 'get_project_status' or (payload.get('structuredContent') or {}).get('tool') == 'get_project_status'
            if project_status and 'structuredContent' in payload:
                assert json.loads(payload['content'][0]['text']) == payload['structuredContent'], 'Canonical status text differs'
            if not full_source_export and not project_status:
                archive.append({'session': session_index, 'file': path.name, 'payload': payload})
            if path.stem.endswith('-before-files'):
                after = path.with_name(path.stem[:-len('-before-files')] + '-after-files.json')
                if after.exists():
                    snapshots.append({'label': path.stem[:-len('-before-files')], 'session': session_index,
                                      **file_changes(payload, read_json(after))})
            if not re.match(r'^[BP][1-9](?:[-_]|$)', path.stem) or path.stem.endswith('-request') or 'structuredContent' not in payload:
                continue
            request_path = path.with_name(path.stem + '-request.json')
            if not request_path.exists():
                continue
            request = read_json(request_path)
            if request['name'] != 'vci_write':
                continue
            document = payload['structuredContent']
            assert json.loads(payload['content'][0]['text']) == document
            metrics = verify_pair(payload, request, audits[path.stem], path.stat().st_mtime)
            if path.stem not in verified_labels:
                verified_pairs += 1
                verified_labels.add(path.stem)
            seen_cases.add(re.match(r'^([BP][1-9])', path.stem).group(1))
            items = []
            for item in (document.get('batch') or {}).get('operations', []):
                outcome = item.get('result') or {}
                notes, types = leaf_exceptions(outcome.get('notes', []))
                items.append({'operationId': item['operationId'], 'status': item['status'], 'failure': item.get('failure'),
                              'outcome': {key: outcome[key] for key in ('leaves', 'succeeded', 'failed', 'skipped', 'projectModified') if key in outcome},
                              'leafExceptionNoteCount': len(notes), 'leafExceptionNotePreviews': [note[:700] for note in notes[:2]],
                              'exceptionTypes': types})
            cases.append({'label': path.stem, 'session': session_index, 'success': document.get('success'),
                          'phase': document.get('phase'), 'error': document.get('error'),
                          'counts': (document.get('batch') or {}).get('counts'), 'items': items, **metrics})
        for label, audit in audits.items():
            if CASE.match(label):
                response_path = directory / (label + '.json')
                if label not in verified_labels:
                    verify_pair(read_json(response_path), read_json(directory / (label + '-request.json')), audit, response_path.stat().st_mtime)
                    verified_pairs += 1
                    verified_labels.add(label)
                archive.append({'session': session_index, 'file': 'audits.jsonl', 'label': label,
                                'correlationVerifiedBeforeRedaction': True, 'payload': audit})
        for measurement in measurements:
            if CASE.match(measurement['label']):
                archive.append({'session': session_index, 'file': 'measurements.jsonl', 'payload': measurement})
    medians = {}
    pending = []
    for case in ('T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'):
        rows = [samples[(case, run)] for run in (1, 2, 3) if (case, run) in samples]
        if len(rows) == 3:
            medians[case] = {key: statistics.median(row[key] for row in rows) for key in METRICS}
        else:
            pending.extend(case + '-r' + str(run) for run in (1, 2, 3) if (case, run) not in samples)
    pending.extend(case for case in [f'B{index}' for index in range(1, 9)] + [f'P{index}' for index in range(1, 10)] if case not in seen_cases)
    return {'validatedTimingSamples': len(samples), 'verifiedAuditPairs': verified_pairs, 'mediansMs': medians,
            'timingSamples': [samples[key] for key in sorted(samples)], 'behaviorAndProbes': cases,
            'fileChanges': snapshots, 'pending': pending, 'matrixPresenceComplete': not pending}, archive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=pathlib.Path)
    parser.add_argument('--redactions', type=pathlib.Path)
    parser.add_argument('--archive', type=pathlib.Path)
    args = parser.parse_args()
    replacements = read_json(args.redactions) if args.redactions else {}
    summary, archive = summarize(args.directories)
    summary['auditCorrelation'] = 'Verified against original raw captures before any redaction; archived response hashes are original, not hashes of redacted text.'
    redacted_summary = redact(summary, replacements)
    if args.archive:
        header = {'type': 'verification', 'summary': redacted_summary, 'redacted': bool(replacements),
                  'correlationVerifiedBeforeRedaction': True, 'archivedHashesDescribe': 'original unredacted response text'}
        content = '\n'.join(json.dumps(row, ensure_ascii=False) for row in [header] + redact(archive, replacements)) + '\n'
        for original in replacements:
            assert original not in content and json.dumps(original, ensure_ascii=False)[1:-1] not in content, 'Sensitive original remains in archive'
        assert not args.archive.exists(), 'Archive already exists'
        args.archive.write_bytes(gzip.compress(content.encode('utf-8'), mtime=0))
        redacted_summary['archivedArtifacts'] = len(archive)
    print(json.dumps(redacted_summary, ensure_ascii=False, separators=(',', ':')))


if __name__ == '__main__':
    main()
