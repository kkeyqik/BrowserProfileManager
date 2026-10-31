using BPM.Core.Models;

namespace BPM.Core.Actions;

/// <summary>Which copies of an extension "Remove from every profile" will uninstall, and which it has to skip.</summary>
public sealed record RemovalPlan(IReadOnlyList<ExtensionInfo> Targets, IReadOnlyList<SkippedRemoval> Skipped);

public sealed record SkippedRemoval(ExtensionInfo Extension, string Reason);

public static class RemovalPlanner
{
    /// <summary>Every installed copy of <paramref name="extensionId"/> on this PC, split into ones the app can uninstall
    /// (through each browser's own page) and ones it can't, with the reason.</summary>
    public static RemovalPlan Plan(ScanResult? scan, string extensionId)
    {
        var targets = new List<ExtensionInfo>();
        var skipped = new List<SkippedRemoval>();
        var copies = scan?.AllExtensions.Where(e => e.Id == extensionId) ?? [];
        foreach (var copy in copies.OrderBy(e => e.Profile.Browser.Name).ThenBy(e => e.Profile.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var browser = copy.Profile.Browser;
            if (browser.Family != BrowserFamily.Chromium)
                skipped.Add(new(copy, $"{browser.Name}: remove it on its add-ons page (about:addons)"));
            else if (copy.Source == ExtensionSource.Policy)
                skipped.Add(new(copy, "installed by an administrator policy, so it can't be removed"));
            else if (browser.ExePath == null)
                skipped.Add(new(copy, $"{browser.Name}'s program wasn't found"));
            else
                targets.Add(copy);
        }
        return new RemovalPlan(targets, skipped);
    }
}
