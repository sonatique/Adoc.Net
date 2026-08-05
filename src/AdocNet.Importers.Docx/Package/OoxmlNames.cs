using System.Xml.Linq;

namespace AdocNet.Importers.Docx;

/// <summary>
/// XML namespaces and element names used by WordprocessingML documents.
/// Held in one place so part readers never hard-code a namespace string.
/// </summary>
internal static class Ns
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    public static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace Cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    public static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace DcTerms = "http://purl.org/dc/terms/";
    public static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
    public static readonly XNamespace Xml = "http://www.w3.org/XML/1998/namespace";
    public static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Relationship type URIs, keyed by the role they play in the package.</summary>
    public const string RelOfficeDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string RelCoreProperties = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    public const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string RelFootnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
    public const string RelEndnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";
    public const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    public const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    /// <summary>Reads a <c>w:val</c> attribute, returning null when absent.</summary>
    public static string? WVal(this XElement? element)
        => element?.Attribute(W + "val")?.Value;

    /// <summary>
    /// Reads an OOXML on/off toggle property. A present element with no
    /// <c>w:val</c> means "on"; <c>0</c>, <c>false</c> and <c>off</c> mean "off".
    /// </summary>
    public static bool IsToggleOn(this XElement? element)
    {
        if (element is null) return false;
        var val = element.WVal();
        if (val is null) return true;
        return val != "0" && val != "false" && val != "off";
    }
}
