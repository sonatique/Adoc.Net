namespace AdocNet.Extensions;

/// <summary>
/// Minimal JSON parser for flat string-valued objects.
/// Handles only <c>{ "key": "value", ... }</c> — no nesting, arrays, numbers, or booleans.
/// Sufficient for parsing <c>extension.json</c> manifest files.
/// </summary>
internal static class SimpleJsonParser
{
    /// <summary>
    /// Parses a flat JSON object with string values into a dictionary.
    /// Non-string values are silently skipped.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>Dictionary of key-value pairs. Empty if the JSON is empty or not an object.</returns>
    /// <exception cref="FormatException">Thrown if the JSON is malformed.</exception>
    public static Dictionary<string, string> ParseFlatObject(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var i = SkipWhitespace(json, 0);
        if (i >= json.Length)
            return result;

        if (json[i] != '{')
            throw new FormatException("Expected '{' at start of JSON object");
        i = SkipWhitespace(json, i + 1);

        if (i < json.Length && json[i] == '}')
            return result;

        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON");

            if (json[i] != '"')
                throw new FormatException($"Expected '\"' for key at position {i}");

            var key = ReadString(json, ref i);

            i = SkipWhitespace(json, i);
            if (i >= json.Length || json[i] != ':')
                throw new FormatException($"Expected ':' after key at position {i}");
            i = SkipWhitespace(json, i + 1);

            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON after ':'");

            if (json[i] == '"')
            {
                var value = ReadString(json, ref i);
                result[key] = value;
            }
            else
            {
                // Skip non-string values (numbers, booleans, null, objects, arrays)
                i = SkipValue(json, i);
            }

            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON");

            if (json[i] == '}')
                break;

            if (json[i] == ',')
            {
                i++;
                continue;
            }

            throw new FormatException($"Expected ',' or '}}' at position {i}");
        }

        return result;
    }

    private static string ReadString(string json, ref int i)
    {
        if (json[i] != '"')
            throw new FormatException($"Expected '\"' at position {i}");
        i++; // skip opening quote

        var start = i;
        var hasEscapes = false;

        while (i < json.Length)
        {
            var c = json[i];
            if (c == '\\')
            {
                hasEscapes = true;
                i += 2; // skip escape sequence
                continue;
            }
            if (c == '"')
            {
                var raw = json.Substring(start, i - start);
                i++; // skip closing quote
                return hasEscapes ? Unescape(raw) : raw;
            }
            i++;
        }

        throw new FormatException("Unterminated string");
    }

    private static string Unescape(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
                switch (raw[i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < raw.Length
                            && int.TryParse(raw.Substring(i + 1, 4),
                                System.Globalization.NumberStyles.HexNumber, null, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append('u');
                        }
                        break;
                    default: sb.Append(raw[i]); break;
                }
            }
            else
            {
                sb.Append(raw[i]);
            }
        }
        return sb.ToString();
    }

    private static int SkipValue(string json, int i)
    {
        if (i >= json.Length)
            throw new FormatException("Unexpected end of JSON");

        var c = json[i];

        // String
        if (c == '"')
        {
            ReadString(json, ref i);
            return i;
        }

        // Object or array — skip balanced braces/brackets
        if (c == '{' || c == '[')
        {
            var open = c;
            var close = c == '{' ? '}' : ']';
            var depth = 1;
            i++;
            var inString = false;
            while (i < json.Length && depth > 0)
            {
                var ch = json[i];
                if (inString)
                {
                    if (ch == '\\') { i += 2; continue; }
                    if (ch == '"') inString = false;
                }
                else
                {
                    if (ch == '"') inString = true;
                    else if (ch == open) depth++;
                    else if (ch == close) depth--;
                }
                i++;
            }
            return i;
        }

        // Number, boolean, null — skip until delimiter
        while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']'
               && !char.IsWhiteSpace(json[i]))
        {
            i++;
        }
        return i;
    }

    /// <summary>
    /// Parses a JSON object containing string properties and one named array of flat objects.
    /// Returns the top-level string properties as metadata and the array entries as a list.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <param name="arrayKey">The key whose value is an array of flat objects.</param>
    /// <returns>Tuple of metadata (string fields) and items (list of flat-object dicts).</returns>
    /// <exception cref="FormatException">Thrown if the JSON is malformed.</exception>
    internal static (Dictionary<string, string> metadata, List<Dictionary<string, string>> items)
        ParseObjectWithArray(string json, string arrayKey)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = new List<Dictionary<string, string>>();

        var i = SkipWhitespace(json, 0);
        if (i >= json.Length || json[i] != '{')
            throw new FormatException("Expected '{' at start of JSON object");
        i = SkipWhitespace(json, i + 1);

        if (i < json.Length && json[i] == '}')
            return (metadata, items);

        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON");

            if (json[i] != '"')
                throw new FormatException($"Expected '\"' for key at position {i}");

            var key = ReadString(json, ref i);

            i = SkipWhitespace(json, i);
            if (i >= json.Length || json[i] != ':')
                throw new FormatException($"Expected ':' after key at position {i}");
            i = SkipWhitespace(json, i + 1);

            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON after ':'");

            if (key == arrayKey && json[i] == '[')
            {
                i = ParseArrayOfFlatObjects(json, i, items);
            }
            else if (json[i] == '"')
            {
                var value = ReadString(json, ref i);
                metadata[key] = value;
            }
            else
            {
                i = SkipValue(json, i);
            }

            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON");

            if (json[i] == '}')
                break;

            if (json[i] == ',')
            {
                i++;
                continue;
            }

            throw new FormatException($"Expected ',' or '}}' at position {i}");
        }

        return (metadata, items);
    }

    private static int ParseArrayOfFlatObjects(string json, int i,
        List<Dictionary<string, string>> items)
    {
        // skip '['
        i = SkipWhitespace(json, i + 1);

        if (i < json.Length && json[i] == ']')
            return i + 1;

        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in array");

            if (json[i] == '{')
            {
                var obj = ParseFlatObjectAt(json, ref i);
                items.Add(obj);
            }
            else
            {
                i = SkipValue(json, i);
            }

            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in array");

            if (json[i] == ']')
                return i + 1;

            if (json[i] == ',')
            {
                i++;
                continue;
            }

            throw new FormatException($"Expected ',' or ']' at position {i}");
        }

        throw new FormatException("Unterminated array");
    }

    private static Dictionary<string, string> ParseFlatObjectAt(string json, ref int i)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        i = SkipWhitespace(json, i + 1); // skip '{'

        if (i < json.Length && json[i] == '}')
        {
            i++;
            return result;
        }

        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in object");

            if (json[i] != '"')
                throw new FormatException($"Expected '\"' for key at position {i}");

            var key = ReadString(json, ref i);

            i = SkipWhitespace(json, i);
            if (i >= json.Length || json[i] != ':')
                throw new FormatException($"Expected ':' after key at position {i}");
            i = SkipWhitespace(json, i + 1);

            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON after ':'");

            if (json[i] == '"')
            {
                var value = ReadString(json, ref i);
                result[key] = value;
            }
            else
            {
                i = SkipValue(json, i);
            }

            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in object");

            if (json[i] == '}')
            {
                i++;
                break;
            }

            if (json[i] == ',')
            {
                i++;
                continue;
            }

            throw new FormatException($"Expected ',' or '}}' at position {i}");
        }

        return result;
    }

    /// <summary>
    /// Parses a JSON array of strings. Returns an empty list for non-array values.
    /// </summary>
    internal static List<string> ParseStringArray(string json, int startIndex)
    {
        var result = new List<string>();
        var i = SkipWhitespace(json, startIndex);

        if (i >= json.Length || json[i] != '[')
            return result;

        i = SkipWhitespace(json, i + 1);

        if (i < json.Length && json[i] == ']')
            return result;

        while (i < json.Length)
        {
            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in array");

            if (json[i] == '"')
            {
                var value = ReadString(json, ref i);
                result.Add(value);
            }
            else
            {
                i = SkipValue(json, i);
            }

            i = SkipWhitespace(json, i);
            if (i >= json.Length)
                throw new FormatException("Unexpected end of JSON in array");

            if (json[i] == ']')
                break;

            if (json[i] == ',')
            {
                i++;
                continue;
            }

            throw new FormatException($"Expected ',' or ']' at position {i}");
        }

        return result;
    }

    private static int SkipWhitespace(string json, int i)
    {
        while (i < json.Length && char.IsWhiteSpace(json[i]))
            i++;
        return i;
    }
}
