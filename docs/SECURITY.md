# Security Considerations

AdocNet processes AsciiDoc markup and produces rendered output. When used with
untrusted input, the following security considerations apply.

## Passthrough Content (XSS)

AsciiDoc's passthrough syntax (`++++` blocks, `+++inline+++`, `pass:[...]`)
is **designed** to emit raw, unescaped content into the output. This is
identical to Asciidoctor's behavior and is a core AsciiDoc feature.

**Risk:** If you render untrusted AsciiDoc to HTML and serve it in a browser,
passthrough blocks can inject arbitrary HTML and JavaScript (XSS).

**Mitigation:** When processing untrusted content:
- Sanitize the rendered HTML output with a library like HtmlSanitizer before serving.
- Alternatively, pre-process the AsciiDoc source to strip passthrough blocks before parsing.

## Include Directives (File Read)

The `include::path[]` directive reads files from the filesystem relative to
the document's base directory.

**Risk:** A malicious document could use `include::` to read sensitive files
outside the intended directory (path traversal).

**Mitigations built in:**
- Includes are disabled by default when no `SourceFilePath` or `BaseDirectory` is set.
- Remote URL includes (`http://`, `https://`) are disabled by default (`AllowUriRead = false`).
- Recursive include depth is limited (default: 10 levels).
- HTTP responses are capped at 10 MB.
- File I/O errors are caught and reported as diagnostics rather than crashing.

**Additional mitigation for untrusted content:**
- Provide a custom `IIncludeReader` that restricts access to a specific directory.
- Set `ExpandIncludes = false` to disable include processing entirely.

## Document Attributes

Document attributes set via `:name: value` in the header can influence
rendering (e.g. `:toc-title:`, `:note-caption:`, `:table-caption:`).

All attribute values are HTML-escaped before insertion into rendered output.
Attributes cannot inject raw HTML into the output.

## Recommendations for Untrusted Input

| Concern | Recommendation |
|---------|---------------|
| XSS via passthrough | Sanitize HTML output or strip passthrough blocks |
| File read via include | Use `ExpandIncludes = false` or custom `IIncludeReader` |
| Resource exhaustion | Use default include depth limit; avoid `AllowUriRead = true` |
| Attribute injection | No action needed — attributes are escaped |
