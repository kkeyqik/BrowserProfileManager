using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BPM.Core.Util;

/// <summary>Tolerant JSON reading for browser files (comments, trailing commas, files held open by the browser).</summary>
public static class JsonUtil
{
    static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 512,
    };

    /// <summary>Reads a JSON file that a running browser may have open. Returns null when missing or unreadable.</summary>
    public static JsonNode? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(fs, documentOptions: DocOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static JsonNode? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text, documentOptions: DocOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Walks object properties: node.At("profile", "info_cache").</summary>
    public static JsonNode? At(this JsonNode? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(key, out node)) return null;
        }
        return node;
    }

    public static string? AsString(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
            _ => null,
        };
    }

    public static long? AsLong(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        switch (v.GetValueKind())
        {
            case JsonValueKind.Number:
                if (v.TryGetValue<long>(out var l)) return l;
                if (v.TryGetValue<double>(out var d)) return (long)d;
                return null;
            case JsonValueKind.String:
                return long.TryParse(v.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null;
            default:
                return null;
        }
    }

    public static double? AsDouble(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.Number => v.TryGetValue<double>(out var d) ? d : null,
            JsonValueKind.String => double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null,
            _ => null,
        };
    }

    public static bool? AsBool(this JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? v.GetValue<bool>() : null;

    /// <summary>String items of an array (non-string items are skipped).</summary>
    public static IEnumerable<string> AsStrings(this JsonNode? node)
    {
        if (node is not JsonArray arr) yield break;
        foreach (var item in arr)
        {
            if (item is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: > 0 } s)
                yield return s;
        }
    }
}
