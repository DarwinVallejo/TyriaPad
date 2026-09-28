using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TyriaPad.Core.Config;

/// <summary>
/// Changes individual values of a JSON with comments without rewriting the rest of the text: the
/// user's comments, order and formatting are kept. It only touches the requested value; if the
/// property does not exist, it adds it at the end of its object (creating any missing intermediate objects).
/// Paths are dot-separated and case-insensitive, as when reading: <c>overlay.scale</c>.
/// Arrays are not edited: they are skipped whole.
/// </summary>
public static class JsonTextEditor
{
    private static readonly JsonReaderOptions s_reader = new()
    {
        CommentHandling = JsonCommentHandling.Allow,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions s_values = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Puts <paramref name="value"/> (number, text, boolean or enum) at the path.</summary>
    public static string Set(string json, string path, object? value)
        => SetRaw(json, path, JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), s_values));

    /// <summary>Puts an already-formed JSON literal at the path.</summary>
    public static string SetRaw(string json, string path, string literal)
    {
        string[] segments = path.Split('.');
        bool bom = json.Length > 0 && json[0] == '﻿';
        byte[] bytes = Encoding.UTF8.GetBytes(bom ? json[1..] : json);
        Scan scan = ScanDocument(bytes);

        byte[] result;
        if (scan.Values.TryGetValue(path, out ValueSpan found))
        {
            result = Splice(bytes, found.Start, found.End, literal);
        }
        else
        {
            // The deepest object that already exists; whatever is missing is written nested inside it.
            int depth = segments.Length - 1;
            while (depth > 0 && !scan.Objects.ContainsKey(string.Join('.', segments[..depth])))
            {
                depth--;
            }

            if (!scan.Objects.TryGetValue(string.Join('.', segments[..depth]), out ObjectSpan? parent))
            {
                throw new FormatException("the document is not a JSON object");
            }

            string blocking = string.Join('.', segments[..(depth + 1)]);
            if (depth < segments.Length - 1 && scan.Values.ContainsKey(blocking))
            {
                throw new FormatException($"'{blocking}' exists but is not an object");
            }

            result = Insert(bytes, parent, segments[depth..], literal);
        }

        string text = Encoding.UTF8.GetString(result);
        return bom ? '﻿' + text : text;
    }

    private static byte[] Insert(byte[] bytes, ObjectSpan parent, string[] names, string literal)
    {
        // Missing intermediate objects go on one line: "a": { "b": value }.
        string value = literal;
        for (int i = names.Length - 1; i > 0; i--)
        {
            value = $"{{ {JsonSerializer.Serialize(names[i])}: {value} }}";
        }

        string member = $"{JsonSerializer.Serialize(names[0])}: {value}";
        if (parent.LastValueEnd < 0)
        {
            return Splice(bytes, parent.Open + 1, parent.Close, $" {member} ");
        }

        // The new member goes on its own line before the closing brace, and the comma right
        // after the last value (before its comment, if it has one).
        string newline = DetectNewline(bytes);
        string indent = parent.MemberIndent ?? parent.Indent + "  ";
        int lineStart = parent.Close;
        while (lineStart > 0 && bytes[lineStart - 1] is (byte)' ' or (byte)'\t')
        {
            lineStart--;
        }

        byte[] withMember = lineStart > 0 && bytes[lineStart - 1] == '\n'
            ? Splice(bytes, lineStart, lineStart, $"{indent}{member}{newline}")
            : Splice(bytes, parent.Close, parent.Close, $" {member} ");
        return HasComma(bytes, parent.LastValueEnd, parent.Close)
            ? withMember
            : Splice(withMember, parent.LastValueEnd, parent.LastValueEnd, ",");
    }

    /// <summary>There is a comma (outside comments) between the last value and the closing brace.</summary>
    private static bool HasComma(byte[] bytes, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (bytes[i] == ',')
            {
                return true;
            }

            if (bytes[i] == '/' && i + 1 < to && bytes[i + 1] == '/')
            {
                while (i < to && bytes[i] != '\n')
                {
                    i++;
                }
            }
            else if (bytes[i] == '/' && i + 1 < to && bytes[i + 1] == '*')
            {
                for (i += 2; i + 1 < to && !(bytes[i] == '*' && bytes[i + 1] == '/'); i++)
                {
                }

                i++;
            }
        }

        return false;
    }

    private static string DetectNewline(byte[] bytes)
    {
        int lf = Array.IndexOf(bytes, (byte)'\n');
        return lf > 0 && bytes[lf - 1] == '\r' ? "\r\n" : "\n";
    }

    private static byte[] Splice(byte[] bytes, int start, int end, string text)
    {
        byte[] insert = Encoding.UTF8.GetBytes(text);
        byte[] result = new byte[bytes.Length - (end - start) + insert.Length];
        bytes.AsSpan(0, start).CopyTo(result);
        insert.CopyTo(result, start);
        bytes.AsSpan(end).CopyTo(result.AsSpan(start + insert.Length));
        return result;
    }

    /// <summary>Indentation of the line where <paramref name="position"/> starts, if the element opens the line.</summary>
    private static string? IndentAt(byte[] bytes, int position)
    {
        int start = position;
        while (start > 0 && bytes[start - 1] is (byte)' ' or (byte)'\t')
        {
            start--;
        }

        return start == 0 || bytes[start - 1] == '\n' ? Encoding.UTF8.GetString(bytes, start, position - start) : null;
    }

    private static Scan ScanDocument(byte[] bytes)
    {
        var scan = new Scan();
        var reader = new Utf8JsonReader(bytes, s_reader);
        var path = new List<string>();
        var objects = new Stack<ObjectSpan>();
        string? name = null;
        int nameStart = -1;

        while (reader.Read())
        {
            int start = (int)reader.TokenStartIndex;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    name = reader.GetString();
                    nameStart = start;
                    if (objects.TryPeek(out ObjectSpan? owner))
                    {
                        owner.MemberIndent ??= IndentAt(bytes, start);
                    }

                    break;
                case JsonTokenType.StartObject:
                    if (name is not null)
                    {
                        path.Add(name);
                    }

                    objects.Push(new ObjectSpan
                    {
                        Open = start,
                        Indent = IndentAt(bytes, name is not null ? nameStart : start) ?? string.Empty,
                    });
                    name = null;
                    break;
                case JsonTokenType.EndObject:
                    ObjectSpan closed = objects.Pop();
                    closed.Close = start;
                    scan.Objects.TryAdd(string.Join('.', path), closed);
                    if (objects.Count > 0)
                    {
                        path.RemoveAt(path.Count - 1);
                        objects.Peek().LastValueEnd = start + 1;
                    }

                    break;
                case JsonTokenType.StartArray:
                    reader.Skip();
                    name = null;
                    if (objects.TryPeek(out ObjectSpan? arrayOwner))
                    {
                        arrayOwner.LastValueEnd = (int)reader.TokenStartIndex + 1;
                    }

                    break;
                case JsonTokenType.Comment:
                    break;
                default:
                    // String values are measured on the original (escaped) text plus the quotes.
                    int end = start + reader.ValueSpan.Length + (reader.TokenType == JsonTokenType.String ? 2 : 0);
                    if (name is not null)
                    {
                        scan.Values.TryAdd(path.Count == 0 ? name : $"{string.Join('.', path)}.{name}", new ValueSpan(start, end));
                    }

                    name = null;
                    if (objects.TryPeek(out ObjectSpan? valueOwner))
                    {
                        valueOwner.LastValueEnd = end;
                    }

                    break;
            }
        }

        return scan;
    }

    private readonly record struct ValueSpan(int Start, int End);

    private sealed class ObjectSpan
    {
        public int Open { get; init; }

        public int Close { get; set; }

        public string Indent { get; init; } = string.Empty;

        public string? MemberIndent { get; set; }

        public int LastValueEnd { get; set; } = -1;
    }

    private sealed class Scan
    {
        public Dictionary<string, ValueSpan> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ObjectSpan> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
