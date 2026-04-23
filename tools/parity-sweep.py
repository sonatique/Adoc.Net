#!/usr/bin/env python3
"""Run all parity-diff tools across a corpus of .adoc documents.

For each document and each format (HTML, PDF, EPUB, EPUB-visual,
DocBook, Reveal.js, Man), generates the reference output via
asciidoctor* and the candidate via adocnet, runs the corresponding
diff tool, and aggregates the numeric results into a single table.

Use to validate v1.0.0 readiness — surfaces parity gaps that don't
appear in HOWTO.adoc by stress-testing on richer documents.

Usage:
    python tools/parity-sweep.py [--glob "spec/conformance/asciidoctor-*.adoc"]
                                 [--include-pdf] [--include-epub-visual]
                                 [--out parity-sweep-out]

Outputs:
    parity-sweep-out/_summary.md    aggregate per-format diff table
    parity-sweep-out/<doc>/<fmt>.diff   per-doc per-format diff dump
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

# Local helpers shared with parity-showcase.py
sys.path.insert(0, str(Path(__file__).resolve().parent))
import _parity_render as pr  # type: ignore

REPO = Path(__file__).resolve().parent.parent
ADOCNET = ["dotnet", "run", "--project", str(REPO / "src" / "AdocNet.Cli"),
           "--no-build", "--"]
ASCIIDOCTOR = "asciidoctor"
ASCIIDOCTOR_PDF = "asciidoctor-pdf"
ASCIIDOCTOR_EPUB3 = "asciidoctor-epub3"
ASCIIDOCTOR_REVEALJS = "asciidoctor-revealjs"

# Search candidates for the asciidoctor-pdf default theme YAML, used when
# rendering AdocNet PDFs in "asciidoctor look" mode (--include-pdf turns this on).
ASCIIDOCTOR_PDF_THEME_CANDIDATES = [
    Path(r"C:\Users\sylva\.local\share\gem\ruby\3.4.0\gems\asciidoctor-pdf-2.3.24\data\themes\default-theme.yml"),
    Path.home() / ".local/share/gem/ruby/3.4.0/gems/asciidoctor-pdf-2.3.24/data/themes/default-theme.yml",
]


def find_asciidoctor_pdf_theme(override: str | None = None) -> Path | None:
    """Locate the asciidoctor-pdf bundled default-theme.yml.

    Honors --asciidoctor-pdf-theme CLI flag first, then probes a known set of
    install paths. Returns None when the theme can't be found — caller should
    skip the pdf-asciidoctor-theme format and warn.
    """
    if override:
        p = Path(override)
        return p if p.exists() else None
    for cand in ASCIIDOCTOR_PDF_THEME_CANDIDATES:
        if cand.exists():
            return cand
    # Fall back to a glob search across all installed asciidoctor-pdf gem versions.
    for base in (Path.home() / ".local/share/gem/ruby",
                 Path(r"C:\Users\sylva\.local\share\gem\ruby")):
        if base.exists():
            for theme in base.rglob("asciidoctor-pdf-*/data/themes/default-theme.yml"):
                return theme
    return None

# Tool paths
PYTHON = sys.executable
TOOLS = REPO / "tools"


@dataclass
class FormatSpec:
    name: str
    ref_cmd: list[str]              # extend with [-o ref_out, src]
    cand_args: list[str]            # extend onto ADOCNET; final is [-o cand_out, src]
    out_ext: str                    # extension of generated file
    diff_tool: str | None           # path to diff tool .py (None = skip diff)
    diff_metric_re: str             # regex captures the numeric "diff size" from diff tool stdout
    enabled: bool = True


FORMATS = [
    FormatSpec(
        name="html",
        # asciidoctor produces a full document by default. Use AdocNet's -e
        # (embedded full-doc) flag to match. We don't pass --theme so the
        # default theme applies to the candidate; the html-diff tool ignores
        # inline CSS / classes presentationally so the comparison is structural.
        ref_cmd=[ASCIIDOCTOR, "-o"],
        cand_args=["-b", "html5", "-e", "-o"],
        out_ext=".html",
        diff_tool="html-diff.py",
        diff_metric_re=r"DOM diff lines:\s*(\d+)",
    ),
    FormatSpec(
        # AdocNet HTML with --theme asciidoctor (drop-in visual compat for
        # users migrating from asciidoctor). Same asciidoctor reference as
        # `html`; candidate is styled to look like asciidoctor's output.
        # Structural DOM is the same as `html` — this panel exists purely
        # for visual inspection, so no diff tool is wired up.
        name="html-asciidoctor-theme",
        ref_cmd=[ASCIIDOCTOR, "-o"],
        cand_args=["-b", "html5", "--theme", "asciidoctor", "-o"],
        out_ext=".html",
        diff_tool=None,
        diff_metric_re="",
        enabled=False,  # opt-in via --include-html-asciidoctor-theme
    ),
    FormatSpec(
        name="docbook",
        ref_cmd=[ASCIIDOCTOR, "-b", "docbook5", "-o"],
        cand_args=["-b", "docbook5", "-o"],
        out_ext=".xml",
        diff_tool="docbook-diff.py",
        diff_metric_re=r"Canonical diff lines:\s*(\d+)",
    ),
    FormatSpec(
        name="man",
        ref_cmd=[ASCIIDOCTOR, "-b", "manpage", "-o"],
        cand_args=["-b", "man", "-o"],
        out_ext=".man",
        diff_tool="man-diff.py",
        diff_metric_re=r"Normalised diff lines:\s*(\d+)",
    ),
    FormatSpec(
        name="revealjs",
        ref_cmd=[ASCIIDOCTOR_REVEALJS, "-o"],
        cand_args=["-b", "revealjs", "-o"],
        out_ext=".html",
        diff_tool="revealjs-diff.py",
        diff_metric_re=r"Slide DOM diff lines:\s*(\d+)",
    ),
    FormatSpec(
        name="epub-struct",
        ref_cmd=[ASCIIDOCTOR_EPUB3, "-o"],
        cand_args=["-b", "epub", "-o"],
        out_ext=".epub",
        diff_tool="epub-diff.py",
        # epub-diff prints "Common: N (X differ, Y identical)" — count the differ value.
        diff_metric_re=r"Common:\s*\d+\s*\((\d+)\s*differ",
    ),
    FormatSpec(
        name="epub-visual",
        ref_cmd=[ASCIIDOCTOR_EPUB3, "-o"],
        cand_args=["-b", "epub", "-o"],
        out_ext=".epub",
        diff_tool="epub-visual-diff.py",
        diff_metric_re=r"px differ \((\d+\.\d+)%\)",
        enabled=False,  # heavy: opt-in via --include-epub-visual
    ),
    FormatSpec(
        name="pdf",
        ref_cmd=[ASCIIDOCTOR_PDF, "-o"],
        cand_args=["-b", "pdf", "-o"],
        out_ext=".pdf",
        diff_tool=None,  # we don't have a numeric pdf-visual-diff metric
        diff_metric_re="",
        enabled=False,  # opt-in via --include-pdf
    ),
    FormatSpec(
        # AdocNet PDF rendered with asciidoctor-pdf's default theme YAML — lets
        # the user see how close the output gets to asciidoctor-pdf's iconic
        # look. Paired with the same asciidoctor-pdf reference as `pdf`, so the
        # side-by-side shows reference vs AdocNet-in-asciidoctor-skin.
        # cand_args is patched at runtime to inject the resolved theme path.
        name="pdf-asciidoctor-theme",
        ref_cmd=[ASCIIDOCTOR_PDF, "-o"],
        cand_args=["-b", "pdf", "--pdf-theme", "__ASCIIDOCTOR_THEME__", "-o"],
        out_ext=".pdf",
        diff_tool=None,
        diff_metric_re="",
        enabled=False,  # opt-in via --include-pdf (same flag as `pdf`)
    ),
]


@dataclass
class Result:
    doc: str
    format_name: str
    diff_value: float | None  # number of diff lines, or pixel-percentage
    error: str | None = None


def run(cmd: list[str], timeout: int = 120) -> subprocess.CompletedProcess:
    # shell=True needed for .bat / sh-wrapper executables on Windows
    # (asciidoctor on Windows is a Ruby gem with a shell-script wrapper).
    # Quote arguments containing spaces so shell parsing preserves them.
    if os.name == "nt":
        quoted = " ".join(f'"{a}"' if " " in a or "/" in a or "\\" in a else a for a in cmd)
        return subprocess.run(quoted, capture_output=True, text=True, timeout=timeout, shell=True)
    return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)


def render(format_spec: FormatSpec, src: Path, work: Path, ref: bool) -> Path | None:
    out = work / (("ref" if ref else "cand") + format_spec.out_ext)
    if ref:
        cmd = format_spec.ref_cmd + [str(out), str(src)]
    else:
        cmd = ADOCNET + format_spec.cand_args + [str(out), str(src)]
    r = run(cmd, timeout=180)
    if not out.exists() or out.stat().st_size == 0:
        return None
    return out


def diff(format_spec: FormatSpec, ref: Path, cand: Path, out_dir: Path) -> float | None:
    if not format_spec.diff_tool:
        return None
    cmd = [PYTHON, str(TOOLS / format_spec.diff_tool), str(ref), str(cand),
           "--out", str(out_dir)]
    r = run(cmd, timeout=180)
    m = re.search(format_spec.diff_metric_re, r.stdout, re.MULTILINE)
    if m:
        try:
            return float(m.group(1))
        except ValueError:
            return None
    return None


def render_visual(format_name: str, ref: Path, cand: Path, out_dir: Path,
                  work_dir: Path, doc_name: str, pandoc: str | None) -> Path | None:
    """Render side-by-side PNG for the (doc, format) pair.

    Returns the path to the stitched PNG, or None if visual rendering for this
    format isn't supported (e.g. pandoc missing for DocBook/Man).
    """
    from PIL import Image  # type: ignore
    out_dir.mkdir(parents=True, exist_ok=True)
    ref_png = out_dir / "_ref.png"
    cand_png = out_dir / "_cand.png"
    try:
        if format_name in ("html", "html-asciidoctor-theme", "revealjs"):
            pr.screenshot(ref, ref_png)
            pr.screenshot(cand, cand_png)
        elif format_name in ("pdf", "pdf-asciidoctor-theme"):
            pr.render_pdf_page(ref, 0, ref_png)
            pr.render_pdf_page(cand, 0, cand_png)
        elif format_name == "epub-struct":
            ref_chap = pr.extract_first_chapter(ref, work_dir)
            cand_chap = pr.extract_first_chapter(cand, work_dir)
            pr.screenshot(ref_chap, ref_png)
            pr.screenshot(cand_chap, cand_png)
        elif format_name in ("docbook", "man") and pandoc is not None:
            ref_html = work_dir / f"_visual-{format_name}-ref.html"
            cand_html = work_dir / f"_visual-{format_name}-cand.html"
            pr.render_via_pandoc(pandoc, ref,
                                 "docbook" if format_name == "docbook" else "man", ref_html)
            pr.render_via_pandoc(pandoc, cand,
                                 "docbook" if format_name == "docbook" else "man", cand_html)
            pr.screenshot(ref_html, ref_png)
            pr.screenshot(cand_html, cand_png)
        else:
            return None
    except Exception as e:
        print(f"      visual render failed: {e!s:.100}")
        return None

    if not ref_png.exists() or not cand_png.exists():
        return None
    left = Image.open(ref_png)
    right = Image.open(cand_png)
    label = f"{doc_name} — {format_name}"
    # For asciidoctor-themed outputs, label the right side explicitly so the
    # viewer sees that AdocNet is rendering with the asciidoctor-style theme.
    if format_name == "pdf-asciidoctor-theme":
        stitched = pr.stitch(left, right, label,
                             label_r="AdocNet (--pdf-theme asciidoctor)")
    elif format_name == "html-asciidoctor-theme":
        stitched = pr.stitch(left, right, label,
                             label_r="AdocNet (--theme asciidoctor)")
    else:
        stitched = pr.stitch(left, right, label)
    out_png = out_dir / "side-by-side.png"
    stitched.save(out_png)
    # Clean up intermediate single-side PNGs
    ref_png.unlink(missing_ok=True)
    cand_png.unlink(missing_ok=True)
    return out_png


def process_doc(src: Path, work_root: Path, out_root: Path,
                formats: list[FormatSpec], visual: bool = False,
                pandoc: str | None = None) -> list[Result]:
    doc_name = src.stem
    doc_work = work_root / doc_name
    doc_work.mkdir(parents=True, exist_ok=True)
    doc_out = out_root / doc_name
    doc_out.mkdir(parents=True, exist_ok=True)

    results: list[Result] = []
    for fmt in formats:
        if not fmt.enabled:
            continue
        try:
            ref = render(fmt, src, doc_work, ref=True)
            if ref is None:
                results.append(Result(doc_name, fmt.name, None, "ref render failed"))
                continue
            cand = render(fmt, src, doc_work, ref=False)
            if cand is None:
                results.append(Result(doc_name, fmt.name, None, "cand render failed"))
                continue
            diff_val = diff(fmt, ref, cand, doc_out / fmt.name)
            results.append(Result(doc_name, fmt.name, diff_val))
            if visual:
                visual_out = render_visual(
                    fmt.name, ref, cand, doc_out / fmt.name,
                    doc_work, doc_name, pandoc)
                if visual_out is not None:
                    print(f"      visual: {visual_out}")
        except subprocess.TimeoutExpired:
            results.append(Result(doc_name, fmt.name, None, "timeout"))
        except Exception as e:
            results.append(Result(doc_name, fmt.name, None, f"error: {e!s:.80}"))
    return results


def write_summary(out_dir: Path, all_results: list[Result], formats: list[FormatSpec]) -> None:
    enabled_formats = [f for f in formats if f.enabled]
    by_doc: dict[str, dict[str, Result]] = {}
    for r in all_results:
        by_doc.setdefault(r.doc, {})[r.format_name] = r

    # Sort docs by aggregate diff (worst first), errors at the end
    def doc_score(doc: str) -> tuple[int, float]:
        rs = by_doc[doc]
        any_error = any(r.error for r in rs.values())
        total = sum(r.diff_value or 0 for r in rs.values() if r.diff_value is not None)
        return (1 if any_error else 0, -total)

    sorted_docs = sorted(by_doc.keys(), key=doc_score)

    lines = ["# Parity Sweep Summary", ""]
    lines.append(f"- Documents: {len(by_doc)}")
    lines.append(f"- Formats: {', '.join(f.name for f in enabled_formats)}")
    lines.append("")
    lines.append("## Results (worst at top)")
    lines.append("")
    header = ["document"] + [f.name for f in enabled_formats]
    lines.append("| " + " | ".join(header) + " |")
    lines.append("|" + "|".join(["---"] * len(header)) + "|")
    for doc in sorted_docs:
        rs = by_doc[doc]
        row = [f"`{doc}`"]
        for f in enabled_formats:
            r = rs.get(f.name)
            if r is None:
                row.append("—")
            elif r.error:
                row.append(f"⚠ {r.error[:30]}")
            elif r.diff_value is None:
                row.append("?")
            elif f.name == "epub-visual":
                row.append(f"{r.diff_value:.1f}%")
            else:
                row.append(str(int(r.diff_value)))
        lines.append("| " + " | ".join(row) + " |")

    # Aggregates per format (median + max, ignoring errors)
    lines.append("")
    lines.append("## Per-format aggregates (excluding errors)")
    lines.append("")
    lines.append("| format | docs OK | min | median | max | sum |")
    lines.append("|---|---|---|---|---|---|")
    for f in enabled_formats:
        vals = [r.diff_value for r in all_results
                if r.format_name == f.name and r.diff_value is not None and r.error is None]
        if not vals:
            lines.append(f"| `{f.name}` | 0 | — | — | — | — |")
            continue
        vals_sorted = sorted(vals)
        median = vals_sorted[len(vals_sorted) // 2]
        suffix = "%" if f.name == "epub-visual" else ""
        lines.append(f"| `{f.name}` | {len(vals)} | {min(vals):.1f}{suffix} | "
                     f"{median:.1f}{suffix} | {max(vals):.1f}{suffix} | "
                     f"{sum(vals):.1f}{suffix} |")

    (out_dir / "_summary.md").write_text("\n".join(lines), encoding="utf-8")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--glob", default="spec/conformance/asciidoctor-*.adoc",
                        help="glob pattern for corpus files (relative to repo root)")
    parser.add_argument("--out", type=Path, default=REPO / "parity-sweep-out")
    parser.add_argument("--limit", type=int, default=None,
                        help="process only first N docs (for quick iteration)")
    parser.add_argument("--include-pdf", action="store_true",
                        help="include PDF formats (default theme + asciidoctor-theme)")
    parser.add_argument("--asciidoctor-pdf-theme", default=None,
                        help="path to asciidoctor-pdf default-theme.yml; auto-discovered "
                             "from the gem install dir when omitted")
    parser.add_argument("--include-html-asciidoctor-theme", action="store_true",
                        help="also render AdocNet HTML with --theme asciidoctor for "
                             "drop-in visual compat inspection")
    parser.add_argument("--include-epub-visual", action="store_true",
                        help="include EPUB visual pixel diff (heavy: ~30s/doc)")
    parser.add_argument("--only", default=None,
                        help="comma-separated list of formats to run (default: all enabled)")
    parser.add_argument("--visual", action="store_true",
                        help="produce side-by-side visual PNGs for each (doc, format) "
                             "pair (in addition to the numeric diff). Use to catch "
                             "rendering regressions that don't show in structural diffs.")
    args = parser.parse_args(argv)

    formats = list(FORMATS)
    asciidoctor_theme = None
    if args.include_pdf:
        asciidoctor_theme = find_asciidoctor_pdf_theme(args.asciidoctor_pdf_theme)
    for f in formats:
        if f.name == "pdf" and args.include_pdf:
            f.enabled = True
        if f.name == "pdf-asciidoctor-theme" and args.include_pdf:
            if asciidoctor_theme is None:
                print("WARNING: asciidoctor-pdf default-theme.yml not found — skipping pdf-asciidoctor-theme. "
                      "Pass --asciidoctor-pdf-theme <path> to override.")
            else:
                # Patch the placeholder with the resolved theme path so AdocNet
                # CLI receives an absolute file path (works on Windows + POSIX).
                f.cand_args = [str(asciidoctor_theme.resolve()) if a == "__ASCIIDOCTOR_THEME__" else a
                               for a in f.cand_args]
                f.enabled = True
        if f.name == "html-asciidoctor-theme" and args.include_html_asciidoctor_theme:
            f.enabled = True
        if f.name == "epub-visual" and args.include_epub_visual:
            f.enabled = True
    if args.only:
        only = set(s.strip() for s in args.only.split(","))
        for f in formats:
            f.enabled = f.enabled and f.name in only

    corpus = sorted(REPO.glob(args.glob))
    if args.limit:
        corpus = corpus[: args.limit]
    if not corpus:
        print(f"no corpus files matched: {args.glob}", file=sys.stderr)
        return 2

    args.out.mkdir(parents=True, exist_ok=True)
    work_root = args.out / "_work"
    work_root.mkdir(parents=True, exist_ok=True)

    pandoc = pr.find_pandoc() if args.visual else None
    if args.visual and pandoc is None:
        print("WARNING: pandoc not found — DocBook/Man visual panels will be skipped.")

    print(f"Sweeping {len(corpus)} docs × {sum(1 for f in formats if f.enabled)} formats"
          f"{' (with --visual)' if args.visual else ''}...")
    all_results: list[Result] = []
    for i, src in enumerate(corpus, 1):
        print(f"  [{i}/{len(corpus)}] {src.name}")
        results = process_doc(src, work_root, args.out, formats,
                              visual=args.visual, pandoc=pandoc)
        all_results.extend(results)
        # Per-doc inline status
        for r in results:
            if r.error:
                print(f"      {r.format_name}: {r.error}")
            elif r.diff_value is not None:
                print(f"      {r.format_name}: {r.diff_value}")

    write_summary(args.out, all_results, formats)
    print(f"\nDone. Summary: {args.out / '_summary.md'}")
    if args.visual:
        # Build an index of all generated visual panels for easy browsing.
        visual_paths = sorted(args.out.rglob("side-by-side.png"))
        if visual_paths:
            lines = ["# Visual Panels Index", "",
                     f"{len(visual_paths)} side-by-side panels generated. Open each PNG to inspect:",
                     ""]
            for p in visual_paths:
                rel = p.relative_to(args.out)
                lines.append(f"- `{rel}`")
            (args.out / "_visual-index.md").write_text("\n".join(lines), encoding="utf-8")
            print(f"Visual index: {args.out / '_visual-index.md'} ({len(visual_paths)} panels)")

    # Cleanup work dir to keep output dir small (per-format diffs preserved in subdirs)
    shutil.rmtree(work_root, ignore_errors=True)

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
