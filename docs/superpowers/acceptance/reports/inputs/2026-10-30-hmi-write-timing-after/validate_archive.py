"""Offline verification of the archived installed-version HMI timing evidence."""

import gzip
import hashlib
import json
import pathlib
import statistics

from client import audit_metrics, validate_write

ROOT = pathlib.Path(__file__).resolve().parent


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def read_lines(name):
    return [json.loads(line) for line in gzip.decompress((ROOT / name).read_bytes()).splitlines()]


def independent_medians(rows):
    return {case: {key: statistics.median(row[key] for row in rows
                   if row['case'] == case and not row['discarded'])
                   for key in ('rootMs', 'itemSumMs', 'remainderMs')}
            for case in ('H1', 'H2', 'H3', 'H4', 'H5')}


def table_count(result):
    return len(result['tables']) + sum(table_count(group) for group in result['groups'])


def main():
    # Independent columns can have their medians in different runs.
    fixture = [{'case': case, 'discarded': False, 'rootMs': root,
                'itemSumMs': item, 'remainderMs': root - item}
               for case in ('H1', 'H2', 'H3', 'H4', 'H5')
               for root, item in ((10, 9), (20, 1), (30, 8))]
    assert independent_medians(fixture)['H1'] == {'rootMs': 20, 'itemSumMs': 8, 'remainderMs': 19}
    manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
    for name, entry in manifest['files'].items():
        data = (ROOT / name).read_bytes()
        assert len(data) == entry['bytes'] and sha256(data) == entry['sha256'], name
        if name.endswith('.gz'):
            raw = gzip.decompress(data)
            assert len(raw) == entry['decompressedBytes']
            assert sha256(raw) == entry['decompressedSha256']
    workloads = json.loads((ROOT / 'workloads.json').read_text(encoding='utf-8'))
    summary = json.loads((ROOT / 'summary.json').read_text(encoding='utf-8'))
    rows = []
    writes = read_lines('timing-writes.jsonl.gz')
    assert len(writes) == 20
    expected = [f'{case}-run{run}' for run in range(4) for case in workloads]
    assert [row['label'] for row in writes] == expected
    for capture in writes:
        label = capture['label']
        case, run_text = label.split('-run')
        response = capture['response']
        request = capture['request']
        audit = capture['auditProjection']
        assert request == {'name': 'hmi_write', 'arguments': workloads[case]}
        assert response['isError'] is False
        assert capture['auditRedactedFields'] == ['projectPath', 'binding.projectPath']
        assert audit['projectPath'] == audit['binding']['projectPath'] == '<disposable-copy>'
        metrics = audit_metrics(response, audit, capture['sourceResponseMtime'])
        validate_write(response['structuredContent'], audit, workloads[case])
        assert metrics == {key: capture['measurement'][key] for key in metrics}
        rows.append({'label': label, 'case': case, 'run': int(run_text),
                     'discarded': run_text == '0', **metrics})
    assert rows == summary['rows']
    assert independent_medians(rows) == summary['mediansMs']
    reads = read_lines('hmi-reads.jsonl.gz')
    assert len(reads) == 22
    assert [row['label'] for row in reads] == ['startup-discarded-read'] + [label + '-warm' for label in expected] + ['timing-final-inventory']
    for capture in reads:
        response = capture['response']
        text = response['content'][0]['text']
        document = response['structuredContent']
        assert json.loads(text) == document and document['success'] is True
        assert document == reads[0]['response']['structuredContent']
        assert sha256(text.encode()) == capture['canonicalResponseSha256']
        results = {item['operationId']: item['result'] for item in document['batch']['operations']}
        assert all(result['isComplete'] for result in results.values())
        assert results['tags']['page']['total'] == 101 and results['tags']['page']['nextOffset'] is None
        assert results['alarms']['page']['total'] == 4 and results['alarms']['page']['nextOffset'] is None
        assert table_count(results['tables']) == 9
    projections = json.loads((ROOT / 'lifecycle-status-projections.json').read_text(encoding='utf-8'))
    assert len(projections['captures']) == 66
    for capture in projections['captures']:
        assert capture['identityRedacted'] is True and capture['success'] is True
        assert capture['projectMatchesConfigured'] is True
        if capture['tool'] == 'close_project':
            assert capture['requestArguments'] == {'saveBeforeClose': False}
        if capture['tool'] == 'get_project_status':
            assert capture['isOpen'] is True and capture['isModified'] is False
    assert summary['validatedCaptures'] == 20 and summary['measuredCaptures'] == 15
    assert summary['discardedWarmups'] == 5
    print('PASS: manifest,20 canonical write/audit pairs,22 complete reads,66 redacted projections,15 measured samples,5 independent medians')
    print(json.dumps(summary['mediansMs'], sort_keys=True))


if __name__ == '__main__':
    main()
