using System.Collections.ObjectModel;
using BPM.App.Services;
using BPM.Core.Diagnostics;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>"Sideloads & policies": extensions other programs push in through the registry, and every browser policy set on this PC.</summary>
public sealed partial class SideloadsViewModel(MainViewModel main) : ObservableObject
{
    public ObservableCollection<SideloadRow> Sideloads { get; } = [];
    public ObservableCollection<PolicyValue> Policies { get; } = [];

    [ObservableProperty] private bool hasRun;

    public bool NoSideloads => HasRun && Sideloads.Count == 0;
    public bool NoPolicies => HasRun && Policies.Count == 0;

    int loadVersion;

    public async Task LoadAsync(ScanResult scan)
    {
        // Loads can overlap (live refresh, actions): only the newest one may fill the page.
        int version = ++loadVersion;
        var browsers = scan.Browsers.Select(b => b.Definition).ToList();
        var extensions = scan.AllExtensions.ToList();
        List<Sideload> sideloads;
        List<PolicyValue> policies;
        try
        {
            (sideloads, policies) = await Task.Run(() => (SideloadInspector.Find(browsers), PolicyInspector.Read(browsers)));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ErrorLog.Write(e);
            if (version == loadVersion) Notifier.Error("Couldn't read sideloads and policies", e.Message);
            return;
        }
        if (version != loadVersion) return;

        Sideloads.Clear();
        foreach (var entry in sideloads.OrderBy(s => s.Scope.IsMachine).ThenBy(s => s.ExtensionId))
        {
            var copies = extensions.Where(e => e.Id == entry.ExtensionId && entry.Browsers.Contains(e.Profile.Browser.Name)).ToList();
            Sideloads.Add(new SideloadRow(entry, copies));
        }
        Policies.Clear();
        foreach (var policy in policies) Policies.Add(policy);
        HasRun = true;
        OnPropertyChanged(nameof(NoSideloads));
        OnPropertyChanged(nameof(NoPolicies));
    }

    [RelayCommand]
    async Task Remove(SideloadRow? row)
    {
        if (row == null) return;
        await ActionService.Instance.RemoveSideloadAsync(row.Entry, row.Name);
        if (main.Scan is { } scan) await LoadAsync(scan);
    }
}

public sealed class SideloadRow(Sideload entry, IReadOnlyList<ExtensionInfo> copies)
{
    public Sideload Entry { get; } = entry;
    public string Id => Entry.ExtensionId;
    public string Name => copies.FirstOrDefault()?.Name
        ?? (Core.Analysis.ProblemFinder.ClaudeIds.Contains(Entry.ExtensionId) ? "Claude (another build)" : Entry.ExtensionId);
    public string? IconPath => copies.Select(c => c.IconPath).FirstOrDefault(p => p != null);
    public string SourceText => Entry.SourceText;
    public string ScopeText => Entry.Scope.Label;
    public string BrowsersText => string.Join(", ", Entry.Browsers);
    public string DisplayKey => Entry.DisplayKey;

    public string CountsText
    {
        get
        {
            int waiting = copies.Count(c => c.NeedsApproval), on = copies.Count(c => c.Enabled);
            if (copies.Count == 0) return "Not in any profile yet";
            var parts = new List<string>();
            if (waiting > 0) parts.Add($"waiting for approval in {waiting}");
            if (on > 0) parts.Add($"on in {on}");
            int other = copies.Count - waiting - on;
            if (other > 0) parts.Add($"off in {other}");
            return $"{copies.Count} profile(s): {string.Join(", ", parts)}";
        }
    }
}
