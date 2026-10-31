namespace BPM.Core.Util;

public static class PathUtil
{
    /// <summary>Expands %VARIABLES% and at most one '*' path segment (e.g. Store-app package folders).
    /// Paths with a '*' only yield entries that exist; plain paths are returned as-is.</summary>
    public static IEnumerable<string> Expand(string pattern)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pattern);
        if (expanded.Contains('%')) return [];

        int star = expanded.IndexOf('*');
        if (star < 0) return [expanded];

        int segStart = expanded.LastIndexOf('\\', star) + 1;
        int segEnd = expanded.IndexOf('\\', star);
        var parent = segStart > 0 ? expanded[..(segStart - 1)] : ".";
        var segment = segEnd < 0 ? expanded[segStart..] : expanded[segStart..segEnd];
        var rest = segEnd < 0 ? "" : expanded[(segEnd + 1)..];
        if (!Directory.Exists(parent)) return [];

        try
        {
            return Directory.EnumerateFileSystemEntries(parent, segment)
                .Select(m => rest.Length == 0 ? m : Path.Combine(m, rest))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\'); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path.TrimEnd('\\'); }
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="dir"/> or inside it.</summary>
    public static bool IsUnder(string path, string dir)
    {
        var p = Normalize(path);
        var d = Normalize(dir);
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Total size of all files below a folder; skips links and anything unreadable.</summary>
    public static long DirectorySize(string dir, CancellationToken ct = default)
    {
        if (!Directory.Exists(dir)) return 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long total = 0;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            try { total += file.Length; } catch (IOException) { }
        }
        return total;
    }
}
