using System.Diagnostics;
using BPM.Core.Actions;
using BPM.Core.Analysis;
using BPM.Core.Models;

namespace BPM.Core.Diagnostics;

public enum CheckStatus { Ok, Info, Warning, Error }

public enum FixKind { None, KeepOneClaudeProfile, RemoveNativeHost, ShowSideloads, OpenExtension, OpenProfile, ShowProfile, ShowPolicies }

/// <summary>One health finding. <see cref="FixData"/> identifies what the fix acts on (a browser id, a registry key, …);
/// <see cref="Group"/> is the heading it's listed under ("Accounts", "Browser links", …).</summary>
public sealed record DoctorCheck(CheckStatus Status, string Title, string Detail, FixKind Fix = FixKind.None, string? FixData = null, string Group = "");

/// <summary>Why the Claude in Chrome extension can't reach the Claude app. Shown as one optional group of the Health page
/// (see <see cref="HealthChecker"/>) when Claude is installed. Grew out of ChromeClaudeDoctor.exe.</summary>
public static class ConnectionDoctor
{
    /// <summary>The Claude checks plus every other browser link (what <c>bpm doctor</c> prints).</summary>
    public static List<DoctorCheck> Run(ScanResult scan, IReadOnlyList<NativeHost> hosts)
    {
        var checks = ClaudeChecks(scan, hosts);
        foreach (var host in hosts.Where(h => h.IsBroken && !IsClaude(h)))
            checks.Add(new(CheckStatus.Warning, $"Broken browser link: {host.Name}",
                $"{host.StatusText}. Used by {string.Join(", ", host.Browsers)} ({host.Scope.Label.ToLowerInvariant()}). The program it belonged to was probably uninstalled; removing the leftover entry is safe.",
                FixKind.RemoveNativeHost, host.DisplayKey));
        if (!hosts.Any(h => h.IsBroken && !IsClaude(h)))
            checks.Add(new(CheckStatus.Ok, "Other browser links are healthy", $"{hosts.Count} native-app link(s) registered; none point to missing programs."));
        return checks;
    }

    /// <summary>Only the Claude in Chrome checks: the extension in your profiles, the Claude app and its browser link,
    /// and policies that block either.</summary>
    public static List<DoctorCheck> ClaudeChecks(ScanResult scan, IReadOnlyList<NativeHost> hosts)
    {
        var checks = new List<DoctorCheck>();
        var claudeIds = ClaudeIdsFor(hosts);
        CheckClaudeExtension(scan, claudeIds, checks);
        CheckClaudeApp(hosts, scan, claudeIds, checks);
        CheckPolicies(scan, claudeIds, checks);
        return checks;
    }

    internal static bool IsClaude(NativeHost host) => host.Name.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase);

    /// <summary>The known Claude extension IDs plus every extension the Claude app's own browser link accepts, so a new
    /// Claude build is recognised without an app update (as ChromeClaudeDoctor did).</summary>
    internal static HashSet<string> ClaudeIdsFor(IEnumerable<NativeHost> hosts)
    {
        var ids = new HashSet<string>(ProblemFinder.ClaudeIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in hosts.Where(h => IsClaude(h) && !h.IsBroken).SelectMany(h => h.AllowedExtensions))
            if (id.Length == 32 && id.All(c => c is >= 'a' and <= 'p')) ids.Add(id);
        return ids;
    }

    static void CheckClaudeExtension(ScanResult scan, IReadOnlySet<string> claudeIds, List<DoctorCheck> checks)
    {
        var claude = scan.AllExtensions.Where(e => claudeIds.Contains(e.Id)).ToList();
        if (claude.Count == 0)
        {
            checks.Add(new(CheckStatus.Info, "Claude in Chrome isn't installed", "No browser profile on this PC has the Claude extension."));
            return;
        }

        var on = claude.Where(e => e.Enabled).ToList();
        if (on.Count == 0)
            checks.Add(new(CheckStatus.Warning, "Claude is installed but turned off everywhere",
                $"It's in {claude.Select(e => e.Profile).Distinct().Count()} profile(s) but on in none. Turn it on in the one profile you use with Claude.",
                FixKind.OpenExtension, claude[0].Id));

        foreach (var group in on.GroupBy(e => e.Profile.Browser.Id))
        {
            var browser = group.First().Profile.Browser;
            var profiles = string.Join(", ", group.Select(e => $"“{e.Profile.Name}”"));
            // The Claude app reaches the extension through Claude's cloud bridge: every open, signed-in copy can connect,
            // and Claude asks which browser to use. A copy only connects while its profile is open.
            checks.Add(group.Count() > 1
                ? new(CheckStatus.Info, $"Claude is on in {group.Count()} {browser.Name} profiles",
                    $"On in {profiles}. Claude can connect to each of them while that profile is open and signed in to Claude, and asks which browser to use. Turn it off in profiles you don't use with Claude if you like.",
                    FixKind.KeepOneClaudeProfile, browser.Id)
                : new(CheckStatus.Ok, $"Claude is on in one {browser.Name} profile", $"{profiles}. Make sure you're signed in to Claude in that profile's side panel."));

            if (group.All(e => !e.Profile.IsOpen))
                checks.Add(new(CheckStatus.Warning, $"No {browser.Name} profile with Claude turned on is open",
                    $"The extension only connects to the Claude app while its profile is open. Open {profiles}, then open its Claude side panel once.",
                    FixKind.OpenExtension, group.First().Id));
        }

        var waiting = claude.Where(e => e.NeedsApproval).ToList();
        if (waiting.Count > 0)
            checks.Add(new(CheckStatus.Info, $"Claude is waiting for approval in {waiting.Count} profile(s)",
                "The Claude app adds the extension to every profile through the registry, so each profile asks whether to turn it on. Leave them off unless you use Claude there.",
                FixKind.ShowSideloads));
    }

    static void CheckClaudeApp(IReadOnlyList<NativeHost> hosts, ScanResult scan, IReadOnlySet<string> claudeIds, List<DoctorCheck> checks)
    {
        bool appRunning = Process.GetProcessesByName("claude").Any(p => { p.Dispose(); return true; });
        checks.Add(appRunning
            ? new(CheckStatus.Ok, "The Claude app is running", "The extension connects to it through the browser link below.")
            : new(CheckStatus.Warning, "The Claude app isn't running", "Open the Claude desktop app; the extension connects to it."));

        var claudeHosts = hosts.Where(IsClaude).ToList();
        if (claudeHosts.Count == 0)
        {
            checks.Add(new(CheckStatus.Error, "The Claude app's browser link isn't registered",
                "Open (or reinstall) the Claude desktop app: it registers the link (native messaging host) when it starts. Then restart the browser."));
            return;
        }
        var installedIds = scan.AllExtensions.Where(e => claudeIds.Contains(e.Id)).Select(e => e.Id).Distinct().ToList();
        foreach (var host in claudeHosts)
        {
            if (host.IsBroken)
            {
                checks.Add(new(CheckStatus.Error, "The Claude app's browser link is broken",
                    $"{host.StatusText} ({host.DisplayKey}). Reopen or reinstall the Claude desktop app to repair it."));
                continue;
            }
            var missing = installedIds.Where(id => !host.AllowedExtensions.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            checks.Add(missing.Count == 0
                ? new(CheckStatus.Ok, "The Claude app's browser link is registered", $"{host.ProgramPath} · used by {string.Join(", ", host.Browsers)}")
                : new(CheckStatus.Warning, "The Claude browser link doesn't accept your Claude extension",
                    $"Not allowed: {string.Join(", ", missing)}. Update the Claude app so it re-registers the link."));
        }

        bool bridgeActive = Process.GetProcessesByName("chrome-native-host").Any(p => { p.Dispose(); return true; });
        checks.Add(new(CheckStatus.Info, bridgeActive ? "A Claude browser link is active right now" : "No Claude browser link is active right now",
            bridgeActive
                ? "An extension is talking to the Claude app at the moment."
                : "Normal when Claude isn't in use. If Claude says the browser isn't connected, open the profile you use with Claude and its Claude side panel."));
    }

    static void CheckPolicies(ScanResult scan, IReadOnlySet<string> claudeIds, List<DoctorCheck> checks)
    {
        foreach (var browser in scan.Browsers.Where(b => b.Definition.PolicyKey != null).DistinctBy(b => b.Definition.PolicyKey))
        {
            var key = browser.Definition.PolicyKey!;
            if (PolicyInspector.ReadValue(key, "NativeMessagingUserLevelHosts") is int userHosts && userHosts == 0)
                checks.Add(new(CheckStatus.Error, $"{browser.Name} ignores per-user browser links",
                    "A policy (NativeMessagingUserLevelHosts = 0) stops the Claude app's link from working. Ask whoever manages this PC, or remove the policy."));

            var blocked = PolicyInspector.ReadList(key, "NativeMessagingBlocklist");
            var allowed = PolicyInspector.ReadList(key, "NativeMessagingAllowlist");
            if (!allowed.Any(a => a.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase))
                && blocked.Any(b => b == "*" || b.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase)))
                checks.Add(new(CheckStatus.Error, $"{browser.Name} blocks the Claude browser link", $"Policy NativeMessagingBlocklist: {string.Join(", ", blocked)}."));

            // ExtensionInstallBlocklist ("*" = every extension) unless the allowlist names Claude (checked by ChromeClaudeDoctor too).
            var installBlocked = PolicyInspector.ReadList(key, "ExtensionInstallBlocklist");
            var installAllowed = PolicyInspector.ReadList(key, "ExtensionInstallAllowlist");
            if (!installAllowed.Any(claudeIds.Contains) && installBlocked.Any(b => b == "*" || claudeIds.Contains(b)))
                checks.Add(new(CheckStatus.Error, $"{browser.Name} blocks the Claude extension",
                    $"Policy ExtensionInstallBlocklist: {string.Join(", ", installBlocked)}. Ask whoever manages this PC, or remove the policy."));

            var settings = ExtensionSettingsPolicy.ReadMachine(browser.Definition);
            bool defaultReported = false;
            foreach (var id in claudeIds)
            {
                // An extension's own entry wins over the "*" default entry.
                var own = ExtensionSettingsPolicy.ModeFor(settings, id);
                var mode = own ?? ExtensionSettingsPolicy.ModeFor(settings, "*");
                if (mode is not ("blocked" or "removed") || (own == null && defaultReported)) continue;
                if (!scan.AllExtensions.Any(e => e.Id == id) && !ProblemFinder.ClaudeIds.Contains(id)) continue; // other builds: only when installed
                checks.Add(own != null
                    ? new(CheckStatus.Warning, $"Claude is blocked by {browser.Name}'s policy",
                        $"ExtensionSettings sets {id} to {mode}. Use “Unblock” on the Claude extension to lift it.",
                        FixKind.OpenExtension, id)
                    : new(CheckStatus.Error, $"{browser.Name}'s policy blocks every extension, including Claude",
                        $"ExtensionSettings sets the default (\"*\") to {mode}. Ask whoever manages this PC to allow {id}."));
                defaultReported |= own == null;
            }
        }
    }
}
