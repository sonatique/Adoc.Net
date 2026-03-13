using System.Text;

namespace AdocNet.Benchmarks;

/// <summary>
/// Generates synthetic AsciiDoc documents of various sizes and complexity
/// for benchmarking purposes.
/// </summary>
internal static class DocumentGenerator
{
    /// <summary>Generates a small ~1 KB document with basic structure.</summary>
    public static string Small()
    {
        return """
            = Small Document

            == Introduction

            This is a *bold* and _italic_ paragraph with `monospace` text.
            Visit https://example.com for more information.

            == Section Two

            * Item one
            * Item two with *bold*
            * Item three

            . First
            . Second
            . Third

            A final paragraph with link:https://example.com[a link macro] and image:logo.png[Logo].
            """;
    }

    /// <summary>Generates a ~50 KB document simulating a documentation page.</summary>
    public static string Medium()
    {
        var sb = new StringBuilder();
        sb.AppendLine("= Medium Documentation Page");
        sb.AppendLine(":author: Test Author");
        sb.AppendLine(":version: 1.0");
        sb.AppendLine(":baseurl: https://example.com");
        sb.AppendLine();

        for (int section = 1; section <= 10; section++)
        {
            sb.AppendLine($"== Section {section}");
            sb.AppendLine();

            // Paragraphs with inline formatting
            for (int p = 0; p < 5; p++)
            {
                sb.AppendLine($"This is paragraph {p + 1} in section {section} with *bold text*, _italic text_, and `monospace code`. " +
                    $"Here is a URL https://example.com/page/{section}/{p} and an attribute reference {{baseurl}}/docs.");
                sb.AppendLine();
            }

            // A list
            sb.AppendLine($"=== Subsection {section}.1 — Features");
            sb.AppendLine();
            for (int item = 0; item < 8; item++)
                sb.AppendLine($"* Feature {item + 1}: supports *inline* formatting in _list_ items");
            sb.AppendLine();

            // A code block
            sb.AppendLine("[source,csharp]");
            sb.AppendLine("----");
            sb.AppendLine($"public class Example{section}");
            sb.AppendLine("{");
            sb.AppendLine($"    public string Name {{ get; set; }} = \"Section {section}\";");
            sb.AppendLine($"    public int Value {{ get; set; }} = {section};");
            sb.AppendLine("}");
            sb.AppendLine("----");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Generates a ~500 KB document simulating a book chapter.</summary>
    public static string Large()
    {
        var sb = new StringBuilder();
        sb.AppendLine("= Large Documentation Chapter");
        sb.AppendLine(":toc:");
        sb.AppendLine(":author: Benchmark Suite");
        sb.AppendLine();

        for (int chapter = 1; chapter <= 50; chapter++)
        {
            sb.AppendLine($"== Chapter {chapter}");
            sb.AppendLine();

            for (int section = 1; section <= 5; section++)
            {
                sb.AppendLine($"=== Section {chapter}.{section}");
                sb.AppendLine();

                // Dense paragraph text
                for (int p = 0; p < 8; p++)
                {
                    sb.Append($"Paragraph {p + 1} of section {chapter}.{section}. ");
                    sb.Append("This paragraph contains *bold*, _italic_, and `monospace` formatting. ");
                    sb.Append("It also includes a bare URL https://example.com/chapter/" + chapter + " ");
                    sb.Append("and link:https://docs.example.com[documentation links]. ");
                    sb.AppendLine("The text continues with more content to simulate realistic document density.");
                    sb.AppendLine();
                }

                // Lists
                for (int item = 0; item < 5; item++)
                    sb.AppendLine($"* List item {item + 1} with *formatting* and https://example.com");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>Generates a document heavy on tables.</summary>
    public static string TableHeavy()
    {
        var sb = new StringBuilder();
        sb.AppendLine("= Table-Heavy Document");
        sb.AppendLine();

        for (int t = 0; t < 20; t++)
        {
            sb.AppendLine($"== Table {t + 1}");
            sb.AppendLine();
            sb.AppendLine("[options=\"header\"]");
            sb.AppendLine("|===");
            sb.AppendLine("| Name | Type | Description | Default");
            for (int row = 0; row < 15; row++)
                sb.AppendLine($"| field_{t}_{row} | string | A *field* description | value_{row}");
            sb.AppendLine("|===");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Generates a document heavy on list nesting.</summary>
    public static string ListHeavy()
    {
        var sb = new StringBuilder();
        sb.AppendLine("= List-Heavy Document");
        sb.AppendLine();

        for (int section = 0; section < 30; section++)
        {
            sb.AppendLine($"== Section {section + 1}");
            sb.AppendLine();

            for (int item = 0; item < 10; item++)
            {
                sb.AppendLine($"* Top-level item {item + 1} with *bold*");
                sb.AppendLine($"** Nested item A under {item + 1}");
                sb.AppendLine($"** Nested item B with _italic_ text");
                sb.AppendLine($"*** Deep nested item with `code`");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
