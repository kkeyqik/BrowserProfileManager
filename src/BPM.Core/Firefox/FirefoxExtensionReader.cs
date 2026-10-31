using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Firefox;

/// <summary>Reads a Firefox profile's add-ons from extensions.json. Read-only.</summary>
public static class FirefoxExtensionReader
{
    static readonly HashSet<string> BuiltInLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        "app-builtin", "app-system-defaults", "app-system-addons", "app-builtin-addons",
    };

    static readonly HashSet<string> OtherProgramLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        "app-global", "app-system-share", "app-system-local", "app-system-user", "winreg-app-global", "winreg-app-user",
    };

    public static List<ExtensionInfo> Read(ProfileInfo profile)
    {
        var list = new List<ExtensionInfo>();
        if (JsonUtil.Load(Path.Combine(profile.Path, "extensions.json")).At("addons") is JsonArray addons)
        {
            foreach (var addon in addons)
                if (Build(profile, addon) is { } info) list.Add(info);
        }
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static ExtensionInfo? Build(ProfileInfo profile, JsonNode? addon)
    {
        if (addon is not JsonObject) return null;
        var id = addon.At("id").AsString();
        if (string.IsNullOrEmpty(id)) return null;

        var kind = addon.At("type").AsString() switch
        {
            "extension" => ExtensionKind.Extension,
            "theme" => ExtensionKind.Theme,
            _ => (ExtensionKind?)null,
        };
        var location = addon.At("location").AsString() ?? "";
        if (kind == null || BuiltInLocations.Contains(location)) return null;

        var source = ClassifySource(addon, location);
        bool enabled = addon.At("active").AsBool() == true;
        var path = addon.At("path").AsString();
        var version = addon.At("version").AsString() ?? "?";
        var name = addon.At("defaultLocale", "name").AsString();

        return new ExtensionInfo
        {
            Profile = profile,
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? id : name,
            Description = addon.At("defaultLocale", "description").AsString(),
            Version = version,
            Enabled = enabled,
            DisableReasons = enabled ? [] : DescribeDisabled(addon),
            Source = source,
            Kind = kind.Value,
            ManifestVersion = (int)(addon.At("manifestVersion").AsLong() ?? 0),
            HomepageUrl = addon.At("defaultLocale", "homepageURL").AsString(),
            StoreUrl = source == ExtensionSource.Store ? $"https://addons.mozilla.org/firefox/addon/{Uri.EscapeDataString(id)}/" : null,
            FolderPath = path,
            IconPath = string.IsNullOrEmpty(path) ? null : FindIcon(path, version),
            FilesPresent = !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)),
            InstalledAt = UnixMillis(addon.At("installDate")),
            UpdatedAt = UnixMillis(addon.At("updateDate")),
            ApiPermissions = addon.At("userPermissions", "permissions").AsStrings().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            HostPermissions = addon.At("userPermissions", "origins").AsStrings().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    static ExtensionSource ClassifySource(JsonNode addon, string location)
    {
        if (addon.At("installTelemetryInfo", "source").AsString() == "enterprise-policy") return ExtensionSource.Policy;
        if (location.Equals("temporary-install", StringComparison.OrdinalIgnoreCase)) return ExtensionSource.Developer;
        if (location.Equals("app-profile", StringComparison.OrdinalIgnoreCase))
        {
            var sourceUrl = addon.At("sourceURI").AsString() ?? "";
            bool amo = sourceUrl.Contains("mozilla.org", StringComparison.OrdinalIgnoreCase)
                || sourceUrl.Contains("mozilla.net", StringComparison.OrdinalIgnoreCase);
            return amo || (addon.At("signedState").AsLong() ?? 0) > 0 ? ExtensionSource.Store : ExtensionSource.Unknown;
        }
        return OtherProgramLocations.Contains(location) ? ExtensionSource.OtherProgram : ExtensionSource.Unknown;
    }

    static List<string> DescribeDisabled(JsonNode addon)
    {
        var reasons = new List<string>();
        if (addon.At("userDisabled").AsBool() == true) reasons.Add("turned off by you");
        if (addon.At("appDisabled").AsBool() == true) reasons.Add("not compatible with this version or not signed");
        if (addon.At("softDisabled").AsBool() == true) reasons.Add("soft-blocked by Mozilla");
        if (addon.At("blocklistState").AsLong() == 2) reasons.Add("blocked by Mozilla");
        if (addon.At("embedderDisabled").AsBool() == true) reasons.Add("turned off by the browser");
        if (reasons.Count == 0) reasons.Add("turned off");
        return reasons;
    }

    static DateTime? UnixMillis(JsonNode? node)
    {
        var ms = node.AsLong();
        if (ms is not > 0) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime; }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static string? FindIcon(string path, string version)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var rel = PickIcon(JsonUtil.Load(Path.Combine(path, "manifest.json")));
                if (rel == null) return null;
                var file = Path.Combine(path, rel.TrimStart('/', '\\').Replace('/', '\\'));
                return File.Exists(file) ? file : null;
            }
            if (!File.Exists(path)) return null;

            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry("manifest.json");
            if (manifestEntry == null) return null;
            string text;
            using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();

            var iconPath = PickIcon(JsonUtil.Parse(text));
            if (iconPath == null) return null;
            var entry = zip.GetEntry(iconPath.TrimStart('/', '\\').Replace('\\', '/'));
            if (entry == null) return null;

            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path + version))).ToLowerInvariant();
            var cacheDir = Path.Combine(AppPaths.IconCacheDir, "firefox");
            var cached = Path.Combine(cacheDir, hash + Path.GetExtension(entry.Name));
            if (File.Exists(cached)) return cached;
            Directory.CreateDirectory(cacheDir);
            entry.ExtractToFile(cached, overwrite: true);
            return cached;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Smallest icon that is at least 32px, else the largest one.</summary>
    static string? PickIcon(JsonNode? manifest)
    {
        if (manifest.At("icons") is not JsonObject icons) return null;
        var candidates = new List<(int Size, string Path)>();
        foreach (var (key, value) in icons)
            if (int.TryParse(key, out var size) && value.AsString() is { Length: > 0 } p) candidates.Add((size, p));

        return candidates.Where(c => c.Size >= 32).OrderBy(c => c.Size)
            .Concat(candidates.OrderByDescending(c => c.Size))
            .Select(c => c.Path)
            .FirstOrDefault();
    }
}
