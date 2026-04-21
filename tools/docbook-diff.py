#!/usr/bin/env python3
"""DocBook XML structural diff tool.

Compares two DocBook XML documents structurally — element tree, attributes,
text content — using XML canonicalisation (C14N) to ignore attribute order,
whitespace-only differences, and empty-element-vs-paired-tag noise.

Usage:
    python docbook-diff.py <reference.xml> <candidate.xml> [--out FILE]

Outputs:
    docbook-diff-out/canonical.diff   unified diff of canonicalised XML
    docbook-diff-out/_summary.md      tag/attribute summary
"""
from __future__ import annotations

import argparse
import difflib
import sys
from collections import Counter
from io import BytesIO
from pathlib import Path
from xml.etree import ElementTree as ET


def parse(path: Path) -> ET.ElementTree:
    return ET.parse(str(path))


def canonicalize(tree: ET.ElementTree) -> str:
    """Emit canonical XML form: sorted attributes, normalised whitespace, no doctype."""
    buf = BytesIO()
    ET.canonicalize(from_file=str(_path_for(tree)), out=buf, strip_text=False)
    text = buf.getvalue().decode("utf-8")
    # Pretty-print for line-by-line diff readability
    return _pretty(text)


def _path_for(tree: ET.ElementTree) -> Path:
    # Workaround: ET.canonicalize wants either a string source, a filename,
    # or an ElementTree-returning iterator. Easier to round-trip through a string.
    raise RuntimeError("not used — see canonicalize_file")


def canonicalize_file(path: Path) -> str:
    # ET.canonicalize wants a text-mode writer. Use StringIO and decode-on-read source.
    from io import StringIO
    src = path.read_text(encoding="utf-8")
    out = StringIO()
    ET.canonicalize(xml_data=src, out=out, strip_text=True)
    return _pretty(out.getvalue())


def _pretty(canonical: str) -> str:
    """Insert one line per element so unified diff lands on meaningful boundaries."""
    # Re-parse canonical output and pretty-print via ET.indent.
    root = ET.fromstring(canonical)
    try:
        ET.indent(root, space="  ")
    except AttributeError:
        pass
    return ET.tostring(root, encoding="unicode") + "\n"


def collect_stats(path: Path) -> dict:
    tag_counts: Counter[str] = Counter()
    attr_counts: Counter[str] = Counter()
    id_set: set[str] = set()
    for _, el in ET.iterparse(str(path), events=("end",)):
        # Strip namespace for stat purposes
        tag = el.tag.rsplit("}", 1)[-1]
        tag_counts[tag] += 1
        for k in el.attrib:
            ka = k.rsplit("}", 1)[-1]
            attr_counts[ka] += 1
            if ka in ("id", "{http://www.w3.org/XML/1998/namespace}id"):
                id_set.add(el.attrib[k])
    return {"tags": tag_counts, "attrs": attr_counts, "ids": id_set}


def write_summary(out_dir: Path, ref_path: Path, cand_path: Path,
                  ref_stats: dict, cand_stats: dict, diff_lines: int) -> None:
    lines: list[str] = ["# DocBook Diff Summary", ""]
    lines.append(f"- Reference: `{ref_path}` ({ref_path.stat().st_size} bytes)")
    lines.append(f"- Candidate: `{cand_path}` ({cand_path.stat().st_size} bytes)")
    lines.append("")

    ref_tags, cand_tags = ref_stats["tags"], cand_stats["tags"]
    ref_attrs, cand_attrs = ref_stats["attrs"], cand_stats["attrs"]
    ref_ids, cand_ids = ref_stats["ids"], cand_stats["ids"]

    lines.append("## Tag counts")
    lines.append("")
    lines.append("| tag | reference | candidate | delta |")
    lines.append("|---|---|---|---|")
    for tag in sorted(set(ref_tags) | set(cand_tags)):
        r = ref_tags.get(tag, 0)
        c = cand_tags.get(tag, 0)
        if r != c:
            lines.append(f"| `{tag}` | {r} | {c} | {c - r:+d} |")
    if all(ref_tags.get(t, 0) == cand_tags.get(t, 0) for t in set(ref_tags) | set(cand_tags)):
        lines.append("| (no differences) | | | |")
    lines.append("")

    lines.append("## Attributes only in reference")
    lines.append("")
    for a in sorted(set(ref_attrs) - set(cand_attrs)):
        lines.append(f"- `{a}` ({ref_attrs[a]} usages)")
    lines.append("")

    lines.append("## Attributes only in candidate")
    lines.append("")
    for a in sorted(set(cand_attrs) - set(ref_attrs)):
        lines.append(f"- `{a}` ({cand_attrs[a]} usages)")
    lines.append("")

    lines.append("## IDs only in reference")
    for i in sorted(ref_ids - cand_ids):
        lines.append(f"- `{i}`")
    lines.append("")

    lines.append("## IDs only in candidate")
    for i in sorted(cand_ids - ref_ids):
        lines.append(f"- `{i}`")
    lines.append("")

    lines.append(f"## Canonical diff size: {diff_lines} lines")
    lines.append("")

    (out_dir / "_summary.md").write_text("\n".join(lines), encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--out", type=Path, default=Path("docbook-diff-out"))
    args = parser.parse_args(argv)

    if not args.reference.exists() or not args.candidate.exists():
        print("error: one of the inputs does not exist", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("*"):
        if old.is_file():
            old.unlink()

    ref_canonical = canonicalize_file(args.reference)
    cand_canonical = canonicalize_file(args.candidate)
    (args.out / "ref.canonical.xml").write_text(ref_canonical, encoding="utf-8")
    (args.out / "cand.canonical.xml").write_text(cand_canonical, encoding="utf-8")

    diff_lines = list(difflib.unified_diff(
        ref_canonical.splitlines(keepends=True),
        cand_canonical.splitlines(keepends=True),
        fromfile=f"REF/{args.reference.name}",
        tofile=f"CAND/{args.candidate.name}",
        n=3,
    ))
    diff_text = "".join(diff_lines)
    (args.out / "canonical.diff").write_text(diff_text, encoding="utf-8")

    ref_stats = collect_stats(args.reference)
    cand_stats = collect_stats(args.candidate)
    write_summary(args.out, args.reference, args.candidate, ref_stats, cand_stats, len(diff_lines))

    print(f"Reference tags: {sum(ref_stats['tags'].values())}")
    print(f"Candidate tags: {sum(cand_stats['tags'].values())}")
    print(f"Canonical diff lines: {len(diff_lines)}")
    print(f"Output: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
