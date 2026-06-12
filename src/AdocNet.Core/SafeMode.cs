namespace AdocNet;

/// <summary>
/// Controls the security level of document processing.
/// Higher values are more restrictive. Matches Asciidoctor's safe mode levels.
/// </summary>
public enum SafeMode
{
    /// <summary>
    /// No restrictions. All features enabled. Suitable only for trusted document sources
    /// (e.g. a local CLI run on your own files); not the default for the library API.
    /// </summary>
    Unsafe = 0,

    /// <summary>
    /// Prevents access to files outside the document's base directory.
    /// Disables include path traversal (<c>..</c>). Locks sensitive attributes.
    /// This is the default for <see cref="ParseOptions.SafeMode"/>.
    /// </summary>
    Safe = 1,

    /// <summary>
    /// Disables filesystem features not explicitly enabled.
    /// Disables URI includes and docinfo injection.
    /// </summary>
    Server = 10,

    /// <summary>
    /// Most restrictive. Disables all includes, all file I/O,
    /// and all macros that access the filesystem.
    /// </summary>
    Secure = 20,
}
