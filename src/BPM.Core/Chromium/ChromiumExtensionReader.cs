using System.Text.Json.Nodes;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Chromium;

/// <summary>Reads a Chromium profile's extensions from Preferences + Secure Preferences and each extension's manifest.
/// Read-only: Secure Preferences is tamper-protected by the browser and is never written.</summary>
public static class ChromiumExtensionReader
{
    public static List<ExtensionInfo> Read(ProfileInfo profile)
    {
        var list = new List<ExtensionInfo>();
        foreach (var (id, settings) in LoadSettings(profile.Path))
        {
            if (Build(profile, id, settings) is { } info) list.Add(info);
        }
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>extensions.settings from both preference files, merged per extension (Secure Preferences wins).</summary>
    public static Dictionary<string, JsonObject> LoadSettings(string profilePath)
    {
        var merged = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var file in new[] { "Preferences", "Secure Preferences" })
        {
            if (JsonUtil.Load(Path.Combine(profilePath, file)).At("extensions", "settings") is not JsonObject settings) continue;
            foreach (var (id, value) in settings)
            {
                if (value is not JsonObject entry) continue;
                if (!merged.TryGetValue(id, out var target)) merged[id] = target = new JsonObject();
                foreach (var (key, v) in entry) target[key] = v?.DeepClone();
            }
        }
        return merged;
    }

    static ExtensionInfo? Build(ProfileInfo profile, string id, JsonObject s)
    {
        int location = (int)(s.At("location").AsLong() ?? 0);
        if (location is 5 or 10) return null; // built into the browser

        var folder = FindFolder(profile.Path, id, s, location);
        var manifestFile = folder != null ? Path.Combine(folder, "manifest.json") : null;
        bool filesPresent = manifestFile != null && File.Exists(manifestFile);
        var manifest = (filesPresent ? JsonUtil.Load(manifestFile!) : null) ?? s.At("manifest");
        if (manifest == null) return null; // leftover entry of a removed extension

        var locale = manifest.At("default_locale").AsString();
        var name = Localize(manifest.At("name").AsString(), folder, locale);
        var updateUrl = manifest.At("update_url").AsString();
        bool fromStore = s.At("from_webstore").AsBool() == true || IsStoreUpdateUrl(updateUrl);

        long bits = DisableReasonCodes.ReadBits(s.At("disable_reasons"));
        long? state = s.At("state").AsLong();
        bool enabled = bits == 0 && state is null or 1;
        var reasons = DisableReasonCodes.Describe(bits);
        if (!enabled && reasons.Count == 0) reasons.Add("turned off");

        var (api, hosts) = ReadPermissions(s, manifest);
        return new ExtensionInfo
        {
            Profile = profile,
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? id : name,
            Description = Localize(manifest.At("description").AsString(), folder, locale),
            Version = manifest.At("version").AsString() ?? "?",
            Enabled = enabled,
            DisableReasonBits = bits,
            DisableReasons = reasons,
            Source = ClassifySource(location, s, fromStore),
            Kind = manifest.At("theme") != null ? ExtensionKind.Theme : manifest.At("app") != null ? ExtensionKind.App : ExtensionKind.Extension,
            Location = location,
            ManifestVersion = (int)(manifest.At("manifest_version").AsLong() ?? 0),
            UpdateUrl = updateUrl,
            HomepageUrl = manifest.At("homepage_url").AsString(),
            StoreUrl = StoreUrlFor(id, updateUrl, fromStore),
            FolderPath = folder,
            IconPath = FindIcon(folder, manifest),
            FilesPresent = filesPresent || location == 4,
            InstalledAt = WebKitTime(s.At("first_install_time")) ?? WebKitTime(s.At("install_time")),
            UpdatedAt = WebKitTime(s.At("last_update_time")),
            ApiPermissions = api,
            HostPermissions = hosts,
        };
    }

    static string? FindFolder(string profilePath, string id, JsonObject s, int location)
    {
        var path = s.At("path").AsString();
        if (location is 4 or 8 && !string.IsNullOrEmpty(path)) return path; // unpacked: absolute path
        if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
        {
            var relative = Path.Combine(profilePath, "Extensions", path);
            if (Directory.Exists(relative)) return relative;
        }
        var root = Path.Combine(profilePath, "Extensions", id);
        if (!Directory.Exists(root)) return null;
        return Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, VersionComparer.Instance).FirstOrDefault();
    }

    static ExtensionSource ClassifySource(int location, JsonObject s, bool fromStore)
    {
        if (s.At("was_installed_by_default").AsBool() == true || s.At("was_installed_by_oem").AsBool() == true)
            return ExtensionSource.Preinstalled;
        return location switch
        {
            4 or 8 => ExtensionSource.Developer,
            7 or 9 => ExtensionSource.Policy,
            2 or 3 or 6 => ExtensionSource.OtherProgram,
            _ => fromStore ? ExtensionSource.Store : ExtensionSource.Unknown,
        };
    }

    static (List<string> Api, List<string> Hosts) ReadPermissions(JsonObject s, JsonNode manifest)
    {
        var api = new List<string>();
        var hosts = new List<string>();
        if (s.At("granted_permissions") is JsonObject granted)
        {
            api.AddRange(granted.At("api").AsStrings());
            api.AddRange(granted.At("manifest_permissions").AsStrings());
            hosts.AddRange(granted.At("explicit_host").AsStrings());
            hosts.AddRange(granted.At("scriptable_host").AsStrings());
        }
        else
        {
            foreach (var p in manifest.At("permissions").AsStrings()) (IsHostPattern(p) ? hosts : api).Add(p);
            hosts.AddRange(manifest.At("host_permissions").AsStrings());
            if (manifest.At("content_scripts") is JsonArray scripts)
                foreach (var script in scripts) hosts.AddRange(script.At("matches").AsStrings());
        }
        var runtime = s.At("runtime_granted_permissions");
        hosts.AddRange(runtime.At("explicit_host").AsStrings());
        hosts.AddRange(runtime.At("scriptable_host").AsStrings());
        return (api.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    static bool IsHostPattern(string p) => p.Contains("://") || p == "<all_urls>";

    static bool IsStoreUpdateUrl(string? url) => url != null
        && (url.Contains("clients2.google.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("edge.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("extension-updates.opera.com", StringComparison.OrdinalIgnoreCase));

    static string? StoreUrlFor(string id, string? updateUrl, bool fromStore)
    {
        if (updateUrl?.Contains("edge.microsoft.com", StringComparison.OrdinalIgnoreCase) == true)
            return $"https://microsoftedge.microsoft.com/addons/detail/{id}";
        if (updateUrl?.Contains("opera.com", StringComparison.OrdinalIgnoreCase) == true)
            return null; // Opera add-on pages need the add-on's name slug
        return fromStore ? $"https://chromewebstore.google.com/detail/{id}" : null;
    }

    /// <summary>Chromium stores times as microseconds since 1601-01-01 (as a string).</summary>
    static DateTime? WebKitTime(JsonNode? node)
    {
        var micros = node.AsLong();
        if (micros is not > 0) return null;
        try { return DateTime.FromFileTimeUtc(micros.Value * 10).ToLocalTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static string? FindIcon(string? folder, JsonNode manifest)
    {
        if (folder == null) return null;
        var candidates = new List<(int Size, string Path)>();
        void Add(JsonNode? icons)
        {
            if (icons is JsonObject map)
            {
                foreach (var (key, value) in map)
                    if (int.TryParse(key, out var size) && value.AsString() is { } p) candidates.Add((size, p));
            }
            else if (icons.AsString() is { } single)
            {
                candidates.Add((16, single));
            }
        }
        Add(manifest.At("icons"));
        if (candidates.Count == 0)
            foreach (var key in new[] { "action", "browser_action", "page_action" }) Add(manifest.At(key, "default_icon"));

        return candidates.Where(c => c.Size >= 32).OrderBy(c => c.Size)
            .Concat(candidates.OrderByDescending(c => c.Size))
            .Select(c => Path.Combine(folder, c.Path.TrimStart('/', '\\').Replace('/', '\\')))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Resolves "__MSG_name__" placeholders from _locales/&lt;locale&gt;/messages.json, preferring the Windows display
    /// language like the browser does, so names match what the browser shows.</summary>
    internal static string? Localize(string? text, string? folder, string? defaultLocale)
    {
        if (text == null || folder == null || !text.StartsWith("__MSG_", StringComparison.Ordinal)) return text;
        var key = text[6..].TrimEnd('_');
        var ui = System.Globalization.CultureInfo.CurrentUICulture;
        foreach (var locale in new[] { ui.Name.Replace('-', '_'), ui.TwoLetterISOLanguageName, defaultLocale, "en", "en_US", "en_GB" })
        {
            if (string.IsNullOrEmpty(locale)) continue;
            if (JsonUtil.Load(Path.Combine(folder, "_locales", locale, "messages.json")) is not JsonObject messages) continue;
            var hit = messages.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (hit.Key != null && hit.Value.At("message").AsString() is { } message) return message;
        }
        return text;
    }

    /// <summary>Orders extension version folders like "1.2.10_0" numerically.</summary>
    sealed class VersionComparer : IComparer<string?>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            bool okA = Version.TryParse((a ?? "").Split('_')[0], out var va);
            bool okB = Version.TryParse((b ?? "").Split('_')[0], out var vb);
            if (okA && okB) return va!.CompareTo(vb);
            return string.CompareOrdinal(a, b);
        }
    }
}
