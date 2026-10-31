using System.Diagnostics;
using BPM.Core.Models;
using BPM.Core.Util;
using Microsoft.Win32;

namespace BPM.Core.Browsers;

/// <summary>Finds installed browsers: Windows' browser registrations (StartMenuInternet), known install paths and data folders.</summary>
public static class BrowserDetector
{
    /// <summary>A browser registered with Windows under Clients\StartMenuInternet.</summary>
    public sealed record Registration(string Name, string ExePath);

    static readonly string[] IgnoredExes = ["iexplore.exe"];

    public static (List<BrowserInfo> Browsers, List<OtherBrowser> Others) Detect()
    {
        var registered = ReadRegistrations();
        var catalogExeNames = BrowserCatalog.All
            .SelectMany(d => d.ExePaths)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Concat(registered.Select(r => Path.GetFileName(r.ExePath)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var running = ProcessUtil.GetRunningImages(catalogExeNames);

        var browsers = new List<BrowserInfo>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in BrowserCatalog.All)
        {
            var dataDir = def.DataDirs.SelectMany(PathUtil.Expand).FirstOrDefault(d => HasData(def, d));
            var candidates = def.ExePaths.SelectMany(PathUtil.Expand).ToList();
            var exe = candidates.FirstOrDefault(File.Exists)
                ?? registered.Select(r => r.ExePath).FirstOrDefault(r => candidates.Any(c => PathUtil.SamePath(c, r)));
            if (exe == null && dataDir == null) continue;

            if (exe != null) matched.Add(PathUtil.Normalize(exe));
            var exeDir = exe != null ? Path.GetDirectoryName(exe) : null;
            browsers.Add(new BrowserInfo
            {
                Definition = def,
                ExePath = exe,
                DataDir = dataDir,
                Version = exe != null ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null,
                IsRunning = exeDir != null && running.Any(img => PathUtil.IsUnder(img, exeDir)),
            });
        }

        var others = registered
            .Where(r => !matched.Contains(PathUtil.Normalize(r.ExePath)))
            .Where(r => !IgnoredExes.Contains(Path.GetFileName(r.ExePath), StringComparer.OrdinalIgnoreCase))
            .Where(r => !browsers.Any(b => b.ExePath != null && PathUtil.SamePath(b.ExePath, r.ExePath)))
            .GroupBy(r => PathUtil.Normalize(r.ExePath), StringComparer.OrdinalIgnoreCase)
            .Select(g => new OtherBrowser(g.First().Name, g.First().ExePath))
            .ToList();
        return (browsers, others);
    }

    static bool HasData(BrowserDefinition def, string dir) => def.Family == BrowserFamily.Chromium
        ? File.Exists(Path.Combine(dir, "Local State"))
        : File.Exists(Path.Combine(dir, "profiles.ini"));

    /// <summary>Browsers registered with Windows (machine 64/32-bit views and current user).</summary>
    public static List<Registration> ReadRegistrations()
    {
        var list = new List<Registration>();
        var roots = new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        };
        foreach (var (hive, view) in roots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var clients = baseKey.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                if (clients == null) continue;
                foreach (var name in clients.GetSubKeyNames())
                {
                    using var entry = clients.OpenSubKey(name);
                    using var command = clients.OpenSubKey($@"{name}\shell\open\command");
                    var exe = ExeFromCommand(command?.GetValue("") as string);
                    if (exe == null || !File.Exists(exe)) continue;
                    var display = entry?.GetValue("") as string;
                    list.Add(new Registration(string.IsNullOrWhiteSpace(display) ? name : display, exe));
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable registry view: skip it.
            }
        }
        return list;
    }

    /// <summary>"C:\path\app.exe" --flag → C:\path\app.exe</summary>
    internal static string? ExeFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return command[..(exe + 4)];
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command; // no ".exe": the first word, without its arguments
    }
}
