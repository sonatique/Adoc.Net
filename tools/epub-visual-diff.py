#!/usr/bin/env python3
"""EPUB visual diff tool.

Renders the XHTML chapters of two EPUB files via headless Chrome and
produces side-by-side + pixel-diff screenshots for each matching chapter.

Pairs chapters by basename (the EPUB convention for chapter filenames).
Asciidoctor-epub3 uses `EPUB/` as the OEBPS root; AdocNet uses `OEBPS/`.
Both are valid; the tool finds the root by looking for `*.opf`.

Usage:
    python tools/epub-visual-diff.py <reference.epub> <candidate.epub> [--out DIR]

Requires:
    - Pillow (pip install pillow)
    - Google Chrome (auto-detected on Windows; override with --chrome)

Outputs:
    epub-visual-diff-out/<chapter>/ref.png
    epub-visual-diff-out/<chapter>/cand.png
    epub-visual-diff-out/<chapter>/diff.png
    epub-visual-diff-out/<chapter>/side-by-side.png
    epub-visual-diff-out/_summary.md
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

from PIL import Image, ImageChops

WINDOW_W = 816
WINDOW_H = 1200

CHROME_CANDIDATES = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium-browser",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
]


def find_chrome(override: str | None) -> str:
    if override:
        return override
    for c in CHROME_CANDIDATES:
        if Path(c).exists():
            return c
    raise RuntimeError("Chrome not found — pass --chrome /path/to/chrome")


def extract(epub: Path, dest: Path) -> Path:
    with zipfile.ZipFile(epub) as zf:
        zf.extractall(dest)
    # Find the OEBPS root: the directory containing the .opf manifest.
    for opf in dest.rglob("*.opf"):
        return opf.parent
    raise RuntimeError(f"no .opf manifest in {epub}")


def list_chapters(oebps_root: Path) -> dict[str, Path]:
    """Map chapter basename → file path. Skip nav/toc files."""
    out: dict[str, Path] = {}
    for x in sorted(oebps_root.rglob("*.xhtml")):
        name = x.stem
        if name in ("nav", "toc"):
            continue
        out[name] = x
    for x in sorted(oebps_root.rglob("*.html")):
        name = x.stem
        if name in ("nav", "toc"):
            continue
        out.setdefault(name, x)
    return out


def screenshot(chrome: str, src: Path, dest: Path) -> None:
    url = "file:///" + str(src.resolve()).replace("\\", "/")
    abs_dest = dest.resolve()
    cmd = [
        chrome,
        "--headless",
        "--no-sandbox",
        "--disable-gpu",
        f"--window-size={WINDOW_W},{WINDOW_H}",
        "--hide-scrollbars",
        f"--screenshot={abs_dest}",
        url,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if not abs_dest.exists():
        raise RuntimeError(f"chrome failed for {src}: {result.stderr[-500:]}")


def pixel_diff(a: Image.Image, b: Image.Image) -> tuple[Image.Image, int, int]:
    """Return (diff image, changed pixel count, total pixel count)."""
    if a.size != b.size:
        # Pad to common size
        w = max(a.width, b.width)
        h = max(a.height, b.height)
        ax = Image.new("RGB", (w, h), "white")
        bx = Image.new("RGB", (w, h), "white")
        ax.paste(a, (0, 0))
        bx.paste(b, (0, 0))
        a, b = ax, bx
    diff = ImageChops.difference(a.convert("RGB"), b.convert("RGB"))
    bbox = diff.getbbox()
    changed = 0
    total = a.width * a.height
    if bbox:
        # Count pixels where any channel differs
        gray = diff.convert("L")
        for px in gray.getdata():
            if px:
                changed += 1
    return diff, changed, total


def side_by_side(left: Image.Image, right: Image.Image, label_l: str, label_r: str) -> Image.Image:
    gap = 20
    w = left.width + right.width + gap
    h = max(left.height, right.height)
    canvas = Image.new("RGB", (w, h), "white")
    canvas.paste(left, (0, 0))
    canvas.paste(right, (left.width + gap, 0))
    return canvas


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--out", type=Path, default=Path("epub-visual-diff-out"))
    parser.add_argument("--chrome", default=None, help="path to chrome.exe (auto-detected if omitted)")
    args = parser.parse_args(argv)

    if not args.reference.exists() or not args.candidate.exists():
        print("error: one of the inputs does not exist", file=sys.stderr)
        return 2

    chrome = find_chrome(args.chrome)
    args.out.mkdir(parents=True, exist_ok=True)
    for old in args.out.rglob("*"):
        if old.is_file():
            old.unlink()
    for old in sorted(args.out.rglob("*"), reverse=True):
        if old.is_dir() and old != args.out:
            old.rmdir()

    with tempfile.TemporaryDirectory() as td:
        td_path = Path(td)
        ref_root = extract(args.reference, td_path / "ref")
        cand_root = extract(args.candidate, td_path / "cand")

        ref_chapters = list_chapters(ref_root)
        cand_chapters = list_chapters(cand_root)
        common = sorted(set(ref_chapters) & set(cand_chapters))
        only_ref = sorted(set(ref_chapters) - set(cand_chapters))
        only_cand = sorted(set(cand_chapters) - set(ref_chapters))

        print(f"Common chapters: {len(common)}")
        print(f"Only in reference: {only_ref}")
        print(f"Only in candidate: {only_cand}")

        results: list[dict] = []
        for chapter in common:
            ch_dir = args.out / chapter
            ch_dir.mkdir(parents=True, exist_ok=True)
            ref_png = ch_dir / "ref.png"
            cand_png = ch_dir / "cand.png"

            try:
                screenshot(chrome, ref_chapters[chapter], ref_png)
                screenshot(chrome, cand_chapters[chapter], cand_png)
            except Exception as e:
                print(f"  {chapter}: render failed — {e}")
                results.append({"chapter": chapter, "error": str(e)})
                continue

            ref_img = Image.open(ref_png)
            cand_img = Image.open(cand_png)
            diff_img, changed, total = pixel_diff(ref_img, cand_img)
            diff_img.save(ch_dir / "diff.png")
            sbs = side_by_side(ref_img, cand_img, "REF", "CAND")
            sbs.save(ch_dir / "side-by-side.png")

            pct = (100.0 * changed / total) if total else 0.0
            print(f"  {chapter}: {changed:,}/{total:,} px differ ({pct:.2f}%)")
            results.append({
                "chapter": chapter,
                "changed_px": changed,
                "total_px": total,
                "pct": pct,
            })

        write_summary(args.out, args.reference, args.candidate,
                      results, only_ref, only_cand)

    return 0


def write_summary(out_dir: Path, ref: Path, cand: Path,
                  results: list[dict], only_ref: list[str], only_cand: list[str]) -> None:
    lines: list[str] = ["# EPUB Visual Diff Summary", ""]
    lines.append(f"- Reference: `{ref.name}`")
    lines.append(f"- Candidate: `{cand.name}`")
    lines.append("")

    lines.append("## Chapter coverage")
    lines.append("")
    lines.append(f"- Common chapters: {len(results)}")
    lines.append(f"- Only in reference: {only_ref or 'none'}")
    lines.append(f"- Only in candidate: {only_cand or 'none'}")
    lines.append("")

    lines.append("## Per-chapter pixel diffs")
    lines.append("")
    lines.append("| chapter | changed px | total px | % differ |")
    lines.append("|---|---|---|---|")
    total_changed = 0
    total_pixels = 0
    for r in results:
        if "error" in r:
            lines.append(f"| `{r['chapter']}` | (error) | — | — |")
            continue
        lines.append(f"| `{r['chapter']}` | {r['changed_px']:,} | {r['total_px']:,} | {r['pct']:.2f}% |")
        total_changed += r["changed_px"]
        total_pixels += r["total_px"]

    if total_pixels:
        overall = 100.0 * total_changed / total_pixels
        lines.append(f"| **TOTAL** | **{total_changed:,}** | **{total_pixels:,}** | **{overall:.2f}%** |")
    lines.append("")

    lines.append("## Outputs per chapter")
    lines.append("")
    lines.append("- `<chapter>/ref.png` — reference render")
    lines.append("- `<chapter>/cand.png` — candidate render")
    lines.append("- `<chapter>/diff.png` — pixel diff (black = identical)")
    lines.append("- `<chapter>/side-by-side.png` — visual side-by-side")
    lines.append("")

    (out_dir / "_summary.md").write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
