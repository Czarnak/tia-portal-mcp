"""Synthetic checks for hash-only import proof; no PLC or project access."""

import hashlib
import json
import pathlib
import tempfile

from build_import_proof import build_proof

ROOT = pathlib.Path(__file__).resolve().parent


def capture(directory, label, contents, compile_report=False):
    if compile_report:
        document = {'tool': 'compile_check', 'success': True, 'result': {'value': {
            'overallState': 'Success', 'totalErrorCount': 0, 'totalWarningCount': 0}}}
    else:
        document = {'tool': 'plc_read', 'success': True, 'batch': {'operations': [
            {'status': 'succeeded', 'failure': None, 'result': {'content': content,
             'contentHash': 'source:sha256:' + hashlib.sha256(content.encode('utf-8')).hexdigest()}}
            for content in contents]}}
    text = json.dumps(document)
    response = {'content': [{'text': text}], 'structuredContent': document}
    (directory / (label + '.json')).write_text(json.dumps(response), encoding='utf-8')


with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
    directory = pathlib.Path(temporary)
    unchanged = 'fixture source\r\n'
    changed = 'VciValue := 1;\r\n'
    capture(directory, 'B5-before-plc', [unchanged, unchanged])
    capture(directory, 'B5-after-plc', [unchanged, changed])
    capture(directory, 'B5-compile0', [], True)
    (directory / 'B5-compile0-request.json').write_text(json.dumps({'name': 'compile_check', 'arguments': {}}), encoding='utf-8')
    proof = build_proof([directory])
    assert len(proof['rows']) == 2 and 'B5' not in proof['pending'] and 'P8-corrupt' in proof['pending']
    first, second = proof['rows']
    assert first['sourceTextEqual'] is True and second['sourceTextEqual'] is False
    assert first['before']['sourceTextSha256'] == first['after']['sourceTextSha256']
    assert second['before']['requestedStartValueOnePresent'] is False
    assert second['after']['requestedStartValueOnePresent'] is True
    assert proof['compileReports'][0]['totalErrorCount'] == 0
    assert proof['compileReports'][0]['compileIndex'] == 0
    assert unchanged not in json.dumps(proof) and changed not in json.dumps(proof)
    path = directory / 'B5-after-plc.json'
    payload = json.loads(path.read_text(encoding='utf-8'))
    payload['structuredContent']['success'] = False
    path.write_text(json.dumps(payload), encoding='utf-8')
    try:
        build_proof([directory])
    except AssertionError:
        pass
    else:
        raise AssertionError('Canonical mismatch accepted')
print('PASS: original canonical hashes, exact source equality, value-one marker, compile counts, pending cases and no source text.')
