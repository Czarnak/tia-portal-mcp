#!/usr/bin/env python3
"""Rewrite markdown links whose target is listed in link-map.json (one-off, not committed to scripts/).

Usage:
  python -I rewrite_links.py --repo . --map link-map.json --tag docs-archive-2026-10-30 [--apply] > rewrites.txt

link-map.json keys are repo-relative paths of REMOVED files or directories (no trailing slash); values are
  {"record": "docs/adr/records/<file>"}   kept original (wins over "adr")
  {"adr": "docs/adr/NNNN-<slug>.md"}      ADR that covers it
  {"tag": true}                           no replacement in the tree: link to the archive tag

Policy: README.md always gets an absolute https://github.com/Czarnak/tia-portal-mcp/blob/main/... URL for
record/adr targets. Anything under docs/superpowers/acceptance/ (reports, inputs) links ADR-covered targets to
the tag instead of the ADR. Links inside fenced code and inline code are left alone. Line endings are preserved.
"""
import argparse
import json
import os
import re
import subprocess

INLINE = re.compile(r"(\[(?:[^\[\]]|\[[^\]]*\])*\]\()(\s*)(<[^>]*>|[^()\s]*(?:\([^()\s]*\)[^()\s]*)*)")
CODE_SPAN = re.compile(r"`[^`\n]*`")
FENCE = re.compile(r"^\s{0,3}(```+|~~~+)")
BASE = "https://github.com/Czarnak/tia-portal-mcp/"
SKIP_SOURCES = ("docs/superpowers/plans/", "docs/superpowers/specs/", "docs/superpowers/evidence/", "docs/adr/records/")


def replacement(rel, repo, tag, entry, resolved, anchor):
    folder = os.path.dirname(rel)
    keep_anchor = f"#{anchor}" if anchor else ""
    in_acceptance = rel.startswith("docs/superpowers/acceptance/")
    target = entry.get("record") or (None if in_acceptance else entry.get("adr"))
    if target:
        if rel == "README.md":
            return f"{BASE}blob/main/{target}" + (keep_anchor if entry.get("record") else "")
        link = os.path.relpath(target, folder or ".").replace(os.sep, "/")
        return link + (keep_anchor if entry.get("record") else "")
    kind = "tree" if os.path.isdir(os.path.join(repo, resolved)) else "blob"
    return f"{BASE}{kind}/{tag}/{resolved}{keep_anchor}"


def rewrite_text(rel, text, repo, tag, mapping, log):
    out, fence = [], None
    for line in text.splitlines(keepends=True):
        marker = FENCE.match(line)
        if marker:
            fence = None if fence == marker.group(1)[0] else (fence or marker.group(1)[0])
            out.append(line)
            continue
        if fence:
            out.append(line)
            continue
        spans = []

        def mask(match):
            spans.append(match.group(0))
            return f"\x00{len(spans) - 1}\x00"

        masked = CODE_SPAN.sub(mask, line)

        def sub(match):
            raw = match.group(3).strip("<>")
            if re.match(r"^[a-z][a-z0-9+.-]*:", raw, re.I) or raw.startswith("#"):
                return match.group(0)
            target, _, anchor = raw.partition("#")
            target = target.split("?", 1)[0]
            resolved = os.path.normpath(os.path.join(os.path.dirname(rel), target)).replace(os.sep, "/").rstrip("/")
            entry = mapping.get(resolved)
            if entry is None:
                return match.group(0)
            new = replacement(rel, repo, tag, entry, resolved, anchor)
            log.append(f"{rel}: {raw} -> {new}")
            return match.group(1) + match.group(2) + new

        rewritten = INLINE.sub(sub, masked)
        out.append(re.sub("\x00(\\d+)\x00", lambda m: spans[int(m.group(1))], rewritten))
    return "".join(out)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=".")
    parser.add_argument("--map", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    with open(args.map, encoding="utf-8") as handle:
        mapping = json.load(handle)
    listing = subprocess.run(["git", "-C", args.repo, "ls-files", "-co", "--exclude-standard", "--", "*.md"],
                             capture_output=True, text=True, check=True).stdout.split()
    log, changed = [], 0
    for rel in sorted(set(listing)):
        if rel.startswith(SKIP_SOURCES):
            continue
        path = os.path.join(args.repo, rel)
        with open(path, encoding="utf-8", newline="") as handle:
            text = handle.read()
        updated = rewrite_text(rel, text, args.repo, args.tag, mapping, log)
        if updated != text:
            changed += 1
            if args.apply:
                with open(path, "w", encoding="utf-8", newline="") as handle:
                    handle.write(updated)
    print("\n".join(log))
    print(f"{len(log)} link(s) in {changed} file(s) {'rewritten' if args.apply else 'would change'}")


if __name__ == "__main__":
    main()
