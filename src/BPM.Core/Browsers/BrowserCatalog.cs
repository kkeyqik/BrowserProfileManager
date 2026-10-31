using BPM.Core.Models;

namespace BPM.Core.Browsers;

/// <summary>Every browser the app knows how to read. Registry locations for Chrome, Edge, Brave, Opera and Comet were
/// read from each browser's own binaries (M1); Chrome's policy route was proven on a real install.</summary>
public static class BrowserCatalog
{
    const string ChromeHosts = @"SOFTWARE\Google\Chrome\NativeMessagingHosts";
    const string ChromiumHosts = @"SOFTWARE\Chromium\NativeMessagingHosts";
    const string ChromeSideload = @"SOFTWARE\Google\Chrome\Extensions";

    public static IReadOnlyList<BrowserDefinition> All { get; } =
    [
        // ---- Google Chrome (all channels share the policy, native-host and sideload keys) ----
        Chrome("chrome", "Google Chrome", @"Google\Chrome", programFiles: true),
        Chrome("chrome-beta", "Google Chrome Beta", @"Google\Chrome Beta", programFiles: true),
        Chrome("chrome-dev", "Google Chrome Dev", @"Google\Chrome Dev", programFiles: true),
        Chrome("chrome-canary", "Google Chrome Canary", @"Google\Chrome SxS", programFiles: false),
        new()
        {
            Id = "chromium", Name = "Chromium",
            DataDirs = [@"%LOCALAPPDATA%\Chromium\User Data"],
            ExePaths = [@"%LOCALAPPDATA%\Chromium\Application\chrome.exe", @"%ProgramFiles%\Chromium\Application\chrome.exe"],
            PolicyKey = @"SOFTWARE\Policies\Chromium",
            NativeHostKeys = [ChromiumHosts],
            ExternalExtensionsKey = @"SOFTWARE\Chromium\Extensions",
        },

        // ---- Microsoft Edge ----
        Edge("edge", "Microsoft Edge", "Edge", localInstall: false),
        Edge("edge-beta", "Microsoft Edge Beta", "Edge Beta", localInstall: false),
        Edge("edge-dev", "Microsoft Edge Dev", "Edge Dev", localInstall: false),
        Edge("edge-canary", "Microsoft Edge Canary", "Edge SxS", localInstall: true),

        // ---- Brave (reads Chrome's sideload list) ----
        Brave("brave", "Brave", "Brave-Browser"),
        Brave("brave-beta", "Brave Beta", "Brave-Browser-Beta"),
        Brave("brave-nightly", "Brave Nightly", "Brave-Browser-Nightly"),

        // ---- Opera (data in Roaming; reads Chrome's sideload list) ----
        new()
        {
            Id = "opera", Name = "Opera",
            DataDirs = [@"%APPDATA%\Opera Software\Opera Stable"],
            ExePaths = [@"%LOCALAPPDATA%\Programs\Opera\opera.exe", @"%ProgramFiles%\Opera\opera.exe"],
            PolicyKey = @"SOFTWARE\Policies\OperaSoftware\Opera",
            NativeHostKeys = [@"SOFTWARE\Opera Software\Opera Stable\NativeMessagingHosts", ChromeHosts, ChromiumHosts],
            ExternalExtensionsKey = ChromeSideload,
            ExtensionsPageUrl = "opera://extensions",
        },
        new()
        {
            Id = "opera-gx", Name = "Opera GX",
            DataDirs = [@"%APPDATA%\Opera Software\Opera GX Stable"],
            ExePaths = [@"%LOCALAPPDATA%\Programs\Opera GX\opera.exe", @"%ProgramFiles%\Opera GX\opera.exe"],
            NativeHostKeys = [@"SOFTWARE\Opera Software\Opera GX Stable\NativeMessagingHosts", ChromeHosts, ChromiumHosts],
            ExternalExtensionsKey = ChromeSideload,
            ExtensionsPageUrl = "opera://extensions",
        },

        // ---- Other Chromium browsers ----
        new()
        {
            Id = "vivaldi", Name = "Vivaldi",
            DataDirs = [@"%LOCALAPPDATA%\Vivaldi\User Data"],
            ExePaths = [@"%LOCALAPPDATA%\Vivaldi\Application\vivaldi.exe", @"%ProgramFiles%\Vivaldi\Application\vivaldi.exe"],
            NativeHostKeys = [ChromeHosts, ChromiumHosts],
            ExtensionsPageUrl = "vivaldi://extensions",
        },
        new()
        {
            Id = "comet", Name = "Comet",
            DataDirs = [@"%LOCALAPPDATA%\Perplexity\Comet\User Data"],
            ExePaths = [@"%ProgramFiles%\Perplexity\Comet\Application\comet.exe", @"%LOCALAPPDATA%\Perplexity\Comet\Application\comet.exe"],
            PolicyKey = @"SOFTWARE\Policies\Perplexity\Comet",
            NativeHostKeys = [ChromeHosts],
            ExternalExtensionsKey = @"SOFTWARE\Comet\Browser\Extensions",
        },
        new()
        {
            Id = "arc", Name = "Arc",
            DataDirs = [@"%LOCALAPPDATA%\Packages\TheBrowserCompany.Arc_*\LocalCache\Local\Arc\User Data"],
            NativeHostKeys = [@"SOFTWARE\ArcBrowser\Arc\NativeMessagingHosts", ChromeHosts],
        },
        new()
        {
            Id = "coccoc", Name = "Cốc Cốc",
            DataDirs = [@"%LOCALAPPDATA%\CocCoc\Browser\User Data"],
            ExePaths = [@"%LOCALAPPDATA%\CocCoc\Browser\Application\browser.exe", @"%ProgramFiles%\CocCoc\Browser\Application\browser.exe"],
            NativeHostKeys = [@"SOFTWARE\CocCoc\Browser\NativeMessagingHosts", ChromeHosts],
        },
        new()
        {
            Id = "yandex", Name = "Yandex Browser",
            DataDirs = [@"%LOCALAPPDATA%\Yandex\YandexBrowser\User Data"],
            ExePaths = [@"%LOCALAPPDATA%\Yandex\YandexBrowser\Application\browser.exe"],
            NativeHostKeys = [ChromeHosts],
            ExtensionsPageUrl = "browser://extensions",
        },
        new()
        {
            Id = "thorium", Name = "Thorium",
            DataDirs = [@"%LOCALAPPDATA%\Thorium\User Data"],
            ExePaths = [@"%LOCALAPPDATA%\Thorium\Application\thorium.exe"],
            NativeHostKeys = [ChromeHosts, ChromiumHosts],
        },

        // ---- Firefox family (listed and opened at about:addons; on/off is done in the browser) ----
        Firefox("firefox", "Mozilla Firefox", @"Mozilla\Firefox", [@"%ProgramFiles%\Mozilla Firefox\firefox.exe", @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"], @"SOFTWARE\Policies\Mozilla\Firefox"),
        Firefox("librewolf", "LibreWolf", "librewolf", [@"%ProgramFiles%\LibreWolf\librewolf.exe"], null),
        Firefox("waterfox", "Waterfox", "Waterfox", [@"%ProgramFiles%\Waterfox\waterfox.exe"], null),
        Firefox("zen", "Zen Browser", "zen", [@"%ProgramFiles%\Zen Browser\zen.exe"], null),
        Firefox("floorp", "Floorp", "Floorp", [@"%ProgramFiles%\Ablaze Floorp\floorp.exe"], null),
    ];

    public static BrowserDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    static BrowserDefinition Chrome(string id, string name, string folder, bool programFiles) => new()
    {
        Id = id, Name = name,
        DataDirs = [$@"%LOCALAPPDATA%\{folder}\User Data"],
        ExePaths = programFiles
            ? [$@"%ProgramFiles%\{folder}\Application\chrome.exe", $@"%ProgramFiles(x86)%\{folder}\Application\chrome.exe", $@"%LOCALAPPDATA%\{folder}\Application\chrome.exe"]
            : [$@"%LOCALAPPDATA%\{folder}\Application\chrome.exe"],
        PolicyKey = @"SOFTWARE\Policies\Google\Chrome",
        PolicyTested = id == "chrome",
        NativeHostKeys = [ChromeHosts],
        ExternalExtensionsKey = ChromeSideload,
    };

    static BrowserDefinition Edge(string id, string name, string folder, bool localInstall) => new()
    {
        Id = id, Name = name,
        DataDirs = [$@"%LOCALAPPDATA%\Microsoft\{folder}\User Data"],
        ExePaths = localInstall
            ? [$@"%LOCALAPPDATA%\Microsoft\{folder}\Application\msedge.exe"]
            : [$@"%ProgramFiles(x86)%\Microsoft\{folder}\Application\msedge.exe", $@"%ProgramFiles%\Microsoft\{folder}\Application\msedge.exe"],
        PolicyKey = @"SOFTWARE\Policies\Microsoft\Edge",
        NativeHostKeys = [@"SOFTWARE\Microsoft\Edge\NativeMessagingHosts", ChromeHosts, ChromiumHosts],
        ExternalExtensionsKey = @"SOFTWARE\Microsoft\Edge\Extensions",
        ExtensionsPageUrl = "edge://extensions",
    };

    static BrowserDefinition Brave(string id, string name, string folder) => new()
    {
        Id = id, Name = name,
        DataDirs = [$@"%LOCALAPPDATA%\BraveSoftware\{folder}\User Data"],
        ExePaths = [$@"%ProgramFiles%\BraveSoftware\{folder}\Application\brave.exe", $@"%ProgramFiles(x86)%\BraveSoftware\{folder}\Application\brave.exe", $@"%LOCALAPPDATA%\BraveSoftware\{folder}\Application\brave.exe"],
        PolicyKey = @"SOFTWARE\Policies\BraveSoftware\Brave",
        NativeHostKeys = [$@"SOFTWARE\BraveSoftware\{folder}\NativeMessagingHosts", ChromeHosts, ChromiumHosts],
        ExternalExtensionsKey = ChromeSideload,
        ExtensionsPageUrl = "brave://extensions",
    };

    static BrowserDefinition Firefox(string id, string name, string roamingFolder, IReadOnlyList<string> exes, string? policyKey) => new()
    {
        Id = id, Name = name,
        Family = BrowserFamily.Firefox,
        DataDirs = [$@"%APPDATA%\{roamingFolder}"],
        ExePaths = exes,
        PolicyKey = policyKey,
        NativeHostKeys = [@"SOFTWARE\Mozilla\NativeMessagingHosts"],
        ExtensionsPageUrl = "about:addons",
    };
}
