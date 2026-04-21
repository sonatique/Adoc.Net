#!/usr/bin/env python3
"""roff/man-page line-based structural diff.

Compares two roff sources after light normalisation:
- comment lines (starting with `.\\"`) are dropped
- the `.TH` line is normalised by stripping the date field (varies by run)
- runs of blank lines are collapsed to one
- trailing whitespace is stripped per line

Usage:
    python man-diff.py <reference.man> <candidate.man> [--out FILE]

Outputs:
    man-diff-out/normalized.diff   unified diff of normalised roff
    man-diff-out/_summary.md       macro-count summary
"""
from __future__ import annotations

import argparse
import difflib
import re
import sys
from collections import Counter
from pathlib import Path


COMMENT_RE = re.compile(r'^\.\\"')
TH_RE = re.compile(r'^\.TH\s+("[^"]*"|\S+)\s+("[^"]*"|\S+)\s+("[^"]*"|\S+)\s+(.*)$')


def normalise(src: str) -> str:
    out: list[str] = []
    prev_blank = False
    for raw in src.splitlines():
        line = raw.rstrip()
        if COMMENT_RE.match(line):
            continue  # drop generator comments
        if line.startswith(".TH"):
            m = TH_RE.match(line)
            if m:
                # Replace the date (3rd field) with a fixed token.
                line = f'.TH {m.group(1)} {m.group(2)} "<DATE>" {m.group(4)}'
        is_blank = (line == "")
        if is_blank and prev_blank:
            continue
        out.append(line)
        prev_blank = is_blank
    return "\n".join(out) + "\n"


def collect_stats(src: str) -> dict:
    macro_counts: Counter[str] = Counter()
    total_lines = 0
    for raw in src.splitlines():
        line = raw.strip()
        if not line:
            continue
        total_lines += 1
        if line.startswith("."):
            macro = line.split(None, 1)[0]  # e.g. ".SH"
            macro_counts[macro] += 1
    return {"macros": macro_counts, "lines": total_lines}


def write_summary(out_dir: Path, ref_path: Path, cand_path: Path,
                  ref_stats: dict, cand_stats: dict, diff_lines: int) -> None:
    lines: list[str] = ["# Man Diff Summary", ""]
    lines.append(f"- Reference: `{ref_path}` ({ref_path.stat().st_size} bytes)")
    lines.append(f"- Candidate: `{cand_path}` ({cand_path.stat().st_size} bytes)")
    lines.append("")

    lines.append("## Macro counts")
    lines.append("")
    lines.append("| macro | reference | candidate | delta |")
    lines.append("|---|---|---|---|")
    ref_m, cand_m = ref_stats["macros"], cand_stats["macros"]
    has_diff = False
    for m in sorted(set(ref_m) | set(cand_m)):
        r = ref_m.get(m, 0)
        c = cand_m.get(m, 0)
        if r != c:
            lines.append(f"| `{m}` | {r} | {c} | {c - r:+d} |")
            has_diff = True
    if not has_diff:
        lines.append("| (no differences) | | | |")
    lines.append("")

    lines.append("## Macros only in reference")
    lines.append("")
    for m in sorted(set(ref_m) - set(cand_m)):
        lines.append(f"- `{m}` ({ref_m[m]} usages)")
    lines.append("")

    lines.append("## Macros only in candidate")
    lines.append("")
    for m in sorted(set(cand_m) - set(ref_m)):
        lines.append(f"- `{m}` ({cand_m[m]} usages)")
    lines.append("")

    lines.append(f"## Normalised line count: ref={ref_stats['lines']}, cand={cand_stats['lines']}")
    lines.append(f"## Normalised diff size: {diff_lines} lines")
    lines.append("")

    (out_dir / "_summary.md").write_text("\n".join(lines), encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--out", type=Path, default=Path("man-diff-out"))
    args = parser.parse_args(argv)

    if not args.reference.exists() or not args.candidate.exists():
        print("error: one of the inputs does not exist", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.glob("*"):
        if old.is_file():
            old.unlink()

    ref_norm = normalise(args.reference.read_text(encoding="utf-8"))
    cand_norm = normalise(args.candidate.read_text(encoding="utf-8"))
    (args.out / "ref.normalised.man").write_text(ref_norm, encoding="utf-8")
    (args.out / "cand.normalised.man").write_text(cand_norm, encoding="utf-8")

    diff_lines = list(difflib.unified_diff(
        ref_norm.splitlines(keepends=True),
        cand_norm.splitlines(keepends=True),
        fromfile=f"REF/{args.reference.name}",
        tofile=f"CAND/{args.candidate.name}",
        n=3,
    ))
    diff_text = "".join(diff_lines)
    (args.out / "normalized.diff").write_text(diff_text, encoding="utf-8")

    ref_stats = collect_stats(ref_norm)
    cand_stats = collect_stats(cand_norm)
    write_summary(args.out, args.reference, args.candidate,
                  ref_stats, cand_stats, len(diff_lines))

    print(f"Reference macros: {sum(ref_stats['macros'].values())}")
    print(f"Candidate macros: {sum(cand_stats['macros'].values())}")
    print(f"Normalised diff lines: {len(diff_lines)}")
    print(f"Output: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
