using System.Text.Json.Nodes;
using BPM.Core.Browsers;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Chromium;

/// <summary>Lists a Chromium browser's profiles from "Local State" and the profile folders.</summary>
public static class ChromiumProfileReader
{
    static readonly HashSet<string> HiddenFolders = new(StringComparer.OrdinalIgnoreCase) { "System Profile", "Guest Profile" };

    public static List<ProfileInfo> Read(BrowserInfo browser)
    {
        var userData = browser.DataDir ?? throw new InvalidOperationException($"{browser.Name} has no data folder.");
        var state = JsonUtil.Load(Path.Combine(userData, "Local State"));
        var cache = state.At("profile", "info_cache") as JsonObject;
        var lastActive = state.At("profile", "last_active_profiles").AsStrings().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lastUsed = state.At("profile", "last_used").AsString();

        var folders = new List<string>();
        if (cache != null) folders.AddRange(cache.Select(kv => kv.Key));
        foreach (var dir in Directory.EnumerateDirectories(userData))
        {
            var name = Path.GetFileName(dir);
            if (!folders.Contains(name, StringComparer.OrdinalIgnoreCase) && File.Exists(Path.Combine(dir, "Preferences")))
                folders.Add(name);
        }

        var profiles = new List<ProfileInfo>();
        foreach (var folder in folders.Where(f => !HiddenFolders.Contains(f)).OrderBy(SortKey).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(userData, folder);
            if (!Directory.Exists(path)) continue; // stale Local State entry
            profiles.Add(ReadProfile(browser, folder, path, cache?[folder], lastUsed, lastActive));
        }

        // Older Opera kept a single profile directly in the data folder.
        if (profiles.Count == 0 && File.Exists(Path.Combine(userData, "Preferences")))
            profiles.Add(ReadProfile(browser, "", userData, null, null, lastActive));

        return profiles;
    }

    static ProfileInfo ReadProfile(BrowserInfo browser, string folder, string path, JsonNode? entry, string? lastUsed, HashSet<string> lastActive)
    {
        var profile = new ProfileInfo
        {
            Browser = browser,
            Key = folder,
            Path = path,
            Name = NonEmpty(entry.At("name").AsString()) ?? (folder.Length > 0 ? folder : browser.Name),
            Email = NonEmpty(entry.At("user_name").AsString()),
            FullName = NonEmpty(entry.At("gaia_name").AsString()),
            GivenName = NonEmpty(entry.At("gaia_given_name").AsString()),
            AvatarPath = File.Exists(Path.Combine(path, "Google Profile Picture.png")) ? Path.Combine(path, "Google Profile Picture.png") : null,
            ColorArgb = Color(entry),
            IsLastUsed = lastUsed != null ? folder.Equals(lastUsed, StringComparison.OrdinalIgnoreCase) : folder == "Default",
            WasOpenLastSession = lastActive.Contains(folder),
            LastActive = UnixSeconds(entry.At("active_time").AsDouble()),
        };
        profile.IsOpen = browser.IsRunning && ProfileLockProbe.IsChromiumProfileOpen(path);
        try
        {
            profile.Extensions.AddRange(ChromiumExtensionReader.Read(profile));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An odd extension entry (bad path, unexpected JSON) marks this profile; the rest of the scan goes on.
            profile.ScanError = e.Message;
        }
        return profile;
    }

    static int SortKey(string folder)
    {
        if (folder.Equals("Default", StringComparison.OrdinalIgnoreCase)) return -1;
        return folder.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) && int.TryParse(folder[8..], out var n) ? n : int.MaxValue;
    }

    static uint? Color(JsonNode? entry)
    {
        var value = entry.At("profile_highlight_color").AsLong() ?? entry.At("default_avatar_fill_color").AsLong();
        return value is { } v ? unchecked((uint)v) : null;
    }

    static DateTime? UnixSeconds(double? seconds) =>
        seconds is > 0 and < 32503680000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.Value * 1000)).LocalDateTime : null;

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
