using System.Text.Json.Nodes;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Diagnostics;

public enum HostStatus { Ok, ManifestMissing, ManifestUnreadable, ProgramMissing }

/// <summary>A native-messaging registration: the link that lets an extension talk to a program on this PC
/// (e.g. the Claude app's chrome-native-host.exe).</summary>
public sealed record NativeHost(
    string Name,
    RegistryScope Scope,
    string Key,
    string? ManifestPath,
    string? ProgramPath,
    IReadOnlyList<string> AllowedExtensions,
    IReadOnlyList<string> Browsers,
    HostStatus Status)
{
    public string DisplayKey => Scope.Display(Key);
    public bool IsBroken => Status != HostStatus.Ok;

    public string StatusText => Status switch
    {
        HostStatus.Ok => "Working",
        HostStatus.ManifestMissing => "Its description file is missing",
        HostStatus.ManifestUnreadable => "Its description file is damaged",
        _ => "Its program is missing",
    };
}

public static class NativeHostInspector
{
    public static List<NativeHost> Find(IEnumerable<BrowserDefinition> browsers)
    {
        var found = new Dictionary<string, (NativeHost Host, List<string> Browsers)>(StringComparer.OrdinalIgnoreCase);
        foreach (var browser in browsers)
        foreach (var hostsKey in browser.NativeHostKeys)
        foreach (var scope in RegistryScope.All)
        {
            try
            {
                using var root = scope.OpenBase();
                using var key = root.OpenSubKey(hostsKey);
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    var hostKey = $@"{hostsKey}\{name}";
                    var id = scope.Display(hostKey);
                    if (found.TryGetValue(id, out var existing))
                    {
                        if (!existing.Browsers.Contains(browser.Name)) existing.Browsers.Add(browser.Name);
                        continue;
                    }
                    using var sub = key.OpenSubKey(name);
                    found[id] = (Describe(name, scope, hostKey, sub?.GetValue("") as string), [browser.Name]);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable registry location: skip it.
            }
        }
        return found.Values
            .Select(v => v.Host with { Browsers = v.Browsers })
            .OrderBy(h => h.IsBroken ? 0 : 1).ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static NativeHost Describe(string name, RegistryScope scope, string key, string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return new(name, scope, key, manifestPath, null, [], [], HostStatus.ManifestMissing);

        var manifest = JsonUtil.Load(manifestPath);
        if (manifest == null)
            return new(name, scope, key, manifestPath, null, [], [], HostStatus.ManifestUnreadable);

        var program = manifest.At("path").AsString();
        if (program != null && !Path.IsPathRooted(program)) program = Path.Combine(Path.GetDirectoryName(manifestPath)!, program);

        var allowed = manifest.At("allowed_origins").AsStrings()
            .Select(o => o.Replace("chrome-extension://", "", StringComparison.OrdinalIgnoreCase).Trim('/'))
            .Concat(manifest.At("allowed_extensions").AsStrings()) // Firefox style
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var status = program != null && File.Exists(program) ? HostStatus.Ok : HostStatus.ProgramMissing;
        return new(name, scope, key, manifestPath, program, allowed, [], status);
    }
}
