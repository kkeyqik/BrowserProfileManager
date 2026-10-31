using BPM.Core.Analysis;
using BPM.Core.Models;

namespace BPM.Core.Diagnostics;

/// <summary>The Health page: accounts that need attention, profiles nobody uses, broken browser links and browser
/// policies, plus the Claude in Chrome checks when Claude is installed. Read-only: fixes are separate actions.</summary>
public static class HealthChecker
{
    public const string Accounts = "Accounts", Profiles = "Profiles", Links = "Browser links", Policies = "Policies", Claude = "Claude in Chrome";

    /// <summary>Groups in the order the page lists them.</summary>
    public static IReadOnlyList<string> GroupOrder { get; } = [Accounts, Profiles, Links, Policies, Claude];

    /// <summary>A profile counts as unused when it wasn't active for this long.</summary>
    public static readonly TimeSpan UnusedAfter = TimeSpan.FromDays(90);

    /// <summary>What a Chromium profile button says instead of the name when the account needs attention (seen on Chrome
    /// 156, English). Matching known phrases, rather than "anything that isn't the name", avoids false alarms from
    /// browsers that label the button differently; other languages simply aren't detected.</summary>
    static readonly string[] AccountProblemPhrases = ["Paused", "Verify that it's you", "Verify it's you", "Sign in again", "Sync is paused", "Sync paused", "Error"];

    /// <param name="buttonLabels">Profile button text of open profiles, keyed by profile folder (see BrowserAutomation.ReadProfileButtonLabels).</param>
    public static List<DoctorCheck> Run(ScanResult scan, IReadOnlyList<NativeHost> hosts, IReadOnlyList<PolicyValue> policies,
        IReadOnlyDictionary<string, string> buttonLabels, DateTime now)
    {
        var checks = new List<DoctorCheck>();
        bool claudeInstalled = scan.AllExtensions.Any(e => ProblemFinder.ClaudeIds.Contains(e.Id));
        CheckAccounts(scan, buttonLabels, checks);
        CheckUnused(scan, now, checks);
        CheckLinks(hosts, claudeInstalled, checks);
        CheckPolicies(policies, checks);
        if (claudeInstalled)
            checks.AddRange(ConnectionDoctor.ClaudeChecks(scan, hosts).Select(c => c with { Group = Claude }));
        return checks
            .OrderBy(c => GroupOrder.ToList().IndexOf(c.Group))
            .ThenByDescending(c => c.Status)
            .ToList();
    }

    static void CheckAccounts(ScanResult scan, IReadOnlyDictionary<string, string> buttonLabels, List<DoctorCheck> checks)
    {
        var chromium = scan.AllProfiles.Where(p => p.Browser.Family == BrowserFamily.Chromium).ToList();
        int problems = 0;
        foreach (var profile in chromium.Where(p => p.Email != null))
        {
            if (!buttonLabels.TryGetValue(profile.Path, out var label)) continue;
            var phrase = AccountProblemPhrases.FirstOrDefault(p => label.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (phrase == null) continue;
            problems++;
            checks.Add(new(CheckStatus.Warning, $"{profile.Name} ({profile.Browser.Name}): “{label}”",
                $"The account in this profile ({profile.Email}) needs you to sign in again, so syncing and Google services may have stopped. Open the profile and follow the browser's prompt.",
                FixKind.OpenProfile, Key(profile), Accounts));
        }
        if (problems == 0 && buttonLabels.Count > 0)
            checks.Add(new(CheckStatus.Ok, "No sign-in problems in open profiles",
                "Closed profiles are checked the next time they're open.", Group: Accounts));

        var signedOut = chromium.Where(p => p.Email == null).ToList();
        if (signedOut.Count > 0)
            checks.Add(new(CheckStatus.Info, Count(signedOut.Count, "profile isn't", "profiles aren't") + " signed in to an account",
                $"{Names(signedOut)}. That's fine if you meant it; signing in lets the browser sync bookmarks, passwords and extensions.",
                FixKind.ShowProfile, Key(signedOut[0]), Accounts));
    }

    static void CheckUnused(ScanResult scan, DateTime now, List<DoctorCheck> checks)
    {
        var unused = scan.AllProfiles
            .Where(p => !p.IsOpen && p.LastActive is { } last && now - last > UnusedAfter)
            .OrderBy(p => p.LastActive)
            .ToList();
        if (unused.Count == 0)
        {
            checks.Add(new(CheckStatus.Ok, "Every profile was used in the last 3 months", "", Group: Profiles));
            return;
        }
        long size = unused.Sum(p => p.SizeBytes ?? 0);
        var sizeText = size > 0 ? $" They take {size / (1024.0 * 1024 * 1024):0.0} GB." : "";
        checks.Add(new(CheckStatus.Info, Count(unused.Count, "profile wasn't", "profiles weren't") + " used in the last 3 months",
            $"{Names(unused, withLastUsed: true)}.{sizeText} To delete one you don't need, open it and use the browser's own profile menu.",
            FixKind.ShowProfile, Key(unused[0]), Profiles));
    }

    static void CheckLinks(IReadOnlyList<NativeHost> hosts, bool claudeShownSeparately, List<DoctorCheck> checks)
    {
        var broken = hosts.Where(h => h.IsBroken && !(claudeShownSeparately && ConnectionDoctor.IsClaude(h))).ToList();
        foreach (var host in broken)
            checks.Add(new(CheckStatus.Warning, $"Broken browser link: {host.Name}",
                $"{host.StatusText}. Used by {string.Join(", ", host.Browsers)} ({host.Scope.Label.ToLowerInvariant()}). The program it belonged to was probably uninstalled; removing the leftover entry is safe.",
                FixKind.RemoveNativeHost, host.DisplayKey, Links));
        if (broken.Count == 0)
            checks.Add(new(CheckStatus.Ok, "Browser links are healthy",
                $"{Count(hosts.Count, "link", "links")} that let extensions talk to programs on this PC; none point to missing programs.", Group: Links));
    }

    static void CheckPolicies(IReadOnlyList<PolicyValue> policies, List<DoctorCheck> checks)
    {
        if (policies.Count == 0)
        {
            checks.Add(new(CheckStatus.Ok, "No browser policies are set", "No browser says “Managed by your organization”.", Group: Policies));
            return;
        }
        foreach (var group in policies.GroupBy(p => p.Browsers))
        {
            var names = group.Select(p => p.Name).Distinct().ToList();
            checks.Add(new(CheckStatus.Info, $"{group.Key}: {Count(names.Count, "policy", "policies")} set",
                $"Because of these, the browser says “Managed by your organization”: {string.Join(", ", names.Take(5))}{(names.Count > 5 ? $" and {names.Count - 5} more" : "")}.",
                FixKind.ShowPolicies, group.Key, Policies));
        }
    }

    /// <summary>"browserId\nprofileKey": what OpenProfile / ShowProfile fixes act on.</summary>
    public static string Key(ProfileInfo profile) => $"{profile.Browser.Id}\n{profile.Key}";

    static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    static string Names(IReadOnlyList<ProfileInfo> profiles, bool withLastUsed = false)
    {
        var names = profiles.Select(p => withLastUsed && p.LastActive is { } t ? $"{p.Name} (last used {t:d MMM yyyy})" : p.Name).ToList();
        return names.Count <= 5 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(5))} and {names.Count - 5} more";
    }
}
