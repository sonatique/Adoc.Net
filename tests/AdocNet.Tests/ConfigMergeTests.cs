using AdocNet.Cli;
using AdocNet.Converters.Html;

namespace AdocNet.Tests;

[TestFixture]
public class ConfigMergeTests
{
    private static CliArgs.Run DefaultArgs(string input = "input.adoc") =>
        new(input, null, null, false);

    [Test]
    public void Config_provides_defaults_for_unset_flags()
    {
        var config = new ProjectConfig
        {
            Format = "docbook",
            Recursive = true,
            Styled = true,
        };

        var result = ConfigMerger.Merge(DefaultArgs(), config);

        Assert.That(result.Format, Is.EqualTo(OutputFormat.DocBook));
        Assert.That(result.Recursive, Is.True);
        Assert.That(result.Styled, Is.True);
    }

    [Test]
    public void Cli_flags_override_config()
    {
        var args = new CliArgs.Run("input.adoc", null, null, false, Format: OutputFormat.Pdf);
        var config = new ProjectConfig { Format = "docbook" };

        var result = ConfigMerger.Merge(args, config);

        Assert.That(result.Format, Is.EqualTo(OutputFormat.Pdf));
    }

    [Test]
    public void Null_config_returns_args_unchanged()
    {
        var args = DefaultArgs();

        var result = ConfigMerger.Merge(args, null);

        Assert.That(result, Is.SameAs(args));
    }

    [Test]
    public void Config_outDir_applies_when_cli_unset()
    {
        var config = new ProjectConfig { OutDir = "dist" };

        var result = ConfigMerger.Merge(DefaultArgs(), config);

        Assert.That(result.OutDir, Is.EqualTo("dist"));
    }

    [Test]
    public void Config_theme_applies_and_sets_styled_true()
    {
        var config = new ProjectConfig { Theme = "asciidoctor" };

        var result = ConfigMerger.Merge(DefaultArgs(), config);

        Assert.That(result.Theme, Is.EqualTo(HtmlTheme.Asciidoctor));
        Assert.That(result.Styled, Is.True);
    }
}
