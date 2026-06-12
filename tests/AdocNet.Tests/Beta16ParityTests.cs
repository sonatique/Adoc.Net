using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class Beta16ParityTests
{
    // ── Collapsible blocks ──────────────────────────────────────────────

    [Test]
    public void Collapsible_example_block_renders_as_details()
    {
        var input = "[%collapsible]\n====\nHidden content.\n====";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("<details>"));
        Assert.That(html, Does.Contain("<summary"));
        Assert.That(html, Does.Contain("</details>"));
    }

    [Test]
    public void Collapsible_block_with_title_uses_title_as_summary()
    {
        var input = "[%collapsible]\n.Click to expand\n====\nHidden.\n====";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("<summary class=\"title\">Click to expand</summary>"));
    }

    [Test]
    public void Collapsible_block_without_title_uses_details_as_summary()
    {
        var input = "[%collapsible]\n====\nHidden.\n====";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("<summary class=\"title\">Details</summary>"));
    }

    [Test]
    public void Non_collapsible_example_block_has_no_details_element()
    {
        var input = "====\nVisible content.\n====";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Not.Contain("<details>"));
        Assert.That(html, Does.Contain("<div"));
    }

    [Test]
    public void Parser_sets_IsCollapsible_on_delimited_block()
    {
        var input = "[%collapsible]\n====\nContent.\n====";
        var doc = AdocParser.Parse(input).Document;

        var block = doc.Children.OfType<DelimitedBlockNode>().FirstOrDefault();
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.IsCollapsible, Is.True);
    }

    [Test]
    public void Parser_does_not_set_IsCollapsible_without_option()
    {
        var input = "====\nContent.\n====";
        var doc = AdocParser.Parse(input).Document;

        var block = doc.Children.OfType<DelimitedBlockNode>().FirstOrDefault();
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.IsCollapsible, Is.False);
    }

    // ── Data URI embedding ──────────────────────────────────────────────

    [Test]
    public void DataUri_image_renders_base64_src()
    {
        // Create a temporary image file
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var imgPath = Path.Combine(tempDir, "test.png");
            var imgBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes
            File.WriteAllBytes(imgPath, imgBytes);

            var doc = new DocumentNode();
            doc.SetAttribute("data-uri", "");
            doc.AddChild(new BlockImageNode { Target = "test.png", Alt = "test" });

            var options = new HtmlRenderOptions { BaseDirectory = tempDir };
            var html = new HtmlRenderer().RenderToString(doc, options);

            Assert.That(html, Does.Contain("data:image/png;base64,"));
            Assert.That(html, Does.Not.Contain("src=\"test.png\""));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void DataUri_missing_image_falls_back_to_path()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("data-uri", "");
        doc.AddChild(new BlockImageNode { Target = "nonexistent.png", Alt = "test" });

        var options = new HtmlRenderOptions { BaseDirectory = Path.GetTempPath() };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("src=\"nonexistent.png\""));
        Assert.That(html, Does.Not.Contain("data:"));
    }

    [Test]
    public void DataUri_out_of_base_image_blocked_by_default_but_allowed_when_Unsafe()
    {
        // Security: :data-uri: must not let a document embed arbitrary local files via an
        // absolute / out-of-base image path when rendering with the default (Safe) mode.
        var rootDir = Path.Combine(Path.GetTempPath(), "adocnet-datauri-" + Guid.NewGuid().ToString("N")[..8]);
        var baseDir = Path.Combine(rootDir, "docs");
        var outsideDir = Path.Combine(rootDir, "secret");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outsideDir);
        try
        {
            var secretImg = Path.Combine(outsideDir, "secret.png");
            File.WriteAllBytes(secretImg, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var doc = new DocumentNode();
            doc.SetAttribute("data-uri", "");
            doc.AddChild(new BlockImageNode { Target = secretImg, Alt = "x" }); // absolute path

            // Default (Safe): refused — falls back to the literal path, no embedding.
            var safeHtml = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { BaseDirectory = baseDir });
            Assert.That(safeHtml, Does.Not.Contain("data:image/png;base64,"));

            // Unsafe: explicit opt-in allows out-of-tree embedding.
            var unsafeHtml = new HtmlRenderer().RenderToString(doc,
                new HtmlRenderOptions { BaseDirectory = baseDir, SafeMode = SafeMode.Unsafe });
            Assert.That(unsafeHtml, Does.Contain("data:image/png;base64,"));
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
    }

    [Test]
    public void DataUri_disabled_renders_plain_path()
    {
        var doc = new DocumentNode();
        // No data-uri attribute
        doc.AddChild(new BlockImageNode { Target = "image.png", Alt = "test" });

        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("src=\"image.png\""));
        Assert.That(html, Does.Not.Contain("data:"));
    }

    // ── Font Awesome CSS injection ──────────────────────────────────────

    [Test]
    public void Icons_font_injects_font_awesome_css_link()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("icons", "font");

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("font-awesome"));
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\""));
    }

    [Test]
    public void Icons_font_with_custom_cdn_uses_custom_url()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("icons", "font");
        doc.SetAttribute("iconfont-cdn", "https://example.com/fa.css");

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("https://example.com/fa.css"));
    }

    [Test]
    public void No_icons_attribute_does_not_inject_font_awesome()
    {
        var doc = new DocumentNode { Title = "Test" };

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Not.Contain("font-awesome"));
        Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\""));
    }

    [Test]
    public void Icons_image_does_not_inject_font_awesome()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("icons", "image");

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Not.Contain("font-awesome"));
    }

    // ── Docinfo injection ───────────────────────────────────────────────

    [Test]
    public void Docinfo_shared_head_injects_in_head()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-docinfo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "docinfo.html"), "<meta name=\"custom\" content=\"test\">");

            var doc = new DocumentNode { Title = "Test" };
            doc.SetAttribute("docinfo", "shared");

            var options = new HtmlRenderOptions { FullDocument = true, BaseDirectory = tempDir };
            var html = new HtmlRenderer().RenderToString(doc, options);

            Assert.That(html, Does.Contain("<meta name=\"custom\" content=\"test\">"));
            // Should be inside <head>, before </head>
            var headEnd = html.IndexOf("</head>", StringComparison.Ordinal);
            var metaPos = html.IndexOf("custom", StringComparison.Ordinal);
            Assert.That(metaPos, Is.LessThan(headEnd));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Docinfo_shared_footer_injects_before_body_close()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-docinfo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "docinfo-footer.html"), "<script>alert('footer')</script>");

            var doc = new DocumentNode { Title = "Test" };
            doc.SetAttribute("docinfo", "shared");

            var options = new HtmlRenderOptions { FullDocument = true, BaseDirectory = tempDir };
            var html = new HtmlRenderer().RenderToString(doc, options);

            Assert.That(html, Does.Contain("<script>alert('footer')</script>"));
            // Should be before </body>
            var bodyEnd = html.IndexOf("</body>", StringComparison.Ordinal);
            var scriptPos = html.IndexOf("alert('footer')", StringComparison.Ordinal);
            Assert.That(scriptPos, Is.LessThan(bodyEnd));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void No_docinfo_attribute_means_no_injection()
    {
        var doc = new DocumentNode { Title = "Test" };

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Not.Contain("docinfo"));
    }

    // ── Safe modes ──────────────────────────────────────────────────────

    [Test]
    public void SafeMode_Unsafe_allows_includes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "included.adoc"), "Included text.");
            var input = "include::included.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = tempDir,
                SafeMode = SafeMode.Unsafe,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Contain("Included text"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_Safe_blocks_parent_directory_includes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "secret.adoc"), "Secret content.");
            var input = "include::../secret.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = subDir,
                SafeMode = SafeMode.Safe,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Not.Contain("Secret content"));
            Assert.That(result.Diagnostics, Has.Some.Property("Message").Contains("outside base directory"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_default_is_Safe_and_blocks_parent_directory_includes()
    {
        // Blocker regression: the default (no explicit SafeMode) must confine includes to the
        // base directory so processing an untrusted document cannot disclose arbitrary files.
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "secret.adoc"), "Secret content.");
            var input = "include::../secret.adoc[]";

            // Note: no SafeMode set — relying on the default.
            var options = new ParseOptions { BaseDirectory = subDir };
            Assert.That(options.SafeMode, Is.EqualTo(SafeMode.Safe));

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Not.Contain("Secret content"));
            Assert.That(result.Diagnostics, Has.Some.Property("Message").Contains("outside base directory"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_Safe_blocks_sibling_directory_with_shared_prefix()
    {
        // Regression: a sibling directory whose name shares the base directory as a
        // string prefix (base ".../docs" vs. ".../docs-private") must be blocked. A naive
        // StartsWith check on the resolved path let this through.
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        var baseDir = Path.Combine(tempDir, "docs");
        var siblingDir = Path.Combine(tempDir, "docs-private");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(siblingDir);
        try
        {
            File.WriteAllText(Path.Combine(siblingDir, "secret.adoc"), "Sibling secret.");
            var input = "include::../docs-private/secret.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = baseDir,
                SafeMode = SafeMode.Safe,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Not.Contain("Sibling secret"));
            Assert.That(result.Diagnostics, Has.Some.Property("Message").Contains("outside base directory"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_Safe_allows_includes_within_base_directory()
    {
        // The boundary fix must not break legitimate in-tree includes.
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        var baseDir = Path.Combine(tempDir, "docs");
        var nested = Path.Combine(baseDir, "chapters");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(nested, "intro.adoc"), "Legitimate include.");
            var input = "include::chapters/intro.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = baseDir,
                SafeMode = SafeMode.Safe,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Contain("Legitimate include"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_Secure_blocks_all_includes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "included.adoc"), "Should not appear.");
            var input = "include::included.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = tempDir,
                SafeMode = SafeMode.Secure,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Not.Contain("Should not appear"));
            Assert.That(result.Diagnostics, Has.Some.Property("Message").Contains("safe mode"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SafeMode_Server_blocks_all_includes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-safe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "data.adoc"), "Server data.");
            var input = "include::data.adoc[]";

            var options = new ParseOptions
            {
                BaseDirectory = tempDir,
                SafeMode = SafeMode.Server,
            };

            var result = AdocParser.Parse(input, options);
            var html = new HtmlRenderer().RenderToString(result.Document);

            Assert.That(html, Does.Not.Contain("Server data"));
            Assert.That(result.Diagnostics, Has.Some.Property("Message").Contains("safe mode"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── STEM/Math ───────────────────────────────────────────────────────

    [Test]
    public void Stem_block_with_latexmath_renders_display_math()
    {
        var input = ":stem: latexmath\n\n[stem]\n--\nx = \\frac{-b}{2a}\n--";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("<div class=\"stemblock\">"));
        Assert.That(html, Does.Contain("\\["));
        Assert.That(html, Does.Contain("\\frac{-b}{2a}"));
        Assert.That(html, Does.Contain("\\]"));
    }

    [Test]
    public void Stem_block_with_asciimath_renders_with_dollar_delimiters()
    {
        var input = ":stem: asciimath\n\n[asciimath]\n--\nsum_(i=1)^n i^3\n--";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("<div class=\"stemblock\">"));
        Assert.That(html, Does.Contain("\\$"));
        Assert.That(html, Does.Contain("sum_(i=1)^n i^3"));
    }

    [Test]
    public void Latexmath_inline_macro_renders_inline_math()
    {
        var input = "The formula latexmath:[E=mc^2] is famous.";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("\\(E=mc^2\\)"));
    }

    [Test]
    public void Stem_inline_macro_defaults_to_asciimath()
    {
        // Asciidoctor's default stem interpreter is asciimath, so stem:[...] without an explicit
        // :stem: type emits AsciiMath dollar delimiters, not LaTeX \(...\).
        var input = "The formula stem:[x^2+y^2] is a circle.";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("\\$x^2+y^2\\$"));
    }

    [Test]
    public void Stem_inline_macro_honors_latexmath_stem_attribute()
    {
        var input = ":stem: latexmath\n\nThe formula stem:[x^2+y^2] is a circle.";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("\\(x^2+y^2\\)"));
    }

    [Test]
    public void Stem_block_over_passthrough_delimiter_is_recognized()
    {
        // Canonical Asciidoctor form: [stem] over a ++++ passthrough block (not just open --).
        var input = ":stem: asciimath\n\n[stem]\n++++\nsqrt(4) = 2\n++++";
        var doc = AdocParser.Parse(input).Document;

        var stemBlock = doc.Children.OfType<StemBlockNode>().FirstOrDefault();
        Assert.That(stemBlock, Is.Not.Null);
        Assert.That(stemBlock!.StemType, Is.EqualTo("asciimath"));
        Assert.That(stemBlock.Content, Does.Contain("sqrt(4) = 2"));

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("<div class=\"stemblock\">"));
        Assert.That(html, Does.Contain("\\$sqrt(4) = 2\\$"));
    }

    [Test]
    public void Asciimath_inline_macro_uses_dollar_delimiters()
    {
        var input = "The formula asciimath:[sum x] here.";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Contain("\\$sum x\\$"));
    }

    [Test]
    public void MathJax_script_injected_when_stem_attribute_set()
    {
        var doc = new DocumentNode { Title = "Math Doc" };
        doc.SetAttribute("stem", "latexmath");

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("mathjax"));
        Assert.That(html, Does.Contain("<script src="));
    }

    [Test]
    public void No_stem_attribute_means_no_MathJax()
    {
        var doc = new DocumentNode { Title = "No Math" };

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Not.Contain("mathjax"));
        Assert.That(html, Does.Not.Contain("MathJax"));
    }

    [Test]
    public void Stem_block_content_is_verbatim()
    {
        // Ensure *bold* is NOT rendered as <strong> inside stem blocks
        var input = ":stem:\n\n[stem]\n--\n*not bold* x^2\n--";
        var doc = AdocParser.Parse(input).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Not.Contain("<strong>"));
        Assert.That(html, Does.Contain("*not bold*"));
    }

    [Test]
    public void Parser_creates_StemBlockNode_for_stem_style()
    {
        var input = ":stem:\n\n[stem]\n--\nx^2\n--";
        var doc = AdocParser.Parse(input).Document;

        var stemBlock = doc.Children.OfType<StemBlockNode>().FirstOrDefault();
        Assert.That(stemBlock, Is.Not.Null);
        // Empty :stem: defaults to asciimath (Asciidoctor parity), not latexmath.
        Assert.That(stemBlock!.StemType, Is.EqualTo("asciimath"));
        Assert.That(stemBlock.Content, Does.Contain("x^2"));
    }

    [Test]
    public void Parser_creates_StemInlineNode_for_latexmath_macro()
    {
        var input = "Formula: latexmath:[E=mc^2].";
        var doc = AdocParser.Parse(input).Document;

        var para = doc.Children.OfType<ParagraphNode>().FirstOrDefault();
        Assert.That(para, Is.Not.Null);
        var stemInline = para!.Inlines.OfType<StemInlineNode>().FirstOrDefault();
        Assert.That(stemInline, Is.Not.Null);
        Assert.That(stemInline!.Content, Is.EqualTo("E=mc^2"));
        Assert.That(stemInline.StemType, Is.EqualTo("latexmath"));
    }

    [Test]
    public void Asciimath_stem_attribute_injects_asciimath_config()
    {
        var doc = new DocumentNode { Title = "AM" };
        doc.SetAttribute("stem", "asciimath");

        var options = new HtmlRenderOptions { FullDocument = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("asciimath"));
        Assert.That(html, Does.Contain("mathjax"));
    }
}
