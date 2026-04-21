#!/usr/bin/env python3
"""Reveal.js HTML structural diff tool.

Compares the slide structure (the `<div class="slides">` subtree) of two
Reveal.js HTML documents. Reuses the canonical-DOM dump approach from
html-diff.py but scopes the comparison to the slide skeleton, ignoring
the surrounding CDN bootstrap (scripts, themes, plugin init).

Usage:
    python revealjs-diff.py <reference.html> <candidate.html> [--out FILE]

Outputs:
    revealjs-diff-out/dom.diff      unified diff of canonical slide DOM
    revealjs-diff-out/_summary.md   structural summary (section/header counts)
"""
from __future__ import annotations

import argparse
import difflib
import sys
from collections import Counter
from pathlib import Path

from bs4 import BeautifulSoup, NavigableString, Tag

IGNORED_TAGS = {"style", "script", "link", "meta"}
IGNORED_ATTRS = {"style", "data-lang", "rel", "tabindex"}


def load_slides(path: Path) -> Tag | None:
    """Return the `<div class="slides">` element from the HTML, or None."""
    soup = BeautifulSoup(path.read_text(encoding="utf-8"), "lxml")
    return soup.find("div", class_="slides")


def strip_noise(root: Tag) -> None:
    for tag in root.find_all(True):
        if tag.name in IGNORED_TAGS:
            tag.decompose()
            continue
        for a in list(tag.attrs):
            if a in IGNORED_ATTRS:
                del tag.attrs[a]
        if "class" in tag.attrs:
            classes = tag.attrs["class"]
            if isinstance(classes, list):
                tag.attrs["class"] = sorted(classes)
            else:
                tag.attrs["class"] = sorted(str(classes).split())


def canonical_dump(root: Tag) -> str:
    lines: list[str] = []
    _walk(root, depth=0, lines=lines)
    return "\n".join(lines) + "\n"


def _walk(node, depth: int, lines: list[str]) -> None:
    indent = "  " * depth
    if isinstance(node, NavigableString):
        text = str(node).strip()
        if text:
            lines.append(f"{indent}{_truncate(text)!r}")
        return
    if not isinstance(node, Tag):
        return
    attr_parts = []
    for k in sorted(node.attrs.keys()):
        v = node.attrs[k]
        if isinstance(v, list):
            v = " ".join(v)
        attr_parts.append(f'{k}="{v}"')
    attr_str = (" " + " ".join(attr_parts)) if attr_parts else ""
    lines.append(f"{indent}<{node.name}{attr_str}>")
    for child in node.children:
        _walk(child, depth + 1, lines)
    lines.append(f"{indent}</{node.name}>")


def _truncate(text: str, limit: int = 200) -> str:
    return text if len(text) <= limit else text[:limit] + "…"


def collect_stats(root: Tag) -> dict:
    tag_counts: Counter[str] = Counter()
    class_counts: Counter[str] = Counter()
    id_set: set[str] = set()
    section_depths: Counter[int] = Counter()
    for el in root.find_all(True):
        tag_counts[el.name] += 1
        classes = el.attrs.get("class") or []
        if isinstance(classes, str):
            classes = classes.split()
        for c in classes:
            class_counts[c] += 1
        if "id" in el.attrs:
            id_set.add(str(el.attrs["id"]))
        if el.name == "section":
            depth = 0
            p = el.parent
            while p is not None and p is not root:
                if p.name == "section":
                    depth += 1
                p = p.parent
            section_depths[depth] += 1
    return {
        "tags": tag_counts,
        "classes": class_counts,
        "ids": id_set,
        "section_depths": section_depths,
    }


def write_summary(out_dir: Path, ref_path: Path, cand_path: Path,
                  ref_stats: dict, cand_stats: dict, dom_diff: str) -> None:
    lines: list[str] = ["# Reveal.js Diff Summary", ""]
    lines.append(f"- Reference: `{ref_path}`")
    lines.append(f"- Candidate: `{cand_path}`")
    lines.append("")

    lines.append("## Section depth distribution")
    lines.append("")
    lines.append("| depth | reference | candidate |")
    lines.append("|---|---|---|")
    rd, cd = ref_stats["section_depths"], cand_stats["section_depths"]
    for d in sorted(set(rd) | set(cd)):
        lines.append(f"| {d} | {rd.get(d, 0)} | {cd.get(d, 0)} |")
    lines.append("")

    ref_tags, cand_tags = ref_stats["tags"], cand_stats["tags"]
    lines.append("## Tag counts (slides subtree)")
    lines.append("")
    lines.append("| tag | reference | candidate | delta |")
    lines.append("|---|---|---|---|")
    has_diff = False
    for tag in sorted(set(ref_tags) | set(cand_tags)):
        r = ref_tags.get(tag, 0)
        c = cand_tags.get(tag, 0)
        if r != c:
            lines.append(f"| `{tag}` | {r} | {c} | {c - r:+d} |")
            has_diff = True
    if not has_diff:
        lines.append("| (no differences) | | | |")
    lines.append("")

    ref_classes, cand_classes = ref_stats["classes"], cand_stats["classes"]
    lines.append("## Classes only in reference")
    lines.append("")
    for c in sorted(set(ref_classes) - set(cand_classes)):
        lines.append(f"- `.{c}` ({ref_classes[c]} usages)")
    lines.append("")

    lines.append("## Classes only in candidate")
    lines.append("")
    for c in sorted(set(cand_classes) - set(ref_classes)):
        lines.append(f"- `.{c}` ({cand_classes[c]} usages)")
    lines.append("")

    ref_ids, cand_ids = ref_stats["ids"], cand_stats["ids"]
    lines.append("## IDs only in reference")
    for i in sorted(ref_ids - cand_ids):
        lines.append(f"- `#{i}`")
    lines.append("")

    lines.append("## IDs only in candidate")
    for i in sorted(cand_ids - ref_ids):
        lines.append(f"- `#{i}`")
    lines.append("")

    lines.append(f"## DOM diff size: {len(dom_diff.splitlines())} lines")
    lines.append("")

    (out_dir / "_summary.md").write_text("\n".join(lines), encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--out", type=Path, default=Path("revealjs-diff-out"))
    args = parser.parse_args(argv)

    if not args.reference.exists() or not args.candidate.exists():
        print("error: one of the inputs does not exist", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("*"):
        if old.is_file():
            old.unlink()

    ref_root = load_slides(args.reference)
    cand_root = load_slides(args.candidate)
    if ref_root is None:
        print("error: reference has no <div class=\"slides\">", file=sys.stderr)
        return 2
    if cand_root is None:
        print("error: candidate has no <div class=\"slides\">", file=sys.stderr)
        return 2

    strip_noise(ref_root)
    strip_noise(cand_root)

    ref_dump = canonical_dump(ref_root)
    cand_dump = canonical_dump(cand_root)
    (args.out / "ref.dump").write_text(ref_dump, encoding="utf-8")
    (args.out / "cand.dump").write_text(cand_dump, encoding="utf-8")

    diff_lines = list(difflib.unified_diff(
        ref_dump.splitlines(keepends=True),
        cand_dump.splitlines(keepends=True),
        fromfile=f"REF/{args.reference.name}",
        tofile=f"CAND/{args.candidate.name}",
        n=3,
    ))
    diff_text = "".join(diff_lines)
    (args.out / "dom.diff").write_text(diff_text, encoding="utf-8")

    ref_stats = collect_stats(ref_root)
    cand_stats = collect_stats(cand_root)
    write_summary(args.out, args.reference, args.candidate,
                  ref_stats, cand_stats, diff_text)

    print(f"Reference slide tags: {sum(ref_stats['tags'].values())}")
    print(f"Candidate slide tags: {sum(cand_stats['tags'].values())}")
    print(f"Slide DOM diff lines: {len(diff_lines)}")
    print(f"Output: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
