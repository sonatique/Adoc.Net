using System.Text;

namespace AdocNet.Stem;

/// <summary>
/// Translates AsciiMath (the notation of <c>asciimath:[]</c>, and of <c>stem:[]</c> by default) to
/// LaTeX, after the grammar and symbol tables of asciimath.org:
/// <c>S ::= v | lEr | uS | bSS</c>, <c>I ::= S_S | S^S | S_S^S | S</c>, <c>E ::= IE | I/I</c>.
/// </summary>
internal static class AsciiMath
{
    private enum Kind { Const, Unary, Text, Binary, Left, Right }

    private readonly record struct Symbol(Kind Kind, string Latex);

    private static readonly Dictionary<string, Symbol> Symbols = BuildSymbols();

    // Longest name first, so "<=>" wins over "<=" and "sube" over "sub".
    private static readonly string[] Names = Symbols.Keys.OrderByDescending(k => k.Length).ToArray();

    private static Dictionary<string, Symbol> BuildSymbols()
    {
        var s = new Dictionary<string, Symbol>(StringComparer.Ordinal);
        // "name latex name latex ...": pairs separated by spaces; '~' in a LaTeX value stands for a space.
        void Add(Kind kind, string pairs)
        {
            var parts = pairs.Split(' ');
            for (int i = 0; i + 1 < parts.Length; i += 2)
                s[parts[i]] = new Symbol(kind, parts[i + 1].Replace('~', ' '));
        }

        Add(Kind.Const, @"+ + - - * \cdot ** \ast *** \star // / \\ \backslash setminus \setminus xx \times |>< \ltimes ><| \rtimes |><| \bowtie -: \div divide \div @ \circ o+ \oplus ox \otimes o. \odot sum \sum prod \prod ^^ \wedge ^^^ \bigwedge vv \vee vvv \bigvee nn \cap nnn \bigcap uu \cup uuu \bigcup");
        Add(Kind.Const, @"= = != \neq < < > > <= \le lt= \le >= \ge gt= \ge -< \prec -<= \preceq >- \succ >-= \succeq in \in !in \notin sub \subset sup \supset sube \subseteq supe \supseteq -= \equiv ~= \cong ~~ \approx ~ \sim prop \propto");
        Add(Kind.Const, @"not \neg => \Rightarrow <=> \Leftrightarrow AA \forall EE \exists _|_ \bot TT \top |-- \vdash |== \models and \text{~and~} or \text{~or~} if \text{~if~}");
        Add(Kind.Const, @"int \int oint \oint del \partial grad \nabla +- \pm -+ \mp O/ \emptyset oo \infty aleph \aleph /_ \angle /_\ \triangle ' ' quad \quad qquad \qquad cdots \cdots vdots \vdots ddots \ddots ldots \ldots ... \ldots diamond \diamond square \square |__ \lfloor __| \rfloor |~ \lceil ~| \rceil CC \mathbb{C} NN \mathbb{N} QQ \mathbb{Q} RR \mathbb{R} ZZ \mathbb{Z} , , lim \lim Lim \operatorname{Lim}");
        Add(Kind.Const, @"dx {dx} dy {dy} dz {dz} dt {dt}");
        Add(Kind.Const, @"uarr \uparrow darr \downarrow rarr \rightarrow -> \to >-> \rightarrowtail ->> \twoheadrightarrow >->> \twoheadrightarrow |-> \mapsto larr \leftarrow harr \leftrightarrow rArr \Rightarrow lArr \Leftarrow hArr \Leftrightarrow");
        Add(Kind.Const, @"sin \sin cos \cos tan \tan sec \sec csc \csc cot \cot arcsin \arcsin arccos \arccos arctan \arctan sinh \sinh cosh \cosh tanh \tanh sech \sech csch \csch coth \coth exp \exp log \log ln \ln det \det dim \dim gcd \gcd min \min max \max mod \operatorname{mod} lcm \operatorname{lcm} lub \operatorname{lub} glb \operatorname{glb}");
        Add(Kind.Const, @"alpha \alpha beta \beta gamma \gamma Gamma \Gamma delta \delta Delta \Delta epsilon \epsilon varepsilon \varepsilon zeta \zeta eta \eta theta \theta Theta \Theta vartheta \vartheta iota \iota kappa \kappa lambda \lambda Lambda \Lambda mu \mu nu \nu xi \xi Xi \Xi pi \pi Pi \Pi rho \rho sigma \sigma Sigma \Sigma tau \tau upsilon \upsilon phi \phi Phi \Phi varphi \varphi chi \chi psi \psi Psi \Psi omega \omega Omega \Omega");

        // One argument, put in place of {0}.
        Add(Kind.Unary, @"sqrt \sqrt{{0}} hat \hat{{0}} bar \overline{{0}} overline \overline{{0}} ul \underline{{0}} underline \underline{{0}} vec \vec{{0}} tilde \tilde{{0}} dot \dot{{0}} ddot \ddot{{0}} obrace \overline{{0}} overbrace \overline{{0}} ubrace \underbrace{{0}} underbrace \underbrace{{0}}");
        Add(Kind.Unary, @"abs \left|~{0}~\right| floor \left\lfloor~{0}~\right\rfloor ceil \left\lceil~{0}~\right\rceil norm \left\|~{0}~\right\|");
        Add(Kind.Unary, @"bb \mathbf{{0}} bbb \mathbb{{0}} cc \mathcal{{0}} tt \mathtt{{0}} fr \mathfrak{{0}} sf \mathsf{{0}} rm \mathrm{{0}}");
        Add(Kind.Text, @"text \text{{0}} mbox \text{{0}}");

        // Two arguments, {0} and {1}. ponytail: overset, underset and stackrel become a superscript or
        // subscript, as the typesetter has no \overset; upgrade if it gains one.
        Add(Kind.Binary, @"frac \frac{{0}}{{1}} root \sqrt[{0}]{{1}} stackrel {{1}}^{{0}} overset {{1}}^{{0}} underset {{1}}_{{0}} color \color{{0}}{{1}}");

        // Brackets; "." is an invisible one ({: and :}).
        Add(Kind.Left, @"( ( [ [ { \{ (: \langle << \langle {: .");
        Add(Kind.Right, @") ) ] ] } \} :) \rangle >> \rangle :} .");
        return s;
    }

    /// <summary>The LaTeX for an AsciiMath formula.</summary>
    public static string ToLatex(string asciiMath)
    {
        var parser = new Parser(asciiMath ?? "");
        var parts = new List<string>();
        while (!parser.AtEnd)
        {
            // A right bracket with no left one is shown as is.
            if (parser.TryRight(out var stray))
                parts.Add(stray == "." ? "" : stray);
            else
                parts.Add(parser.Expression());
        }
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    /// <summary>
    /// A parsed piece: its LaTeX; for a bracketed group, also its content without the brackets (what an
    /// argument keeps, as brackets around one only group it) and its comma-separated entries.
    /// </summary>
    private readonly record struct Node(string Latex, string? Inner = null, IReadOnlyList<string>? Entries = null)
    {
        public string Argument => Inner ?? Latex;
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public bool AtEnd { get { SkipSpaces(); return _pos >= text.Length; } }

        private void SkipSpaces()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private bool Peek(string s) => string.CompareOrdinal(text, _pos, s, 0, s.Length) == 0;

        /// <summary>The longest symbol name at the position, not consumed.</summary>
        private string? PeekSymbol()
        {
            SkipSpaces();
            foreach (var name in Names)
                if (Peek(name)) return name;
            return null;
        }

        private bool AtRight() => PeekSymbol() is { } name && Symbols[name].Kind == Kind.Right;

        public bool TryRight(out string latex)
        {
            latex = "";
            if (!AtRight()) return false;
            var name = PeekSymbol()!;
            _pos += name.Length;
            latex = Symbols[name].Latex;
            return true;
        }

        /// <summary>E ::= IE | I/I, up to a right bracket or the end.</summary>
        public string Expression() => string.Join(" ", ExpressionNodes().Select(n => n.Latex));

        private List<Node> ExpressionNodes()
        {
            var nodes = new List<Node>();
            while (!AtEnd && !AtRight())
            {
                var node = Intermediate();
                SkipSpaces();
                if (IsOperator("/"))
                {
                    _pos++;
                    var denominator = AtEnd || AtRight() ? new Node("") : Intermediate();
                    node = new Node(@"\frac{" + node.Argument + "}{" + denominator.Argument + "}");
                }
                nodes.Add(node);
            }
            return nodes;
        }

        /// <summary>True at "/", "_" or "^" used alone, not as the start of a longer symbol ("//", "_|_", "^^").</summary>
        private bool IsOperator(string mark)
        {
            SkipSpaces();
            return Peek(mark) && PeekSymbol() is null;
        }

        /// <summary>I ::= S_S | S^S | S_S^S | S.</summary>
        private Node Intermediate()
        {
            var node = Simple();
            var latex = node.Latex;
            bool scripted = false;
            foreach (var mark in new[] { "_", "^" })
            {
                SkipSpaces();
                if (!IsOperator(mark)) continue;
                _pos++;
                var script = AtEnd || AtRight() ? new Node("") : Simple();
                latex = (latex.Length == 0 ? "{}" : latex) + mark + "{" + script.Argument + "}";
                scripted = true;
            }
            return scripted ? new Node(latex) : node;
        }

        /// <summary>S ::= v | lEr | uS | bSS.</summary>
        private Node Simple()
        {
            SkipSpaces();
            if (_pos >= text.Length)
                return new Node("");

            if (text[_pos] == '"')
            {
                int end = text.IndexOf('"', _pos + 1);
                if (end < 0) end = text.Length;
                var quoted = text.Substring(_pos + 1, end - _pos - 1);
                _pos = Math.Min(text.Length, end + 1);
                return new Node(@"\text{" + EscapeText(quoted) + "}");
            }

            if (PeekSymbol() is { } name)
            {
                var symbol = Symbols[name];
                _pos += name.Length;
                switch (symbol.Kind)
                {
                    case Kind.Left:
                        return Group(symbol.Latex);
                    case Kind.Unary:
                        return new Node(symbol.Latex.Replace("{0}", Simple().Argument));
                    case Kind.Text:
                        return new Node(symbol.Latex.Replace("{0}", EscapeText(RawArgument())));
                    case Kind.Binary:
                        // A colour is a name or a code, not math.
                        var first = name == "color" ? RawArgument() : Simple().Argument;
                        var second = Simple().Argument;
                        return new Node(symbol.Latex.Replace("{0}", first).Replace("{1}", second));
                    default:
                        return new Node(symbol.Latex);
                }
            }

            char c = text[_pos];
            if (char.IsDigit(c))
            {
                int start = _pos;
                while (_pos < text.Length && (char.IsDigit(text[_pos])
                       || (text[_pos] == '.' && _pos + 1 < text.Length && char.IsDigit(text[_pos + 1]))))
                    _pos++;
                return new Node(text.Substring(start, _pos - start));
            }

            _pos++;
            if (c == '|' && _pos < text.Length && text[_pos] == '|')
            {
                _pos++;
                return new Node(@"\|");
            }
            return new Node(c switch
            {
                '#' or '%' or '&' or '$' => @"\" + c,
                '\\' => @"\backslash",
                _ => c.ToString(),
            });
        }

        /// <summary>lEr: a bracketed expression; a matrix when it lists bracketed rows of equal length.</summary>
        private Node Group(string open)
        {
            var content = ExpressionNodes();
            string close = TryRight(out var right) ? right : ".";
            string Wrap(string body) => @"\left" + open + " " + body + @" \right" + close;

            var rows = content.Count >= 3 ? Rows(content) : null;
            if (rows is not null)
                return new Node(Wrap(@"\begin{matrix}" + string.Join(@" \\ ", rows.Select(r => string.Join(" & ", r))) + @"\end{matrix}"));

            var inner = string.Join(" ", content.Select(n => n.Latex));
            var entries = new List<string> { "" };
            foreach (var node in content)
            {
                if (node.Latex == ",") entries.Add("");
                else entries[^1] = (entries[^1] + " " + node.Latex).Trim();
            }
            return new Node(Wrap(inner), inner, entries.Count > 1 ? entries : null);
        }

        /// <summary>The rows of [[a,b],[c,d]]: bracketed groups between commas, each with the same number (2+) of entries.</summary>
        private static List<IReadOnlyList<string>>? Rows(List<Node> content)
        {
            var rows = new List<IReadOnlyList<string>>();
            for (int i = 0; i < content.Count; i++)
            {
                if (i % 2 == 1)
                {
                    if (content[i].Latex != ",") return null;
                }
                else if (content[i].Entries is { } entries && (rows.Count == 0 || entries.Count == rows[0].Count))
                {
                    rows.Add(entries);
                }
                else
                {
                    return null;
                }
            }
            return content.Count % 2 == 1 ? rows : null;
        }

        /// <summary>The text of text(...) as written, up to the matching bracket.</summary>
        private string RawArgument()
        {
            SkipSpaces();
            if (_pos >= text.Length || text[_pos] is not ('(' or '[' or '{'))
                return Simple().Argument;
            char open = text[_pos], close = open switch { '(' => ')', '[' => ']', _ => '}' };
            int depth = 0, start = _pos + 1;
            for (; _pos < text.Length; _pos++)
            {
                if (text[_pos] == open) depth++;
                else if (text[_pos] == close && --depth == 0) break;
            }
            var raw = text.Substring(start, Math.Min(_pos, text.Length) - start);
            _pos = Math.Min(text.Length, _pos + 1);
            return raw;
        }

        private static string EscapeText(string s) =>
            s.Replace(@"\", "").Replace("{", @"\{").Replace("}", @"\}");
    }
}
