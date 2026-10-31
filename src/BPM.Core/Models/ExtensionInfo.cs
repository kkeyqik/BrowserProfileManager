using System.Text.Json.Serialization;

namespace BPM.Core.Models;

/// <summary>Where an extension came from.</summary>
public enum ExtensionSource
{
    /// <summary>Installed by the user from the Chrome Web Store, Edge Add-ons or addons.mozilla.org.</summary>
    Store,
    /// <summary>Loaded unpacked / temporary (developer mode).</summary>
    Developer,
    /// <summary>Pushed in by another program through the registry or an external file ("sideloaded").</summary>
    OtherProgram,
    /// <summary>Force-installed by an enterprise policy.</summary>
    Policy,
    /// <summary>Shipped with the browser or by the PC maker.</summary>
    Preinstalled,
    /// <summary>Installed from a file or source the app can't classify.</summary>
    Unknown,
}

public enum ExtensionKind { Extension, Theme, App }

public enum RiskLevel { Low, Medium, High }

/// <summary>Risk verdict plus the plain-English reasons behind it.</summary>
public sealed record RiskAssessment(RiskLevel Level, IReadOnlyList<string> Reasons)
{
    public static readonly RiskAssessment None = new(RiskLevel.Low, []);
}

/// <summary>One extension inside one profile.</summary>
public sealed class ExtensionInfo
{
    [JsonIgnore] public ProfileInfo Profile { get; set; } = null!;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "?";

    public bool Enabled { get; init; }

    /// <summary>Raw Chromium disable-reason bit mask (0 for Firefox; see <see cref="Chromium.DisableReasonCodes"/>).</summary>
    public long DisableReasonBits { get; init; }

    /// <summary>Plain-English reasons the extension is off, e.g. "turned off by you", "blocked by policy".</summary>
    public IReadOnlyList<string> DisableReasons { get; init; } = [];

    public ExtensionSource Source { get; init; }
    public ExtensionKind Kind { get; init; }

    /// <summary>Chromium location code from Preferences (1 = normal install, 4 = unpacked, …); 0 for Firefox.</summary>
    public int Location { get; init; }

    /// <summary>Manifest version (2 or 3); 0 when unknown.</summary>
    public int ManifestVersion { get; init; }

    public string? UpdateUrl { get; init; }
    public string? HomepageUrl { get; init; }
    public string? StoreUrl { get; init; }

    /// <summary>Folder (Chromium) or .xpi file (Firefox) holding the extension's code.</summary>
    public string? FolderPath { get; init; }
    public string? IconPath { get; init; }
    public bool FilesPresent { get; init; }
    public long? SizeBytes { get; init; }

    public DateTime? InstalledAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>API permissions (tabs, cookies, storage, …), granted ones when the browser records them.</summary>
    public IReadOnlyList<string> ApiPermissions { get; init; } = [];

    /// <summary>Site access patterns (https://*/*, &lt;all_urls&gt;, …) including content-script matches.</summary>
    public IReadOnlyList<string> HostPermissions { get; init; } = [];

    public RiskAssessment Risk { get; set; } = RiskAssessment.None;

    [JsonIgnore] public bool BlockedByPolicy => (DisableReasonBits & Chromium.DisableReasonCodes.BlockedByPolicy) != 0;
    [JsonIgnore] public bool NeedsApproval => (DisableReasonBits & Chromium.DisableReasonCodes.ExternalExtension) != 0;
    [JsonIgnore] public string StateText => Enabled ? "On" : "Off";
}
