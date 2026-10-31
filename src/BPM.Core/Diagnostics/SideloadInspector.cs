using System.Text.RegularExpressions;
using BPM.Core.Models;

namespace BPM.Core.Diagnostics;

/// <summary>An extension another program pushed into browsers through the registry. Browsers then ask in every profile
/// whether to turn it on ("waiting for approval").</summary>
public sealed record Sideload(
    string ExtensionId,
    RegistryScope Scope,
    string Key,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> Browsers)
{
    public string DisplayKey => Scope.Display(Key);
    public string? UpdateUrl => Values.GetValueOrDefault("update_url");

    /// <summary>A local .crx file the program installs from, when it doesn't use a store.</summary>
    public string? CrxPath => Values.GetValueOrDefault("path");

    public string SourceText => UpdateUrl switch
    {
        { } u when u.Contains("clients2.google.com", StringComparison.OrdinalIgnoreCase) => "Chrome Web Store",
        { } u when u.Contains("edge.microsoft.com", StringComparison.OrdinalIgnoreCase) => "Edge Add-ons",
        { } u => u,
        null when CrxPath != null => "Local file: " + CrxPath,
        _ => "Unknown",
    };
}

public static partial class SideloadInspector
{
    [GeneratedRegex("^[a-p]{32}$")]
    private static partial Regex ExtensionId();

    public static List<Sideload> Find(IEnumerable<BrowserDefinition> browsers)
    {
        var found = new Dictionary<string, (Sideload Entry, List<string> Browsers)>(StringComparer.OrdinalIgnoreCase);
        foreach (var browser in browsers.Where(b => b.ExternalExtensionsKey != null))
        foreach (var scope in RegistryScope.All)
        {
            try
            {
                using var root = scope.OpenBase();
                using var key = root.OpenSubKey(browser.ExternalExtensionsKey!);
                if (key == null) continue;
                foreach (var id in key.GetSubKeyNames().Where(n => ExtensionId().IsMatch(n)))
                {
                    var entryKey = $@"{browser.ExternalExtensionsKey}\{id}";
                    var display = scope.Display(entryKey);
                    if (found.TryGetValue(display, out var existing))
                    {
                        if (!existing.Browsers.Contains(browser.Name)) existing.Browsers.Add(browser.Name);
                        continue;
                    }
                    using var sub = key.OpenSubKey(id);
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in sub?.GetValueNames() ?? [])
                        if (sub!.GetValue(name)?.ToString() is { } value) values[name] = value;
                    found[display] = (new Sideload(id, scope, entryKey, values, []), [browser.Name]);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable registry location: skip it.
            }
        }
        return found.Values.Select(v => v.Entry with { Browsers = v.Browsers }).ToList();
    }
}
