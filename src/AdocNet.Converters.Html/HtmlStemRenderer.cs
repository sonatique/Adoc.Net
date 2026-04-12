using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    private static void RenderStemBlock(StringBuilder sb, StemBlockNode block)
    {
        sb.Append("<div class=\"stemblock\">\n");
        if (block.Title is not null)
        {
            sb.Append("<div class=\"title\">");
            EscapeTo(sb, block.Title);
            sb.Append("</div>\n");
        }
        sb.Append("<div class=\"content\">\n");
        if (string.Equals(block.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("\\$");
            sb.Append(block.Content);
            sb.Append("\\$");
        }
        else
        {
            sb.Append("\\[");
            sb.Append(block.Content);
            sb.Append("\\]");
        }
        sb.Append("\n</div>\n</div>\n");
    }
}
