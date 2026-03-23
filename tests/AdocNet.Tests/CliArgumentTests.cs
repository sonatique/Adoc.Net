using System.IO;
using AdocNet.Cli;
using AdocNet.Converters.Html;

namespace AdocNet.Tests;

[TestFixture]
public class CliArgumentTests
{
    // ── Format parsing ──────────────────────────────────────────────────

    [Test]
    public void Format_docbook_parses_correctly()
    {
        var result = Program.ParseArguments(["-b", "docbook", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Format, Is.EqualTo(OutputFormat.DocBook));
    }

    [Test]
    public void Format_epub_parses_correctly()
    {
        var result = Program.ParseArguments(["-b", "epub", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Format, Is.EqualTo(OutputFormat.Epub));
    }

    [Test]
    public void Format_xml_is_alias_for_docbook()
    {
        var result = Program.ParseArguments(["-b", "xml", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Format, Is.EqualTo(OutputFormat.DocBook));
    }

    [Test]
    public void Format_html_parses_correctly()
    {
        var result = Program.ParseArguments(["-b", "html", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Format, Is.EqualTo(OutputFormat.Html));
    }

    [Test]
    public void Format_pdf_parses_correctly()
    {
        var result = Program.ParseArguments(["-b", "pdf", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Format, Is.EqualTo(OutputFormat.Pdf));
    }

    [Test]
    public void Format_unknown_returns_error()
    {
        var result = Program.ParseArguments(["-b", "unknown", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        var error = (CliArgs.Error)result;
        Assert.That(error.Message, Does.Contain("Unknown format"));
    }

    // ── Styled flag ─────────────────────────────────────────────────────

    [Test]
    public void Styled_flag_sets_Styled_true()
    {
        var result = Program.ParseArguments(["-e", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Styled, Is.True);
    }

    // ── Theme parsing ───────────────────────────────────────────────────

    [Test]
    public void Theme_default_sets_theme_and_styled()
    {
        var result = Program.ParseArguments(["--theme", "default", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Theme, Is.EqualTo(HtmlTheme.Default));
        Assert.That(run.Styled, Is.True);
    }

    [Test]
    public void Theme_asciidoctor_sets_theme()
    {
        var result = Program.ParseArguments(["--theme", "asciidoctor", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Theme, Is.EqualTo(HtmlTheme.Asciidoctor));
    }

    [Test]
    public void Theme_clean_sets_theme()
    {
        var result = Program.ParseArguments(["--theme", "clean", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Theme, Is.EqualTo(HtmlTheme.Clean));
    }

    [Test]
    public void Theme_unknown_returns_error()
    {
        var result = Program.ParseArguments(["--theme", "unknown", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        var error = (CliArgs.Error)result;
        Assert.That(error.Message, Does.Contain("Unknown theme"));
    }

    [Test]
    public void Theme_without_value_returns_error()
    {
        var result = Program.ParseArguments(["--theme"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        var error = (CliArgs.Error)result;
        Assert.That(error.Message, Does.Contain("--theme"));
    }

    // ── Out-dir parsing ─────────────────────────────────────────────

    [Test]
    public void OutDir_flag_sets_OutDir()
    {
        var result = Program.ParseArguments(["-D", "build", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.OutDir, Is.EqualTo("build"));
    }

    [Test]
    public void OutDir_without_value_returns_error()
    {
        var result = Program.ParseArguments(["-D"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        var error = (CliArgs.Error)result;
        Assert.That(error.Message, Does.Contain("-D"));
    }

    // ── Watch flag ────────────────────────────────────────────────────

    [Test]
    public void Watch_long_flag_sets_Watch_true()
    {
        var result = Program.ParseArguments(["--watch", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Watch, Is.True);
    }

    [Test]
    public void Watch_short_flag_sets_Watch_true()
    {
        var result = Program.ParseArguments(["-w", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Watch, Is.True);
    }

    // ── Verbose flag ──────────────────────────────────────────────────

    [Test]
    public void Verbose_long_flag_sets_Verbose_true()
    {
        var result = Program.ParseArguments(["--verbose", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Verbose, Is.True);
    }

    [Test]
    public void Verbose_short_flag_sets_Verbose_true()
    {
        var result = Program.ParseArguments(["-v", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Verbose, Is.True);
    }

    // ── Quiet flag ────────────────────────────────────────────────────

    [Test]
    public void Quiet_long_flag_sets_Quiet_true()
    {
        var result = Program.ParseArguments(["--quiet", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Quiet, Is.True);
    }

    [Test]
    public void Quiet_short_flag_sets_Quiet_true()
    {
        var result = Program.ParseArguments(["-q", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Quiet, Is.True);
    }

    // ── Recursive flag ────────────────────────────────────────────────

    [Test]
    public void Recursive_long_flag_sets_Recursive_true()
    {
        var result = Program.ParseArguments(["--recursive", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Recursive, Is.True);
    }

    [Test]
    public void Recursive_short_flag_sets_Recursive_true()
    {
        var result = Program.ParseArguments(["-r", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.Recursive, Is.True);
    }

    // ── Verbose + Quiet conflict ──────────────────────────────────────

    [Test]
    public void Verbose_and_Quiet_together_returns_error()
    {
        var result = Program.ParseArguments(["--verbose", "--quiet", "input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        var error = (CliArgs.Error)result;
        Assert.That(error.Message, Does.Contain("verbose"));
        Assert.That(error.Message, Does.Contain("quiet"));
    }

    // ── -o with directory input ────────────────────────────────────────

    [Test]
    public void Output_file_with_directory_input_returns_error()
    {
        var result = Program.ParseArguments(["-o", "out.html", Path.GetTempPath()]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        Assert.That(((CliArgs.Error)result).Message, Does.Contain("-o").And.Contain("directory"));
    }

    // ── Preview subcommand ──────────────────────────────────────────

    [Test]
    public void Preview_subcommand_parses_correctly()
    {
        var result = Program.ParseArguments(["preview", "docs"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Preview>());
        var preview = (CliArgs.Preview)result;
        Assert.That(preview.InputPath, Is.EqualTo("docs"));
        Assert.That(preview.Port, Is.EqualTo(5500));
        Assert.That(preview.NoOpen, Is.False);
    }

    [Test]
    public void Preview_with_port_parses_correctly()
    {
        var result = Program.ParseArguments(["preview", "docs", "--port", "8080"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Preview>());
        Assert.That(((CliArgs.Preview)result).Port, Is.EqualTo(8080));
    }

    [Test]
    public void Preview_with_no_open_parses_correctly()
    {
        var result = Program.ParseArguments(["preview", "docs", "--no-open"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Preview>());
        Assert.That(((CliArgs.Preview)result).NoOpen, Is.True);
    }

    [Test]
    public void Preview_without_input_returns_error()
    {
        var result = Program.ParseArguments(["preview"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        Assert.That(((CliArgs.Error)result).Message, Does.Contain("preview"));
    }

    [Test]
    public void Preview_with_invalid_port_returns_error()
    {
        var result = Program.ParseArguments(["preview", "docs", "--port", "abc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
        Assert.That(((CliArgs.Error)result).Message, Does.Contain("port"));
    }

    // ── Help text ───────────────────────────────────────────────────────

    [Test]
    public void Help_text_mentions_all_four_formats()
    {
        using var writer = new StringWriter();
        Program.PrintHelp(writer);
        var help = writer.ToString();

        Assert.That(help, Does.Contain("html"));
        Assert.That(help, Does.Contain("pdf"));
        Assert.That(help, Does.Contain("docbook"));
        Assert.That(help, Does.Contain("epub"));
    }

    [Test]
    public void Help_text_mentions_styled_and_theme_options()
    {
        using var writer = new StringWriter();
        Program.PrintHelp(writer);
        var help = writer.ToString();

        Assert.That(help, Does.Contain("--embedded"));
        Assert.That(help, Does.Contain("--theme"));
    }

    [Test]
    public void Help_text_mentions_new_v09_flags()
    {
        using var writer = new StringWriter();
        Program.PrintHelp(writer);
        var help = writer.ToString();

        Assert.That(help, Does.Contain("-D"));
        Assert.That(help, Does.Contain("--watch"));
        Assert.That(help, Does.Contain("--verbose"));
        Assert.That(help, Does.Contain("--quiet"));
        Assert.That(help, Does.Contain("--recursive"));
        Assert.That(help, Does.Contain("directory"));
    }
}
