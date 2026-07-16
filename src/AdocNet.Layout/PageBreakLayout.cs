namespace AdocNet.Layout;

/// <summary>
/// An explicit page break (<c>&lt;&lt;&lt;</c>). Continuous renderers may ignore
/// it (it has no visual form of its own); paged renderers start a new page.
/// </summary>
public sealed class PageBreakLayout : BlockLayout
{
}
