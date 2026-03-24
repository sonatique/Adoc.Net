using AdocNet.Highlighting;

namespace AdocNet.Tests;

[TestFixture]
public class SyntaxTokenizerTests
{
    // ── Round-trip: concatenated tokens = original source ────────────────

    [TestCase("csharp", "public class Foo { }")]
    [TestCase("java", "public class Foo { }")]
    [TestCase("javascript", "const x = 42;")]
    [TestCase("python", "def foo():\n    pass")]
    [TestCase("json", """{"key": "value", "num": 42}""")]
    [TestCase("xml", """<root attr="val">text</root>""")]
    [TestCase("sql", "SELECT * FROM users WHERE id = 1")]
    public void Tokens_concatenate_to_original_source(string lang, string source)
    {
        var tokens = SyntaxTokenizer.Tokenize(source, lang);
        var reconstructed = string.Concat(tokens.Select(t => t.Text));
        Assert.That(reconstructed, Is.EqualTo(source));
    }

    // ── C# ──────────────────────────────────────────────────────────────

    [Test]
    public void CSharp_class_keyword_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("public class Foo { }", "csharp");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "public"));
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "class"));
    }

    [Test]
    public void CSharp_string_literal_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("var s = \"hello\";", "csharp");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.String && t.Text.Contains("hello")));
    }

    [Test]
    public void CSharp_comment_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("// a comment\nint x;", "csharp");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Comment && t.Text.Contains("comment")));
    }

    [Test]
    public void CSharp_number_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("int x = 42;", "csharp");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Number && t.Text == "42"));
    }

    [Test]
    public void CSharp_preprocessor_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("#if DEBUG\n#endif", "csharp");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Preprocessor));
    }

    // ── JavaScript ──────────────────────────────────────────────────────

    [Test]
    public void JavaScript_keywords_are_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("const x = 42;", "js");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "const"));
    }

    // ── Python ──────────────────────────────────────────────────────────

    [Test]
    public void Python_keywords_are_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("def foo():\n    return True", "python");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "def"));
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "return"));
    }

    // ── JSON ────────────────────────────────────────────────────────────

    [Test]
    public void JSON_string_and_number_are_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("{\"key\": 42}", "json");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.String));
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Number && t.Text == "42"));
    }

    // ── XML ─────────────────────────────────────────────────────────────

    [Test]
    public void XML_tag_is_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("<root>text</root>", "xml");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text.Contains("root")));
    }

    // ── SQL ─────────────────────────────────────────────────────────────

    [Test]
    public void SQL_keywords_are_tokenized()
    {
        var tokens = SyntaxTokenizer.Tokenize("SELECT * FROM users", "sql");
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "SELECT"));
        Assert.That(tokens.Any(t => t.Kind == TokenKind.Keyword && t.Text == "FROM"));
    }

    // ── Unsupported language ────────────────────────────────────────────

    [Test]
    public void Unsupported_language_returns_single_plain_token()
    {
        var tokens = SyntaxTokenizer.Tokenize("hello world", "rust");
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Plain));
    }

    [Test]
    public void Null_language_returns_single_plain_token()
    {
        var tokens = SyntaxTokenizer.Tokenize("hello world", null);
        Assert.That(tokens, Has.Count.EqualTo(1));
        Assert.That(tokens[0].Kind, Is.EqualTo(TokenKind.Plain));
    }

    // ── Language support ────────────────────────────────────────────────

    [TestCase("csharp")]
    [TestCase("cs")]
    [TestCase("java")]
    [TestCase("javascript")]
    [TestCase("js")]
    [TestCase("python")]
    [TestCase("py")]
    [TestCase("json")]
    [TestCase("xml")]
    [TestCase("html")]
    [TestCase("sql")]
    public void Language_is_supported(string lang)
    {
        Assert.That(SyntaxTokenizer.IsLanguageSupported(lang), Is.True);
    }

    [Test]
    public void Tokenization_is_deterministic()
    {
        var source = "public class Foo { int x = 42; }";
        var tokens1 = SyntaxTokenizer.Tokenize(source, "csharp");
        var tokens2 = SyntaxTokenizer.Tokenize(source, "csharp");
        Assert.That(tokens1, Is.EqualTo(tokens2));
    }
}
