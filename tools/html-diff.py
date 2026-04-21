#!/usr/bin/env python3
"""HTML structural diff tool.

Compares two HTML documents structurally — DOM tree shape, classes, ids,
attributes — while ignoring inline CSS, whitespace, attribute order, and
generator-specific noise. Designed to surface real semantic differences
between AdocNet and asciidoctor HTML output for the same input document,
the same way pdf-visual-diff.py works for PDF and epub-diff.py works for EPUB.

Usage:
    python html-diff.py <reference.html> <candidate.html> [--out FILE]

Outputs:
    html-diff-out/dom.diff      unified diff of canonical DOM dumps
    html-diff-out/_summary.md   structural summary (element counts, class usage)
"""
from __future__ import annotations

import argparse
import difflib
import sys
from collections import Counter
from pathlib import Path

from bs4 import BeautifulSoup, NavigableString, Tag

# Tags whose content is presentational / generator-specific and not part of the
# semantic DOM we're comparing. Stripped before diffing.
IGNORED_TAGS = {"style", "script", "link", "meta"}

# Attributes that are presentational, generator-specific, or vary harmlessly
# between renderers. Stripped from every element before diffing.
IGNORED_ATTRS = {
    "style",        # inline CSS — themed differently per renderer
    "data-lang",    # asciidoctor-specific code-block hint, harmless variation
    "rel",          # link rel often differs (noopener etc.) — separate concern
    "tabindex",     # accessibility hint, harmless
}


def load_doc(path: Path) -> BeautifulSoup:
    text = path.read_text(encoding="utf-8")
    return BeautifulSoup(text, "lxml")


def strip_noise(soup: BeautifulSoup) -> None:
    """Remove non-semantic noise from the parsed tree in-place."""
    for tag in soup.find_all(True):
        if tag.name in IGNORED_TAGS:
            tag.decompose()
            continue
        # Drop ignored attributes
        for a in list(tag.attrs):
            if a in IGNORED_ATTRS:
                del tag.attrs[a]
        # Sort class lists for stable comparison
        if "class" in tag.attrs:
            classes = tag.attrs["class"]
            if isinstance(classes, list):
                tag.attrs["class"] = sorted(classes)
            else:
                tag.attrs["class"] = sorted(str(classes).split())


def canonical_dump(soup: BeautifulSoup) -> str:
    """Render the soup as a canonical newline-per-tag string for unified diff."""
    body = soup.find("body")
    root = body if body else soup
    lines: list[str] = []
    _walk(root, depth=0, lines=lines)
    return "\n".join(lines) + "\n"


def _walk(node: Tag | NavigableString, depth: int, lines: list[str]) -> None:
    indent = "  " * depth
    if isinstance(node, NavigableString):
        text = str(node).strip()
        if text:
            lines.append(f"{indent}{_truncate(text)!r}")
        return
    if not isinstance(node, Tag):
        return

    # Build start tag with sorted attributes
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


def collect_stats(soup: BeautifulSoup) -> dict:
    body = soup.find("body") or soup
    tag_counts: Counter[str] = Counter()
    class_counts: Counter[str] = Counter()
    id_set: set[str] = set()
    for el in body.find_all(True):
        tag_counts[el.name] += 1
        classes = el.attrs.get("class") or []
        if isinstance(classes, str):
            classes = classes.split()
        for c in classes:
            class_counts[c] += 1
        if "id" in el.attrs:
            id_set.add(str(el.attrs["id"]))
    return {
        "tags": tag_counts,
        "classes": class_counts,
        "ids": id_set,
    }


def write_summary(out_dir: Path, ref_path: Path, cand_path: Path,
                  ref_stats: dict, cand_stats: dict, dom_diff: str) -> None:
    lines: list[str] = ["# HTML Diff Summary", ""]
    lines.append(f"- Reference: `{ref_path}`")
    lines.append(f"- Candidate: `{cand_path}`")
    lines.append("")

    ref_tags, cand_tags = ref_stats["tags"], cand_stats["tags"]
    ref_classes, cand_classes = ref_stats["classes"], cand_stats["classes"]
    ref_ids, cand_ids = ref_stats["ids"], cand_stats["ids"]

    lines.append("## Tag counts (body)")
    lines.append("")
    lines.append("| tag | reference | candidate | delta |")
    lines.append("|---|---|---|---|")
    all_tags = sorted(set(ref_tags) | set(cand_tags))
    for tag in all_tags:
        r = ref_tags.get(tag, 0)
        c = cand_tags.get(tag, 0)
        if r != c:
            lines.append(f"| `{tag}` | {r} | {c} | {c - r:+d} |")
    lines.append("")

    lines.append("## Classes only in reference")
    lines.append("")
    only_ref_classes = sorted(set(ref_classes) - set(cand_classes))
    for c in only_ref_classes:
        lines.append(f"- `.{c}` ({ref_classes[c]} usages)")
    lines.append("")

    lines.append("## Classes only in candidate")
    lines.append("")
    only_cand_classes = sorted(set(cand_classes) - set(ref_classes))
    for c in only_cand_classes:
        lines.append(f"- `.{c}` ({cand_classes[c]} usages)")
    lines.append("")

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
    parser.add_argument("reference", type=Path, help="Reference HTML (e.g. asciidoctor output)")
    parser.add_argument("candidate", type=Path, help="Candidate HTML (e.g. AdocNet output)")
    parser.add_argument("--out", type=Path, default=Path("html-diff-out"),
                        help="Output directory (default: ./html-diff-out)")
    args = parser.parse_args(argv)

    if not args.reference.exists():
        print(f"error: reference HTML not found: {args.reference}", file=sys.stderr)
        return 2
    if not args.candidate.exists():
        print(f"error: candidate HTML not found: {args.candidate}", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("*"):
        if old.is_file():
            old.unlink()

    ref_soup = load_doc(args.reference)
    cand_soup = load_doc(args.candidate)

    strip_noise(ref_soup)
    strip_noise(cand_soup)

    ref_stats = collect_stats(ref_soup)
    cand_stats = collect_stats(cand_soup)

    ref_dump = canonical_dump(ref_soup)
    cand_dump = canonical_dump(cand_soup)

    diff_lines = list(difflib.unified_diff(
        ref_dump.splitlines(keepends=True),
        cand_dump.splitlines(keepends=True),
        fromfile=f"REF/{args.reference.name}",
        tofile=f"CAND/{args.candidate.name}",
        n=3,
    ))
    dom_diff = "".join(diff_lines)
    (args.out / "dom.diff").write_text(dom_diff, encoding="utf-8")
    (args.out / "ref.dump").write_text(ref_dump, encoding="utf-8")
    (args.out / "cand.dump").write_text(cand_dump, encoding="utf-8")

    write_summary(args.out, args.reference, args.candidate, ref_stats, cand_stats, dom_diff)

    print(f"Reference body tags: {sum(ref_stats['tags'].values())}")
    print(f"Candidate body tags: {sum(cand_stats['tags'].values())}")
    print(f"DOM diff lines: {len(diff_lines)}")
    print(f"Output: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
