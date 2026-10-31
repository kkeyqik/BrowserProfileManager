using BPM.Core.Analysis;
using BPM.Core.Browsers;
using BPM.Core.Chromium;
using BPM.Core.Firefox;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Scanning;

/// <summary>Finds every browser, profile and extension on this PC. Read-only.</summary>
public static class Scanner
{
    public static Task<ScanResult> ScanAsync(CancellationToken ct = default) => Task.Run(() => Scan(ct), ct);

    public static ScanResult Scan(CancellationToken ct = default)
    {
        var result = new ScanResult();
        var (browsers, others) = BrowserDetector.Detect();
        result.OtherBrowsers.AddRange(others);
        foreach (var browser in browsers)
        {
            ct.ThrowIfCancellationRequested();
            ReadProfiles(browser); // never throws: problems end up in ScanError
            result.Browsers.Add(browser);
        }
        return result;
    }

    /// <summary>(Re)reads one browser's profiles and extensions, e.g. after its files changed.</summary>
    public static void ReadProfiles(BrowserInfo browser)
    {
        browser.Profiles.Clear();
        browser.ScanError = null;
        if (browser.DataDir == null) return;
        try
        {
            var profiles = browser.Family == BrowserFamily.Chromium
                ? ChromiumProfileReader.Read(browser)
                : FirefoxProfileReader.Read(browser);
            foreach (var profile in profiles)
            {
                profile.Browser = browser;
                foreach (var extension in profile.Extensions)
                {
                    extension.Profile = profile;
                    extension.Risk = RiskAnalyzer.Assess(extension);
                }
                browser.Profiles.Add(profile);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // One unreadable or odd browser folder must never stop the whole scan.
            browser.ScanError = e.Message;
        }
    }

    /// <summary>Fills in profile folder sizes (slow for big profiles, so callers run it in the background).</summary>
    public static void FillSizes(ScanResult result, Action<ProfileInfo>? onProfileDone = null, CancellationToken ct = default) =>
        FillSizes(result.AllProfiles.ToList(), onProfileDone, ct);

    /// <summary>Same, for a fixed list of profiles. Pass a snapshot taken on the thread that owns the scan, so a live
    /// refresh replacing a browser can't change the list while sizes are measured.</summary>
    public static void FillSizes(IReadOnlyList<ProfileInfo> profiles, Action<ProfileInfo>? onProfileDone = null, CancellationToken ct = default)
    {
        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            profile.SizeBytes = PathUtil.DirectorySize(profile.Path, ct);
            onProfileDone?.Invoke(profile);
        }
    }

    /// <summary>Everything about a browser's profiles and extensions that the app shows, as one string. Two reads with the
    /// same signature look the same, so a live refresh can skip rebuilding the screen (browsers rewrite their settings
    /// files often for reasons the app doesn't show). Live state (open, running, size) is left out.</summary>
    public static string Signature(BrowserInfo browser)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(browser.ScanError).Append('\n');
        foreach (var p in browser.Profiles)
        {
            sb.Append("P|").Append(p.Key).Append('|').Append(p.Name).Append('|').Append(p.Email).Append('|').Append(p.FullName)
              .Append('|').Append(p.AvatarPath).Append('|').Append(p.ColorArgb).Append('|').Append(p.IsLastUsed)
              .Append('|').Append(p.LastActive?.Date.Ticks).Append('|').Append(p.ScanError).Append('\n');
            foreach (var e in p.Extensions)
            {
                sb.Append("E|").Append(e.Id).Append('|').Append(e.Name).Append('|').Append(e.Version).Append('|').Append(e.Enabled)
                  .Append('|').Append(e.DisableReasonBits).Append('|').Append(e.Source).Append('|').Append(e.IconPath)
                  .Append('|').Append(e.FilesPresent).Append('|').Append(e.UpdatedAt?.Ticks)
                  .Append('|').Append(string.Join(",", e.ApiPermissions)).Append('|').Append(string.Join(",", e.HostPermissions))
                  .Append('\n');
            }
        }
        return sb.ToString();
    }
}
