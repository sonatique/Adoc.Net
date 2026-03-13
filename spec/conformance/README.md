# Conformance Test Corpus

These `.adoc` files are used to compare AdocNet output against Asciidoctor.

Asciidoctor is **not** installed on this system, so expected output files
(`.expected.html`) must be generated on a machine that has it available.

## Generating expected output

If Asciidoctor is installed:

```bash
cd spec/conformance
for f in *.adoc; do asciidoctor -o "${f%.adoc}.expected.html" "$f"; done
```

The conformance tests compare our HTML output against these expected files
after normalizing away Asciidoctor-specific wrappers.

## Without Asciidoctor

The tests still verify:

- Parsing completes without errors
- Output is deterministic across runs
- HTML matches expected output (if `.expected.html` exists; otherwise Inconclusive)

## Real-world sources

The following files were downloaded from open-source projects for conformance
testing. `include::` directives have been replaced with comments where necessary.

| File | Source | License | Features |
|------|--------|---------|----------|
| `asciidoctor-tables.adoc` | [asciidoc-docs](https://github.com/asciidoctor/asciidoc-docs) tables module | MIT | Tables, callouts, attribute lists, examples |
| `asciidoctor-macros.adoc` | [asciidoc-docs](https://github.com/asciidoctor/asciidoc-docs) macros module | MIT | Links, URLs, xrefs, inline macros, notes |
| `asciidoctor-lists.adoc` | [asciidoc-docs](https://github.com/asciidoctor/asciidoc-docs) lists module | MIT | Unordered lists, nesting, markers, titles |
| `asciidoctor-admonitions.adoc` | [asciidoc-docs](https://github.com/asciidoctor/asciidoc-docs) blocks module | MIT | Admonition blocks (NOTE, TIP, WARNING, etc.) |
| `asciidoctor-document-header.adoc` | [asciidoc-docs](https://github.com/asciidoctor/asciidoc-docs) document module | MIT | Document headers, metadata, attributes |
| `spring-security-auth.adoc` | [Spring Security](https://github.com/spring-projects/spring-security) | Apache 2.0 | Tabs, code blocks, xrefs, tables, anchors |
| `quarkus-getting-started.adoc` | [Quarkus](https://github.com/quarkusio/quarkusio.github.io) | Apache 2.0 | Document attributes, tips, code blocks, numbered sections |
