using AdocNet.Converters.Html;

namespace AdocNet.Cli;

internal static class ConfigMerger
{
    public static CliArgs.Run Merge(CliArgs.Run args, ProjectConfig? config)
    {
        if (config is null)
            return args;

        var format = args.Format;
        if (format == OutputFormat.Html && config.Format is not null)
        {
            format = config.Format.ToLowerInvariant() switch
            {
                "html" => OutputFormat.Html,
                "pdf" => OutputFormat.Pdf,
                "docbook" or "xml" => OutputFormat.DocBook,
                "epub" => OutputFormat.Epub,
                _ => format,
            };
        }

        var outDir = args.OutDir ?? config.OutDir;

        var recursive = args.Recursive || (config.Recursive ?? false);

        var styled = args.Styled;
        var theme = args.Theme;

        if (!styled && config.Theme is not null)
        {
            var parsedTheme = config.Theme.ToLowerInvariant() switch
            {
                "default" => HtmlTheme.Default,
                "asciidoctor" => HtmlTheme.Asciidoctor,
                "clean" => HtmlTheme.Clean,
                _ => (HtmlTheme?)null,
            };

            if (parsedTheme is not null)
            {
                theme = parsedTheme.Value;
                styled = true;
            }
        }

        if (!styled && (config.Styled ?? false))
            styled = true;

        IReadOnlyDictionary<string, string>? attributes = args.Attributes ?? config.Attributes;

        return args with
        {
            Format = format,
            OutDir = outDir,
            Recursive = recursive,
            Styled = styled,
            Theme = theme,
            Attributes = attributes,
        };
    }
}
