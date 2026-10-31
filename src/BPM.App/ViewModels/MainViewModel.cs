using System.Collections.ObjectModel;
using System.Windows;
using BPM.App.Services;
using BPM.Core.Browsers;
using BPM.Core.Models;
using BPM.Core.Scanning;
using BPM.Core.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BPM.App.ViewModels;

public sealed record NavItem(string Title, SymbolRegular Icon, ObservableObject Page);

public sealed partial class MainViewModel : ObservableObject
{
    readonly LiveWatcher watcher;
    CancellationTokenSource? sizeCts;

    public MainViewModel(AppSettings settings)
    {
        AppSettings = settings;
        Dashboard = new DashboardViewModel(this);
        Browsers = new BrowsersViewModel();
        AllExtensions = new AllExtensionsViewModel();
        Settings = new SettingsViewModel(settings);
        ChangeLog = new ChangeLogViewModel();
        Doctor = new DoctorViewModel(this);
        Sideloads = new SideloadsViewModel(this);
        NavItems =
        [
            new("Dashboard", SymbolRegular.Board24, Dashboard),
            new("Browsers & profiles", SymbolRegular.Globe24, Browsers),
            new("All extensions", SymbolRegular.PuzzlePiece24, AllExtensions),
            new("Health", SymbolRegular.HeartPulse24, Doctor),
            new("Sideloads & policies", SymbolRegular.ShieldKeyhole24, Sideloads),
            new("Change log", SymbolRegular.History24, ChangeLog),
            new("Settings", SymbolRegular.Settings24, Settings),
        ];
        selectedNav = NavItems[0];
        watcher = new LiveWatcher(id => _ = RefreshBrowserAsync(id));
        ActionService.Instance.CurrentScan = () => Scan;
        ActionService.Instance.RefreshBrowser = RefreshBrowserAsync;
    }

    public ActionService Actions => ActionService.Instance;
    public ChangeLogViewModel ChangeLog { get; }
    public DoctorViewModel Doctor { get; }
    public SideloadsViewModel Sideloads { get; }

    public AppSettings AppSettings { get; }
    public DashboardViewModel Dashboard { get; }
    public BrowsersViewModel Browsers { get; }
    public AllExtensionsViewModel AllExtensions { get; }
    public SettingsViewModel Settings { get; }
    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem? selectedNav;
    [ObservableProperty] private ScanResult? scan;
    [ObservableProperty] private string statusText = "Press Scan to look for browsers on this PC.";
    [ObservableProperty] private bool isNavCompact;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    private bool isScanning;

    public void Navigate(ObservableObject page) => SelectedNav = NavItems.FirstOrDefault(n => n.Page == page) ?? SelectedNav;

    [RelayCommand(CanExecute = nameof(CanScan))]
    async Task ScanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning browsers…";
        try
        {
            var result = await Scanner.ScanAsync();
            Apply(result);
            watcher.Watch(result);
            _ = FillSizesAsync(result);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = "Scan failed: " + e.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    bool CanScan() => !IsScanning;

    void Apply(ScanResult result)
    {
        PolicyCache.Clear();
        Scan = result;
        Dashboard.Load(result);
        Browsers.Load(result);
        AllExtensions.Load(result);
        UpdateStatus();
        _ = Doctor.LoadAsync(result);
        _ = Sideloads.LoadAsync(result);
    }

    void UpdateStatus()
    {
        if (Scan is not { } r) return;
        StatusText = $"Scanned {r.ScannedAt:HH:mm} · {r.Browsers.Count} browsers · {r.AllProfiles.Count()} profiles · {r.AllExtensions.Count()} extensions";
    }

    async Task FillSizesAsync(ScanResult result)
    {
        sizeCts?.Cancel();
        var cts = sizeCts = new CancellationTokenSource();
        // Snapshot on the UI thread: a live refresh may swap browsers in the scan while sizes are measured.
        var profiles = result.AllProfiles.ToList();
        try
        {
            await Task.Run(() => Scanner.FillSizes(profiles, p => Ui(() => ApplySize(p)), cts.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Shows a measured size on the profile now in the scan (a live refresh may have replaced the measured one).</summary>
    void ApplySize(ProfileInfo measured)
    {
        if (Scan is not { } current) return;
        var profile = current.AllProfiles.FirstOrDefault(p => p.Browser.Id == measured.Browser.Id && p.Path == measured.Path);
        if (profile == null) return;
        profile.SizeBytes = measured.SizeBytes;
        Browsers.OnSizeUpdated(profile);
    }

    readonly HashSet<string> refreshing = [];
    readonly HashSet<string> refreshAgain = [];

    /// <summary>Re-reads one browser, e.g. after it rewrote a profile's settings or after an action. A request that arrives
    /// while the same browser is being read runs once more afterwards instead of in parallel.</summary>
    public async Task RefreshBrowserAsync(string browserId)
    {
        if (!refreshing.Add(browserId))
        {
            refreshAgain.Add(browserId);
            return;
        }
        try
        {
            do
            {
                refreshAgain.Remove(browserId);
                await RefreshBrowserOnceAsync(browserId);
            }
            while (refreshAgain.Contains(browserId));
        }
        finally
        {
            refreshing.Remove(browserId);
        }
    }

    async Task RefreshBrowserOnceAsync(string browserId)
    {
        if (Scan is not { } current || IsScanning) return;
        var old = current.Browsers.FirstOrDefault(b => b.Id == browserId);
        if (old == null) return;

        var fresh = new BrowserInfo
        {
            Definition = old.Definition,
            ExePath = old.ExePath,
            DataDir = old.DataDir,
            Version = old.Version,
            IsRunning = old.IsRunning,
        };
        await Task.Run(() => Scanner.ReadProfiles(fresh));

        int index = current.Browsers.IndexOf(old);
        if (Scan != current || IsScanning || index < 0) return;

        // Browsers rewrite their settings often (window positions, timestamps…). When nothing the app shows has
        // changed, keep the screen as it is so lists, selection and scroll position don't jump.
        if (Scanner.Signature(fresh) == Scanner.Signature(old)) return;

        PolicyCache.Clear();
        foreach (var profile in fresh.Profiles)
            profile.SizeBytes = old.Profiles.FirstOrDefault(p => p.Key == profile.Key)?.SizeBytes;
        bool profilesChanged = !fresh.Profiles.Select(p => p.Path).SequenceEqual(old.Profiles.Select(p => p.Path));

        current.Browsers[index] = fresh;
        Dashboard.Load(current);
        Browsers.ReplaceBrowser(old, fresh);
        AllExtensions.Load(current);
        UpdateStatus();
        if (profilesChanged) watcher.Watch(current); // new profiles get watched too
        await Task.WhenAll(Doctor.LoadAsync(current), Sideloads.LoadAsync(current));
    }

    /// <summary>Refreshes "running" and "open" badges without a full rescan.</summary>
    public async Task RefreshLiveStateAsync()
    {
        if (Scan is not { } current || IsScanning) return;
        // Snapshot on the UI thread: a live refresh may replace a browser in the scan while this runs.
        var snapshot = current.Browsers.Select(b => (Browser: b, Profiles: b.Profiles.ToList())).ToList();
        var states = await Task.Run(() =>
        {
            var running = ProcessUtil.GetRunningImages(snapshot.Select(s => s.Browser.ExePath).OfType<string>().Select(Path.GetFileName).OfType<string>());
            return snapshot.Select(s =>
            {
                var b = s.Browser;
                var dir = b.ExePath != null ? Path.GetDirectoryName(b.ExePath) : null;
                bool isRunning = dir != null && running.Any(img => PathUtil.IsUnder(img, dir));
                var open = s.Profiles.Select(p => (Profile: p, IsOpen: isRunning && (b.Family == BrowserFamily.Chromium
                    ? ProfileLockProbe.IsChromiumProfileOpen(p.Path)
                    : ProfileLockProbe.IsFirefoxProfileOpen(p.Path)))).ToList();
                return (Browser: b, IsRunning: isRunning, Open: open);
            }).ToList();
        });
        if (Scan != current) return;

        bool changed = false;
        foreach (var (browser, isRunning, open) in states)
        {
            changed |= browser.IsRunning != isRunning;
            browser.IsRunning = isRunning;
            foreach (var (profile, isOpen) in open)
            {
                changed |= profile.IsOpen != isOpen;
                profile.IsOpen = isOpen;
            }
        }
        if (!changed) return; // this runs every few seconds: leave the screen alone when nothing changed
        Browsers.UpdateLiveState();
        Dashboard.Load(current);
    }

    static void Ui(Action action) => Application.Current?.Dispatcher.BeginInvoke(action);
}
