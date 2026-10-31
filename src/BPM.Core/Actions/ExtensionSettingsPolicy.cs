using System.Text.Json.Nodes;
using BPM.Core.Models;
using BPM.Core.Util;
using Microsoft.Win32;

namespace BPM.Core.Actions;

/// <summary>The browsers' "ExtensionSettings" policy: per-extension installation_mode (blocked / removed / …) for every profile.
/// Proven in M1: Chrome on a PC that isn't company-managed ignores per-user policies but applies machine-wide ones (HKLM).</summary>
public static class ExtensionSettingsPolicy
{
    public const string ValueName = "ExtensionSettings";
    public const string Blocked = "blocked";
    public const string Removed = "removed";

    /// <summary>Sets an extension's installation_mode in the policy JSON. With <paramref name="mode"/> null it lifts only a
    /// "blocked" / "removed" mode (an admin's force_installed or allowed is left alone). Other extensions' entries and
    /// other settings are kept. Returns null when nothing is left (delete the value).</summary>
    /// <exception cref="InvalidDataException">The existing value isn't a JSON object: it's left untouched rather than overwritten.</exception>
    public static string? Apply(string? existingJson, string extensionId, string? mode) =>
        ApplyExact(existingJson, extensionId, mode, liftOnlyBlocks: true);

    /// <summary>Like <see cref="Apply"/>, but a null <paramref name="mode"/> removes any installation_mode (used by undo to
    /// put back exactly what was there before).</summary>
    public static string? ApplyExact(string? existingJson, string extensionId, string? mode, bool liftOnlyBlocks = false)
    {
        var root = Parse(existingJson);
        if (mode != null)
        {
            if (root[extensionId] is not JsonObject entry)
            {
                entry = new JsonObject();
                root[extensionId] = entry;
            }
            entry["installation_mode"] = mode;
        }
        else if (root[extensionId] is JsonObject entry)
        {
            var current = entry["installation_mode"].AsString();
            if (!liftOnlyBlocks || current is Blocked or Removed) entry.Remove("installation_mode");
            if (entry.Count == 0) root.Remove(extensionId);
        }
        return root.Count == 0 ? null : root.ToJsonString();
    }

    /// <summary>True when two policy values mean the same thing (formatting differences ignored).</summary>
    public static bool SameJson(string? a, string? b)
    {
        bool emptyA = string.IsNullOrWhiteSpace(a), emptyB = string.IsNullOrWhiteSpace(b);
        if (emptyA || emptyB) return emptyA == emptyB;
        return JsonNode.DeepEquals(JsonUtil.Parse(a), JsonUtil.Parse(b));
    }

    static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonUtil.Parse(json) as JsonObject
            ?? throw new InvalidDataException("The browser's existing ExtensionSettings policy isn't valid JSON, so the app won't change it.");
    }

    /// <summary>installation_mode the policy currently sets for an extension, if any.</summary>
    public static string? ModeFor(string? json, string extensionId) =>
        JsonUtil.Parse(json).At(extensionId, "installation_mode").AsString();

    /// <summary>Every catalog browser that reads the same policy key (Chrome's channels share one).</summary>
    public static IReadOnlyList<BrowserDefinition> SharingPolicy(BrowserDefinition browser) =>
        Browsers.BrowserCatalog.All.Where(b => b.PolicyKey != null && string.Equals(b.PolicyKey, browser.PolicyKey, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The machine-wide (HKLM, 64-bit view) ExtensionSettings value for a browser.</summary>
    public static string? ReadMachine(BrowserDefinition browser)
    {
        if (browser.PolicyKey == null) return null;
        // No catch on purpose: callers build the new value from this one, so "unreadable" must never look like "empty".
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(browser.PolicyKey);
        return key?.GetValue(ValueName) as string;
    }

    /// <summary>Registry operation that writes <paramref name="newJson"/> (or deletes the value when null).</summary>
    public static RegistryOperation Write(BrowserDefinition browser, string? newJson)
    {
        var key = browser.PolicyKey ?? throw new InvalidOperationException($"{browser.Name} has no policy support.");
        return newJson == null
            ? new RegistryOperation(RegistryOpKind.DeleteValue, key, ValueName)
            : new RegistryOperation(RegistryOpKind.SetString, key, ValueName, newJson);
    }
}
