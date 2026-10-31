using System.Collections.ObjectModel;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BPM.App.ViewModels;

/// <summary>"Browsers & profiles" page: browser/profile tree, the selected profile's extensions and details.</summary>
public sealed partial class BrowsersViewModel : ObservableObject
{
    public ObservableCollection<BrowserNode> Browsers { get; } = [];
    public ObservableCollection<ExtensionRow> VisibleExtensions { get; } = [];
    public IReadOnlyList<FilterOption> Filters => FilterOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfile), nameof(SelectedBrowser), nameof(HasProfile), nameof(HasBrowser), nameof(HasNothing))]
    private object? selectedNode;

    [ObservableProperty] private ExtensionRow? selectedExtension;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private FilterOption selectedFilter = FilterOption.All[0];

    public ProfileNode? SelectedProfile => SelectedNode as ProfileNode;
    public BrowserNode? SelectedBrowser => SelectedNode as BrowserNode;
    public bool HasProfile => SelectedProfile != null;
    public bool HasBrowser => SelectedBrowser != null;
    public bool HasNothing => SelectedNode == null;

    partial void OnSelectedNodeChanged(object? value) => RefreshList();
    partial void OnSearchTextChanged(string value) => RefreshList();
    partial void OnSelectedFilterChanged(FilterOption value) => RefreshList();

    void RefreshList()
    {
        var keep = SelectedExtension?.Id;
        VisibleExtensions.Clear();
        if (SelectedProfile is { } profile)
            foreach (var row in profile.Extensions.Where(r => SelectedFilter.Match(r) && r.Matches(SearchText)))
                VisibleExtensions.Add(row);
        SelectedExtension = VisibleExtensions.FirstOrDefault(r => r.Id == keep) ?? VisibleExtensions.FirstOrDefault();
    }

    /// <summary>Rebuilds the tree from a new scan, keeping the current selection where possible.</summary>
    public void Load(ScanResult result)
    {
        var browserId = SelectedProfile?.Browser.Info.Id ?? SelectedBrowser?.Info.Id;
        var profileKey = SelectedProfile?.Info.Key;

        Browsers.Clear();
        foreach (var browser in result.Browsers) Browsers.Add(new BrowserNode(browser));

        var node = Browsers.FirstOrDefault(b => b.Info.Id == browserId) ?? Browsers.FirstOrDefault();
        if (node == null)
        {
            SelectedNode = null;
            return;
        }
        ProfileNode? profile = profileKey != null
            ? node.Profiles.FirstOrDefault(p => p.Info.Key == profileKey)
            : node.Profiles.FirstOrDefault(p => p.IsLastUsed) ?? node.Profiles.FirstOrDefault();
        Select(profile != null ? profile : node);
    }

    /// <summary>Swaps one browser's subtree after a live refresh.</summary>
    public void ReplaceBrowser(BrowserInfo old, BrowserInfo fresh)
    {
        int index = Browsers.ToList().FindIndex(b => b.Info == old);
        if (index < 0) return;
        var oldNode = Browsers[index];
        var selectedKey = SelectedProfile is { } selected && selected.Browser == oldNode ? selected.Info.Key : null;
        bool browserSelected = SelectedBrowser == oldNode;

        var node = new BrowserNode(fresh) { IsExpanded = oldNode.IsExpanded };
        Browsers[index] = node;
        if (selectedKey != null && node.Profiles.FirstOrDefault(p => p.Info.Key == selectedKey) is { } profile) Select(profile);
        else if (browserSelected || selectedKey != null) Select(node);
    }

    public void SelectProfile(ProfileInfo profile)
    {
        var node = Browsers.SelectMany(b => b.Profiles).FirstOrDefault(p => p.Info == profile)
            ?? Browsers.SelectMany(b => b.Profiles).FirstOrDefault(p => p.Info.Path == profile.Path);
        if (node != null) Select(node);
    }

    void Select(object node)
    {
        if (node is ProfileNode p)
        {
            p.Browser.IsExpanded = true;
            p.IsSelected = true;
        }
        else if (node is BrowserNode b)
        {
            b.IsSelected = true;
        }
        SelectedNode = node;
    }

    public void OnSizeUpdated(ProfileInfo profile)
    {
        var node = Browsers.SelectMany(b => b.Profiles).FirstOrDefault(p => p.Info == profile);
        if (node != null) node.SizeBytes = profile.SizeBytes;
    }

    public void UpdateLiveState()
    {
        foreach (var browser in Browsers)
        {
            browser.IsRunning = browser.Info.IsRunning;
            foreach (var profile in browser.Profiles) profile.IsOpen = profile.Info.IsOpen;
        }
    }
}
