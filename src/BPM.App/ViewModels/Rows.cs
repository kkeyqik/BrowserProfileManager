using System.Collections.ObjectModel;
using BPM.App.Services;
using BPM.Core.Actions;
using BPM.Core.Analysis;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>A browser in the tree.</summary>
public sealed partial class BrowserNode : ObservableObject
{
    public BrowserNode(BrowserInfo info)
    {
        Info = info;
        isRunning = info.IsRunning;
        Profiles = new ObservableCollection<ProfileNode>(info.Profiles.Select(p => new ProfileNode(this, p)));
    }

    public BrowserInfo Info { get; }
    public ObservableCollection<ProfileNode> Profiles { get; }

    public string Name => Info.Name;
    public string Version => Info.Version ?? "unknown version";
    public System.Windows.Media.ImageSource? Icon => ImageCache.FromProgram(Info.ExePath);
    public string ProfileCountText => Profiles.Count == 1 ? "1 profile" : $"{Profiles.Count} profiles";
    public int ExtensionCount => Profiles.Sum(p => p.Extensions.Count);
    public string ExePath => Info.ExePath ?? "Not found (data folder only)";
    public string DataDir => Info.DataDir ?? "No data folder";
    public string FamilyText => Info.Family == BrowserFamily.Chromium ? "Chromium-based" : "Firefox-based";
    public string PolicyText => Info.Definition.PolicyKey == null
        ? "Not supported: on/off works one profile at a time"
        : Info.Definition.PolicyTested ? "Supported (tested on this kind of browser)" : "Supported (not yet tested)";

    public string RunningText => IsRunning ? "Yes" : "No";

    /// <summary>Chromium browsers whose program is on this PC can get new profiles from the app.</summary>
    public bool CanCreateProfiles => Info.Family == BrowserFamily.Chromium && Info.ExePath != null && Info.DataDir != null;

    [RelayCommand] Task CreateProfile() => ActionService.Instance.CreateProfileAsync(Info);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningText))]
    private bool isRunning;

    [ObservableProperty] private bool isExpanded = true;
    [ObservableProperty] private bool isSelected;
}

/// <summary>A profile in the tree.</summary>
public sealed partial class ProfileNode : ObservableObject
{
    public ProfileNode(BrowserNode browser, ProfileInfo info)
    {
        Browser = browser;
        Info = info;
        isOpen = info.IsOpen;
        sizeBytes = info.SizeBytes;
        Extensions = info.Extensions.Select(e => new ExtensionRow(e)).ToList();
    }

    public BrowserNode Browser { get; }
    public ProfileInfo Info { get; }
    public IReadOnlyList<ExtensionRow> Extensions { get; }

    public string Name => Info.Name;
    public string? Email => Info.Email;
    public string Key => Info.Key.Length > 0 ? Info.Key : "(main profile)";
    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";
    public string? AvatarPath => Info.AvatarPath;
    public uint? ColorArgb => Info.ColorArgb;
    public bool IsLastUsed => Info.IsLastUsed;
    public int EnabledCount => Extensions.Count(e => e.Enabled);
    public string ExtensionCountText => $"{EnabledCount} on · {Extensions.Count - EnabledCount} off";
    public string LastActiveText => Info.LastActive is { } t ? $"Last active {t:d MMM yyyy}" : "";

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private long? sizeBytes;
    [ObservableProperty] private bool isSelected;

    [RelayCommand] Task Open() => ActionService.Instance.OpenProfileAsync(Info);
    [RelayCommand] Task ShowExtensionsPage() => ActionService.Instance.ShowExtensionsPageAsync(Info);
    [RelayCommand] Task CreateShortcut() => ActionService.Instance.CreateShortcutAsync(Info);
    [RelayCommand] Task InstallExtension() => ActionService.Instance.ShowInstallAsync(preselect: Info);
    public bool CanInstallExtensions => Info.Browser.Family == BrowserFamily.Chromium && Info.Browser.ExePath != null;
}

/// <summary>One extension in one profile.</summary>
public sealed partial class ExtensionRow : ObservableObject
{
    IReadOnlyList<PermissionLine>? permissions;

    public ExtensionRow(ExtensionInfo info) => Info = info;

    public ExtensionInfo Info { get; }
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Version => Info.Version;
    public string? Description => Info.Description;
    public bool Enabled => Info.Enabled;
    public string StateText => Info.Enabled ? "On" : "Off";
    public string ReasonText => string.Join("; ", Info.DisableReasons);
    public RiskLevel Risk => Info.Risk.Level;
    public string RiskText => Info.Risk.Level.ToString();
    public IReadOnlyList<string> RiskReasons => Info.Risk.Reasons;
    public string? IconPath => Info.IconPath;
    public string? StoreUrl => Info.StoreUrl;
    public string? HomepageUrl => Info.HomepageUrl;
    public string? FolderPath => Info.FolderPath;
    public string ProfileName => Info.Profile.Name;
    public string BrowserName => Info.Profile.Browser.Name;
    public string Where => $"{BrowserName} · {ProfileName}";
    public string InstalledText => Info.InstalledAt is { } t ? t.ToString("d MMM yyyy") : "Unknown";
    public string UpdatedText => Info.UpdatedAt is { } t ? t.ToString("d MMM yyyy") : "Unknown";
    public string FormatText => Info.ManifestVersion > 0 ? $"Manifest V{Info.ManifestVersion}" : "Unknown";
    public string KindText => Info.Kind.ToString();

    public string SourceText => Info.Source switch
    {
        ExtensionSource.Store => "Store",
        ExtensionSource.Developer => "Developer",
        ExtensionSource.OtherProgram => "Another program",
        ExtensionSource.Policy => "Policy",
        ExtensionSource.Preinstalled => "Pre-installed",
        _ => "Unknown",
    };

    /// <summary>Permissions in plain English, most powerful first.</summary>
    public IReadOnlyList<PermissionLine> Permissions => permissions ??= BuildPermissions();

    IReadOnlyList<PermissionLine> BuildPermissions()
    {
        var lines = new List<PermissionLine>();
        var hosts = PermissionCatalog.DescribeHosts(Info.HostPermissions);
        if (Info.HostPermissions.Count > 0) lines.Add(new PermissionLine(hosts.Description, hosts.Weight));
        lines.AddRange(Info.ApiPermissions.Select(PermissionCatalog.Describe).Select(p => new PermissionLine(p.Description, p.Weight)));
        return lines.DistinctBy(l => l.Text).OrderByDescending(l => l.Weight).ToList();
    }

    public bool Matches(string search) => string.IsNullOrWhiteSpace(search)
        || Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Id.Contains(search, StringComparison.OrdinalIgnoreCase);

    public bool IsChromium => Info.Profile.Browser.Family == BrowserFamily.Chromium;
    public bool SupportsPolicies => Info.Profile.Browser.SupportsPolicies;
    public string AllProfilesText => $"Block in all {BrowserName} profiles (asks for administrator permission)";
    /// <summary>Unblock is offered only for what it can lift: a "blocked" / "removed" entry in the machine-wide
    /// ExtensionSettings policy. Blocks from other policies (e.g. a blocklist set by an administrator) stay.</summary>
    public bool HasPolicyEntry => PolicyCache.ModeFor(Info) is ExtensionSettingsPolicy.Blocked or ExtensionSettingsPolicy.Removed;
    public bool CanReinstall => StoreUrl != null;

    [RelayCommand] Task TurnOn() => ActionService.Instance.SetEnabledHereAsync(Info, true);
    [RelayCommand] Task TurnOff() => ActionService.Instance.SetEnabledHereAsync(Info, false);
    [RelayCommand] Task Remove() => ActionService.Instance.RemoveHereAsync(Info);
    [RelayCommand] Task ShowPage() => ActionService.Instance.ShowPageAsync(Info);
    [RelayCommand] Task BlockEverywhere() => ActionService.Instance.SetPolicyAsync(Info, ExtensionSettingsPolicy.Blocked);
    [RelayCommand] Task RemoveEverywhere() => ActionService.Instance.RemoveEverywhereAsync(Info);
    [RelayCommand] Task UnblockEverywhere() => ActionService.Instance.SetPolicyAsync(Info, null);
    [RelayCommand] Task ResetData() => ActionService.Instance.ResetDataAsync(Info);
    [RelayCommand] Task Reinstall() => ActionService.Instance.ReinstallAsync(Info);
    [RelayCommand] Task InstallElsewhere() => ActionService.Instance.ShowInstallAsync(ActionService.SourceOf(Info));

    [RelayCommand] void CopyId() => Shell.Copy(Id, $"Copied the ID of {Name}");
    [RelayCommand] void OpenStorePage() { if (StoreUrl != null) Shell.OpenUrl(StoreUrl); }
    [RelayCommand] void OpenHomepage() { if (HomepageUrl != null) Shell.OpenUrl(HomepageUrl); }
    [RelayCommand] void ShowFiles() { if (FolderPath != null) Shell.ShowInExplorer(FolderPath); }
}

public sealed record PermissionLine(string Text, PermissionWeight Weight);

/// <summary>Quick filters above the extension lists.</summary>
public sealed record FilterOption(string Title, Func<ExtensionRow, bool> Match)
{
    public static IReadOnlyList<FilterOption> All { get; } =
    [
        new("All extensions", _ => true),
        new("On", r => r.Enabled),
        new("Off", r => !r.Enabled),
        new("Waiting for approval", r => r.Info.NeedsApproval),
        new("High risk", r => r.Risk == RiskLevel.High),
        new("Not from a store", r => r.Info.Source is ExtensionSource.Developer or ExtensionSource.OtherProgram or ExtensionSource.Unknown),
    ];

    public override string ToString() => Title;
}
