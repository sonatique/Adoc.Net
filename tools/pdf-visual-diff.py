"""PDF visual diff tool. Renders two PDFs and produces side-by-side + pixel-diff
images for each page and for standard regions (header, footer, body-top).

Usage:
    python tools/pdf-visual-diff.py <reference.pdf> <candidate.pdf> [out_dir]

Requires: PyMuPDF (`pip install pymupdf`), Pillow (`pip install pillow`)

The diff regions are chosen because they're where rendering bugs hide:
- footer (last 80pt): page numbers, footer text alignment, footer logo position
- header (top 100pt): header text alignment, header height, body content top
- body-top (top 250pt): top margin, first paragraph spacing, title position

Pixel-diff highlights changed regions; identical pixels turn black.
"""
import sys
from pathlib import Path

import fitz  # PyMuPDF
from PIL import Image, ImageChops, ImageDraw, ImageFont

DPI = 150

REGIONS = {
    "header": lambda h: (0, 0, None, 100),
    "body_top": lambda h: (0, 0, None, 250),
    "footer": lambda h: (0, h - 80, None, h),
    "full": lambda h: (0, 0, None, h),
}


def render_page(pdf_path: Path, page_no: int, region_pts: tuple) -> Image.Image:
    doc = fitz.open(str(pdf_path))
    page = doc[page_no]
    x0, y0, x1, y1 = region_pts
    if x1 is None:
        x1 = page.rect.width
    if y1 is None:
        y1 = page.rect.height
    clip = fitz.Rect(x0, y0, x1, y1)
    pix = page.get_pixmap(dpi=DPI, clip=clip)
    img = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
    doc.close()
    return img


def side_by_side(left: Image.Image, right: Image.Image, label_l: str, label_r: str) -> Image.Image:
    gap = 20
    w = left.width + right.width + gap
    h = max(left.height, right.height) + 30
    out = Image.new("RGB", (w, h), "white")
    out.paste(left, (0, 30))
    out.paste(right, (left.width + gap, 30))
    draw = ImageDraw.Draw(out)
    draw.text((5, 5), label_l, fill="black")
    draw.text((left.width + gap + 5, 5), label_r, fill="black")
    return out


def pixel_diff(a: Image.Image, b: Image.Image) -> Image.Image:
    if a.size != b.size:
        # Pad smaller to match
        w = max(a.width, b.width)
        h = max(a.height, b.height)
        ap = Image.new("RGB", (w, h), "white"); ap.paste(a)
        bp = Image.new("RGB", (w, h), "white"); bp.paste(b)
        a, b = ap, bp
    diff = ImageChops.difference(a, b)
    # Amplify differences for visibility
    return diff.point(lambda p: min(255, p * 4))


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 1
    ref = Path(argv[1])
    cand = Path(argv[2])
    out_dir = Path(argv[3]) if len(argv) > 3 else Path("pdf-diff-out")
    out_dir.mkdir(parents=True, exist_ok=True)

    ref_doc = fitz.open(str(ref))
    cand_doc = fitz.open(str(cand))
    n_pages = min(len(ref_doc), len(cand_doc))
    page_h = ref_doc[0].rect.height
    ref_doc.close(); cand_doc.close()

    for page_no in range(n_pages):
        for region_name, region_fn in REGIONS.items():
            region = region_fn(page_h)
            ref_img = render_page(ref, page_no, region)
            cand_img = render_page(cand, page_no, region)

            sxs = side_by_side(ref_img, cand_img,
                               f"REF {ref.name}", f"CAND {cand.name}")
            diff = pixel_diff(ref_img, cand_img)

            sxs.save(out_dir / f"p{page_no+1}_{region_name}_sxs.png")
            diff.save(out_dir / f"p{page_no+1}_{region_name}_diff.png")
            print(f"  p{page_no+1} {region_name}: side-by-side + diff saved")

    print(f"\nDone. {n_pages} pages × {len(REGIONS)} regions in {out_dir}/")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
