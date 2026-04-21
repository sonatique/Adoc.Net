"""Shared rendering helpers for parity-showcase.py and parity-sweep.py.

All functions take input file paths and produce PNG output. Each format is
rendered into a `panel image` (single PNG showing the rendered content) that
can be stitched into a side-by-side view.

Helpers:
- screenshot()       Chrome headless --screenshot for HTML/EPUB/Reveal.js
- render_pdf_page()  PyMuPDF rasterise a PDF page
- render_via_pandoc()  Convert man/docbook source via pandoc to HTML
- extract_chapter()  Pull first non-nav chapter file from an EPUB
- stitch()           Compose two panel images side-by-side with labels
"""
from __future__ import annotations

import os
import subprocess
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

WIN_W = 800
WIN_H = 1100
LABEL_FONT_SIZE = 22

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


def screenshot(src: Path, dest: Path, w: int = WIN_W, h: int = WIN_H,
               virtual_time_budget_ms: int = 8000) -> None:
    """Chrome headless --screenshot of `src` to `dest` PNG."""
    url = "file:///" + str(src.resolve()).replace("\\", "/")
    abs_dest = dest.resolve()
    cmd = [
        CHROME, "--headless", "--no-sandbox", "--disable-gpu",
        f"--window-size={w},{h}", "--hide-scrollbars",
        f"--virtual-time-budget={virtual_time_budget_ms}",
        f"--screenshot={abs_dest}", url,
    ]
    subprocess.run(cmd, capture_output=True, text=True, timeout=60, check=False)
    if not abs_dest.exists():
        raise RuntimeError(f"chrome failed for {src}")


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
    Windows and corrupt non-ASCII glyphs.
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


def extract_first_chapter(epub: Path, work_dir: Path) -> Path:
    """Extract the first non-nav XHTML/HTML chapter from an EPUB."""
    out_dir = work_dir / f"_extract_{epub.stem}"
    out_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(epub) as zf:
        zf.extractall(out_dir)
    for x in sorted(out_dir.rglob("*.xhtml")):
        if x.stem in ("nav", "toc"):
            continue
        return x
    for x in sorted(out_dir.rglob("*.html")):
        if x.stem in ("nav", "toc"):
            continue
        return x
    raise RuntimeError(f"no chapter file in {epub}")


def stitch(left: Image.Image, right: Image.Image, label: str,
           label_l: str = "Asciidoctor (reference)",
           label_r: str = "AdocNet (candidate)") -> Image.Image:
    """Stitch two panel images side-by-side with a top label and per-side sub-labels."""
    pad = 16
    label_h = LABEL_FONT_SIZE + 24
    total_w = left.width + right.width + pad * 3
    total_h = max(left.height, right.height) + label_h * 2 + pad
    canvas = Image.new("RGB", (total_w, total_h), "white")
    draw = ImageDraw.Draw(canvas)
    title_font = find_font(LABEL_FONT_SIZE)
    sub_font = find_font(LABEL_FONT_SIZE - 6)
    draw.text((pad, pad), label, fill="#1a1a1a", font=title_font)
    draw.text((pad, pad + label_h), label_l, fill="#555", font=sub_font)
    draw.text((left.width + pad * 2, pad + label_h), label_r, fill="#555", font=sub_font)
    canvas.paste(left, (pad, pad + label_h * 2))
    canvas.paste(right, (left.width + pad * 2, pad + label_h * 2))
    return canvas
