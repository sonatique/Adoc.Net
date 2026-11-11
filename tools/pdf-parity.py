"""Themed-PDF parity check: AdocNet vs asciidoctor-pdf.

Renders one document with both engines (same theme, same fonts) and compares the two PDFs:

  1. Text layer (primary) — per page, words in reading order are diffed; matched words must sit
     within --tol points of each other and use the same font (subset prefix ignored), size and
     colour.
  2. Structure — page count, page labels, outline (bookmarks), link annotations (count and
     targets), images (count and boxes), the document title, and per page the set of fill
     colours and stroke colour/width pairs (graphics).
  3. Pixels (secondary) — % of pixels whose grey level differs by more than 32/255 after a light
     blur, per page; the worst pages are broken down by region (header, body, footer).

Any text, position, font or structure mismatch fails the run (exit code 1), whatever the pixel
score. A pixel percentage alone cannot tell an anti-alias shift from wrong content.

Usage:
    python tools/pdf-parity.py                      # the bundled fixture
    python tools/pdf-parity.py --doc D --theme T --fontsdir F [--out O]
    python tools/pdf-parity.py --ref ref.pdf --cand cand.pdf   # compare existing PDFs
Options:
    --dpi N     raster resolution for the pixel score (default 60)
    --tol PT    position tolerance for matched words, in points (default 0.5)
    --png       also write side-by-side page images (ref | cand | diff)
    --max N     mismatches listed per category (default 40)
    --allow F   known, intended differences: one regex per line ('#' comments), matched against
                the mismatch messages (default: known-differences.txt beside the fixture document)

Requires: asciidoctor-pdf on PATH, the .NET SDK, PyMuPDF and Pillow.
"""
import argparse
import difflib
import json
import re
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
INVISIBLE = dict.fromkeys(map(ord, "​‌‍⁠﻿­"))


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


# ── Text layer ─────────────────────────────────────────────────────────────

def words(page):
    """Words in reading order: (text, x0, y0, x1, y1, font, size, colour)."""
    spans = []
    for block in page.get_text("dict")["blocks"]:
        for line in block.get("lines", []):
            for s in line["spans"]:
                spans.append((fitz.Rect(s["bbox"]), re.sub(r"^[A-Z]{6}\+", "", s["font"]), round(s["size"], 1),
                              f"#{s['color']:06x}"))
    out = []
    for x0, y0, x1, y1, text, *_ in page.get_text("words"):
        text = text.translate(INVISIBLE)
        if not text:
            continue
        centre = fitz.Point((x0 + x1) / 2, (y0 + y1) / 2)
        font, size, colour = next(((f, sz, c) for r, f, sz, c in spans if r.contains(centre)), ("?", 0.0, "?"))
        out.append((text, x0, y0, x1, y1, font, size, colour))
    # Reading order: group words into lines by vertical centre (fonts of different sizes on one
    # line have different boxes), then left to right within each line.
    out.sort(key=lambda w: (w[2] + w[4]) / 2)
    lines, current, centre = [], [], None
    for w in out:
        c = (w[2] + w[4]) / 2
        if current and abs(c - centre) > 0.4 * (w[4] - w[2]):
            lines.append(current)
            current = []
        if not current:
            centre = c
        current.append(w)
    if current:
        lines.append(current)
    return [w for line in lines for w in sorted(line, key=lambda w: w[1])]


def compare_text(rdoc, cdoc, tol, report):
    for i in range(min(len(rdoc), len(cdoc))):
        rw, cw = words(rdoc[i]), words(cdoc[i])
        matcher = difflib.SequenceMatcher(a=[w[0] for w in rw], b=[w[0] for w in cw], autojunk=False)
        for op, a0, a1, b0, b1 in matcher.get_opcodes():
            if op == "equal":
                for r, c in zip(rw[a0:a1], cw[b0:b1]):
                    dx, dy = c[1] - r[1], c[4] - r[4]
                    if abs(dx) > tol or abs(dy) > tol:
                        report("position", f"p{i + 1}: '{r[0]}' moved dx={dx:+.2f} dy={dy:+.2f}")
                    if (r[5], r[6], r[7]) != (c[5], c[6], c[7]):
                        report("font", f"p{i + 1}: '{r[0]}' ref {r[5]} {r[6]} {r[7]} | ours {c[5]} {c[6]} {c[7]}")
            else:
                ref_text = " ".join(w[0] for w in rw[max(a0 - 2, 0):a1 + 2])
                our_text = " ".join(w[0] for w in cw[max(b0 - 2, 0):b1 + 2])
                report("text", f"p{i + 1}: ref '{ref_text}' | ours '{our_text}'")


# ── Structure ─────────────────────────────────────────────────────────────

def links(page):
    out = []
    for link in page.get_links():
        if link.get("uri"):
            out.append(f"uri:{link['uri']}")
        elif link.get("page", -1) >= 0:
            out.append(f"page:{link['page'] + 1}")
        else:
            out.append(f"named:{link.get('nameddest') or link.get('name')}")
    return sorted(out)


def images(page):
    return sorted((round(i["bbox"][0]), round(i["bbox"][1]), round(i["bbox"][2]), round(i["bbox"][3]))
                  for i in page.get_image_info())


def graphics(page):
    """Set of fill colours and (stroke colour, width) pairs drawn on the page, white ignored.
    Sets, not counts: engines split the same picture into different shapes."""
    hexc = lambda c: "#%02x%02x%02x" % tuple(int(v * 255 + .5) for v in c[:3])
    fills, strokes = set(), set()
    for d in page.get_drawings():
        if d.get("fill") and hexc(d["fill"]) != "#ffffff":
            fills.add(hexc(d["fill"]))
        if d.get("color") and d.get("width"):
            strokes.add((hexc(d["color"]), round(d["width"], 2)))
    return fills, strokes


def compare_structure(rdoc, cdoc, report):
    if len(rdoc) != len(cdoc):
        report("structure", f"page count: ref {len(rdoc)} | ours {len(cdoc)}")
    rtitle, ctitle = rdoc.metadata.get("title", ""), cdoc.metadata.get("title", "")
    if rtitle != ctitle:
        report("structure", f"title metadata: ref '{rtitle}' | ours '{ctitle}'")
    rlabels = [p.get_label() for p in rdoc]
    clabels = [p.get_label() for p in cdoc]
    if rlabels != clabels:
        report("structure", f"page labels: ref {rlabels} | ours {clabels}")
    rtoc, ctoc = rdoc.get_toc(simple=True), cdoc.get_toc(simple=True)
    if rtoc != ctoc:
        for line in difflib.unified_diff([f"{l} {t} -> p{p}" for l, t, p in rtoc],
                                         [f"{l} {t} -> p{p}" for l, t, p in ctoc], "ref", "ours", lineterm="", n=0):
            if not line.startswith(("---", "+++", "@@")):
                report("structure", f"outline {line}")
    for i in range(min(len(rdoc), len(cdoc))):
        rl, cl = links(rdoc[i]), links(cdoc[i])
        if rl != cl:
            report("structure", f"p{i + 1} links: ref {rl} | ours {cl}")
        ri, ci = images(rdoc[i]), images(cdoc[i])
        if ri != ci:
            report("structure", f"p{i + 1} images: ref {ri} | ours {ci}")
        (rf, rs), (cf, cs) = graphics(rdoc[i]), graphics(cdoc[i])
        if rf != cf:
            report("graphics", f"p{i + 1} fills: ref only {sorted(rf - cf)} | ours only {sorted(cf - rf)}")
        if rs != cs:
            report("graphics", f"p{i + 1} strokes: ref only {sorted(rs - cs)} | ours only {sorted(cs - rs)}")


# ── Pixels (secondary) ────────────────────────────────────────────────────

def raster(page, dpi, clip=None):
    pix = page.get_pixmap(dpi=dpi, colorspace=fitz.csGRAY, clip=clip)
    return Image.frombytes("L", (pix.width, pix.height), pix.samples)


def page_score(a: Image.Image, b: Image.Image):
    if b.size != a.size:
        b = b.resize(a.size)
    # Light blur so sub-pixel glyph placement and anti-aliasing don't count as differences.
    diff = ImageChops.difference(a.filter(ImageFilter.BoxBlur(1)), b.filter(ImageFilter.BoxBlur(1)))
    changed = sum(diff.point(lambda v: 255 if v > THRESHOLD else 0).histogram()[255:])
    return 100.0 * changed / (a.width * a.height), diff


def regions(page):
    r = page.rect
    return {"header": fitz.Rect(0, 0, r.width, 72), "body": fitz.Rect(0, 72, r.width, r.height - 72),
            "footer": fitz.Rect(0, r.height - 72, r.width, r.height)}


def pixel_score(rdoc, cdoc, dpi, png_dir):
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
    worst = []
    for i in sorted(range(n), key=lambda k: -pages[k])[:3]:
        if pages[i] <= 0 or i >= len(rdoc) or i >= len(cdoc):
            continue
        parts = {name: round(page_score(raster(rdoc[i], dpi, clip), raster(cdoc[i], dpi, clip))[0], 2)
                 for name, clip in regions(rdoc[i]).items()}
        worst.append((i + 1, pages[i], parts))
    return pages, (round(sum(pages) / n, 2) if n else 0.0), worst


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--doc", type=Path, default=DEFAULT_DOC)
    ap.add_argument("--theme", type=Path, default=DEFAULT_THEME)
    ap.add_argument("--fontsdir", type=Path, default=DEFAULT_FONTS)
    ap.add_argument("--out", type=Path, default=REPO / "pdf-parity-out")
    ap.add_argument("--ref", type=Path)
    ap.add_argument("--cand", type=Path)
    ap.add_argument("--dpi", type=int, default=60)
    ap.add_argument("--tol", type=float, default=0.5)
    ap.add_argument("--max", type=int, default=40)
    ap.add_argument("--png", action="store_true")
    ap.add_argument("--allow", type=Path)
    args = ap.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    args.out.mkdir(parents=True, exist_ok=True)
    if args.ref and args.cand:
        ref, cand = args.ref, args.cand
    else:
        ref, cand = render_both(args.doc.resolve(), args.theme.resolve(), args.fontsdir.resolve(), args.out)
    rdoc, cdoc = fitz.open(str(ref)), fitz.open(str(cand))

    allow_file = args.allow or (FIXTURE / "known-differences.txt" if not (args.ref and args.cand) and args.doc.resolve() == DEFAULT_DOC.resolve() else None)
    allowed_patterns = []
    if allow_file and allow_file.exists():
        for line in allow_file.read_text(encoding="utf-8").splitlines():
            if line.strip() and not line.lstrip().startswith("#"):
                allowed_patterns.append(re.compile(line.strip()))
    found = {"text": [], "position": [], "font": [], "structure": [], "graphics": []}
    allowed = []

    def report(kind, msg):
        (allowed if any(p.search(msg) for p in allowed_patterns) else found[kind]).append(msg)

    compare_structure(rdoc, cdoc, report)
    compare_text(rdoc, cdoc, args.tol, report)

    png_dir = None
    if args.png:
        png_dir = args.out / "pages"
        png_dir.mkdir(exist_ok=True)
    pages, score, worst = pixel_score(rdoc, cdoc, args.dpi, png_dir)

    for kind, items in found.items():
        print(f"{kind.upper()} mismatches: {len(items)}")
        for msg in items[:args.max]:
            print(f"  {msg}")
        if len(items) > args.max:
            print(f"  ... {len(items) - args.max} more")
    if allowed:
        print(f"ALLOWED (known, intended) differences: {len(allowed)}")
        for msg in allowed[:args.max]:
            print(f"  {msg}")
    print(f"pages: ref {len(rdoc)}  cand {len(cdoc)}")
    print("pixel % per page: " + "  ".join(f"{i + 1}:{s:g}" for i, s in enumerate(pages)))
    for page, s, parts in worst:
        print(f"  worst p{page} ({s:g}%): " + ", ".join(f"{k} {v:g}%" for k, v in parts.items()))
    print(f"PIXEL SCORE {score:g}  (secondary; mean % differing pixels)")

    (args.out / "score.json").write_text(json.dumps(
        {"ref_pages": len(rdoc), "cand_pages": len(cdoc), "page_scores": pages, "score": score,
         **{k: v for k, v in found.items()}}, indent=2, ensure_ascii=False), encoding="utf-8")
    failed = sum(len(v) for v in found.values())
    print("RESULT: " + ("PASS" if failed == 0 else f"FAIL ({failed} mismatches)"))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
