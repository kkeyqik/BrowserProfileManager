using System.Collections.ObjectModel;
using BPM.App.Services;
using BPM.Core.Diagnostics;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>"Health": accounts that need attention, unused profiles, broken browser links and policies (plus Claude in
/// Chrome checks when Claude is installed).</summary>
public sealed partial class DoctorViewModel(MainViewModel main) : ObservableObject
{
    List<NativeHost> hosts = [];

    public ObservableCollection<CheckRow> Checks { get; } = [];
    public ObservableCollection<NativeHost> Hosts { get; } = [];

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool hasRun;
    [ObservableProperty] private string summary = "Run a scan first.";

    int loadVersion;

    public async Task LoadAsync(ScanResult scan)
    {
        // Loads can overlap (live refresh, actions): only the newest one may fill the page.
        int version = ++loadVersion;
        // Read the scan on the UI thread: a live refresh may replace a browser in it while the checks run.
        var definitions = scan.Browsers.Select(b => b.Definition).ToList();
        var snapshot = scan.Snapshot();
        var running = scan.Browsers.Where(b => b.IsRunning).ToList();
        IsRunning = true;
        try
        {
            var (checks, found) = await Task.Run(() =>
            {
                var links = NativeHostInspector.Find(definitions);
                var policies = PolicyInspector.Read(definitions);
                // Profile buttons of open windows show sign-in problems ("Paused"); closed profiles can't be checked.
                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var browser in running)
                    foreach (var (path, label) in BPM.Core.Automation.BrowserAutomation.ReadProfileButtonLabels(browser))
                        labels[path] = label;
                return (HealthChecker.Run(snapshot, links, policies, labels, DateTime.Now), links);
            });
            if (version != loadVersion) return;
            hosts = found;
            Checks.Clear();
            foreach (var check in checks) Checks.Add(new CheckRow(check)); // already in page order
            Hosts.Clear();
            foreach (var host in hosts) Hosts.Add(host);
            int problems = checks.Count(c => c.Status is CheckStatus.Warning or CheckStatus.Error);
            Summary = problems == 0 ? "Everything looks fine." : problems == 1 ? "1 thing needs attention." : $"{problems} things need attention.";
            HasRun = true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (version != loadVersion) return;
            ErrorLog.Write(e);
            Summary = "The checks couldn't finish: " + e.Message;
        }
        finally
        {
            if (version == loadVersion) IsRunning = false;
        }
    }

    [RelayCommand]
    async Task Rerun()
    {
        if (main.Scan is { } scan) await LoadAsync(scan);
    }

    [RelayCommand]
    async Task Fix(CheckRow? row)
    {
        if (row == null) return;
        var check = row.Check;
        switch (check.Fix)
        {
            case FixKind.KeepOneClaudeProfile when check.FixData != null:
                await ActionService.Instance.KeepOneClaudeProfileAsync(check.FixData);
                break;
            case FixKind.RemoveNativeHost when hosts.FirstOrDefault(h => h.DisplayKey == check.FixData) is { } host:
                await ActionService.Instance.RemoveNativeHostAsync(host);
                break;
            case FixKind.ShowSideloads:
                main.Navigate(main.Sideloads);
                return;
            case FixKind.OpenExtension when check.FixData != null:
                main.AllExtensions.Show(check.FixData);
                main.Navigate(main.AllExtensions);
                return;
            case FixKind.OpenProfile when FindProfile(check.FixData) is { } profile:
                await ActionService.Instance.OpenProfileAsync(profile);
                return; // its button updates once the window is open: run the checks again then
            case FixKind.ShowProfile when FindProfile(check.FixData) is { } profile:
                main.Navigate(main.Browsers);
                main.Browsers.SelectProfile(profile);
                return;
            case FixKind.ShowPolicies:
                main.Navigate(main.Sideloads);
                return;
        }
        await Rerun();
    }

    /// <summary>The profile a fix names (browser id and profile folder, see HealthChecker.Key) in the latest scan.</summary>
    ProfileInfo? FindProfile(string? key) =>
        main.Scan?.AllProfiles.FirstOrDefault(p => key == HealthChecker.Key(p));

    [RelayCommand]
    async Task RemoveHost(NativeHost? host)
    {
        if (host == null) return;
        await ActionService.Instance.RemoveNativeHostAsync(host);
        await Rerun();
    }
}

public sealed class CheckRow(DoctorCheck check)
{
    public DoctorCheck Check { get; } = check;
    public CheckStatus Status => Check.Status;
    public string Title => Check.Title;
    public string Detail => Check.Detail;
    public string Group => Check.Group;
    public bool HasFix => Check.Fix != FixKind.None;

    public string FixText => Check.Fix switch
    {
        FixKind.KeepOneClaudeProfile => "Choose profile…",
        FixKind.RemoveNativeHost => "Remove link…",
        FixKind.ShowSideloads => "Show sideloads",
        FixKind.OpenExtension => "Show extension",
        FixKind.OpenProfile => "Open profile",
        FixKind.ShowProfile => "Show",
        FixKind.ShowPolicies => "Show policies",
        _ => "",
    };
}
