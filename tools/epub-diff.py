#!/usr/bin/env python3
"""EPUB structural diff tool.

Compares two EPUB files (which are zip containers of XHTML, OPF, NCX and
assets). For every part present in either side, normalise whitespace and
attribute order, then emit a unified diff. Intended to surface gaps between
AdocNet and asciidoctor-epub3 output for the same input document, the same
way pdf-visual-diff.py works for PDF.

Usage:
    python epub-diff.py <reference.epub> <candidate.epub> [--out DIR]

Outputs:
    epub-diff-out/<part-path>.diff   per-part diffs (only when content differs)
    epub-diff-out/_summary.md        list of parts only-in-each side and parts that differ
"""
from __future__ import annotations

import argparse
import difflib
import re
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET


XML_LIKE = {".xhtml", ".html", ".xml", ".opf", ".ncx", ".xml.en"}
TEXT_LIKE = XML_LIKE | {".css", ".txt", ".md"}


def load_parts(path: Path) -> dict[str, bytes]:
    """Return dict of {archive-relative-path -> raw bytes} for every part in the EPUB."""
    parts: dict[str, bytes] = {}
    with zipfile.ZipFile(path) as zf:
        for info in zf.infolist():
            if info.is_dir():
                continue
            parts[info.filename] = zf.read(info.filename)
    return parts


def is_text_like(name: str) -> bool:
    suffix = Path(name).suffix.lower()
    return suffix in TEXT_LIKE


def is_xml_like(name: str) -> bool:
    suffix = Path(name).suffix.lower()
    return suffix in XML_LIKE


def canonicalize_xml(content: bytes) -> str:
    """Parse + re-emit XML with sorted attributes and normalised whitespace.

    Returns a multiline pretty-printed string. On parse errors, falls back to
    the raw decoded text — better to diff raw than to skip the file entirely.
    """
    try:
        # ElementTree drops namespaces if we don't use ET.parse; but for diff
        # purposes namespace stripping actually helps comparison. Keep it.
        text = content.decode("utf-8", errors="replace")
        # Strip the XML declaration, doctype, and BOM for stable comparison
        text = re.sub(r"^\ufeff", "", text)
        text = re.sub(r"<\?xml[^?]*\?>\s*", "", text)
        text = re.sub(r"<!DOCTYPE[^>]*>\s*", "", text)
        root = ET.fromstring(text)
        _sort_attrs_recursive(root)
        try:
            ET.indent(root, space="  ")
        except AttributeError:
            pass  # Python < 3.9
        return ET.tostring(root, encoding="unicode")
    except ET.ParseError:
        return content.decode("utf-8", errors="replace")


def _sort_attrs_recursive(elem: ET.Element) -> None:
    if elem.attrib:
        sorted_attrs = dict(sorted(elem.attrib.items()))
        elem.attrib.clear()
        elem.attrib.update(sorted_attrs)
    for child in elem:
        _sort_attrs_recursive(child)


def normalise_text(content: bytes) -> str:
    """For non-XML text parts (CSS, plain text), normalise line endings + collapse trailing whitespace."""
    text = content.decode("utf-8", errors="replace")
    # Normalise line endings
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    # Strip trailing whitespace per line
    text = "\n".join(line.rstrip() for line in text.split("\n"))
    # Drop trailing empty lines
    text = text.rstrip() + "\n"
    return text


def diff_parts(name: str, ref_bytes: bytes, cand_bytes: bytes) -> str | None:
    """Return a unified diff string if the parts differ in normalised form, else None."""
    if is_xml_like(name):
        ref_text = canonicalize_xml(ref_bytes)
        cand_text = canonicalize_xml(cand_bytes)
    elif is_text_like(name):
        ref_text = normalise_text(ref_bytes)
        cand_text = normalise_text(cand_bytes)
    else:
        # Binary part: just compare bytes equality, return one-line note if different
        if ref_bytes == cand_bytes:
            return None
        return f"BINARY DIFFERS: {name} (ref={len(ref_bytes)}B, cand={len(cand_bytes)}B)\n"

    if ref_text == cand_text:
        return None

    lines = list(difflib.unified_diff(
        ref_text.splitlines(keepends=True),
        cand_text.splitlines(keepends=True),
        fromfile=f"REF/{name}",
        tofile=f"CAND/{name}",
        n=3,
    ))
    return "".join(lines)


def safe_filename(part: str) -> str:
    """Map an archive path to a filesystem-safe filename for the diff output."""
    return part.replace("/", "__").replace("\\", "__")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path, help="Reference EPUB (e.g. asciidoctor output)")
    parser.add_argument("candidate", type=Path, help="Candidate EPUB (e.g. AdocNet output)")
    parser.add_argument("--out", type=Path, default=Path("epub-diff-out"),
                        help="Output directory for per-part diffs and summary (default: ./epub-diff-out)")
    args = parser.parse_args(argv)

    if not args.reference.exists():
        print(f"error: reference EPUB not found: {args.reference}", file=sys.stderr)
        return 2
    if not args.candidate.exists():
        print(f"error: candidate EPUB not found: {args.candidate}", file=sys.stderr)
        return 2

    ref_parts = load_parts(args.reference)
    cand_parts = load_parts(args.candidate)

    args.out.mkdir(parents=True, exist_ok=True)
    # Clear previous run
    for old in args.out.glob("*"):
        if old.is_file():
            old.unlink()

    only_ref = sorted(set(ref_parts) - set(cand_parts))
    only_cand = sorted(set(cand_parts) - set(ref_parts))
    common = sorted(set(ref_parts) & set(cand_parts))

    differing: list[tuple[str, int]] = []
    for part in common:
        diff_text = diff_parts(part, ref_parts[part], cand_parts[part])
        if diff_text:
            out_path = args.out / f"{safe_filename(part)}.diff"
            out_path.write_text(diff_text, encoding="utf-8")
            differing.append((part, len(diff_text)))

    summary_lines: list[str] = ["# EPUB Diff Summary", ""]
    summary_lines.append(f"- Reference: `{args.reference}` ({len(ref_parts)} parts, {sum(len(b) for b in ref_parts.values())} bytes)")
    summary_lines.append(f"- Candidate: `{args.candidate}` ({len(cand_parts)} parts, {sum(len(b) for b in cand_parts.values())} bytes)")
    summary_lines.append("")

    summary_lines.append(f"## Parts only in reference ({len(only_ref)})")
    for p in only_ref:
        summary_lines.append(f"- `{p}` ({len(ref_parts[p])} bytes)")
    summary_lines.append("")

    summary_lines.append(f"## Parts only in candidate ({len(only_cand)})")
    for p in only_cand:
        summary_lines.append(f"- `{p}` ({len(cand_parts[p])} bytes)")
    summary_lines.append("")

    summary_lines.append(f"## Parts present in both but differing ({len(differing)})")
    for p, sz in differing:
        summary_lines.append(f"- `{p}` -> `{safe_filename(p)}.diff` ({sz} byte diff)")
    summary_lines.append("")

    identical_count = len(common) - len(differing)
    summary_lines.append(f"## Parts identical in both ({identical_count})")
    summary_lines.append("")

    (args.out / "_summary.md").write_text("\n".join(summary_lines), encoding="utf-8")

    print(f"Reference parts: {len(ref_parts)}")
    print(f"Candidate parts: {len(cand_parts)}")
    print(f"Only in reference: {len(only_ref)}")
    print(f"Only in candidate: {len(only_cand)}")
    print(f"Common: {len(common)} ({len(differing)} differ, {identical_count} identical)")
    print(f"Output: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
