#!/usr/bin/env python3
"""Markdown link inventory / broken-link checker.

Usage: python -I link_inventory.py [--repo REPO] [--out DIR] [--fail-on-broken]
Scans every *.md (excluding .git, bin, obj, node_modules, .claude, priv) for
  1. inline links  [text](target)  and images ![alt](target)
  2. reference definitions  [id]: target
  3. autolinks <http://...>
  4. backtick path mentions (docs/..., superpowers/..., or first segment = existing top-level entry)
Writes link-inventory.json and link-matrix.csv. Read-only on the repo.
"""
import argparse, csv, glob, json, os, re, sys
from collections import Counter, defaultdict
from urllib.parse import unquote

EXCLUDE = {'.git', 'bin', 'obj', 'node_modules', '.claude', 'priv'}
SP = 'docs/superpowers'

INLINE = re.compile(r'(!?)\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*(<[^>]*>|[^()\s]*(?:\([^()\s]*\)[^()\s]*)*)(?:\s+(?:"[^"]*"|\'[^\']*\'))?\s*\)')
REFDEF = re.compile(r'^\s{0,3}\[[^\]]+\]:[ \t]+(<[^>]*>|[^\s:]\S*)')
AUTOLINK = re.compile(r'<((?:https?|mailto|ftp):[^>\s]+)>')
BACKTICK = re.compile(r'`([^`\n]+)`')
FENCE = re.compile(r'^\s{0,3}(```+|~~~+)')


def walk_md(repo):
    for root, dirs, files in os.walk(repo):
        dirs[:] = [d for d in dirs if d not in EXCLUDE]
        for f in files:
            if f.lower().endswith('.md'):
                yield os.path.relpath(os.path.join(root, f), repo).replace(os.sep, '/')


def source_class(rel):
    if rel == 'AGENTS.md':
        return 'AGENTS.md'
    if '/' not in rel:
        return 'root-md'
    if rel.startswith(SP + '/'):
        sub = rel[len(SP) + 1:]
        if sub == 'README.md': return 'superpowers-readme'
        if sub.startswith('plans/'): return 'superpowers-plan'
        if sub.startswith('specs/'): return 'superpowers-spec'
        if sub.startswith('acceptance/reports/inputs/'): return 'superpowers-evidence'
        if sub.startswith('acceptance/reports/'): return 'superpowers-report'
        if sub.startswith('acceptance/'): return 'superpowers-acceptance-other'
        return 'superpowers-other'
    if rel.startswith('docs/'):
        parts = rel.split('/')
        if len(parts) == 2: return 'docs-top-level'
        return {'guides': 'docs-guides', 'development': 'docs-development', 'roadmap': 'docs-roadmap',
                'SupportedOperations': 'docs-SupportedOperations'}.get(parts[1], 'docs-other')
    return 'other-md'


def target_class(res):
    """res: repo-relative posix path (no trailing slash handling needed) of an EXISTING target."""
    r = res.rstrip('/')
    if r == SP: return 'superpowers-readme'
    if r.startswith(SP + '/'):
        sub = r[len(SP) + 1:]
        if sub == 'README.md': return 'superpowers-readme'
        if sub == 'plans' or sub.startswith('plans/'): return 'superpowers-plan'
        if sub == 'specs' or sub.startswith('specs/'): return 'superpowers-spec'
        if sub == 'acceptance/reports/inputs' or sub.startswith('acceptance/reports/inputs/'): return 'superpowers-evidence-input'
        if sub == 'acceptance/reports' or sub.startswith('acceptance/reports/'): return 'superpowers-report'
        return 'superpowers-acceptance-other'
    if r == 'docs' or r.startswith('docs/'): return 'other-docs'
    if '/' not in r and r.lower().endswith('.md'): return 'other-docs'  # root README/AGENTS/etc.
    return 'code-file'


def classify_resolved(rel_res, exists):
    return target_class(rel_res) if exists else 'broken'


def has_glob(s):
    return any(c in s for c in '*?<>{}[]$%') or '...' in s


def resolve_inline(repo, src, raw):
    """Return (kind, resolved_rel or None, exists, anchor_only)."""
    t = raw.strip()
    if t.startswith('<') and t.endswith('>'): t = t[1:-1].strip()
    if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', t) and not re.match(r'^[a-zA-Z]:[\\/]', t):
        return 'external', t, True, False
    if t.startswith('#'): return 'anchor', src, True, True
    path = t.split('#', 1)[0].split('?', 1)[0]
    if path == '': return 'anchor', src, True, True
    path = unquote(path).replace('\\', '/')
    if path.startswith('/'):
        full = os.path.normpath(os.path.join(repo, path.lstrip('/')))
    else:
        full = os.path.normpath(os.path.join(repo, os.path.dirname(src), path))
    rel = os.path.relpath(full, repo).replace(os.sep, '/')
    if rel.startswith('..'): return 'outside', rel, False, False
    return 'local', rel, os.path.exists(full), False


def backtick_candidate(s, top):
    s = s.strip()
    if ' ' in s or '\t' in s or len(s) > 200 or s.startswith(('-', '/', '\\')): return None
    if re.match(r'^[a-zA-Z]:[\\/]', s) or s.startswith(('http:', 'https:', '%', '~', '.\\')): return None
    s2 = s.replace('\\', '/')
    if s2.startswith('./'): s2 = s2[2:]
    if 'superpowers/' in s2 or s2.rstrip('/') == 'superpowers': return s2
    first = s2.split('/', 1)[0]
    if '/' in s2 and first in top: return s2
    if '/' not in s2 and s2.lower().endswith('.md') and s2 in top: return s2
    return None


def resolve_backtick(repo, src, s):
    """Try repo root, docs/, containing dir. Returns (rel, exists, is_glob)."""
    s = re.sub(r':\d+(-\d+)?$', '', s.split('#', 1)[0])
    cands = [s, 'docs/' + s, os.path.join(os.path.dirname(src), s).replace(os.sep, '/')]
    pat = has_glob(s)
    for c in cands:
        full = os.path.normpath(os.path.join(repo, c))
        rel = os.path.relpath(full, repo).replace(os.sep, '/')
        if rel.startswith('..'): continue
        if pat:
            if glob.glob(full.replace('[', '[[]'), recursive=True) or glob.glob(full): return rel, True, True
        elif os.path.exists(full): return rel, True, False
    return os.path.normpath(s).replace(os.sep, '/'), False, pat


def main():
    ap = argparse.ArgumentParser()
    here = os.path.dirname(os.path.abspath(__file__))
    ap.add_argument('--repo', default='C:/Users/lukas/Documents/RND/PLC_Related/tia-portal-mcp')
    ap.add_argument('--out', default=here)
    ap.add_argument('--fail-on-broken', action='store_true')
    a = ap.parse_args()
    repo = os.path.abspath(a.repo)
    top = set(os.listdir(repo)) - {'.git'}
    recs = []
    for rel in sorted(walk_md(repo)):
        sc = source_class(rel)
        with open(os.path.join(repo, rel), encoding='utf-8', errors='replace') as fh:
            lines = fh.read().split('\n')
        fence = None
        for i, line in enumerate(lines, 1):
            m = FENCE.match(line)
            if m:
                tok = m.group(1)[0]
                if fence is None: fence = tok
                elif fence == tok: fence = None
                continue
            infence = fence is not None
            found = []  # (kind, raw)
            masked = line
            # inline-code spans are scanned separately for backtick paths; links inside code spans still counted as links
            for m in INLINE.finditer(line):
                found.append(('link', m.group(2)))
            m = REFDEF.match(line)
            if m: found.append(('refdef', m.group(1)))
            for m in AUTOLINK.finditer(line):
                found.append(('autolink', m.group(1)))
            for raw_kind, raw in found:
                kind, res, exists, anch = resolve_inline(repo, rel, raw)
                if kind == 'external': tc = 'external-url'
                elif kind == 'anchor': tc = 'self-anchor'
                elif kind == 'outside': tc = 'broken'
                else: tc = classify_resolved(res, exists)
                recs.append(dict(source=rel, source_class=sc, line=i, syntax=raw_kind, raw_target=raw,
                                 resolved=res if kind != 'external' else None, target_class=tc,
                                 has_anchor=('#' in raw and not raw.strip().startswith('#')), in_fence=infence))
            for m in BACKTICK.finditer(line):
                cand = backtick_candidate(m.group(1), top)
                if not cand: continue
                res, exists, pat = resolve_backtick(repo, rel, cand)
                recs.append(dict(source=rel, source_class=sc, line=i, syntax='backtick', raw_target=m.group(1),
                                 resolved=res, target_class=classify_resolved(res, exists),
                                 has_anchor='#' in cand, in_fence=infence, glob=pat))
    outd = a.out
    with open(os.path.join(outd, 'link-inventory.json'), 'w', encoding='utf-8') as f:
        json.dump(recs, f, indent=1, ensure_ascii=False)
    srcs = ['root-md', 'AGENTS.md', 'docs-top-level', 'docs-guides', 'docs-development', 'docs-roadmap',
            'docs-SupportedOperations', 'superpowers-plan', 'superpowers-spec', 'superpowers-report',
            'superpowers-acceptance-other', 'superpowers-evidence', 'superpowers-readme', 'other-md']
    tgts = ['superpowers-plan', 'superpowers-spec', 'superpowers-report', 'superpowers-evidence-input',
            'superpowers-acceptance-other', 'superpowers-readme', 'other-docs', 'external-url', 'code-file',
            'self-anchor', 'broken']
    for variant, flt in (('link-matrix.csv', lambda r: not r['in_fence']),
                         ('link-matrix-infence.csv', lambda r: r['in_fence'])):
        cnt = defaultdict(Counter)
        for r in recs:
            if flt(r): cnt[r['source_class']][r['target_class']] += 1
        with open(os.path.join(outd, variant), 'w', newline='', encoding='utf-8') as f:
            w = csv.writer(f)
            w.writerow(['source_class\\target_class'] + tgts + ['TOTAL'])
            for s in srcs:
                row = [cnt[s][t] for t in tgts]
                w.writerow([s] + row + [sum(row)])
            tot = [sum(cnt[s][t] for s in srcs) for t in tgts]
            w.writerow(['TOTAL'] + tot + [sum(tot)])
    broken = [r for r in recs if r['target_class'] == 'broken' and not r['in_fence']]
    print(f'{len(recs)} records, {len(broken)} broken (non-fenced)')
    if a.fail_on_broken and broken:
        for r in broken: print(f"{r['source']}:{r['line']} -> {r['raw_target']}")
        sys.exit(1)


if __name__ == '__main__':
    main()
