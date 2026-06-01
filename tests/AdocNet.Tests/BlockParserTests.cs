using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class BlockParserTests
{
    // ── Empty / blank input ────────────────────────────────────────────────────

    [Test]
    public void Empty_input_produces_empty_document()
    {
        var result = BlockParser.Parse("");
        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Title, Is.Null);
            Assert.That(result.Document.Children, Is.Empty);
            Assert.That(result.Document.Attributes.ContainsKey("backend"), Is.True); // default attributes are populated
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Blank_lines_only_produces_empty_document()
    {
        var result = BlockParser.Parse("\n\n\n");
        Assert.That(result.Document.Children, Is.Empty);
    }

    // ── Document title ─────────────────────────────────────────────────────────

    [Test]
    public void Title_only_sets_document_title()
    {
        var result = BlockParser.Parse("= My Document");
        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Title, Is.EqualTo("My Document"));
            Assert.That(result.Document.Children, Is.Empty);
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Line_with_single_equals_but_no_space_is_not_a_title()
    {
        var result = BlockParser.Parse("=NotATitle");
        Assert.That(result.Document.Title, Is.Null);
    }

    // ── Attributes ─────────────────────────────────────────────────────────────

    [Test]
    public void Title_and_attributes_are_parsed()
    {
        var input = "= My Document\n:author: Jane\n:version: 2.0";
        var result = BlockParser.Parse(input);
        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Title, Is.EqualTo("My Document"));
            Assert.That(result.Document.Attributes["author"], Is.EqualTo("Jane"));
            Assert.That(result.Document.Attributes["version"], Is.EqualTo("2.0"));
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Attribute_with_empty_value_is_accepted()
    {
        var result = BlockParser.Parse("= Doc\n:flag:");
        Assert.That(result.Document.Attributes["flag"], Is.EqualTo(string.Empty));
    }

    [Test]
    public void Malformed_attribute_produces_warning_diagnostic()
    {
        // :bad attr contains a space → truly malformed even with flag-style support
        var input = "= Doc\n:bad attr";
        var result = BlockParser.Parse(input);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(result.Diagnostics[0].Range.Start.Line, Is.EqualTo(2));
        });
    }

    [Test]
    public void Attributes_after_blank_line_are_parsed_as_body_attributes()
    {
        // After the blank line the header ends; ":foo: bar" is now a body attribute.
        var input = "= Doc\n\n:foo: bar";
        var result = BlockParser.Parse(input);
        Assert.Multiple(() =>
        {
            Assert.That(result.Document.Attributes["foo"], Is.EqualTo("bar"));
            Assert.That(result.Document.Children, Has.Count.EqualTo(0));
        });
    }

    // ── Sections ───────────────────────────────────────────────────────────────

    [Test]
    public void Single_section_with_one_paragraph()
    {
        var input = "= Doc\n\n== Introduction\n\nHello world.";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Title, Is.EqualTo("Doc"));
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));

        var section = (SectionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(section.Level, Is.EqualTo(1));
            Assert.That(section.Title, Is.EqualTo("Introduction"));
            Assert.That(section.Children, Has.Count.EqualTo(1));
        });

        Assert.That(((ParagraphNode)section.Children[0]).Text, Is.EqualTo("Hello world."));
    }

    [Test]
    public void Multiple_sections_each_own_paragraphs()
    {
        var input = "= Doc\n\n== First\n\nFirst paragraph.\n\n== Second\n\nSecond paragraph.";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Children, Has.Count.EqualTo(2));

        var s1 = (SectionNode)result.Document.Children[0];
        Assert.That(s1.Title, Is.EqualTo("First"));
        Assert.That(((ParagraphNode)s1.Children[0]).Text, Is.EqualTo("First paragraph."));

        var s2 = (SectionNode)result.Document.Children[1];
        Assert.That(s2.Title, Is.EqualTo("Second"));
        Assert.That(((ParagraphNode)s2.Children[0]).Text, Is.EqualTo("Second paragraph."));
    }

    [Test]
    public void Subsection_level_is_derived_from_equals_count()
    {
        var result = BlockParser.Parse("=== Deep");
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Level, Is.EqualTo(2));
    }

    [Test]
    public void Section_title_without_space_is_not_a_section()
    {
        var result = BlockParser.Parse("==NotASection");
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    // ── Paragraphs ─────────────────────────────────────────────────────────────

    [Test]
    public void Text_without_title_becomes_paragraph()
    {
        var result = BlockParser.Parse("Just some text.");
        Assert.That(result.Document.Title, Is.Null);
        Assert.That(((ParagraphNode)result.Document.Children[0]).Text, Is.EqualTo("Just some text."));
    }

    [Test]
    public void Text_before_first_section_is_preamble_under_document()
    {
        var input = "= Doc\n\nPreamble text.\n\n== Section One\n\nBody text.";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Children, Has.Count.EqualTo(2));

        var preamble = (ParagraphNode)result.Document.Children[0];
        Assert.That(preamble.Text, Is.EqualTo("Preamble text."));

        var section = (SectionNode)result.Document.Children[1];
        Assert.That(section.Title, Is.EqualTo("Section One"));
        Assert.That(((ParagraphNode)section.Children[0]).Text, Is.EqualTo("Body text."));
    }

    [Test]
    public void Consecutive_lines_form_single_paragraph()
    {
        var input = "Line one.\nLine two.\nLine three.";
        var result = BlockParser.Parse(input);
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(((ParagraphNode)result.Document.Children[0]).Text,
            Is.EqualTo("Line one.\nLine two.\nLine three."));
    }

    [Test]
    public void Blank_line_separates_paragraphs()
    {
        var input = "First.\n\nSecond.";
        var result = BlockParser.Parse(input);
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
    }

    // ── Source ranges ──────────────────────────────────────────────────────────

    [Test]
    public void Section_source_range_is_set_to_its_line()
    {
        var result = BlockParser.Parse("== Introduction");
        var section = (SectionNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(section.Source.Start.Line, Is.EqualTo(1));
            Assert.That(section.Source.Start.Column, Is.EqualTo(1));
            Assert.That(section.Source.End.Line, Is.EqualTo(1));
        });
    }

    [Test]
    public void Paragraph_source_range_spans_its_lines()
    {
        var input = "= Doc\n\n== S\n\nLine one.\nLine two.";
        var result = BlockParser.Parse(input);
        var section = (SectionNode)result.Document.Children[0];
        var para = (ParagraphNode)section.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(para.Source.Start.Line, Is.EqualTo(5));
            Assert.That(para.Source.End.Line, Is.EqualTo(6));
        });
    }

    // ── Substitutions on delimited blocks ───────────────────────────────────────

    [Test]
    public void Subs_attribute_stored_on_delimited_block()
    {
        var result = BlockParser.Parse("[subs=\"attributes\"]\n----\n{name}\n----");
        var block = result.Document.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(block.Substitutions, Is.EqualTo(SubstitutionKind.Attributes));
    }

    [Test]
    public void Subs_none_stored_on_listing_block()
    {
        var result = BlockParser.Parse("[subs=\"none\"]\n----\nraw content\n----");
        var block = result.Document.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(block.Substitutions, Is.EqualTo(SubstitutionKind.None));
    }

    [Test]
    public void Block_without_subs_has_null_substitutions()
    {
        var result = BlockParser.Parse("[source,java]\n----\ncode\n----");
        var block = result.Document.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(block.Substitutions, Is.Null);
    }

    // ── Discrete headings ─────────────────────────────────────────────────

    [Test]
    public void Discrete_heading_parsed_with_IsDiscrete_true()
    {
        var result = BlockParser.Parse("[discrete]\n== Not a real section\n\nParagraph after.");
        // Discrete heading should NOT nest as a section child — it's a sibling block
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.That(section.IsDiscrete, Is.True);
        Assert.That(section.Title, Is.EqualTo("Not a real section"));
        // Paragraph is a sibling, not nested under the discrete section
        Assert.That(result.Document.Children.OfType<ParagraphNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void Discrete_heading_gets_auto_id()
    {
        // Asciidoctor auto-generates IDs for discrete headings from the title text.
        var result = BlockParser.Parse("[discrete]\n== My Heading");
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.That(section.IsDiscrete, Is.True);
        Assert.That(section.Id, Is.EqualTo("_my_heading"));
    }

    [Test]
    public void Discrete_heading_with_explicit_id()
    {
        var result = BlockParser.Parse("[discrete#custom-id]\n== My Heading");
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.That(section.IsDiscrete, Is.True);
        Assert.That(section.Id, Is.EqualTo("custom-id"));
    }

    // ── Hardbreaks ────────────────────────────────────────────────────────

    [Test]
    public void Hardbreaks_option_sets_flag_on_paragraph()
    {
        var result = BlockParser.Parse("[%hardbreaks]\nLine one\nLine two\nLine three");
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        Assert.That(para.HasHardbreaks, Is.True);
    }

    [Test]
    public void Hardbreaks_document_attribute_sets_flag_on_all_paragraphs()
    {
        var result = BlockParser.Parse(":hardbreaks-option:\n\nFirst para\nline two\n\nSecond para\nline two");
        var paras = result.Document.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paras, Has.Count.EqualTo(2));
        Assert.That(paras[0].HasHardbreaks, Is.True);
        Assert.That(paras[1].HasHardbreaks, Is.True);
    }

    // ── Table of Contents ───────────────────────────────────────────────────────

    [Test]
    public void Toc_node_generated_when_toc_attribute_set()
    {
        var doc = BlockParser.Parse(":toc:\n\n== First\n\n=== Nested\n\n== Second").Document;
        var toc = doc.Children.OfType<TocNode>().FirstOrDefault();
        Assert.That(toc, Is.Not.Null);
        Assert.That(toc!.Entries, Has.Count.EqualTo(2));
        Assert.That(toc.Entries[0].Title, Is.EqualTo("First"));
        Assert.That(toc.Entries[0].Children, Has.Count.EqualTo(1));
        Assert.That(toc.Entries[0].Children[0].Title, Is.EqualTo("Nested"));
        Assert.That(toc.Entries[1].Title, Is.EqualTo("Second"));
    }

    [Test]
    public void Toc_respects_toclevels()
    {
        var doc = BlockParser.Parse(":toc:\n:toclevels: 1\n\n== First\n\n=== Nested\n\n== Second").Document;
        var toc = doc.Children.OfType<TocNode>().First();
        Assert.That(toc.Entries, Has.Count.EqualTo(2));
        // Nested is level 2, excluded by toclevels=1
        Assert.That(toc.Entries[0].Children, Has.Count.EqualTo(0));
    }

    [Test]
    public void Toc_not_generated_when_toc_not_set()
    {
        var doc = BlockParser.Parse("== First\n\n== Second").Document;
        var toc = doc.Children.OfType<TocNode>().FirstOrDefault();
        Assert.That(toc, Is.Null);
    }

    [Test]
    public void Toc_excludes_discrete_headings()
    {
        var doc = BlockParser.Parse(":toc:\n\n== Real\n\n[discrete]\n== Not In Toc\n\n== Also Real").Document;
        var toc = doc.Children.OfType<TocNode>().First();
        Assert.That(toc.Entries, Has.Count.EqualTo(2));
        Assert.That(toc.Entries.Select(e => e.Title), Is.EqualTo(new[] { "Real", "Also Real" }));
    }

    [Test]
    public void Toc_entries_have_section_ids()
    {
        var doc = BlockParser.Parse(":toc:\n\n== My Section").Document;
        var toc = doc.Children.OfType<TocNode>().First();
        Assert.That(toc.Entries[0].Id, Is.EqualTo("_my_section"));
    }

    [Test]
    public void Toc_placement_stored_from_attribute()
    {
        var doc = BlockParser.Parse(":toc: left\n\n== Section").Document;
        var toc = doc.Children.OfType<TocNode>().First();
        Assert.That(toc.Placement, Is.EqualTo(TocPlacement.Left));
    }

    // ── Author / Revision line parsing ──────────────────────────────────────

    [Test]
    public void Author_line_populates_author_attributes()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe <john@example.com>\n\nContent").Document;
        Assert.That(doc.Attributes["author"], Is.EqualTo("John Doe"));
        Assert.That(doc.Attributes["firstname"], Is.EqualTo("John"));
        Assert.That(doc.Attributes["lastname"], Is.EqualTo("Doe"));
        Assert.That(doc.Attributes["email"], Is.EqualTo("john@example.com"));
        Assert.That(doc.Attributes["authorinitials"], Is.EqualTo("JD"));
    }

    [Test]
    public void Author_line_with_middle_name()
    {
        var doc = BlockParser.Parse("= Title\nJohn Michael Doe <john@example.com>\n\nContent").Document;
        Assert.That(doc.Attributes["author"], Is.EqualTo("John Michael Doe"));
        Assert.That(doc.Attributes["firstname"], Is.EqualTo("John"));
        Assert.That(doc.Attributes["middlename"], Is.EqualTo("Michael"));
        Assert.That(doc.Attributes["lastname"], Is.EqualTo("Doe"));
    }

    [Test]
    public void Author_line_without_email()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe\n\nContent").Document;
        Assert.That(doc.Attributes["author"], Is.EqualTo("John Doe"));
        Assert.That(doc.Attributes.ContainsKey("email"), Is.False);
    }

    [Test]
    public void Multiple_authors_separated_by_semicolon()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe <john@ex.com>; Jane Smith <jane@ex.com>\n\nContent").Document;
        Assert.That(doc.Attributes["author"], Is.EqualTo("John Doe"));
        Assert.That(doc.Attributes["email"], Is.EqualTo("john@ex.com"));
        Assert.That(doc.Attributes["author_2"], Is.EqualTo("Jane Smith"));
        Assert.That(doc.Attributes["email_2"], Is.EqualTo("jane@ex.com"));
    }

    [Test]
    public void Revision_line_populates_rev_attributes()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe\nv1.0, 2024-01-15: Initial release\n\nContent").Document;
        Assert.That(doc.Attributes["revnumber"], Is.EqualTo("1.0"));
        Assert.That(doc.Attributes["revdate"], Is.EqualTo("2024-01-15"));
        Assert.That(doc.Attributes["revremark"], Is.EqualTo("Initial release"));
    }

    [Test]
    public void Revision_line_date_only()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe\n2024-01-15\n\nContent").Document;
        Assert.That(doc.Attributes.ContainsKey("revnumber"), Is.False);
        Assert.That(doc.Attributes["revdate"], Is.EqualTo("2024-01-15"));
    }

    [Test]
    public void Revision_line_without_v_prefix_still_extracts_revnumber()
    {
        // The 'v' prefix is optional: the part before the first comma is the
        // revnumber, matching Asciidoctor (verified via docbook5 output).
        var doc = BlockParser.Parse("= Title\nJohn Doe\n1.0, 2024-01-15: first\n\nContent").Document;
        Assert.That(doc.Attributes["revnumber"], Is.EqualTo("1.0"));
        Assert.That(doc.Attributes["revdate"], Is.EqualTo("2024-01-15"));
        Assert.That(doc.Attributes["revremark"], Is.EqualTo("first"));
    }

    [Test]
    public void No_author_when_first_line_after_title_is_attribute()
    {
        var doc = BlockParser.Parse("= Title\n:key: value\n\nContent").Document;
        Assert.That(doc.Attributes.ContainsKey("author"), Is.False);
        Assert.That(doc.Attributes["key"], Is.EqualTo("value"));
    }

    // ── Indented literal paragraphs ─────────────────────────────────────────────

    [Test]
    public void Indented_line_creates_literal_paragraph()
    {
        var doc = BlockParser.Parse("Normal paragraph.\n\n  indented line\n  another indented line\n\nBack to normal.").Document;
        var blocks = doc.Children.OfType<BlockNode>().ToList();
        Assert.That(blocks, Has.Count.EqualTo(3));
        Assert.That(blocks[0], Is.TypeOf<ParagraphNode>());
        Assert.That(blocks[1], Is.TypeOf<DelimitedBlockNode>());
        var literal = (DelimitedBlockNode)blocks[1];
        Assert.That(literal.BlockKind, Is.EqualTo(DelimitedBlockKind.Literal));
        Assert.That(literal.Content, Does.Contain("indented line"));
        Assert.That(literal.Content, Does.Contain("another indented line"));
        Assert.That(blocks[2], Is.TypeOf<ParagraphNode>());
    }

    [Test]
    public void Indented_literal_strips_common_indent()
    {
        var doc = BlockParser.Parse("Text\n\n    line one\n    line two\n\nMore text").Document;
        var literal = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(literal.Content, Is.EqualTo("line one\nline two"));
    }

    [Test]
    public void Single_space_indent_triggers_literal_paragraph()
    {
        var doc = BlockParser.Parse("Text\n\n code line\n\nMore").Document;
        var literal = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.That(literal.BlockKind, Is.EqualTo(DelimitedBlockKind.Literal));
    }

    // ── Video / Audio block macros ────────────────────────────────────────────

    [Test]
    public void Video_macro_parsed()
    {
        var doc = BlockParser.Parse("video::intro.mp4[width=640,height=480,poster=thumb.png]").Document;
        var video = doc.Children.OfType<VideoNode>().First();
        Assert.That(video.Target, Is.EqualTo("intro.mp4"));
        Assert.That(video.Width, Is.EqualTo("640"));
        Assert.That(video.Height, Is.EqualTo("480"));
        Assert.That(video.Poster, Is.EqualTo("thumb.png"));
    }

    [Test]
    public void Video_macro_with_options()
    {
        var doc = BlockParser.Parse("[%autoplay%loop%controls]\nvideo::clip.mp4[]").Document;
        var video = doc.Children.OfType<VideoNode>().First();
        Assert.That(video.Autoplay, Is.True);
        Assert.That(video.Loop, Is.True);
        Assert.That(video.Controls, Is.True);
    }

    [Test]
    public void Audio_macro_parsed()
    {
        var doc = BlockParser.Parse("audio::song.mp3[]").Document;
        var audio = doc.Children.OfType<AudioNode>().First();
        Assert.That(audio.Target, Is.EqualTo("song.mp3"));
    }

    [Test]
    public void Revision_line_without_author_populates_rev_attributes()
    {
        var result = BlockParser.Parse("= Title\nv1.0, 2024-01-15: Release\n\nContent");
        Assert.That(result.Document.Attributes.ContainsKey("author"), Is.False);
        Assert.That(result.Document.Attributes["revnumber"], Is.EqualTo("1.0"));
        Assert.That(result.Document.Attributes["revdate"], Is.EqualTo("2024-01-15"));
        Assert.That(result.Document.Attributes["revremark"], Is.EqualTo("Release"));
    }

    // ── Page break ────────────────────────────────────────────────────────────

    [Test]
    public void Page_break_between_paragraphs_produces_PageBreakNode()
    {
        var result = BlockParser.Parse("First paragraph.\n\n<<<\n\nSecond paragraph.");
        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.TypeOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.TypeOf<PageBreakNode>());
        Assert.That(result.Document.Children[2], Is.TypeOf<ParagraphNode>());
    }

    // ── Thematic break ────────────────────────────────────────────────────────

    [Test]
    public void Thematic_break_between_paragraphs_produces_ThematicBreakNode()
    {
        var result = BlockParser.Parse("First paragraph.\n\n'''\n\nSecond paragraph.");
        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.TypeOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.TypeOf<ThematicBreakNode>());
        Assert.That(result.Document.Children[2], Is.TypeOf<ParagraphNode>());
    }

    // ── Inline pass macro ─────────────────────────────────────────────────────

    [Test]
    public void Pass_macro_preserves_raw_content_in_paragraph()
    {
        var result = BlockParser.Parse("Text pass:[<em>raw</em>] end.");
        var para = (ParagraphNode)result.Document.Children[0];
        var passNode = para.Inlines.OfType<PassthroughInlineNode>().Single();
        Assert.That(passNode.Content, Is.EqualTo("<em>raw</em>"));
    }
}
