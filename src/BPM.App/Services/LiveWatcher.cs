using System.Windows;
using System.Windows.Threading;
using BPM.Core.Models;

namespace BPM.App.Services;

/// <summary>Watches each profile's settings files and reports (debounced, on the UI thread) which browser changed.</summary>
public sealed class LiveWatcher(Action<string> onBrowserChanged)
{
    static readonly HashSet<string> WatchedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Preferences", "Secure Preferences", "Local State", "extensions.json",
    };

    readonly List<FileSystemWatcher> watchers = [];
    readonly Dictionary<string, DispatcherTimer> pending = [];

    public void Watch(ScanResult scan)
    {
        Stop();
        foreach (var browser in scan.Browsers)
        {
            var folders = browser.Profiles.Select(p => p.Path).Append(browser.DataDir).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders.Where(Directory.Exists))
            {
                try
                {
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    };
                    var id = browser.Id;
                    FileSystemEventHandler handler = (_, e) => { if (e.Name != null && WatchedFiles.Contains(e.Name)) Schedule(id); };
                    w.Changed += handler;
                    w.Created += handler;
                    w.Renamed += (_, e) => { if (WatchedFiles.Contains(e.Name ?? "")) Schedule(id); };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    // A folder we can't watch just won't refresh live.
                }
            }
        }
    }

    public void Stop()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
        // Refreshes already waiting still run: a late refresh is harmless, a lost one leaves the screen stale.
    }

    void Schedule(string browserId) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (!pending.TryGetValue(browserId, out var timer))
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                onBrowserChanged(browserId);
            };
            pending[browserId] = timer;
        }
        timer.Stop();
        timer.Start(); // restart: wait until the browser has finished writing
    });
}
