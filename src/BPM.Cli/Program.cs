using System.Text.Json;
using System.Text.Json.Serialization;
using BPM.Core.Models;
using BPM.Core.Scanning;

// bpm scan            summary of every browser, profile and extension
// bpm scan --json     the full scan as JSON (contains profile names and account emails)
// bpm uninstall <browser id> <profile folder> <extension id>
//                     removes one extension from one profile through the browser's page, exactly like the app (for testing)
var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
var result = Scanner.Scan();

// bpm newprofile <browser id> <name>
//                     creates a new profile with that name and opens it, exactly like the app's "New profile…" button
if (args.Length == 3 && args[0] == "newprofile")
{
    var browser = result.Browsers.FirstOrDefault(b => b.Id == args[1]);
    if (browser == null)
    {
        Console.WriteLine($"Browser not found: {args[1]}");
        return;
    }
    var created = BPM.Core.Actions.ProfileCreator.Create(browser, args[2]);
    Console.WriteLine($"Created “{created.Name}” in {created.Key} ({created.Path})");
    return;
}

// bpm install <browser id> <profile folder> <store link or id | extension folder | .crx file>
//                     installs one extension in one profile through the browser's own pages, like the app (a folder or .crx
//                     is loaded from where it is; the app loads its own copy instead)
if (args.Length == 4 && args[0] == "install")
{
    var profile = result.AllProfiles.FirstOrDefault(p => p.Browser.Id == args[1] && p.Key == args[2]);
    if (profile == null)
    {
        Console.WriteLine($"Profile not found: {args[1]} / {args[2]}");
        return;
    }
    var what = args[3];
    if (what.EndsWith(".crx", StringComparison.OrdinalIgnoreCase) && File.Exists(what))
        what = BPM.Core.Actions.ExtensionInstall.UnpackCrx(what, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(what))!, Path.GetFileNameWithoutExtension(what)));
    var source = Directory.Exists(what)
        ? BPM.Core.Actions.ExtensionInstall.FromFolder(what)
        : BPM.Core.Actions.ExtensionInstall.ParseStore(what) ?? throw new ArgumentException("Not a store link, extension ID, folder or .crx file: " + what);
    if (BPM.Core.Actions.ExtensionInstall.CheckTarget(profile, source) is { } reason)
    {
        Console.WriteLine($"Skipped: {reason}");
        return;
    }
    if (source.Kind == BPM.Core.Actions.InstallSourceKind.Store)
    {
        var outcome = await BPM.Core.Automation.BrowserAutomation.InstallFromStoreAsync(profile, source.StoreId!);
        Console.WriteLine($"{outcome.Outcome}: {outcome.Message}");
    }
    else
    {
        var loaded = await BPM.Core.Automation.BrowserAutomation.LoadUnpackedAsync(profile, source.FolderPath!, source.Name);
        Console.WriteLine($"{loaded.Result.Outcome}: {loaded.Result.Message}{(loaded.TurnedOnDeveloperMode ? " (turned Developer mode on)" : "")}");
    }
    return;
}

// bpm doctor          the Doctor page's checks (Claude in Chrome connection, browser links, policies)
if (args.Length == 1 && args[0] == "doctor")
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    var links = BPM.Core.Diagnostics.NativeHostInspector.Find(result.Browsers.Select(b => b.Definition));
    foreach (var check in BPM.Core.Diagnostics.ConnectionDoctor.Run(result, links).OrderByDescending(c => c.Status))
        Console.WriteLine($"[{check.Status,-7}] {check.Title}\n          {check.Detail}");
    return;
}

// bpm page <browser id> <profile folder> <extension id>
//                     opens the extension's page in that profile with the app's window and profile checks; changes nothing
if (args.Length == 4 && args[0] == "page")
{
    var target = result.AllExtensions.FirstOrDefault(e =>
        e.Profile.Browser.Id == args[1] && e.Profile.Key == args[2] && e.Id == args[3]);
    if (target == null)
    {
        Console.WriteLine($"Not found: {args[3]} in {args[1]} / {args[2]}");
        return;
    }
    var shown = await BPM.Core.Automation.BrowserAutomation.ShowExtensionPageAsync(target);
    Console.WriteLine($"{shown.Outcome}: {shown.Message}");
    return;
}

if (args.Length == 4 && args[0] == "uninstall")
{
    var target = result.AllExtensions.FirstOrDefault(e =>
        e.Profile.Browser.Id == args[1] && e.Profile.Key == args[2] && e.Id == args[3]);
    if (target == null)
    {
        Console.WriteLine($"Not found: {args[3]} in {args[1]} / {args[2]}");
        return;
    }
    var outcome = await BPM.Core.Automation.BrowserAutomation.UninstallAsync(target);
    Console.WriteLine($"{outcome.Outcome}: {outcome.Message}");
    return;
}

if (args.Length == 5 && args[0] == "onoff" && args[4] is "on" or "off")
{
    var target = result.AllExtensions.FirstOrDefault(e =>
        e.Profile.Browser.Id == args[1] && e.Profile.Key == args[2] && e.Id == args[3]);
    if (target == null)
    {
        Console.WriteLine($"Not found: {args[3]} in {args[1]} / {args[2]}");
        return;
    }
    var outcome = await BPM.Core.Automation.BrowserAutomation.SetEnabledAsync(target, args[4] == "on");
    Console.WriteLine($"{outcome.Outcome}: {outcome.Message}");
    return;
}

if (json)
{
    var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"Scan of {result.MachineName} at {result.ScannedAt:yyyy-MM-dd HH:mm}");
foreach (var browser in result.Browsers)
{
    Console.WriteLine();
    Console.WriteLine($"{browser.Name} {browser.Version}  {(browser.IsRunning ? "[running]" : "")}  profiles: {browser.Profiles.Count}  policies: {(browser.SupportsPolicies ? "yes" : "no")}");
    Console.WriteLine($"   exe:  {browser.ExePath ?? "(not found)"}");
    Console.WriteLine($"   data: {browser.DataDir ?? "(none)"}");
    if (browser.ScanError != null) Console.WriteLine($"   ERROR: {browser.ScanError}");
    foreach (var profile in browser.Profiles)
    {
        var flags = string.Join(", ", new[] { profile.IsOpen ? "open" : null, profile.IsLastUsed ? "last used" : null }.OfType<string>());
        Console.WriteLine($"   > {profile.Name} [{profile.Key}] {profile.Email}{(flags.Length > 0 ? $"  <{flags}>" : "")}  ({profile.Extensions.Count} extensions)");
        foreach (var e in profile.Extensions)
        {
            var why = e.Enabled ? "" : $"  - {string.Join("; ", e.DisableReasons)}";
            Console.WriteLine($"       [{(e.Enabled ? "ON " : "OFF")}] {e.Name} {e.Version}  {e.Source}, MV{e.ManifestVersion}, risk {e.Risk.Level}{why}");
        }
    }
}
if (result.OtherBrowsers.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Other browsers (not managed): " + string.Join(", ", result.OtherBrowsers.Select(o => o.Name)));
}
