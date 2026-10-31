using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BPM.Core.Analysis;
using BPM.Core.Browsers;
using BPM.Core.Chromium;
using BPM.Core.Diagnostics;
using BPM.Core.Journal;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.App.Services;

/// <summary>"BrowserProfileManager.exe --demo": the app shows a made-up PC (profiles, extensions, problems) instead of
/// scanning this one, so it can be shown in screenshots and videos without anyone's real profiles or emails.
/// Nothing on the PC is read for the lists or changed: every action is turned off.</summary>
public static class DemoMode
{
    public static bool IsOn { get; private set; }

    static readonly string Dir = Path.Combine(Path.GetTempPath(), "BrowserProfileManager-Demo");
    static readonly DateTime Now = DateTime.Now;

    /// <summary>Switches demo mode on before anything loads: settings, change log and icons live in a temp folder.</summary>
    public static void Start()
    {
        IsOn = true;
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        Directory.CreateDirectory(Dir);
        AppPaths.UseDataDir(Dir);
        SeedChangeLog();
    }

    // ---------- extensions ----------

    sealed record DemoExtension(string Id, string Name, string Version, Color Color, string Letters,
        string[] Api, string[] Hosts, ExtensionSource Source = ExtensionSource.Store, int Manifest = 3,
        string Description = "");

    static readonly DemoExtension UBlock = new("cjpalhdlnbpafiamejdnhcphjbkeiagm", "uBlock Origin", "1.62.0", Rgb(0x80, 0x00, 0x00), "uB",
        ["storage", "tabs", "webNavigation", "webRequest", "declarativeNetRequest", "contextMenus"], ["<all_urls>"], Description: "Finally, an efficient blocker. Easy on CPU and memory.");
    static readonly DemoExtension Grammarly = new("kbfnbcaeplbcioakkpcpgfkobkghlhen", "Grammarly: AI Writing Assistant", "14.1190.0", Rgb(0x15, 0xC3, 0x9A), "G",
        ["storage", "tabs", "scripting", "contextMenus", "notifications"], ["<all_urls>"], Description: "Improve your writing with all-in-one assistance.");
    static readonly DemoExtension Bitwarden = new("nngceckbapebfimnlniiiahkandclblb", "Bitwarden Password Manager", "2024.10.1", Rgb(0x17, 0x5D, 0xDC), "B",
        ["tabs", "contextMenus", "storage", "clipboardWrite", "webRequest", "webRequestAuthProvider", "idle", "alarms", "unlimitedStorage"], ["<all_urls>"], Description: "A secure and free password manager for all of your devices.");
    static readonly DemoExtension DocsOffline = new("ghbmnnjooekpmoecnnnilnnbdlolhkhi", "Google Docs Offline", "1.111.1", Rgb(0x42, 0x85, 0xF4), "D",
        ["storage", "unlimitedStorage"], ["https://docs.google.com/*", "https://drive.google.com/*"], Description: "Edit, create, and view your documents without internet access.");
    static readonly DemoExtension DarkReader = new("eimadpbcbfnmbkopoojfekhnkhdbieeh", "Dark Reader", "4.9.96", Rgb(0x1E, 0x1E, 0x1E), "DR",
        ["storage", "tabs", "contextMenus", "alarms", "scripting"], ["<all_urls>"], Description: "Dark mode for every website.");
    static readonly DemoExtension CouponSaver = new("hfapbcheiepjppjbnkphkmegjlipojba", "Coupon Saver Pro", "3.2.0", Rgb(0xF5, 0x9E, 0x0B), "%",
        ["cookies", "tabs", "webRequest", "history", "storage"], ["<all_urls>"], Description: "Automatically finds coupon codes while you shop.");
    static readonly DemoExtension PdfToolbox = new("mhjfbmdgcfjbbpaeojofohoefgiehjai", "PDF Toolbox", "12.0.3", Rgb(0xDC, 0x26, 0x26), "P",
        ["nativeMessaging", "tabs", "downloads", "storage"], ["<all_urls>"], ExtensionSource.OtherProgram, Description: "Open, convert and sign PDFs. Installed by the PDF Suite desktop app.");
    static readonly DemoExtension TeamDashboard = new("baadcafedeadbeefcafebabefeedface", "Team Dashboard (dev build)", "0.9.4", Rgb(0x6D, 0x28, 0xD9), "TD",
        ["tabs", "scripting", "storage", "debugger"], ["<all_urls>"], ExtensionSource.Developer, Description: "Internal tool loaded in developer mode.");
    static readonly DemoExtension TabSaver = new("cdbcgipcahpfphgkmbmmbbpjkhhacpcl", "Tab Groups Saver", "2.1.0", Rgb(0x0E, 0xA5, 0xE9), "T",
        ["tabGroups", "storage"], [], Description: "Save and restore your tab groups.");
    static readonly DemoExtension Weather = new("ldpmmlhnidimbhpafkfbcffaljmkohob", "Weather Now Widget", "1.4.2", Rgb(0x64, 0x74, 0x8B), "W",
        ["geolocation", "tabs", "storage", "management"], ["<all_urls>"], Manifest: 2, Description: "Current weather in your toolbar.");

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    // ---------- the made-up PC ----------

    public static ScanResult CreateScan()
    {
        var scan = new ScanResult { MachineName = "DEMO-PC", UserName = "alex" };

        var chrome = Browser("chrome", "141.0.7390.55", running: true);
        var work = Profile(chrome, "Default", "Work", "alex.morgan@example.com", "Alex", 0xFF3B82F6, hoursAgo: 0, open: true, sizeMb: 2450, lastUsed: true);
        var personal = Profile(chrome, "Profile 1", "Personal", "alex@example.com", "Alex", 0xFF22C55E, hoursAgo: 2, open: true, sizeMb: 1180);
        var shopping = Profile(chrome, "Profile 2", "Shopping", null, null, 0xFFF59E0B, hoursAgo: 24 * 18, open: false, sizeMb: 640);
        var family = Profile(chrome, "Profile 3", "Family", "sam.morgan@example.com", "Sam", 0xFFEC4899, hoursAgo: 24 * 150, open: false, sizeMb: 820);
        Add(work, UBlock, Grammarly, Bitwarden, DocsOffline, TeamDashboard);
        Add(work, PdfToolbox, waiting: true);
        Add(personal, UBlock, Bitwarden, DarkReader);
        Add(personal, Grammarly with { Version = "14.1185.0" });
        Add(personal, TabSaver, offByUser: true);
        Add(personal, PdfToolbox, waiting: true);
        Add(shopping, UBlock, CouponSaver);
        Add(shopping, PdfToolbox, waiting: true);
        Add(family, DocsOffline, CouponSaver);
        Add(family, PdfToolbox, waiting: true);
        Add(family, Weather, flagged: true);

        var edge = Browser("edge", "141.0.3537.71", running: false);
        var edgePersonal = Profile(edge, "Default", "Personal", "alex@example.com", "Alex", 0xFF0EA5E9, hoursAgo: 24 * 3, open: false, sizeMb: 530, lastUsed: true);
        Add(edgePersonal, UBlock, Bitwarden, DarkReader);

        var brave = Browser("brave", "1.84.132", running: true);
        var bravePrivate = Profile(brave, "Default", "Private", null, null, 0xFFF97316, hoursAgo: 5, open: true, sizeMb: 410, lastUsed: true);
        Add(bravePrivate, Bitwarden, DarkReader);

        scan.Browsers.AddRange([chrome, edge, brave]);
        foreach (var e in scan.AllExtensions) e.Risk = RiskAnalyzer.Assess(e);
        return scan;
    }

    /// <summary>What the open profiles' buttons say: "Personal" shows a paused account, for the Health page.</summary>
    public static Dictionary<string, string> ProfileButtonLabels(ScanResult scan) =>
        scan.AllProfiles.Where(p => p.IsOpen).ToDictionary(p => p.Path, p => p.Name == "Personal" ? "Paused" : p.GivenName ?? p.Name);

    public static List<NativeHost> Hosts() =>
    [
        Host("com.bitwarden.desktop", RegistryScope.User, @"C:\Program Files\Bitwarden\Bitwarden.exe", HostStatus.Ok),
        Host("com.google.drive.nativeproxy", RegistryScope.User, @"C:\Program Files\Google\Drive File Stream\GoogleDriveFS.exe", HostStatus.Ok),
        Host("com.pdfsuite.connector", RegistryScope.Machine32, @"C:\Program Files (x86)\PDF Suite\connector.exe", HostStatus.Ok),
        Host("com.oldvpn.helper", RegistryScope.User, @"C:\Program Files\OldVPN\vpn-helper.exe", HostStatus.ProgramMissing),
    ];

    public static List<PolicyValue> Policies() =>
    [
        new("Microsoft Edge", RegistryScope.Machine, @"SOFTWARE\Policies\Microsoft\Edge", "HomepageLocation", "https://intranet.example.com"),
        new("Microsoft Edge", RegistryScope.Machine, @"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", "1"),
    ];

    public static List<Sideload> Sideloads() =>
    [
        new(PdfToolbox.Id, RegistryScope.Machine32, $@"SOFTWARE\Google\Chrome\Extensions\{PdfToolbox.Id}",
            new Dictionary<string, string> { ["update_url"] = "https://clients2.google.com/service/update2/crx" }, ["Google Chrome", "Brave"]),
    ];

    static BrowserInfo Browser(string id, string version, bool running)
    {
        var definition = BrowserCatalog.Find(id)!;
        // The real program, when it's installed, only so the browser shows its own icon.
        var exe = definition.ExePaths.SelectMany(PathUtil.Expand).FirstOrDefault(File.Exists);
        return new BrowserInfo { Definition = definition, ExePath = exe, Version = version, DataDir = Path.Combine(Dir, id), IsRunning = running };
    }

    static ProfileInfo Profile(BrowserInfo browser, string key, string name, string? email, string? given, uint color,
        int hoursAgo, bool open, long sizeMb, bool lastUsed = false)
    {
        var profile = new ProfileInfo
        {
            Browser = browser, Key = key, Path = Path.Combine(browser.DataDir!, key), Name = name, Email = email,
            GivenName = given, FullName = given != null ? $"{given} Morgan" : null, ColorArgb = color,
            IsLastUsed = lastUsed, LastActive = Now.AddHours(-hoursAgo), IsOpen = open, SizeBytes = sizeMb * 1024 * 1024,
        };
        browser.Profiles.Add(profile);
        return profile;
    }

    static void Add(ProfileInfo profile, params DemoExtension[] extensions)
    {
        foreach (var e in extensions) Add(profile, e);
    }

    static void Add(ProfileInfo profile, DemoExtension e, bool waiting = false, bool offByUser = false, bool flagged = false)
    {
        long bits = (waiting ? DisableReasonCodes.ExternalExtension : 0) | (offByUser ? DisableReasonCodes.UserAction : 0)
                    | (flagged ? DisableReasonCodes.Greylist : 0);
        profile.Extensions.Add(new ExtensionInfo
        {
            Profile = profile, Id = e.Id, Name = e.Name, Description = e.Description, Version = e.Version,
            Enabled = bits == 0, DisableReasonBits = bits, DisableReasons = DisableReasonCodes.Describe(bits),
            Source = e.Source, Kind = ExtensionKind.Extension, Location = e.Source switch
            {
                ExtensionSource.Developer => 4,
                ExtensionSource.OtherProgram => 3,
                _ => 1,
            },
            ManifestVersion = e.Manifest,
            StoreUrl = e.Source == ExtensionSource.Store ? $"https://chromewebstore.google.com/detail/{e.Id}" : null,
            FolderPath = Path.Combine(profile.Path, "Extensions", e.Id),
            IconPath = Icon(e),
            FilesPresent = true,
            InstalledAt = Now.AddDays(-(e.Id[0] - 'a' + 30) * 9),
            UpdatedAt = Now.AddDays(-(e.Id[1] - 'a' + 2)),
            ApiPermissions = e.Api,
            HostPermissions = e.Hosts,
        });
    }

    static NativeHost Host(string name, RegistryScope scope, string program, HostStatus status) =>
        new(name, scope, $@"SOFTWARE\Google\Chrome\NativeMessagingHosts\{name}", Path.Combine(Dir, name + ".json"), program,
            [], ["Google Chrome", "Microsoft Edge", "Brave"], status);

    /// <summary>A simple rounded-square icon with the extension's letters, drawn once into the demo folder.</summary>
    static string Icon(DemoExtension e)
    {
        var path = Path.Combine(Dir, "icons", e.Id + ".png");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const int size = 96;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(e.Color), null, new Rect(0, 0, size, size), 22, 22);
            var text = new FormattedText(e.Letters, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                e.Letters.Length > 1 ? 38 : 50, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    static void SeedChangeLog()
    {
        ChangeRecord Record(int hoursAgo, ChangeKind kind, string title, string detail, bool canUndo = true) =>
            new() { Time = Now.AddHours(-hoursAgo), Kind = kind, Title = title, Detail = detail, CanUndo = canUndo };
        ChangeJournal.Add(Record(1, ChangeKind.ExtensionSwitch, "Turned Tab Groups Saver off", "Google Chrome · Personal"));
        ChangeJournal.Add(Record(5, ChangeKind.ProfileCreated, "Created the Google Chrome profile “Family”", "Folder Profile 3. To delete it, use the profile menu in Google Chrome.", canUndo: false));
        ChangeJournal.Add(Record(26, ChangeKind.ExtensionRemoval, "Removed Coupon Saver Pro", "Google Chrome · Work · reinstall from its store page", canUndo: false));
        ChangeJournal.Add(Record(30, ChangeKind.ExtensionData, "Reset Dark Reader's data", "Google Chrome · Personal · 2 folder(s) backed up"));
        ChangeJournal.Add(Record(52, ChangeKind.Shortcut, "Created a desktop shortcut for Work", @"C:\Users\alex\Desktop\Work - Google Chrome.lnk"));
    }
}
