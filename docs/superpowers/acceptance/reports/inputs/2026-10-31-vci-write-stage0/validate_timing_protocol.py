"""Qualify the corrected Stage 0 timing workloads from original private captures."""

import argparse
import json
import os
import pathlib
import re

SKIPS = ('alreadyMapped', 'equal', 'conflict', 'targetChanged')
DEPENDENT_STAGES = ['create_workspace', 'export', 'delete_mapping', 'delete_mapping', 'delete_mapping', 'export']


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def document(path):
    response = load(path)
    value = response['structuredContent']
    assert json.loads(response['content'][0]['text']) == value, 'Canonical document differs: ' + path.name
    return value


def good_document(path):
    value = document(path)
    assert value['success'] is True and not load(path).get('isError', False), 'Tool did not succeed: ' + path.name
    return value


def check_outcomes(value, stages, counters):
    assert value['success'] is True and value.get('error') is None and value['phase'] == 'applied'
    batch = value['batch']
    assert batch['operationCount'] == len(stages)
    assert batch['counts'] == {'succeeded': len(stages), 'failed': 0, 'omitted': 0, 'skipped': 0}
    assert [item['operation'] for item in batch['operations']] == stages
    actual = []
    for item, (leaves, succeeded, equal, mapped) in zip(batch['operations'], counters):
        assert item['status'] == 'succeeded' and item.get('failure') is None
        outcome = item['result']
        expected = {'leaves': leaves, 'succeeded': succeeded, 'failed': 0, 'formatFallbacks': 0}
        assert all(type(outcome[key]) is int and outcome[key] == number for key, number in expected.items()), 'Unexpected VCI leaf counters'
        expected_skips = dict.fromkeys(SKIPS, 0)
        expected_skips.update(equal=equal, alreadyMapped=mapped)
        assert outcome['skipped'] == expected_skips, 'Unexpected VCI skips'
        actual.append({'operation': item['operation'], **expected, 'skipped': expected_skips})
    return actual


def check_case_write(case, value, arguments):
    operations = arguments['operations']
    if case in ('T3', 'T4'):
        assert [item['operation'] for item in operations] == DEPENDENT_STAGES
        assert operations[1]['scope'] == operations[5]['scope']
        assert operations[1].get('relativeDirectory') != operations[5].get('relativeDirectory')
        assert len({json.dumps(item['scope'], sort_keys=True) for item in operations[2:5]}) == 3
    if case == 'T4':
        assert arguments['dryRun'] is True and value['success'] is True and value.get('error') is None
        assert value['phase'] == 'preview' and value['batch'] is None and value['verification'] is None
        return {'stages': DEPENDENT_STAGES, 'preview': True}
    assert not arguments.get('dryRun', False)
    stages, counters = {
        'T1': (['create_workspace'], [(1, 1, 0, 0)]),
        'T2': (['export'], [(3, 3, 0, 0)]),
        'T3': (DEPENDENT_STAGES, [(1, 1, 0, 0), (10, 10, 0, 0), (1, 1, 0, 0), (1, 1, 0, 0), (1, 1, 0, 0), (10, 3, 0, 7)]),
        'T5': (['sync_to_workspace'], [(10, 3, 7, 0)]),
        'T6': (['sync_to_project'], [(3, 3, 0, 0)]),
        'T7': (['export'], [(58, 58, 0, 0)]),
    }[case]
    assert [item['operation'] for item in operations] == stages
    if case == 'T5':
        assert operations[0]['scope'][-1]['nodeType'] == 'TagTableFolder', 'T5 must use the corrected tag-table control'
    return {'stages': check_outcomes(value, stages, counters)}


def workspace_count(value):
    counts = []
    for item in (value.get('batch') or {}).get('operations', []):
        if item['operation'] == 'list_workspaces':
            assert item['status'] == 'succeeded'
            counts.extend(workspace['mappingCount'] for workspace in item['result']['workspaces'] if workspace['name'] == 'base')
    assert len(counts) == 1, 'Public base workspace inventory missing or ambiguous'
    return counts[0]


def source_value(content):
    declaration = re.findall(r'\bVciValue\s*:\s*Int\s*(?::=\s*(-?\d+))?\s*;', content, re.IGNORECASE)
    assert len(declaration) == 1, 'Expected exactly one VciValue Int declaration'
    assignments = re.findall(r'\bVciValue\s*:=\s*(-?\d+)\s*;', content, re.IGNORECASE)
    assert len(assignments) <= 1, 'Ambiguous VciValue assignment'
    return int(assignments[0]) if assignments else int(declaration[0] or 0)


def required_suffixes(case):
    suffixes = ['.json', '-request.json', '-restore-baseline-status.json', '-restore-baseline-warm.json',
                '-restore-open-request.json', '-restore-close-request.json']
    if case == 'T4':
        suffixes += ['-before-files.json', '-after-files.json']
    if case == 'T5':
        suffixes += ['-prepare-tables.json', '-prepare-mappings.json', '-project-change.json', '-project-change-request.json']
    if case == 'T6':
        suffixes += ['-unmap7.json', '-before-plc.json', '-after-plc.json', '-before-plc-request.json']
        suffixes += ['-compile' + str(index) + '.json' for index in range(3)]
    return suffixes


def validate_run(directory, case, label):
    path = lambda suffix: directory / (label + suffix)
    request = load(path('-request.json'))
    assert request['name'] == 'vci_write'
    value = good_document(path('.json'))
    details = check_case_write(case, value, request['arguments'])
    close = load(path('-restore-close-request.json'))
    assert close == {'name': 'close_project', 'arguments': {'saveBeforeClose': False}}
    reopened = load(path('-restore-open-request.json'))
    status = good_document(path('-restore-baseline-status.json'))['result']['value']
    assert status['isOpen'] is True and status['isModified'] is False, 'Restoration is not publicly clean'
    assert os.path.normcase(os.path.normpath(status['path'])) == os.path.normcase(os.path.normpath(reopened['arguments']['projectPath']))
    assert workspace_count(good_document(path('-restore-baseline-warm.json'))) == 216, 'Restored base mapping count differs'
    details.update(restorationOpenUnmodified=True, restoredBaseMappingCount=216)
    observed = []
    for suffix in ('-before-read.json', '-before-read-workspaces.json'):
        if path(suffix).exists():
            observed.append(workspace_count(document(path(suffix))))
    expected_count = {'T5': 226, 'T6': 209}.get(case, 216)
    assert all(count == expected_count for count in observed), 'Pre-write base mapping count differs'
    details['beforeBaseMappingCount'] = observed[0] if observed else None
    if case == 'T4':
        assert load(path('-before-files.json')) == load(path('-after-files.json')), 'Preview changed filesystem evidence'
        details['filesUnchanged'] = True
    if case == 'T5':
        tables = good_document(path('-prepare-tables.json'))['batch']
        assert len(tables['operations']) == 10 and all(item['operation'] == 'create_tag_table' and item['status'] == 'succeeded' for item in tables['operations'])
        check_outcomes(good_document(path('-prepare-mappings.json')), ['export'] * 10, [(1, 1, 0, 0)] * 10)
        changes = good_document(path('-project-change.json'))['batch']['operations']
        assert len(changes) == 3 and all(item['operation'] == 'create_tag' and item['status'] == 'succeeded' for item in changes)
        tag_changes = load(path('-project-change-request.json'))['arguments']['operations']
        assert [item['operation'] for item in tag_changes] == ['create_tag'] * 3
        assert len({item['tableName'] for item in tag_changes}) == 3
        details['identityPreservingTagChanges'] = 3
    if case == 'T6':
        check_outcomes(good_document(path('-unmap7.json')), ['delete_mapping'] * 7, [(1, 1, 0, 0)] * 7)
        before = good_document(path('-before-plc.json'))['batch']['operations']
        after = good_document(path('-after-plc.json'))['batch']['operations']
        targets = load(path('-before-plc-request.json'))['arguments']['operations']
        assert len(before) == len(after) == len(targets) == 3
        assert len({target['blockPath'] for target in targets}) == 3
        for index, (old, new, target) in enumerate(zip(before, after, targets)):
            assert old['operationId'] == new['operationId'] == target['operationId']
            assert old['status'] == new['status'] == 'succeeded'
            assert old['result']['format'] == new['result']['format'] == 'source'
            assert source_value(old['result']['content']) == 0 and source_value(new['result']['content']) == 1
            report = good_document(path('-compile' + str(index) + '.json'))['result']['value']
            assert report['blockPath'] == target['blockPath']
            assert report['totalErrorCount'] == 0 and all(plc['errorCount'] == 0 for plc in report['plcs'])
        details.update(untimedMappingsRemoved=7, variablesChangedFromZeroToOne=3, zeroErrorCompileReports=3)
    return details, request['arguments']


def validate(directory):
    directory = pathlib.Path(directory)
    cases = {}
    for case in ('T1', 'T2', 'T3', 'T4', 'T5', 'T6', 'T7'):
        runs, pending, errors, arguments = [], [], [], []
        for index in (1, 2, 3):
            label = case + '-r' + str(index)
            missing = [label + suffix for suffix in required_suffixes(case) if not (directory / (label + suffix)).exists()]
            if missing:
                pending.extend(missing)
                continue
            try:
                details, args = validate_run(directory, case, label)
                runs.append({'label': label, **details})
                arguments.append(args)
            except (AssertionError, KeyError, TypeError, ValueError) as error:
                errors.append({'label': label, 'error': str(error) or type(error).__name__})
        if len(arguments) == 3 and any(args != arguments[0] for args in arguments[1:]):
            errors.append({'error': 'Measured vci_write arguments differ across samples'})
        status = 'pending' if pending else 'invalid' if errors else 'valid'
        cases[case] = {'status': status, 'validatedRuns': len(runs), 'runs': runs, 'pending': pending, 'errors': errors}
    return {'cases': cases, 'allCasesQualified': all(case['status'] == 'valid' for case in cases.values()),
            'invalidCompletedCases': [name for name, case in cases.items() if case['status'] == 'invalid'],
            'stateEvidenceLimit': 'Restoration uses public open/unmodified status and workspace inventory; AP21 hashes do not prove full project state.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=pathlib.Path)
    result = validate(parser.parse_args().directory)
    print(json.dumps(result, separators=(',', ':')))
    raise SystemExit(1 if result['invalidCompletedCases'] else 0)


if __name__ == '__main__':
    main()
