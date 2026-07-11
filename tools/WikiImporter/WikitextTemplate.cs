namespace Tsundosika.LimbusAssistant.WikiImporter;

public sealed class WikitextTemplate
{
    public required string Name { get; init; }

    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    public string Value(string key) => Parameters.GetValueOrDefault(key, "").Trim();

    public static List<WikitextTemplate> ExtractAll(string text, string templateName)
    {
        var results = new List<WikitextTemplate>();
        var index = 0;
        while ((index = text.IndexOf("{{", index, StringComparison.Ordinal)) >= 0)
        {
            var nameEnd = text.IndexOfAny(['|', '}', '\n'], index + 2);
            if (nameEnd < 0)
            {
                break;
            }
            var name = text[(index + 2)..nameEnd].Trim();
            if (!name.Equals(templateName, StringComparison.OrdinalIgnoreCase))
            {
                index += 2;
                continue;
            }
            var block = ExtractBalanced(text, index);
            if (block is null)
            {
                break;
            }
            results.Add(new WikitextTemplate
            {
                Name = name,
                Parameters = ParseParameters(block),
            });
            index += block.Length;
        }
        return results;
    }

    static string? ExtractBalanced(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length - 1; i++)
        {
            if (text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                i++;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }
        return null;
    }

    static Dictionary<string, string> ParseParameters(string block)
    {
        var inner = block[2..^2];
        var parts = SplitTopLevel(inner);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }
            var key = part[..equals].Trim();
            if (key.Length > 0 && !parameters.ContainsKey(key))
            {
                parameters[key] = part[(equals + 1)..].Trim();
            }
        }
        return parameters;
    }

    static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var linkDepth = 0;
        var segmentStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i < text.Length - 1 && text[i] == '{' && text[i + 1] == '{')
            {
                depth++;
                i++;
            }
            else if (i < text.Length - 1 && text[i] == '}' && text[i + 1] == '}')
            {
                depth--;
                i++;
            }
            else if (i < text.Length - 1 && text[i] == '[' && text[i + 1] == '[')
            {
                linkDepth++;
                i++;
            }
            else if (i < text.Length - 1 && text[i] == ']' && text[i + 1] == ']')
            {
                linkDepth--;
                i++;
            }
            else if (text[i] == '|' && depth == 0 && linkDepth == 0)
            {
                parts.Add(text[segmentStart..i]);
                segmentStart = i + 1;
            }
        }
        parts.Add(text[segmentStart..]);
        return parts;
    }
}
