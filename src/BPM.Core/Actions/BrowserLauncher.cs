using System.Diagnostics;
using System.Runtime.InteropServices;
using BPM.Core.Models;

namespace BPM.Core.Actions;

/// <summary>Opens browsers in a specific profile and creates per-profile desktop shortcuts.</summary>
public static class BrowserLauncher
{
    /// <summary>Starts (or reuses) the browser in <paramref name="profile"/>. Only web addresses can be passed:
    /// browsers refuse internal pages like chrome://extensions from the command line (see BrowserAutomation).</summary>
    public static void OpenProfile(ProfileInfo profile, string? webUrl = null, bool newWindow = false)
    {
        var browser = profile.Browser;
        var exe = browser.ExePath ?? throw new InvalidOperationException($"{browser.Name}'s program wasn't found on this PC.");
        if (webUrl != null && !webUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !webUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only web addresses can be opened this way.", nameof(webUrl));

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in ProfileArguments(profile, newWindow)) start.ArgumentList.Add(arg);
        if (webUrl != null) start.ArgumentList.Add(webUrl);
        Process.Start(start)?.Dispose();
    }

    public static IEnumerable<string> ProfileArguments(ProfileInfo profile, bool newWindow = false)
    {
        if (profile.Browser.Family == BrowserFamily.Chromium)
        {
            if (profile.Key.Length > 0) yield return $"--profile-directory={profile.Key}";
            if (newWindow) yield return "--new-window";
        }
        else
        {
            yield return "-P";
            yield return profile.Name;
            if (!profile.IsOpen && profile.Browser.IsRunning) yield return "--new-instance";
        }
    }

    /// <summary>Creates "&lt;profile&gt; - &lt;browser&gt;.lnk" on the desktop that opens straight into the profile. Returns its path.</summary>
    public static string CreateDesktopShortcut(ProfileInfo profile)
    {
        var browser = profile.Browser;
        var exe = browser.ExePath ?? throw new InvalidOperationException($"{browser.Name}'s program wasn't found on this PC.");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var fileName = string.Concat($"{profile.Name} - {browser.Name}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(desktop, fileName + ".lnk");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(desktop, $"{fileName} ({n}).lnk"); // never overwrite a file

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host isn't available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = exe;
            link.Arguments = string.Join(" ", ProfileArguments(profile).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            var profileIcon = Path.Combine(profile.Path, "Google Profile.ico");
            link.IconLocation = File.Exists(profileIcon) ? profileIcon : exe + ",0";
            link.Description = $"Open {browser.Name} as {profile.Name}";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
        return path;
    }
}
