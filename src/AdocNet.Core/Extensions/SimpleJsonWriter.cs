namespace AdocNet.Extensions;

/// <summary>
/// Minimal JSON writer for serializing registry structures.
/// Companion to <see cref="SimpleJsonParser"/> — handles writing where the parser handles reading.
/// </summary>
internal static class SimpleJsonWriter
{
    /// <summary>
    /// Serializes a registry structure to indented JSON.
    /// </summary>
    /// <param name="metadata">Top-level string fields (e.g. version).</param>
    /// <param name="arrayKey">The key for the array of objects (e.g. "extensions").</param>
    /// <param name="items">List of flat-object dictionaries to serialize.</param>
    /// <param name="fieldOrder">Order of fields within each object for deterministic output.</param>
    /// <returns>Formatted JSON string.</returns>
    internal static string SerializeRegistry(
        Dictionary<string, string> metadata,
        string arrayKey,
        List<Dictionary<string, string>> items,
        string[] fieldOrder)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");

        // Write metadata fields
        foreach (var kvp in metadata)
        {
            sb.Append("  \"").Append(Escape(kvp.Key)).Append("\": \"")
              .Append(Escape(kvp.Value)).AppendLine("\",");
        }

        // Write array
        sb.Append("  \"").Append(Escape(arrayKey)).AppendLine("\": [");

        for (int idx = 0; idx < items.Count; idx++)
        {
            var item = items[idx];
            sb.AppendLine("    {");

            var fieldsWritten = 0;
            foreach (var field in fieldOrder)
            {
                if (!item.TryGetValue(field, out var value))
                    continue;

                if (fieldsWritten > 0)
                    sb.AppendLine(",");

                sb.Append("      \"").Append(Escape(field)).Append("\": \"")
                  .Append(Escape(value)).Append('"');
                fieldsWritten++;
            }

            if (fieldsWritten > 0)
                sb.AppendLine();

            sb.Append("    }");
            if (idx < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.Append('}');

        return sb.ToString();
    }

    internal static string Escape(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
