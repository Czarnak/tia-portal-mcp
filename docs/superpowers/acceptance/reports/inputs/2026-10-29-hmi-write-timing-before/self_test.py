"""Offline checks only: importing client never starts a server."""

import copy
import datetime
import hashlib
import io
import json
import queue

from client import audit_metrics, await_response, read_responses, validate_write


def rejects(function, exception):
    try:
        function()
    except exception:
        return
    raise AssertionError('Expected ' + exception.__name__)


def main():
    args = {'dryRun': True, 'operations': [{'operationId': 'h4', 'operation': 'create_tag'}]}
    document = {'contractVersion': '1.0', 'success': True, 'error': None, 'phase': 'preview',
                'effects': [{}], 'batch': None, 'verification': None}
    text = json.dumps(document)
    result = {'structuredContent': document, 'content': [{'type': 'text', 'text': text}], 'isError': False}
    stamp = datetime.datetime(2026, 10, 29, tzinfo=datetime.timezone.utc)
    record = {'recordVersion': 2, 'tool': 'hmi_write', 'accessMode': 'full', 'responseText': text,
              'responseHash': 'sha256:' + hashlib.sha256(text.encode()).hexdigest(), 'timestamp': stamp.isoformat(),
              'durationMs': 100, 'phase': 'preview',
              'items': [{'operationId': 'h4', 'operation': 'create_tag', 'durationMs': None, 'status': 'skipped'}]}
    assert audit_metrics(result, record, stamp.timestamp() + .1) == {'rootMs': 100, 'itemSumMs': 0, 'remainderMs': 100}
    validate_write(document, record, args)
    rejects(lambda: audit_metrics(result, record, stamp.timestamp() + 20), AssertionError)
    invalid = copy.deepcopy(record)
    invalid['responseHash'] = 'sha256:' + '0' * 64
    rejects(lambda: audit_metrics(result, invalid, stamp.timestamp() + .1), AssertionError)
    actual = copy.deepcopy(record)
    actual['items'][0]['durationMs'] = 30
    assert audit_metrics(result, actual, stamp.timestamp() + .1)['remainderMs'] == 70
    rejects(lambda: validate_write(document, actual, args), AssertionError)
    actual_doc = copy.deepcopy(document)
    actual_doc.update(phase='applied', batch={'operationCount': 1,
        'counts': {'failed': 0, 'omitted': 0, 'skipped': 0, 'succeeded': 1},
        'operations': [{'operationId': 'h4', 'operation': 'create_tag', 'status': 'succeeded'}]},
        verification={'success': True})
    actual['phase'] = 'applied'
    actual['items'][0]['status'] = 'succeeded'
    validate_write(actual_doc, actual, {'operations': args['operations']})
    responses = queue.Queue()
    read_responses(io.StringIO('{"method":"notice"}\n{"id":7,"result":{"ok":true}}\n'), responses)
    assert await_response(responses, 7, 1) == {'ok': True}
    rejects(lambda: await_response(responses, 8, 1), RuntimeError)
    rejects(lambda: await_response(queue.Queue(), 1, .001), TimeoutError)
    responses.put({'id': 9, 'error': {'code': -1}})
    rejects(lambda: await_response(responses, 9, 1), RuntimeError)
    print('PASS: audit hash/freshness, independent metrics, preview/actual, queue response/EOF/timeout/error; no server started')


if __name__ == '__main__':
    main()
