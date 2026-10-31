using System.Text.Json.Serialization;

namespace BPM.Core.Models;

/// <summary>One browser profile (a Chromium "Profile N" folder or a Firefox profile).</summary>
public sealed class ProfileInfo
{
    [JsonIgnore] public BrowserInfo Browser { get; set; } = null!;

    /// <summary>Stable key inside the browser: the Chromium folder name ("Default", "Profile 3") or the Firefox profile path from profiles.ini.</summary>
    public required string Key { get; init; }

    /// <summary>Full path of the profile folder.</summary>
    public required string Path { get; init; }

    /// <summary>Display name ("Work", "Your Chrome", "default-release").</summary>
    public required string Name { get; init; }

    /// <summary>Account signed in to the browser profile, if any.</summary>
    public string? Email { get; init; }
    public string? FullName { get; init; }

    /// <summary>First name of the signed-in account ("Alex"); Chrome shows it on the profile button.</summary>
    public string? GivenName { get; init; }

    /// <summary>Profile picture on disk, if the browser saved one.</summary>
    public string? AvatarPath { get; init; }

    /// <summary>Profile theme colour (ARGB) when the browser records one.</summary>
    public uint? ColorArgb { get; init; }

    public bool IsLastUsed { get; init; }
    public bool WasOpenLastSession { get; init; }

    /// <summary>Firefox: the profile marked Default in profiles.ini.</summary>
    public bool IsDefault { get; init; }

    public DateTime? LastActive { get; init; }

    /// <summary>True when the profile's databases are locked by a running browser (see ProfileLockProbe).</summary>
    public bool IsOpen { get; set; }

    /// <summary>Folder size; filled in later because it is slow for big profiles.</summary>
    public long? SizeBytes { get; set; }

    public string? ScanError { get; set; }

    public List<ExtensionInfo> Extensions { get; } = [];

    [JsonIgnore] public string DisplayTitle => Email is { Length: > 0 } ? $"{Name} · {Email}" : Name;
}
