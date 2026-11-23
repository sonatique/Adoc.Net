using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;
using AdocNet.Stem;

namespace AdocNet.Tests;

/// <summary>AdocNet.Stem: the AsciiMath translator and the CSharpMath-based renderer.</summary>
[TestFixture]
public class StemRendererTests
{
    [TestCase("sum_(i=1)^n i^3=((n(n+1))/2)^2", @"\sum_{i = 1}^{n} i^{3} = \left( \frac{n \left( n + 1 \right)}{2} \right)^{2}")]
    [TestCase("sqrt(x^2+y^2)", @"\sqrt{x^{2} + y^{2}}")]
    [TestCase("root(3)(x)", @"\sqrt[3]{x}")]
    [TestCase("(a+b)/(c+d)", @"\frac{a + b}{c + d}")]
    [TestCase("x_(i+1)^2", @"x_{i + 1}^{2}")]
    [TestCase("dy/dx", @"\frac{{dy}}{{dx}}")]
    [TestCase("lim_(x->oo) 1/x = 0", @"\lim_{x \to \infty} \frac{1}{x} = 0")]
    [TestCase("[[a,b],[c,d]]", @"\left[ \begin{matrix}a & b \\ c & d\end{matrix} \right]")]
    [TestCase("(1,2)", @"\left( 1 , 2 \right)")]
    [TestCase("abs(x) <= 1", @"\left| x \right| \le 1")]
    [TestCase("hat x + bar y + vec v", @"\hat{x} + \overline{y} + \vec{v}")]
    [TestCase("bbb N sube RR", @"\mathbb{N} \subseteq \mathbb{R}")]
    [TestCase("text(if ) x > 0", @"\text{if } x > 0")]
    [TestCase("\"for all\" x", @"\text{for all} x")]
    [TestCase("sin^2 x + cos^2 x = 1", @"\sin^{2} x + \cos^{2} x = 1")]
    [TestCase("x_1, ..., x_n", @"x_{1} , \ldots , x_{n}")]
    [TestCase("color(red)(x)", @"\color{red}{x}")]
    [TestCase("a // b _|_ c ^^ d", @"a / b \bot c \wedge d")]
    [TestCase("(: u, v :)", @"\left\langle u , v \right\rangle")]
    [TestCase("x)", @"x )")]
    [TestCase("(x", @"\left( x \right.")]
    [TestCase("100% & #1", @"100 \% \& \# 1")]
    public void AsciiMath_translates_to_latex(string asciiMath, string latex)
    {
        Assert.That(AsciiMath.ToLatex(asciiMath), Is.EqualTo(latex));
    }

    private static readonly StemRenderer Renderer = new();

    [Test]
    public void A_formula_becomes_an_outline_measured_in_em()
    {
        var graphic = Renderer.Render("x^2", "latexmath", display: false)!;

        Assert.Multiple(() =>
        {
            Assert.That(graphic.Width, Is.InRange(0.8, 1.5), "x² is about one em wide");
            Assert.That(graphic.Ascent, Is.InRange(0.5, 1.2));
            Assert.That(graphic.Descent, Is.InRange(-0.1, 0.3));
            // Only the commands every output reads (PDF's path parser has no Q).
            Assert.That(Regex.IsMatch(graphic.PathData, @"^([MLCZ]|-?\d+(\.\d+)?| )+$"), Is.True, graphic.PathData[..Math.Min(80, graphic.PathData.Length)]);
            Assert.That(graphic.PathData, Does.StartWith("M"));
        });
    }

    [Test]
    public void Display_style_sets_limits_above_and_below()
    {
        var inline = Renderer.Render(@"\sum_{i=1}^{n} i", "latexmath", display: false)!;
        var display = Renderer.Render(@"\sum_{i=1}^{n} i", "latexmath", display: true)!;

        Assert.That(display.Height, Is.GreaterThan(inline.Height * 1.3));
    }

    [Test]
    public void AsciiMath_content_is_translated_first()
    {
        var ascii = Renderer.Render("sqrt(x)", "asciimath", display: false)!;
        var latex = Renderer.Render(@"\sqrt{x}", "latexmath", display: false)!;

        Assert.That(ascii.PathData, Is.EqualTo(latex.PathData));
    }

    [TestCase(@"\frac{a")]
    [TestCase(@"\nosuchcommand")]
    [TestCase("")]
    [TestCase("   ")]
    public void Content_that_cannot_be_typeset_gives_null(string latex)
    {
        Assert.That(Renderer.Render(latex, "latexmath", display: false), Is.Null);
    }

    [TestCase("latexmath")]
    [TestCase("asciimath")]
    public void A_deeply_nested_formula_is_refused_not_overflowing_the_stack(string type)
    {
        var deep = new string('{', 1000) + "x" + new string('}', 1000);
        var parens = new string('(', 1000) + "x";

        Assert.That(Renderer.Render(deep, type, display: false), Is.Null);
        Assert.That(Renderer.Render(parens, type, display: false), Is.Null);
        Assert.That(Renderer.Render(new string('(', 40) + "x" + new string(')', 40), type, display: false), Is.Not.Null,
            "ordinary nesting still typesets");
    }

    [Test]
    public void Concurrent_rendering_gives_the_same_outlines()
    {
        var formulas = Enumerable.Range(0, 64).Select(i => $@"\frac{{{i}}}{{x_{i}}} + \sqrt{{{i}}}").ToArray();
        var expected = formulas.Select(f => new StemRenderer().Render(f, "latexmath", true)!.PathData).ToArray();

        var shared = new StemRenderer();
        var actual = new string[formulas.Length];
        Parallel.For(0, formulas.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            i => actual[i] = shared.Render(formulas[i], "latexmath", true)!.PathData);

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void Every_output_takes_the_real_outlines()
    {
        var doc = AdocParser.Parse("= Doc\n\nEnergy stem:[\\int_0^\\infty e^{-x}\\,dx = 1] and asciimath:[[[a,b],[c,d]]].\n\n[latexmath]\n++++\n\\frac{a}{b}\n++++\n").Document;

        var html = new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { StemRenderer = Renderer });
        var pdf = Encoding.Latin1.GetString(new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions { StemRenderer = Renderer }));

        Assert.That(Regex.Matches(html, "<svg ").Count, Is.EqualTo(3));
        Assert.That(Regex.Matches(pdf, @" c\n").Count, Is.GreaterThan(50), "glyph outlines drawn as curves");
    }
}
