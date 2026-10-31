"""Synthetic checks only; never starts a server or reads live TIA state."""

import copy
import json
import pathlib
import tempfile

from validate_timing_protocol import check_case_write, check_outcomes, source_value, validate

ROOT = pathlib.Path(__file__).resolve().parent


def rejects(action):
    try:
        action()
    except AssertionError:
        return
    raise AssertionError('Invalid workload accepted')


def outcome_document(stages, counts):
    operations = []
    for index, (operation, (leaves, succeeded, equal, mapped)) in enumerate(zip(stages, counts)):
        operations.append({'operationId': str(index), 'operation': operation, 'status': 'succeeded', 'failure': None,
                           'result': {'leaves': leaves, 'succeeded': succeeded, 'failed': 0, 'formatFallbacks': 0,
                                      'skipped': {'alreadyMapped': mapped, 'equal': equal, 'conflict': 0, 'targetChanged': 0}}})
    return {'success': True, 'error': None, 'phase': 'applied', 'batch': {'operationCount': len(stages),
             'counts': {'succeeded': len(stages), 'failed': 0, 'omitted': 0, 'skipped': 0}, 'operations': operations}}


value = outcome_document(['sync_to_workspace'], [(10, 3, 7, 0)])
args = {'operations': [{'operation': 'sync_to_workspace', 'scope': [{'nodeType': 'TagTableFolder', 'name': 'Tags'}]}]}
assert check_case_write('T5', value, args)['stages'][0]['succeeded'] == 3
bad = copy.deepcopy(value)
bad['batch']['operations'][0]['result']['succeeded'] = 0
rejects(lambda: check_case_write('T5', bad, args))
bad = copy.deepcopy(value)
bad['batch']['operations'][0]['result']['skipped']['conflict'] = 1
rejects(lambda: check_case_write('T5', bad, args))
assert source_value('VAR VciValue : Int; END_VAR BEGIN END_DATA_BLOCK') == 0
assert source_value('VAR VciValue : Int; END_VAR BEGIN VciValue := 1; END_DATA_BLOCK') == 1
rejects(lambda: source_value('VciValue : Bool;'))
with tempfile.TemporaryDirectory(dir=ROOT) as temporary:
    directory = pathlib.Path(temporary)
    assert validate(directory)['cases']['T1']['status'] == 'pending'

    def write(label, suffix, document):
        response = {'structuredContent': document, 'content': [{'text': json.dumps(document)}], 'isError': False}
        (directory / (label + suffix)).write_text(json.dumps(response), encoding='utf-8')

    for index in (1, 2, 3):
        label = 'T1-r' + str(index)
        write(label, '.json', outcome_document(['create_workspace'], [(1, 1, 0, 0)]))
        (directory / (label + '-request.json')).write_text(json.dumps({'name': 'vci_write', 'arguments': {
            'operations': [{'operation': 'create_workspace', 'operationId': 'create', 'workspaceName': 'timing'}]}}), encoding='utf-8')
        (directory / (label + '-restore-close-request.json')).write_text(json.dumps({'name': 'close_project', 'arguments': {'saveBeforeClose': False}}), encoding='utf-8')
        (directory / (label + '-restore-open-request.json')).write_text(json.dumps({'name': 'open_project', 'arguments': {'projectPath': str(directory / 'fixture.ap21')}}), encoding='utf-8')
        write(label, '-restore-baseline-status.json', {'success': True, 'result': {'value': {'isOpen': True,
              'isModified': False, 'path': str(directory / 'fixture.ap21')}}})
        write(label, '-restore-baseline-warm.json', {'success': True, 'batch': {'operations': [{'operation': 'list_workspaces',
              'status': 'succeeded', 'result': {'workspaces': [{'name': 'base', 'mappingCount': 216}]}}]}})
    result = validate(directory)
    assert result['cases']['T1']['status'] == 'valid' and result['cases']['T2']['status'] == 'pending'
    assert result['invalidCompletedCases'] == [] and not result['allCasesQualified']
    wrong = outcome_document(['create_workspace'], [(2, 1, 0, 0)])
    write('T1-r3', '.json', wrong)
    assert validate(directory)['invalidCompletedCases'] == ['T1']
print('PASS: exact counters/skips, corrected control, source 0-to-1, partial matrices and invalid completed cases')
