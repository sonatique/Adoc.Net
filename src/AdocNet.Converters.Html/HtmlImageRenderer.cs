using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    private static void RenderBlockImage(StringBuilder sb, BlockImageNode image, HtmlRenderState state)
    {
        sb.Append("<div class=\"imageblock");
        if (image.Roles.Count > 0)
        {
            foreach (var role in image.Roles)
            {
                sb.Append(' ');
                EscapeTo(sb, role);
            }
        }
        sb.Append('"');
        if (image.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, image.Id);
            sb.Append('"');
        }
        sb.Append(">\n<div class=\"content\">\n");
        // Wrap in link if Link is set
        if (image.Link is not null)
        {
            sb.Append("<a class=\"image\" href=\"");
            EscapeTo(sb, image.Link);
            sb.Append("\">");
        }
        sb.Append("<img src=\"");
        AppendImageSrc(sb, image.Target, state);
        sb.Append("\" alt=\"");
        EscapeTo(sb, image.Alt);
        sb.Append('"');
        if (image.Width is not null)
        {
            sb.Append(" width=\"");
            EscapeTo(sb, image.Width);
            sb.Append('"');
        }
        if (image.Height is not null)
        {
            sb.Append(" height=\"");
            EscapeTo(sb, image.Height);
            sb.Append('"');
        }
        sb.Append('>');
        if (image.Link is not null)
            sb.Append("</a>");
        sb.Append('\n');
        sb.Append("</div>\n");
        // Asciidoctor renders block image titles below the image with "Figure N." prefix
        if (image.Title is not null)
        {
            sb.Append("<div class=\"title\">Figure ");
            sb.Append(state.FigureCounter++);
            sb.Append(". ");
            EscapeTo(sb, image.Title);
            sb.Append("</div>\n");
        }
        sb.Append("</div>\n");
    }

    private static void RenderVideo(StringBuilder sb, VideoNode video)
    {
        sb.Append("<div class=\"videoblock\">\n<div class=\"content\">\n<video src=\"");
        EscapeTo(sb, video.Target);
        sb.Append('"');
        if (video.Width is not null)
        {
            sb.Append(" width=\"");
            EscapeTo(sb, video.Width);
            sb.Append('"');
        }
        if (video.Height is not null)
        {
            sb.Append(" height=\"");
            EscapeTo(sb, video.Height);
            sb.Append('"');
        }
        if (video.Poster is not null)
        {
            sb.Append(" poster=\"");
            EscapeTo(sb, video.Poster);
            sb.Append('"');
        }
        if (video.Autoplay) sb.Append(" autoplay");
        if (video.Loop) sb.Append(" loop");
        if (video.Controls) sb.Append(" controls");
        sb.Append(">\nYour browser does not support the video tag.\n</video>\n</div>\n</div>\n");
    }

    private static void RenderAudio(StringBuilder sb, AudioNode audio)
    {
        sb.Append("<div class=\"audioblock\">\n<div class=\"content\">\n<audio src=\"");
        EscapeTo(sb, audio.Target);
        sb.Append('"');
        if (audio.Width is not null)
        {
            sb.Append(" width=\"");
            EscapeTo(sb, audio.Width);
            sb.Append('"');
        }
        if (audio.Autoplay) sb.Append(" autoplay");
        if (audio.Loop) sb.Append(" loop");
        if (audio.Controls) sb.Append(" controls");
        sb.Append(">\nYour browser does not support the audio tag.\n</audio>\n</div>\n</div>\n");
    }
}
