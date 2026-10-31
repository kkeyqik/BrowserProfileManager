using System.Text.Json.Serialization;

namespace BPM.Core.Models;

public enum BrowserFamily { Chromium, Firefox }

/// <summary>Static facts about one browser product (from <see cref="Browsers.BrowserCatalog"/>).</summary>
public sealed record BrowserDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public BrowserFamily Family { get; init; } = BrowserFamily.Chromium;

    /// <summary>Candidate data folders: Chromium = folder holding "Local State", Firefox = folder holding profiles.ini.
    /// Environment variables are expanded and one '*' segment is allowed (Store-app package folders).</summary>
    public required IReadOnlyList<string> DataDirs { get; init; }

    /// <summary>Candidate executables, same expansion rules as <see cref="DataDirs"/>.</summary>
    public IReadOnlyList<string> ExePaths { get; init; } = [];

    /// <summary>Enterprise policy key under HKLM/HKCU, e.g. SOFTWARE\Policies\Google\Chrome. Null when the browser has none.</summary>
    public string? PolicyKey { get; init; }

    /// <summary>True when the policy route was proven on a real install (M1 test), not just found in the browser's binaries.</summary>
    public bool PolicyTested { get; init; }

    /// <summary>Registry keys (under HKCU/HKLM) the browser reads native-messaging host registrations from, in lookup order.</summary>
    public IReadOnlyList<string> NativeHostKeys { get; init; } = [];

    /// <summary>Registry key other programs use to push ("sideload") extensions into this browser.</summary>
    public string? ExternalExtensionsKey { get; init; }

    /// <summary>The browser's own extensions page, e.g. chrome://extensions or about:addons.</summary>
    public string ExtensionsPageUrl { get; init; } = "chrome://extensions";
}

/// <summary>A browser found on this PC.</summary>
public sealed class BrowserInfo
{
    public required BrowserDefinition Definition { get; init; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public BrowserFamily Family => Definition.Family;

    public string? ExePath { get; init; }
    public string? Version { get; init; }
    public string? DataDir { get; init; }
    public bool IsRunning { get; set; }
    public string? ScanError { get; set; }

    public List<ProfileInfo> Profiles { get; } = [];

    [JsonIgnore] public bool IsInstalled => ExePath != null;
    [JsonIgnore] public bool SupportsPolicies => Definition.PolicyKey != null;
}

/// <summary>A browser registered with Windows that the app doesn't know how to read.</summary>
public sealed record OtherBrowser(string Name, string ExePath);
