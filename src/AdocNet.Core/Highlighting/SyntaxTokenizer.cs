using System.Text.RegularExpressions;

namespace AdocNet.Highlighting;

/// <summary>
/// Tokenizes source code for syntax highlighting.
/// Supports C#, Java, JavaScript, Python, JSON, XML/HTML, and SQL.
/// Quality target: 80% correct for common patterns (not IDE-grade).
/// </summary>
public static class SyntaxTokenizer
{
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = "csharp", ["cs"] = "csharp", ["c#"] = "csharp",
        ["java"] = "java",
        ["javascript"] = "javascript", ["js"] = "javascript",
        ["python"] = "python", ["py"] = "python",
        ["json"] = "json",
        ["xml"] = "xml", ["html"] = "xml",
        ["sql"] = "sql",
    };

    private static readonly Dictionary<string, List<(Regex Pattern, TokenKind Kind)>> Languages = BuildLanguages();

    /// <summary>
    /// Tokenizes source code in the given language. Returns a flat list of tokens
    /// whose concatenated Text equals the original source.
    /// Unknown languages return a single Plain token containing the full source.
    /// </summary>
    public static List<SyntaxToken> Tokenize(string source, string? language)
    {
        if (string.IsNullOrEmpty(source))
            return [new SyntaxToken(TokenKind.Plain, source ?? "")];

        if (language is null || !LanguageAliases.TryGetValue(language, out var canonical))
            return [new SyntaxToken(TokenKind.Plain, source)];

        if (!Languages.TryGetValue(canonical, out var rules))
            return [new SyntaxToken(TokenKind.Plain, source)];

        return TokenizeWithRules(source, rules);
    }

    /// <summary>Returns true if the given language identifier is supported.</summary>
    public static bool IsLanguageSupported(string language) =>
        LanguageAliases.ContainsKey(language);

    /// <summary>Returns the CSS class name for a token kind, or null for Plain.</summary>
    public static string? GetCssClass(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => "hl-kw",
        TokenKind.String => "hl-s",
        TokenKind.Comment => "hl-c",
        TokenKind.Number => "hl-n",
        TokenKind.Type => "hl-t",
        TokenKind.Punctuation => "hl-p",
        TokenKind.Attribute => "hl-a",
        TokenKind.Preprocessor => "hl-pp",
        _ => null,
    };

    private static List<SyntaxToken> TokenizeWithRules(string source, List<(Regex Pattern, TokenKind Kind)> rules)
    {
        var tokens = new List<SyntaxToken>();
        int pos = 0;
        int plainStart = 0;

        while (pos < source.Length)
        {
            Match? bestMatch = null;
            TokenKind bestKind = TokenKind.Plain;

            foreach (var (pattern, kind) in rules)
            {
                var match = pattern.Match(source, pos);
                if (match.Success && match.Index == pos && match.Length > 0)
                {
                    bestMatch = match;
                    bestKind = kind;
                    break; // first match wins (rules are priority-ordered)
                }
            }

            if (bestMatch is not null)
            {
                // Flush accumulated plain text
                if (plainStart < pos)
                    tokens.Add(new SyntaxToken(TokenKind.Plain, source.Substring(plainStart, pos - plainStart)));

                tokens.Add(new SyntaxToken(bestKind, bestMatch.Value));
                pos += bestMatch.Length;
                plainStart = pos;
            }
            else
            {
                pos++;
            }
        }

        // Flush remaining plain text
        if (plainStart < source.Length)
            tokens.Add(new SyntaxToken(TokenKind.Plain, source.Substring(plainStart)));

        return tokens;
    }

    // ── Language definitions ─────────────────────────────────────────────

    private static Dictionary<string, List<(Regex, TokenKind)>> BuildLanguages()
    {
        return new Dictionary<string, List<(Regex, TokenKind)>>(StringComparer.Ordinal)
        {
            ["csharp"] = BuildCSharp(),
            ["java"] = BuildJava(),
            ["javascript"] = BuildJavaScript(),
            ["python"] = BuildPython(),
            ["json"] = BuildJson(),
            ["xml"] = BuildXml(),
            ["sql"] = BuildSql(),
        };
    }

    private static Regex R(string pattern) =>
        new(pattern, RegexOptions.Compiled);

    private static List<(Regex, TokenKind)> BuildCSharp() =>
    [
        (R(@"//[^\n]*"), TokenKind.Comment),
        (R(@"/\*[\s\S]*?\*/"), TokenKind.Comment),
        (R(@"#\s*(?:if|else|elif|endif|define|undef|region|endregion|pragma|nullable|error|warning|line)\b[^\n]*"), TokenKind.Preprocessor),
        (R(@"\[[\w.]+(?:\([^)]*\))?\]"), TokenKind.Attribute),
        (R(@"@""(?:[^""]|"""")*"""), TokenKind.String),
        (R(@"\$""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"'(?:[^'\\]|\\.)'"), TokenKind.String),
        (R(@"\b(?:abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while|yield|async|await|when|record|init|required|global|nint|nuint)\b"), TokenKind.Keyword),
        (R(@"\b(?:bool|byte|char|decimal|double|float|int|long|sbyte|short|uint|ulong|ushort|string|object|void|var|dynamic|nint|nuint)\b"), TokenKind.Type),
        (R(@"\b(?:0[xX][0-9a-fA-F_]+[lLuU]*|0[bB][01_]+[lLuU]*|\d[\d_]*\.?\d*(?:[eE][+-]?\d+)?[fFdDmMlLuU]*)"), TokenKind.Number),
        (R(@"[{}()\[\];,.:?!<>=+\-*/%&|^~@]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildJava() =>
    [
        (R(@"//[^\n]*"), TokenKind.Comment),
        (R(@"/\*[\s\S]*?\*/"), TokenKind.Comment),
        (R(@"@\w+"), TokenKind.Attribute),
        (R(@"""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"'(?:[^'\\]|\\.)'"), TokenKind.String),
        (R(@"\b(?:abstract|assert|boolean|break|byte|case|catch|char|class|const|continue|default|do|double|else|enum|extends|final|finally|float|for|goto|if|implements|import|instanceof|int|interface|long|native|new|null|package|private|protected|public|return|short|static|strictfp|super|switch|synchronized|this|throw|throws|transient|try|void|volatile|while|true|false|var|record|sealed|permits|yield)\b"), TokenKind.Keyword),
        (R(@"\b(?:boolean|byte|char|double|float|int|long|short|void|String|Integer|Long|Double|Float|Boolean|Character|Object|List|Map|Set|Array)\b"), TokenKind.Type),
        (R(@"\b(?:0[xX][0-9a-fA-F_]+[lL]?|0[bB][01_]+[lL]?|\d[\d_]*\.?\d*(?:[eE][+-]?\d+)?[fFdDlL]?)"), TokenKind.Number),
        (R(@"[{}()\[\];,.:?!<>=+\-*/%&|^~@]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildJavaScript() =>
    [
        (R(@"//[^\n]*"), TokenKind.Comment),
        (R(@"/\*[\s\S]*?\*/"), TokenKind.Comment),
        (R(@"`(?:[^`\\]|\\.)*`"), TokenKind.String),
        (R(@"""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"'(?:[^'\\]|\\.)*'"), TokenKind.String),
        (R(@"\b(?:async|await|break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|finally|for|from|function|if|import|in|instanceof|let|new|null|of|return|static|super|switch|this|throw|try|typeof|undefined|var|void|while|with|yield|true|false)\b"), TokenKind.Keyword),
        (R(@"\b(?:Array|Boolean|Date|Error|Function|JSON|Map|Math|Number|Object|Promise|Proxy|RegExp|Set|String|Symbol|WeakMap|WeakSet|console|document|window)\b"), TokenKind.Type),
        (R(@"\b(?:0[xX][0-9a-fA-F]+|0[oO][0-7]+|0[bB][01]+|\d+\.?\d*(?:[eE][+-]?\d+)?n?)"), TokenKind.Number),
        (R(@"[{}()\[\];,.:?!<>=+\-*/%&|^~@]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildPython() =>
    [
        (R(@"#[^\n]*"), TokenKind.Comment),
        (R(@"@\w+"), TokenKind.Attribute),
        (R("(?:\"{3}[\\s\\S]*?\"{3}|'''[\\s\\S]*?''')"), TokenKind.String),
        (R(@"""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"'(?:[^'\\]|\\.)*'"), TokenKind.String),
        (R(@"\b(?:False|None|True|and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield)\b"), TokenKind.Keyword),
        (R(@"\b(?:int|float|str|bool|list|dict|tuple|set|bytes|type|object|range|complex|frozenset|bytearray|memoryview)\b"), TokenKind.Type),
        (R(@"\b(?:0[xX][0-9a-fA-F_]+|0[oO][0-7_]+|0[bB][01_]+|\d[\d_]*\.?\d*(?:[eE][+-]?\d+)?[jJ]?)"), TokenKind.Number),
        (R(@"[{}()\[\];,.:?!<>=+\-*/%&|^~@]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildJson() =>
    [
        (R(@"""(?:[^""\\]|\\.)*"""), TokenKind.String),
        (R(@"\b(?:true|false|null)\b"), TokenKind.Keyword),
        (R(@"-?\d+\.?\d*(?:[eE][+-]?\d+)?"), TokenKind.Number),
        (R(@"[{}()\[\];,:]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildXml() =>
    [
        (R(@"<!--[\s\S]*?-->"), TokenKind.Comment),
        (R(@"<!\[CDATA\[[\s\S]*?\]\]>"), TokenKind.String),
        (R(@"</?[\w:.-]+"), TokenKind.Keyword),
        (R(@"/>|>"), TokenKind.Keyword),
        (R(@"""[^""]*"""), TokenKind.String),
        (R(@"'[^']*'"), TokenKind.String),
        (R(@"\b[\w:.-]+(?==)"), TokenKind.Attribute),
        (R(@"[=]"), TokenKind.Punctuation),
    ];

    private static List<(Regex, TokenKind)> BuildSql() =>
    [
        (R(@"--[^\n]*"), TokenKind.Comment),
        (R(@"/\*[\s\S]*?\*/"), TokenKind.Comment),
        (R(@"'(?:[^']|'')*'"), TokenKind.String),
        (R(@"\b(?:SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|CREATE|DROP|ALTER|TABLE|INDEX|VIEW|INTO|VALUES|SET|JOIN|INNER|LEFT|RIGHT|OUTER|CROSS|ON|AND|OR|NOT|IN|IS|NULL|AS|ORDER|BY|GROUP|HAVING|DISTINCT|UNION|ALL|EXISTS|BETWEEN|LIKE|LIMIT|OFFSET|TOP|CASE|WHEN|THEN|ELSE|END|BEGIN|COMMIT|ROLLBACK|GRANT|REVOKE|PRIMARY|KEY|FOREIGN|REFERENCES|CONSTRAINT|DEFAULT|CHECK|UNIQUE|CASCADE|TRUNCATE|EXEC|EXECUTE|DECLARE|IF|WHILE|RETURN|PROCEDURE|FUNCTION|TRIGGER|DATABASE|SCHEMA|USE|GO|WITH|COUNT|SUM|AVG|MIN|MAX|ASC|DESC)\b"), TokenKind.Keyword),
        (R(@"\b(?:INT|INTEGER|BIGINT|SMALLINT|TINYINT|FLOAT|REAL|DECIMAL|NUMERIC|CHAR|VARCHAR|NCHAR|NVARCHAR|TEXT|NTEXT|DATE|DATETIME|DATETIME2|TIMESTAMP|TIME|BIT|BINARY|VARBINARY|IMAGE|BLOB|CLOB|BOOLEAN|SERIAL|UUID|MONEY|XML|JSON)\b"), TokenKind.Type),
        (R(@"\b\d+\.?\d*\b"), TokenKind.Number),
        (R(@"[{}()\[\];,.:?!<>=+\-*/%&|^~@]"), TokenKind.Punctuation),
    ];
}
