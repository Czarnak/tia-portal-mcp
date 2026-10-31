"""Synthetic offline checks for timing correlation, partial matrices and redaction."""

import copy
import datetime
import gzip
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import tempfile

from summarize import file_changes, leaf_exceptions, redact, summarize, verify_pair

ROOT = pathlib.Path(__file__).resolve().parent


def rejects(action):
    try:
        action()
    except AssertionError:
        return
    raise AssertionError('Invalid evidence was accepted')


def fixture(directory, label, root_ms, item_ms, preview=False):
    operation = {'operationId': 'one', 'operation': 'export'}
    request = {'name': 'vci_write', 'arguments': {'operations': [operation], 'dryRun': preview}}
    document = {'success': True, 'phase': 'preview' if preview else 'applied'}
    text = json.dumps(document)
    response = {'content': [{'text': text}], 'structuredContent': document, 'isError': False}
    stamp = datetime.datetime.now(datetime.timezone.utc)
    audit = {'recordVersion': 2, 'accessMode': 'full', 'tool': 'vci_write', 'responseText': text,
             'responseHash': 'sha256:' + hashlib.sha256(text.encode()).hexdigest(), 'timestamp': stamp.isoformat(),
             'durationMs': root_ms, 'phase': document['phase'],
             'items': [dict(operation, durationMs=None if preview else item_ms)]}
    metric = {'label': label, 'rootMs': root_ms, 'itemSumMs': 0 if preview else item_ms,
              'remainderMs': root_ms - (0 if preview else item_ms)}
    for suffix, value in (('.json', response), ('-request.json', request)):
        path = directory / (label + suffix)
        path.write_text(json.dumps(value), encoding='utf-8')
        os.utime(path, (stamp.timestamp() + root_ms / 1000,) * 2)
    for name, row in (('audits.jsonl', {'label': label, 'record': audit}), ('measurements.jsonl', metric)):
        with (directory / name).open('a', encoding='utf-8') as stream:
            stream.write(json.dumps(row) + '\n')
    return response, request, audit, stamp.timestamp() + root_ms / 1000


with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
    directory = pathlib.Path(temporary)
    pair = fixture(directory, 'T1-r1', 10, 9)
    assert verify_pair(*pair)['remainderMs'] == 1
    bad = list(copy.deepcopy(pair))
    bad[2]['responseHash'] = 'sha256:wrong'
    rejects(lambda: verify_pair(*bad))
    bad = list(copy.deepcopy(pair))
    bad[0]['structuredContent']['success'] = False
    rejects(lambda: verify_pair(*bad))
    summary, _ = summarize([directory])
    assert 'T1' not in summary['mediansMs'] and 'T1-r2' in summary['pending']
    fixture(directory, 'T1-r2-write', 20, 1)
    fixture(directory, 'T1-r3', 30, 8)
    fixture(directory, 'T4-r1', 4, 0, True)
    (directory / 'setup-all-tree.json').write_text('{}', encoding='utf-8')
    (directory / 'T5-prepare-read-request.json').write_text(json.dumps({'name': 'plc_read', 'arguments': {
        'operations': [{'operation': 'get_block_content'}]}}), encoding='utf-8')
    status_label = 'P9-final-baseline-status'
    status_document = {'success': True, 'result': {'value': {'isOpen': True, 'isModified': False,
                       'author': 'PRIVATE_TEST_AUTHOR', 'metadata': {'historyEntries': ['PRIVATE_TEST_HISTORY']}}}}
    status_response = {'structuredContent': status_document, 'content': [{'text': json.dumps(status_document)}]}
    (directory / (status_label + '.json')).write_text(json.dumps(status_response), encoding='utf-8')
    (directory / (status_label + '-request.json')).write_text(json.dumps({
        'name': 'get_project_status', 'arguments': {}}), encoding='utf-8')
    summary, archive = summarize([directory])
    assert summary['mediansMs']['T1'] == {'rootMs': 20, 'itemSumMs': 8, 'remainderMs': 19}
    assert summary['validatedTimingSamples'] == 4 and not summary['matrixPresenceComplete']
    assert not any(row['file'].startswith('rpc') for row in archive)
    assert not any(row['file'] in ('setup-all-tree.json', 'T5-prepare-read-request.json') for row in archive)
    assert not any(row['file'].startswith(status_label) for row in archive)
    assert any(row['file'] == 'T1-r1.json' for row in archive)
    bad_status = copy.deepcopy(status_response)
    bad_status['structuredContent']['success'] = False
    (directory / (status_label + '.json')).write_text(json.dumps(bad_status), encoding='utf-8')
    rejects(lambda: summarize([directory]))
    (directory / (status_label + '.json')).write_text(json.dumps(status_response), encoding='utf-8')
    exact_cases = [f'B{index}' for index in range(1, 9)] + [f'P{index}' for index in range(1, 10)]
    for label in exact_cases:
        fixture(directory, label, 5, 2)
    summary, archive = summarize([directory])
    assert {row['label'] for row in summary['behaviorAndProbes']} == set(exact_cases)
    assert not set(exact_cases) & set(summary['pending'])
    assert {label + '.json' for label in exact_cases} <= {row['file'] for row in archive}
    archive_path = directory / 'exact-cases.jsonl.gz'
    subprocess.run([sys.executable, str(ROOT / 'summarize.py'), str(directory), '--archive', str(archive_path)],
                   check=True, capture_output=True, text=True)
    archived = [json.loads(line) for line in gzip.decompress(archive_path.read_bytes()).decode('utf-8').splitlines()]
    assert not any(row.get('file', '').startswith(status_label) for row in archived)
    assert 'PRIVATE_TEST_AUTHOR' not in json.dumps(archived) and 'PRIVATE_TEST_HISTORY' not in json.dumps(archived)
    assert {label + '.json' for label in exact_cases} <= {row.get('file') for row in archived}
    assert set(exact_cases) <= {row['label'] for row in archived if row.get('file') == 'audits.jsonl'
                               and row.get('correlationVerifiedBeforeRedaction') is True}
    fixture(directory, 'T1-r1-write', 40, 1)
    rejects(lambda: summarize([directory]))
value = {'text': json.dumps({'path': 'C:\\private\\Project', 'name': 'Sensitive'}), 'path': 'C:\\private\\Project'}
redacted = redact(value, {'C:\\private\\Project': '<fixture>', 'Sensitive': '<name>'})
assert json.loads(redacted['text']) == {'path': '<fixture>', 'name': '<name>'}
assert 'private' not in json.dumps(redacted) and redacted['path'] == '<fixture>'
notes = ['PROBE_EXCEPTION leaf\nSiemens.Engineering.EngineeringException: native message',
         'PROBE_EXCEPTION top-level/no-leaf\nSystem.Exception: aggregate']
kept, groups = leaf_exceptions(notes)
assert len(kept) == 1 and groups[0]['messageLengthMin'] == len('native message')
old = {'roots': [{'root': 'root', 'files': [{'path': 'a', 'length': 1, 'sha256': 'old'}], 'directories': ['.']}]}
new = copy.deepcopy(old)
new['roots'][0]['files'][0]['sha256'] = 'new'
assert file_changes(old, new)['changedFiles'] == ['root/a']
print('PASS: original audit correlation, independent medians, pending/duplicate handling, preview, redaction, notes and file changes')
