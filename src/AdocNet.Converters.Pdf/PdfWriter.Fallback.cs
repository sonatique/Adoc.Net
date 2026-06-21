using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AdocNet.Converters.Pdf;

internal sealed partial class PdfWriter
{
    // ── Unicode glyph fallback chain (issues #52, #72, #75) ──────────────
    //
    // The standard PDF base fonts are WinAnsi-encoded and can't show characters
    // like ✓, →, or ⇒ (the typographic output of `->`/`=>` etc.), which otherwise
    // rendered as '?'. These ordered fallback fonts form a chain: each codepoint
    // is routed to the FIRST font (after the primary) that has a glyph for it
    // (see FontForCodePoint). Fonts are parsed on first need and each is
    // registered (hence embedded + subset + given a ToUnicode CMap) only once a
    // glyph from it is actually used, so a document with no special characters
    // embeds nothing extra, and the symbol font is embedded only when a glyph
    // DejaVu lacks is actually used.

    private static readonly string[] FallbackFontResources =
    {
        // 1) DejaVu Sans: the general cover font for arrows (incl. ⇒/⇐), dingbats
        //    (✓ ✗), geometric shapes, bullets, math symbols, and more.
        "AdocNet.Converters.Pdf.Resources.DejaVuSans.ttf",
        // 2) Symbola: a broad symbol font consulted only for codepoints DejaVu
        //    lacks — notably SMP symbol blocks (e.g. U+1F6C7 🛇) the Avalonia
        //    preview shows via system fallback but DejaVu can't (#75).
        "AdocNet.Converters.Pdf.Resources.Symbola.ttf",
    };

    private sealed class FallbackCandidate
    {
        public required TrueTypeFont Font;
        public string? Key; // embedded font key, assigned on first use
    }

    private List<FallbackCandidate>? _fallbackCandidates;

    private List<FallbackCandidate> FallbackCandidates()
    {
        if (_fallbackCandidates is not null) return _fallbackCandidates;
        _fallbackCandidates = new List<FallbackCandidate>();
        var asm = typeof(PdfWriter).Assembly;
        foreach (var res in FallbackFontResources)
        {
            try
            {
                using var s = asm.GetManifestResourceStream(res);
                if (s is null) continue;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _fallbackCandidates.Add(new FallbackCandidate { Font = TrueTypeFont.Parse(ms.ToArray()) });
            }
            catch
            {
                // A missing/unparseable fallback font just leaves '?' behaviour intact.
            }
        }
        return _fallbackCandidates;
    }

    /// <summary>True when <paramref name="codePoint"/> cannot be shown by <paramref name="primaryFont"/>.</summary>
    private bool NeedsFallback(int codePoint, string primaryFont)
    {
        if (_embeddedFonts.TryGetValue(primaryFont, out var ttf))
            return ttf.GetGlyphId(codePoint) == 0;
        // Standard WinAnsi base font: a non-BMP codepoint is never representable;
        // a BMP one is representable when ≤ 0xFF or explicitly mapped.
        if (codePoint > 0xFFFF) return true;
        return codePoint > 0xFF && MapUnicodeToWinAnsi((char)codePoint) == "?";
    }

    /// <summary>
    /// Resolves the font key that should render <paramref name="codePoint"/>: the
    /// primary font when it can show it, otherwise the first fallback font that has
    /// the glyph (registered on first use), otherwise the primary font (renders '?').
    /// </summary>
    private string FontForCodePoint(int codePoint, string primaryFont)
    {
        if (!NeedsFallback(codePoint, primaryFont)) return primaryFont;
        foreach (var cand in FallbackCandidates())
        {
            if (cand.Font.GetGlyphId(codePoint) != 0)
            {
                cand.Key ??= RegisterEmbeddedFont($"__fb{_embeddedFonts.Count}", cand.Font);
                return cand.Key;
            }
        }
        return primaryFont;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into consecutive runs that share a render
    /// font, routing characters the primary font can't show to a fallback font.
    /// Iterates by Unicode codepoint so a non-BMP character (a UTF-16 surrogate
    /// pair) is treated as one glyph rather than two missing halves (issue #72).
    /// Returns <c>null</c> (fast path) when the whole string renders in
    /// <paramref name="primaryFont"/> — the common case — so ordinary text is
    /// measured and emitted exactly as before.
    /// </summary>
    internal List<(string Text, string Font)>? SplitFontRuns(string text, string primaryFont)
    {
        if (string.IsNullOrEmpty(text)) return null;

        bool needsAny = false;
        foreach (var cp in PdfFontEmbedder.EnumerateCodePoints(text))
            if (NeedsFallback(cp, primaryFont)) { needsAny = true; break; }
        if (!needsAny) return null;

        var runs = new List<(string, string)>();
        var sb = new StringBuilder();
        string runFont = primaryFont;
        foreach (var cp in PdfFontEmbedder.EnumerateCodePoints(text))
        {
            string f = FontForCodePoint(cp, primaryFont);
            if (sb.Length > 0 && f != runFont)
            {
                runs.Add((sb.ToString(), runFont));
                sb.Clear();
            }
            if (sb.Length == 0) runFont = f;
            sb.Append(char.ConvertFromUtf32(cp));
        }
        if (sb.Length > 0) runs.Add((sb.ToString(), runFont));
        return runs;
    }

    /// <summary>
    /// Expands text segments so each renders in a single font, splitting out runs
    /// of characters the segment's own font can't show onto a fallback font.
    /// Returns the input unchanged (no allocation) when nothing needs fallback.
    /// </summary>
    internal List<TextSegment> ExpandSegmentsForFallback(List<TextSegment> segments)
    {
        List<TextSegment>? result = null;
        for (int i = 0; i < segments.Count; i++)
        {
            var runs = SplitFontRuns(segments[i].Text, segments[i].Font);
            if (runs is null)
            {
                result?.Add(segments[i]);
                continue;
            }
            result ??= new List<TextSegment>(segments.GetRange(0, i));
            var seg = segments[i];
            foreach (var (t, f) in runs)
                result.Add(seg with { Text = t, Font = f });
        }
        return result ?? segments;
    }
}
