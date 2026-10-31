using System.Text.RegularExpressions;
using BPM.Core.Browsers;
using BPM.Core.Models;

namespace BPM.Core.Firefox;

/// <summary>Lists a Firefox-family browser's profiles from profiles.ini and the profile folders.
/// Only prefs.js is read for account info; sign-in, cookie and history stores are never opened.</summary>
public static partial class FirefoxProfileReader
{
    public static List<ProfileInfo> Read(BrowserInfo browser)
    {
        var dataDir = browser.DataDir ?? throw new InvalidOperationException($"{browser.Name} has no data folder.");
        var sections = ParseIni(Path.Combine(dataDir, "profiles.ini"));

        var installDefaults = sections
            .Where(s => s.Name.StartsWith("Install", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Get("Default"))
            .OfType<string>()
            .Select(NormalizeKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var profiles = new List<ProfileInfo>();
        foreach (var section in sections.Where(s => s.Name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)))
        {
            var key = section.Get("Path");
            if (string.IsNullOrWhiteSpace(key)) continue;

            bool relative = section.Get("IsRelative") != "0";
            string path;
            try
            {
                path = Path.GetFullPath(relative ? Path.Combine(dataDir, key.Replace('/', '\\')) : key.Replace('/', '\\'));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue; // a malformed profiles.ini entry: skip it, keep the other profiles
            }
            if (!Directory.Exists(path)) continue;

            bool isDefault = section.Get("Default") == "1" || installDefaults.Contains(NormalizeKey(key));
            profiles.Add(ReadProfile(browser, key, path, section.Get("Name") ?? key, isDefault));
        }
        return profiles;
    }

    static ProfileInfo ReadProfile(BrowserInfo browser, string key, string path, string name, bool isDefault)
    {
        var prefs = Path.Combine(path, "prefs.js");
        var profile = new ProfileInfo
        {
            Browser = browser,
            Key = key,
            Path = path,
            Name = name,
            Email = ReadSyncUser(prefs),
            IsDefault = isDefault,
            IsLastUsed = isDefault,
            LastActive = File.Exists(prefs) ? File.GetLastWriteTime(prefs) : null,
        };
        profile.IsOpen = browser.IsRunning && ProfileLockProbe.IsFirefoxProfileOpen(path);
        try
        {
            profile.Extensions.AddRange(FirefoxExtensionReader.Read(profile));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // An odd add-on entry marks this profile; the rest of the scan goes on.
            profile.ScanError = e.Message;
        }
        return profile;
    }

    static string NormalizeKey(string path) => path.Replace('\\', '/').TrimEnd('/');

    static string? ReadSyncUser(string prefsPath)
    {
        if (!File.Exists(prefsPath)) return null;
        try
        {
            using var fs = new FileStream(prefsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            while (reader.ReadLine() is { } line)
            {
                if (!line.Contains("services.sync.username", StringComparison.Ordinal)) continue;
                var match = SyncUserPattern().Match(line);
                if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) return match.Groups[1].Value;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // prefs.js unreadable: treat as signed out.
        }
        return null;
    }

    [GeneratedRegex(@"user_pref\(\s*""services\.sync\.username""\s*,\s*""((?:[^""\\]|\\.)*)""\s*\)")]
    private static partial Regex SyncUserPattern();

    sealed class IniSection(string name)
    {
        public string Name { get; } = name;
        readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string key, string value) => values[key] = value;
        public string? Get(string key) => values.GetValueOrDefault(key);
    }

    static List<IniSection> ParseIni(string file)
    {
        var sections = new List<IniSection>();
        if (!File.Exists(file)) return sections;
        try
        {
            IniSection? current = null;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is ';' or '#') continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    sections.Add(current = new IniSection(line[1..^1]));
                    continue;
                }
                int eq = line.IndexOf('=');
                if (current == null || eq <= 0) continue;
                current.Set(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // profiles.ini unreadable: report what was parsed so far.
        }
        return sections;
    }
}
