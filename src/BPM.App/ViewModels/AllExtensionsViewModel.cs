using System.Collections.ObjectModel;
using BPM.App.Services;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>"All extensions": each extension once, with every profile it's installed in.</summary>
public sealed partial class AllExtensionsViewModel : ObservableObject
{
    List<ExtensionGroup> all = [];

    public ObservableCollection<ExtensionGroup> Groups { get; } = [];
    public ObservableCollection<ExtensionRow> Installs { get; } = [];
    public IReadOnlyList<GroupFilter> Filters => GroupFilter.All;

    [ObservableProperty] private ExtensionGroup? selectedGroup;
    [ObservableProperty] private ExtensionRow? selectedInstall;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private GroupFilter selectedFilter = GroupFilter.All[0];

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSelectedFilterChanged(GroupFilter value) => Refresh();

    partial void OnSelectedGroupChanged(ExtensionGroup? value)
    {
        var keep = SelectedInstall?.Info.Profile.Path;
        Installs.Clear();
        if (value != null) foreach (var row in value.Installs) Installs.Add(row);
        SelectedInstall = Installs.FirstOrDefault(r => r.Info.Profile.Path == keep) ?? Installs.FirstOrDefault();
    }

    public void Load(ScanResult scan)
    {
        var keep = SelectedGroup?.Id;
        all = scan.AllExtensions
            .GroupBy(e => e.Id)
            .Select(g => new ExtensionGroup(g.Key, g.Select(e => new ExtensionRow(e)).ToList()))
            .OrderByDescending(g => g.Installs.Count)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Refresh(keep);
    }

    [RelayCommand]
    Task InstallExtension() => ActionService.Instance.ShowInstallAsync();

    [RelayCommand]
    Task RemoveEverywhere() => SelectedGroup?.Installs.FirstOrDefault() is { } copy
        ? ActionService.Instance.RemoveEverywhereAsync(copy.Info)
        : Task.CompletedTask;

    /// <summary>Selects one extension (used by the dashboard's "Show" links).</summary>
    public void Show(string extensionId)
    {
        SearchText = "";
        SelectedFilter = GroupFilter.All[0];
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == extensionId) ?? SelectedGroup;
    }

    void Refresh() => Refresh(SelectedGroup?.Id);

    void Refresh(string? keepId)
    {
        Groups.Clear();
        foreach (var group in all.Where(g => SelectedFilter.Match(g) && g.Matches(SearchText))) Groups.Add(group);
        SelectedGroup = Groups.FirstOrDefault(g => g.Id == keepId) ?? Groups.FirstOrDefault();
    }
}

/// <summary>One extension across all profiles.</summary>
public sealed class ExtensionGroup
{
    public ExtensionGroup(string id, List<ExtensionRow> installs)
    {
        Id = id;
        Installs = installs.OrderBy(r => r.BrowserName).ThenBy(r => r.ProfileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        Name = installs.GroupBy(r => r.Name).OrderByDescending(g => g.Count()).First().Key;
        IconPath = installs.Select(r => r.IconPath).FirstOrDefault(p => p != null);
        Versions = installs.Select(r => r.Version).Distinct().OrderBy(v => System.Version.TryParse(v, out var parsed) ? parsed : new System.Version(0, 0)).ToList();
        MaxRisk = installs.Max(r => r.Risk);
    }

    public string Id { get; }
    public string Name { get; }
    public string? IconPath { get; }
    public IReadOnlyList<ExtensionRow> Installs { get; }
    public IReadOnlyList<string> Versions { get; }
    public RiskLevel MaxRisk { get; }
    public string RiskText => MaxRisk.ToString();
    public int OnCount => Installs.Count(r => r.Enabled);
    public int OffCount => Installs.Count - OnCount;
    public bool HasManyVersions => Versions.Count > 1;
    public string Summary => $"{(Installs.Count == 1 ? "1 profile" : $"{Installs.Count} profiles")} · {OnCount} on · {OffCount} off";
    public string BrowsersText => string.Join(", ", Installs.Select(r => r.BrowserName).Distinct());
    public string VersionsText => Versions.Count == 1 ? $"Version {Versions[0]}" : $"Versions {string.Join(", ", Versions)}";

    public bool Matches(string search) => string.IsNullOrWhiteSpace(search)
        || Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || Id.Contains(search, StringComparison.OrdinalIgnoreCase);
}

public sealed record GroupFilter(string Title, Func<ExtensionGroup, bool> Match)
{
    public static IReadOnlyList<GroupFilter> All { get; } =
    [
        new("All extensions", _ => true),
        new("In several profiles", g => g.Installs.Count > 1),
        new("Different versions", g => g.HasManyVersions),
        new("High risk", g => g.MaxRisk == RiskLevel.High),
        new("Waiting for approval", g => g.Installs.Any(r => r.Info.NeedsApproval)),
        new("Not from a store", g => g.Installs.Any(r => r.Info.Source is ExtensionSource.Developer or ExtensionSource.OtherProgram or ExtensionSource.Unknown)),
    ];

    public override string ToString() => Title;
}
