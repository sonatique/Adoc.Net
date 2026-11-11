"""Themed-PDF parity score: AdocNet vs asciidoctor-pdf.

Renders one document with both engines (same theme, same fonts) and scores the
result by page count and per-page pixel difference.

Usage:
    python tools/pdf-parity.py                      # the bundled fixture
    python tools/pdf-parity.py --doc D --theme T --fontsdir F [--out O]
    python tools/pdf-parity.py --ref ref.pdf --cand cand.pdf   # score only
Options:
    --dpi N     raster resolution for scoring (default 60)
    --png       also write side-by-side page images (ref | cand | diff)

Score per page: % of pixels whose grey level differs by more than 32/255 after
anti-alias blur; a page missing on one side scores 100. The overall score is
the mean over max(ref pages, cand pages) — lower is better, 0 is identical.

Requires: asciidoctor-pdf on PATH, the .NET SDK, PyMuPDF and Pillow.
"""
import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

import fitz  # PyMuPDF
from PIL import Image, ImageChops, ImageFilter

REPO = Path(__file__).resolve().parent.parent
FIXTURE = REPO / "spec" / "pdf-parity" / "themed-manual"
DEFAULT_DOC = FIXTURE / "guide" / "manual.adoc"
DEFAULT_THEME = FIXTURE / "theme" / "parity-theme.yml"
DEFAULT_FONTS = REPO / "src" / "AdocNet.Converters.Epub" / "Resources"
THRESHOLD = 32


def run(cmd):
    exe = shutil.which(cmd[0])
    if exe is None:
        sys.exit(f"not found on PATH: {cmd[0]}")
    result = subprocess.run([exe, *cmd[1:]], capture_output=True, text=True)
    if result.returncode != 0:
        sys.exit(f"{cmd[0]} failed ({result.returncode}):\n{result.stdout}\n{result.stderr}")
    for line in (result.stdout + result.stderr).splitlines():
        if line.strip():
            print(f"  [{Path(cmd[0]).stem}] {line}")


def render_both(doc: Path, theme: Path, fonts: Path, out: Path):
    ref, cand = out / "ref.pdf", out / "cand.pdf"
    run(["asciidoctor-pdf", "-S", "unsafe", "-a", f"pdf-theme={theme}", "-a", f"pdf-fontsdir={fonts}",
         "-o", str(ref), str(doc)])
    run(["dotnet", "run", "--project", str(REPO / "src" / "AdocNet.Cli.Pdf"), "-c", "Release", "--",
         "-S", "unsafe", "--pdf-theme", str(theme), "--pdf-fontsdir", str(fonts), "-o", str(cand), str(doc)])
    return ref, cand


def raster(page, dpi):
    pix = page.get_pixmap(dpi=dpi, colorspace=fitz.csGRAY)
    return Image.frombytes("L", (pix.width, pix.height), pix.samples)


def page_score(a: Image.Image, b: Image.Image):
    if b.size != a.size:
        b = b.resize(a.size)
    # Light blur so sub-pixel glyph placement and anti-aliasing don't count as differences.
    diff = ImageChops.difference(a.filter(ImageFilter.BoxBlur(1)), b.filter(ImageFilter.BoxBlur(1)))
    changed = sum(diff.point(lambda v: 255 if v > THRESHOLD else 0).histogram()[255:])
    return 100.0 * changed / (a.width * a.height), diff


def score(ref: Path, cand: Path, dpi: int, png_dir: Path | None):
    rdoc, cdoc = fitz.open(str(ref)), fitz.open(str(cand))
    n = max(len(rdoc), len(cdoc))
    pages = []
    for i in range(n):
        if i >= len(rdoc) or i >= len(cdoc):
            pages.append(100.0)
            continue
        a, b = raster(rdoc[i], dpi), raster(cdoc[i], dpi)
        s, diff = page_score(a, b)
        pages.append(round(s, 2))
        if png_dir is not None:
            b = b.resize(a.size)
            sheet = Image.new("L", (a.width * 3 + 20, a.height), 255)
            sheet.paste(a, (0, 0))
            sheet.paste(b, (a.width + 10, 0))
            sheet.paste(ImageChops.invert(diff), (2 * a.width + 20, 0))
            sheet.save(png_dir / f"p{i + 1:02d}.png")
    return {
        "ref_pages": len(rdoc),
        "cand_pages": len(cdoc),
        "page_scores": pages,
        "score": round(sum(pages) / n, 2) if n else 0.0,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--doc", type=Path, default=DEFAULT_DOC)
    ap.add_argument("--theme", type=Path, default=DEFAULT_THEME)
    ap.add_argument("--fontsdir", type=Path, default=DEFAULT_FONTS)
    ap.add_argument("--out", type=Path, default=REPO / "pdf-parity-out")
    ap.add_argument("--ref", type=Path)
    ap.add_argument("--cand", type=Path)
    ap.add_argument("--dpi", type=int, default=60)
    ap.add_argument("--png", action="store_true")
    args = ap.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    if args.ref and args.cand:
        ref, cand = args.ref, args.cand
    else:
        ref, cand = render_both(args.doc.resolve(), args.theme.resolve(), args.fontsdir.resolve(), args.out)

    png_dir = None
    if args.png:
        png_dir = args.out / "pages"
        png_dir.mkdir(exist_ok=True)
    result = score(ref, cand, args.dpi, png_dir)
    (args.out / "score.json").write_text(json.dumps(result, indent=2))

    print(f"pages: ref {result['ref_pages']}  cand {result['cand_pages']}")
    print("per page %: " + "  ".join(f"{i + 1}:{s:g}" for i, s in enumerate(result["page_scores"])))
    print(f"SCORE {result['score']:g}  (mean % differing pixels; 0 = identical)")


if __name__ == "__main__":
    main()
