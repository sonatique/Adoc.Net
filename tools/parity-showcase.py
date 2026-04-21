#!/usr/bin/env python3
"""Render side-by-side parity showcases for HTML, EPUB, DocBook, and Man.

Visual formats (HTML, EPUB) -> screenshot via Chrome headless.
Textual formats (DocBook, Man) -> render canonical text excerpts on a PNG canvas
so a single image grid can showcase all four formats consistently.

Usage:
    python tools/parity-showcase.py
"""
from __future__ import annotations

import re
import subprocess
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

from PIL import Image, ImageDraw, ImageFont

import os
# Resolve /tmp to the actual Windows temp dir when running under bash/MSYS.
_tempbase = os.environ.get("TEMP") or os.environ.get("TMP") or "/tmp"
if _tempbase == "/tmp" and os.name == "nt":
    _tempbase = r"C:\Users\sylva\AppData\Local\Temp"
WORK = Path(_tempbase) / "parity-showcase"
OUT = Path("parity-showcase-out")
WIN_W = 800
WIN_H = 1100

CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PANDOC_CANDIDATES = [
    r"C:\Program Files\Pandoc\pandoc.exe",
    r"C:\Users\sylva\AppData\Local\Pandoc\pandoc.exe",
    "/usr/bin/pandoc",
]


def find_pandoc() -> str | None:
    for p in PANDOC_CANDIDATES:
        if Path(p).exists():
            return p
    return None


# Lightweight stylesheet for pandoc-rendered DocBook/man HTML so the screenshot
# is readable (default browser styles are cramped). Kept minimal so the focus
# remains on content/structure differences, not theme polish.
PANDOC_PREVIEW_CSS = """
<style>
body { font-family: Georgia, "Times New Roman", serif; max-width: 720px;
       margin: 1em auto; padding: 0 1em; color: #1a1a1a; line-height: 1.5; }
h1 { font-family: "Helvetica Neue", Arial, sans-serif; font-size: 1.4em;
     margin-top: 1.2em; padding-bottom: 0.2em; border-bottom: 1px solid #ccc; }
h2 { font-family: "Helvetica Neue", Arial, sans-serif; font-size: 1.15em;
     margin-top: 1em; }
h3 { font-family: "Helvetica Neue", Arial, sans-serif; font-size: 1.05em; }
p  { margin: 0.7em 0; }
pre, code { font-family: Consolas, "Courier New", monospace; font-size: 0.9em; }
pre { background: #f4f4f4; padding: 0.6em 0.8em; border-left: 3px solid #ccc;
      overflow-x: auto; }
code { background: #f4f4f4; padding: 0.05em 0.25em; border-radius: 2px; }
ul, ol { padding-left: 1.6em; }
li { margin: 0.25em 0; }
blockquote { margin: 0.6em 0 0.6em 1em; padding-left: 0.8em;
             border-left: 3px solid #ddd; color: #444; }
dl dt { font-weight: bold; margin-top: 0.6em; }
dl dd { margin-left: 1.5em; }
</style>"""

LABEL_FONT_SIZE = 22
TEXT_FONT_SIZE = 11
LINE_H = 14


def find_font(size: int, mono: bool = False) -> ImageFont.FreeTypeFont:
    candidates_mono = [
        r"C:\Windows\Fonts\consola.ttf",
        r"C:\Windows\Fonts\cour.ttf",
    ]
    candidates_sans = [
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf",
    ]
    for path in (candidates_mono if mono else candidates_sans):
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def screenshot(src: Path, dest: Path, w: int = WIN_W, h: int = WIN_H) -> None:
    url = "file:///" + str(src.resolve()).replace("\\", "/")
    abs_dest = dest.resolve()
    cmd = [
        CHROME, "--headless", "--no-sandbox", "--disable-gpu",
        f"--window-size={w},{h}", "--hide-scrollbars",
        # Asciidoctor's reference HTML pulls fonts from fonts.googleapis.com.
        # Without virtual-time-budget Chrome screenshots before the network
        # fetch completes — text renders as invisible (font-display: block
        # with no fallback) and the page looks blank. 8s budget is generous.
        "--virtual-time-budget=8000",
        f"--screenshot={abs_dest}", url,
    ]
    subprocess.run(cmd, capture_output=True, text=True, timeout=60, check=False)
    if not abs_dest.exists():
        raise RuntimeError(f"chrome failed for {src}")


def extract_chapter(epub: Path) -> Path:
    """Extract the first non-nav XHTML chapter for screenshotting."""
    out_dir = WORK / f"_extract_{epub.stem}"
    out_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(epub) as zf:
        zf.extractall(out_dir)
    for x in sorted(out_dir.rglob("*.xhtml")):
        if x.stem in ("nav", "toc"):
            continue
        return x
    raise RuntimeError(f"no chapter file in {epub}")


def text_to_png(lines: list[str], width: int, height: int, mono: bool = True) -> Image.Image:
    img = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(img)
    font = find_font(TEXT_FONT_SIZE, mono=mono)
    y = 8
    for line in lines:
        if y > height - LINE_H:
            break
        # Truncate over-long lines so they don't run off the canvas.
        max_chars = (width - 16) // 7  # ~7 px per char in mono at size 11
        if len(line) > max_chars:
            line = line[:max_chars - 1] + "…"
        draw.text((8, y), line, fill="black", font=font)
        y += LINE_H
    return img


def stitch(left: Image.Image, right: Image.Image, label: str,
           label_l: str = "Asciidoctor (reference)", label_r: str = "AdocNet (candidate)") -> Image.Image:
    pad = 16
    label_h = LABEL_FONT_SIZE + 24
    total_w = left.width + right.width + pad * 3
    total_h = max(left.height, right.height) + label_h * 2 + pad
    canvas = Image.new("RGB", (total_w, total_h), "white")
    draw = ImageDraw.Draw(canvas)
    title_font = find_font(LABEL_FONT_SIZE)
    sub_font = find_font(LABEL_FONT_SIZE - 6)
    # Top label (format name)
    draw.text((pad, pad), label, fill="#1a1a1a", font=title_font)
    # Sub-labels (left/right)
    draw.text((pad, pad + label_h), label_l, fill="#555", font=sub_font)
    draw.text((left.width + pad * 2, pad + label_h), label_r, fill="#555", font=sub_font)
    # Body
    canvas.paste(left, (pad, pad + label_h * 2))
    canvas.paste(right, (left.width + pad * 2, pad + label_h * 2))
    return canvas


def render_pdf_page(pdf_path: Path, page_no: int, dest: Path, dpi: int = 130) -> None:
    """Rasterise one page of a PDF to PNG via PyMuPDF."""
    import fitz  # PyMuPDF
    doc = fitz.open(str(pdf_path))
    page = doc[page_no]
    pix = page.get_pixmap(dpi=dpi)
    pix.save(str(dest))
    doc.close()


def render_via_pandoc(pandoc: str, src: Path, src_format: str, dest: Path) -> None:
    """Render a DocBook XML or man source to a stand-alone HTML preview file
    via pandoc, with a lightweight stylesheet inlined for screenshot readability.

    Capture stdout as bytes and decode as UTF-8 explicitly — pandoc emits UTF-8
    regardless of locale, but Python's text=True would decode using cp1252 on
    Windows and corrupt non-ASCII (e.g. \\(bu -> Â· mojibake).
    """
    raw = subprocess.run(
        [pandoc, "-f", src_format, "-t", "html", str(src)],
        capture_output=True, check=False, timeout=60,
    ).stdout
    body_html = raw.decode("utf-8", errors="replace")
    full = f"""<!DOCTYPE html>
<html><head><meta charset="utf-8">{PANDOC_PREVIEW_CSS}</head>
<body>{body_html}</body></html>"""
    dest.write_text(full, encoding="utf-8")


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)

    # ── PDF (rasterised page 1 via PyMuPDF) ──────────────────────
    pdf_ref = WORK / "howto-ref.pdf"
    pdf_cand = WORK / "howto-cand.pdf"
    if pdf_ref.exists() and pdf_cand.exists():
        print("Rendering PDF...")
        render_pdf_page(pdf_ref, 0, OUT / "_pdf-ref.png")
        render_pdf_page(pdf_cand, 0, OUT / "_pdf-cand.png")
        pdf_l = Image.open(OUT / "_pdf-ref.png")
        pdf_r = Image.open(OUT / "_pdf-cand.png")
        stitch(pdf_l, pdf_r,
               "PDF — page 1 raster (achieved visual parity in v1.0.0-beta.25)"
               ).save(OUT / "pdf.png")
    else:
        print("Skipping PDF (howto-ref.pdf / howto-cand.pdf not found in WORK).")

    # ── HTML ─────────────────────────────────────────────────────
    print("Rendering HTML...")
    screenshot(WORK / "html-ref.html", OUT / "_html-ref.png")
    screenshot(WORK / "html-cand.html", OUT / "_html-cand.png")
    html_l = Image.open(OUT / "_html-ref.png")
    html_r = Image.open(OUT / "_html-cand.png")
    stitch(html_l, html_r, "HTML — diff: 0 lines (byte-identical structural DOM)").save(OUT / "html.png")

    # ── HTML with --theme asciidoctor (drop-in compat for migrators) ─
    html_compat = WORK / "html-cand-asciidoctor-theme.html"
    if html_compat.exists():
        print("Rendering HTML (asciidoctor theme)...")
        screenshot(html_compat, OUT / "_html-asciidoctor-theme.png")
        compat_r = Image.open(OUT / "_html-asciidoctor-theme.png")
        stitch(html_l, compat_r,
               "HTML — same source rendered with --theme asciidoctor (iconic look)"
               ).save(OUT / "html-asciidoctor-theme.png")

    # ── EPUB ─────────────────────────────────────────────────────
    print("Rendering EPUB...")
    epub_ref_chapter = extract_chapter(WORK / "howto-ref.epub")
    epub_cand_chapter = extract_chapter(WORK / "howto-cand.epub")
    screenshot(epub_ref_chapter, OUT / "_epub-ref.png")
    screenshot(epub_cand_chapter, OUT / "_epub-cand.png")
    epub_l = Image.open(OUT / "_epub-ref.png")
    epub_r = Image.open(OUT / "_epub-cand.png")
    stitch(epub_l, epub_r, "EPUB — pixel diff: 33.49% (residual is body-font fallback)").save(OUT / "epub.png")

    # ── DocBook (pandoc -> HTML -> screenshot) ─────────────────────
    pandoc = find_pandoc()
    if pandoc is None:
        print("WARNING: pandoc not found — skipping DocBook + Man visual previews.")
    else:
        print("Rendering DocBook (via pandoc -> HTML)...")
        render_via_pandoc(pandoc, WORK / "docbook-ref.xml", "docbook", WORK / "_docbook-ref.html")
        render_via_pandoc(pandoc, WORK / "docbook-cand.xml", "docbook", WORK / "_docbook-cand.html")
        screenshot(WORK / "_docbook-ref.html", OUT / "_docbook-ref.png")
        screenshot(WORK / "_docbook-cand.html", OUT / "_docbook-cand.png")
        db_l = Image.open(OUT / "_docbook-ref.png")
        db_r = Image.open(OUT / "_docbook-cand.png")
        stitch(db_l, db_r,
               "DocBook — XML diff: 0 lines (rendered via pandoc -> HTML for visual comparison)"
               ).save(OUT / "docbook.png")

        # ── Man (pandoc -> HTML -> screenshot) ─────────────────────────
        print("Rendering Man (via pandoc -> HTML)...")
        render_via_pandoc(pandoc, WORK / "man-ref.man", "man", WORK / "_man-ref.html")
        render_via_pandoc(pandoc, WORK / "man-cand.man", "man", WORK / "_man-cand.html")
        screenshot(WORK / "_man-ref.html", OUT / "_man-ref.png")
        screenshot(WORK / "_man-cand.html", OUT / "_man-cand.png")
        m_l = Image.open(OUT / "_man-ref.png")
        m_r = Image.open(OUT / "_man-cand.png")
        stitch(m_l, m_r,
               "Man — roff diff: 142 lines (rendered via pandoc -> HTML for visual comparison)"
               ).save(OUT / "man.png")

    # Clean up intermediate PNGs
    for tmp in OUT.glob("_*.png"):
        tmp.unlink()

    print(f"Done. Outputs in {OUT}/")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
