using BPM.Core.Chromium;
using BPM.Core.Models;

namespace BPM.Core.Analysis;

public enum ProblemSeverity { Info, Warning, Critical }

/// <summary>Something worth the user's attention, e.g. "Claude is on in 3 Chrome profiles".</summary>
public sealed record Problem(ProblemSeverity Severity, string Title, string Detail, string? ExtensionId = null);

/// <summary>Finds the issues the dashboard lists under "Problems found".</summary>
public static class ProblemFinder
{
    /// <summary>Extension IDs of Claude in Chrome (store build plus the IDs its native bridge also accepts).</summary>
    public static readonly IReadOnlySet<string> ClaudeIds = new HashSet<string>
    {
        "fcoeoabgfenejglbffodgkkbkcdhcgfn", "dihbgbndebgnbjfmelmegjepbnkhlgni", "dngcpimnedloihjnnfngkgjoidhnaolf",
    };

    public static List<Problem> Find(ScanResult scan)
    {
        var problems = new List<Problem>();
        var all = scan.AllExtensions.ToList();

        foreach (var g in all.Where(e => (e.DisableReasonBits & DisableReasonCodes.Greylist) != 0).GroupBy(e => e.Id))
            problems.Add(new(ProblemSeverity.Critical, $"{g.First().Name} was flagged by the store",
                $"The store marked it harmful or unwanted. Found in {Where(g)}. Remove it.", g.Key));

        foreach (var g in all.Where(e => (e.DisableReasonBits & DisableReasonCodes.Corrupted) != 0 || !e.FilesPresent).GroupBy(e => e.Id))
            problems.Add(new(ProblemSeverity.Warning, $"{g.First().Name} is damaged",
                $"Its files are missing or damaged in {Where(g)}. Repair or reinstall it from the browser's extensions page.", g.Key));

        foreach (var g in all.Where(e => e.Enabled && e.Risk.Level == RiskLevel.High && e.Source is ExtensionSource.Developer or ExtensionSource.Unknown).GroupBy(e => e.Id))
            problems.Add(new(ProblemSeverity.Warning, $"{g.First().Name} is powerful and not from a store",
                $"It has wide access ({g.First().Risk.Reasons.FirstOrDefault() ?? "high risk"}) and no store reviewed it. Found in {Where(g)}.", g.Key));

        foreach (var g in all.Where(e => e.NeedsApproval).GroupBy(e => e.Id).OrderByDescending(g => g.Count()))
            problems.Add(new(ProblemSeverity.Info, $"{g.First().Name} is waiting for approval in {Count(g)}",
                "Another program added it. Approve it in the browser, or remove its sideload entry so new profiles stop asking.", g.Key));

        foreach (var g in all.GroupBy(e => e.Id).Where(g => g.Select(e => e.Version).Distinct().Count() > 1))
        {
            var versions = g.Select(e => e.Version).Distinct().OrderBy(v => Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0)).ToList();
            problems.Add(new(ProblemSeverity.Info, $"{g.First().Name} has {versions.Count} different versions",
                $"Versions {string.Join(", ", versions)} across {Count(g)}. Profiles you rarely open keep old copies until the browser updates them.", g.Key));
        }

        foreach (var g in all.Where(e => e.BlockedByPolicy).GroupBy(e => e.Id))
            problems.Add(new(ProblemSeverity.Info, $"{g.First().Name} is blocked by policy", $"Blocked in {Where(g)}.", g.Key));

        foreach (var browser in scan.Browsers.Where(b => b.ScanError != null))
            problems.Add(new(ProblemSeverity.Warning, $"Couldn't read {browser.Name}", browser.ScanError!));
        foreach (var profile in scan.AllProfiles.Where(p => p.ScanError != null))
            problems.Add(new(ProblemSeverity.Warning, $"Couldn't read {profile.Browser.Name} profile {profile.Name}", profile.ScanError!));

        return problems.OrderByDescending(p => p.Severity).ToList();
    }

    static string Count(IEnumerable<ExtensionInfo> group)
    {
        int n = group.Select(e => e.Profile).Distinct().Count();
        return n == 1 ? "1 profile" : $"{n} profiles";
    }

    static string Where(IEnumerable<ExtensionInfo> group)
    {
        var names = group.Select(e => $"{e.Profile.Name} ({e.Profile.Browser.Name})").Distinct().ToList();
        return names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more";
    }
}
