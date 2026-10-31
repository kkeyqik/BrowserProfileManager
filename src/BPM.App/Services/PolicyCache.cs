using System.Collections.Concurrent;
using BPM.Core.Actions;
using BPM.Core.Models;

namespace BPM.App.Services;

/// <summary>Machine-wide ExtensionSettings policy per browser, read once per scan.</summary>
public static class PolicyCache
{
    static readonly ConcurrentDictionary<string, string?> Values = new();

    /// <summary>installation_mode the machine policy sets for this extension ("blocked", "removed", …), or null.</summary>
    public static string? ModeFor(ExtensionInfo extension)
    {
        var browser = extension.Profile.Browser.Definition;
        if (browser.PolicyKey == null) return null;
        var json = Values.GetOrAdd(browser.Id, _ =>
        {
            // Display only (which buttons to show); the actions themselves re-read the registry and report errors.
            try { return ExtensionSettingsPolicy.ReadMachine(browser); }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
        });
        return ExtensionSettingsPolicy.ModeFor(json, extension.Id);
    }

    public static void Clear() => Values.Clear();
}
