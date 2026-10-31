using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Actions;

/// <summary>A profile the app just created: its folder name ("Profile 40") and full path.</summary>
public sealed record CreatedProfile(string Key, string Path, string Name);

/// <summary>Creates a new, empty profile in a Chromium browser. The browser makes a new profile when it's started with
/// --profile-directory pointing at a folder that doesn't exist yet; the app only seeds that brand-new folder with a
/// starter Preferences file holding the chosen name, so the profile isn't called "Person 38". Nothing that already
/// exists is changed, and the browser's tamper-protected files are written by the browser itself.</summary>
public static partial class ProfileCreator
{
    public const int MaxNameLength = 50;

    /// <summary>Why a profile with this name can't be created, or null when it can.</summary>
    public static string? CheckCanCreate(BrowserInfo browser, string? name)
    {
        if (browser.Family != BrowserFamily.Chromium) return $"Creating profiles isn't available for {browser.Name} yet.";
        if (browser.ExePath == null) return $"{browser.Name}'s program wasn't found on this PC.";
        if (browser.DataDir == null || !Directory.Exists(browser.DataDir)) return $"{browser.Name}'s data folder wasn't found.";
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0) return "Type a name for the new profile.";
        if (trimmed.Length > MaxNameLength) return $"Keep the name under {MaxNameLength} characters.";
        if (trimmed.Any(char.IsControl)) return "The name can't contain special control characters.";
        return null;
    }

    /// <summary>True when another profile of the browser already shows this name (allowed, but worth a warning).</summary>
    public static bool NameInUse(BrowserInfo browser, string name) =>
        browser.Profiles.Any(p => p.Name.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>The next unused "Profile N" folder. It stays ahead of the folders on disk, the browser's own counter
    /// (Local State "profiles_created" is the number the browser gives its next profile: Chrome made Profile 41 and then
    /// read 42), and any folder this app made before (<paramref name="usedBefore"/>). A profile deleted in the browser
    /// leaves no folder, but the browser refuses to open that folder name again until it restarts (seen Nov 2026).</summary>
    public static string NextFolderName(string userDataDir, IEnumerable<string>? usedBefore = null)
    {
        int highest = 0;
        foreach (var dir in Directory.EnumerateDirectories(userDataDir))
            if (ProfileNumber(Path.GetFileName(dir)) is { } n) highest = Math.Max(highest, n);

        var state = JsonUtil.Load(Path.Combine(userDataDir, "Local State"));
        if (state.At("profile", "info_cache") is JsonObject cache)
            foreach (var (key, _) in cache)
                if (ProfileNumber(key) is { } n) highest = Math.Max(highest, n);
        int created = (int)(state.At("profile", "profiles_created").AsLong() ?? 0);
        foreach (var folder in usedBefore ?? [])
            if (ProfileNumber(folder) is { } n) highest = Math.Max(highest, n);

        int next = Math.Max(highest + 1, created + 1);
        while (Directory.Exists(Path.Combine(userDataDir, $"Profile {next}"))) next++;
        return $"Profile {next}";
    }

    static int? ProfileNumber(string? folder)
    {
        if (folder == null) return null;
        var match = ProfileFolder().Match(folder);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : null;
    }

    [GeneratedRegex(@"^Profile (\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileFolder();

    /// <summary>Makes the new profile's folder with a starter Preferences file that names it. Doesn't start the browser.</summary>
    public static CreatedProfile Prepare(BrowserInfo browser, string name, IEnumerable<string>? usedBefore = null)
    {
        if (CheckCanCreate(browser, name) is { } problem) throw new InvalidOperationException(problem);
        name = name.Trim();
        var key = NextFolderName(browser.DataDir!, usedBefore);
        var path = Path.Combine(browser.DataDir!, key);
        if (Directory.Exists(path)) throw new InvalidOperationException($"The folder {key} already exists, so nothing was created.");

        var prefs = new JsonObject
        {
            ["profile"] = new JsonObject
            {
                ["name"] = name,
                // Keep the chosen name: with the default flag set, the browser would swap in the account's name on sign-in.
                ["using_default_name"] = false,
            },
        };
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Preferences"), prefs.ToJsonString());
        return new CreatedProfile(key, path, name);
    }

    /// <summary>Creates the profile and opens it: the browser finishes setting it up the moment it opens the folder.</summary>
    public static CreatedProfile Create(BrowserInfo browser, string name, IEnumerable<string>? usedBefore = null)
    {
        var created = Prepare(browser, name, usedBefore);
        var start = new ProcessStartInfo(browser.ExePath!) { UseShellExecute = false };
        start.ArgumentList.Add($"--profile-directory={created.Key}");
        Process.Start(start)?.Dispose();
        return created;
    }
}
