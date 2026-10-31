"""Publish hash-only import observations from original canonical PLC read captures."""

import argparse
import hashlib
import json
import pathlib
import re

CASES = ('B5', 'B5-malformed', 'B6', 'T6-r1', 'T6-r2', 'T6-r3',
         'P8-corrupt', 'P8-missing', 'P8-malformed')


def sha256(text):
    return 'sha256:' + hashlib.sha256(text.encode('utf-8')).hexdigest()


def read_capture(path):
    response = json.loads(path.read_text(encoding='utf-8-sig'))
    text = response['content'][0]['text']
    document = response['structuredContent']
    assert json.loads(text) == document, 'Canonical read text differs'
    return document, sha256(text)


def source_observation(item):
    result = item.get('result') or {}
    content = result.get('content')
    available = isinstance(content, str)
    return {
        'readStatus': item.get('status'),
        'failureCategory': (item.get('failure') or {}).get('category'),
        'contentHash': result.get('contentHash'),
        'sourceTextSha256': sha256(content) if available else None,
        'requestedStartValueOnePresent': bool(re.search(r'(?im)^\s*VciValue\s*:=\s*1\s*;', content)) if available else None,
    }, content if available else None


def build_proof(directories):
    directories = [pathlib.Path(directory) for directory in directories]
    rows, compile_reports, pending = [], [], []
    for case in CASES:
        matches = [directory for directory in directories
                   if (directory / (case + '-before-plc.json')).exists()
                   and (directory / (case + '-after-plc.json')).exists()]
        assert len(matches) <= 1, 'Duplicate original proof captures: ' + case
        if not matches:
            pending.append(case)
            continue
        directory = matches[0]
        before, before_hash = read_capture(directory / (case + '-before-plc.json'))
        after, after_hash = read_capture(directory / (case + '-after-plc.json'))
        assert before.get('tool') == after.get('tool') == 'plc_read', 'Expected PLC read captures'
        before_items = (before.get('batch') or {}).get('operations', [])
        after_items = (after.get('batch') or {}).get('operations', [])
        for index in range(max(len(before_items), len(after_items))):
            left = before_items[index] if index < len(before_items) else {}
            right = after_items[index] if index < len(after_items) else {}
            first, first_text = source_observation(left)
            second, second_text = source_observation(right)
            rows.append({
                'case': case,
                'itemIndex': index,
                'before': first,
                'after': second,
                'sourceTextEqual': first_text == second_text if first_text is not None and second_text is not None else None,
                'canonicalResponseTextSha256OriginalBefore': before_hash,
                'canonicalResponseTextSha256OriginalAfter': after_hash,
            })
        for path in sorted(directory.glob(case + '-compile*.json')):
            match = re.fullmatch(re.escape(case) + r'-compile(\d+)', path.stem)
            if match is None:
                continue
            index = int(match.group(1))
            document, canonical_hash = read_capture(path)
            result = document.get('result') or {}
            value = result.get('value') or {}
            failure = document.get('error') or result.get('failure') or {}
            compile_reports.append({
                'case': case,
                'compileIndex': index,
                'success': document.get('success'),
                'overallState': value.get('overallState'),
                'totalErrorCount': value.get('totalErrorCount'),
                'totalWarningCount': value.get('totalWarningCount'),
                'failureCategory': failure.get('category'),
                'canonicalResponseTextSha256Original': canonical_hash,
            })
    return {
        'format': 'vci-stage0-import-proof-1',
        'hashesDescribe': 'Original unredacted canonical response text and exact decoded source strings encoded as UTF-8, preserving line endings. These are not hashes of anonymized text.',
        'limitation': 'Hash equality supports unchanged PLC source; transaction rollback also requires the VCI outcome and candidate accounting. Missing source observations have null equality.',
        'rows': rows,
        'compileReports': compile_reports,
        'pending': pending,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directories', nargs='+', type=pathlib.Path)
    args = parser.parse_args()
    print(json.dumps(build_proof(args.directories), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
