namespace BPM.Core.Analysis;

/// <summary>How risky a permission is, from least to most concerning.</summary>
public enum PermissionWeight { Info = 0, Low = 1, Medium = 2, High = 3 }

/// <summary>A plain-English description and risk weight for one extension permission.</summary>
public sealed record PermissionInfo(string Key, string Description, PermissionWeight Weight);

/// <summary>Catalog of known browser extension permissions and host-access summaries.</summary>
public static class PermissionCatalog
{
    private static readonly PermissionInfo[] Table =
    [
        // High
        new("debugger", "Take full control of pages through the debugger", PermissionWeight.High),
        new("proxy", "Control how your traffic is routed (proxy settings)", PermissionWeight.High),
        new("nativeMessaging", "Talk to programs installed on this computer", PermissionWeight.High),
        new("cookies", "Read and change cookies, which can include your sign-ins", PermissionWeight.High),
        new("webRequestBlocking", "Block or change any network request", PermissionWeight.High),
        new("webRequestFilterResponse", "Read and change the content of web responses", PermissionWeight.High),
        new("webRequestFilterResponse.serviceWorkerScript", "Change the code websites run in the background", PermissionWeight.High),
        new("history", "Read and change your browsing history", PermissionWeight.High),
        new("clipboardRead", "Read what you copy to the clipboard", PermissionWeight.High),
        new("desktopCapture", "Capture your screen", PermissionWeight.High),
        new("userScripts", "Run user scripts on websites", PermissionWeight.High),
        new("webAuthenticationProxy", "Act as your passkey / security-key provider", PermissionWeight.High),
        new("vpnProvider", "Provide a VPN for your traffic", PermissionWeight.High),
        new("pkcs11", "Use security devices and certificates", PermissionWeight.High),
        new("privacy", "Change your privacy settings", PermissionWeight.High),

        // Medium
        new("management", "Manage your other extensions, apps and themes", PermissionWeight.Medium),
        new("tabs", "Read your browsing history (addresses of open tabs)", PermissionWeight.Medium),
        new("webNavigation", "Read your browsing history as you navigate", PermissionWeight.Medium),
        new("topSites", "Read a list of your most visited sites", PermissionWeight.Medium),
        new("sessions", "Read your recently closed tabs and other devices", PermissionWeight.Medium),
        new("bookmarks", "Read and change your bookmarks", PermissionWeight.Medium),
        new("readingList", "Read and change your reading list", PermissionWeight.Medium),
        new("downloads", "Manage your downloads", PermissionWeight.Medium),
        new("downloads.open", "Open downloaded files", PermissionWeight.Medium),
        new("geolocation", "Detect your physical location", PermissionWeight.Medium),
        new("identity.email", "Know your email address", PermissionWeight.Medium),
        new("browsingData", "Delete your browsing data", PermissionWeight.Medium),
        new("contentSettings", "Change site settings such as cookies, JavaScript, camera and microphone", PermissionWeight.Medium),
        new("scripting", "Insert code into websites it can access", PermissionWeight.Medium),
        new("declarativeNetRequest", "Block or redirect content on websites", PermissionWeight.Medium),
        new("declarativeNetRequestWithHostAccess", "Block or redirect content on websites it can access", PermissionWeight.Medium),
        new("declarativeNetRequestFeedback", "See which requests it blocked or changed", PermissionWeight.Medium),
        new("webRequest", "Observe your network requests", PermissionWeight.Medium),
        new("tabCapture", "Capture the content of your tabs", PermissionWeight.Medium),
        new("pageCapture", "Save complete pages, including what is on them", PermissionWeight.Medium),
        new("certificateProvider", "Provide certificates for authentication", PermissionWeight.Medium),
        new("platformKeys", "Use your client certificates", PermissionWeight.Medium),
        new("browserSettings", "Change browser settings", PermissionWeight.Medium),
        new("accessibilityFeatures.modify", "Change your accessibility settings", PermissionWeight.Medium),
        new("find", "Read the text of all open tabs", PermissionWeight.Medium),

        // Low
        new("activeTab", "Access the site you are on when you click the extension", PermissionWeight.Low),
        new("clipboardWrite", "Change what is on your clipboard", PermissionWeight.Low),
        new("notifications", "Show notifications", PermissionWeight.Low),
        new("background", "Keep running in the background", PermissionWeight.Low),
        new("identity", "Sign you in to its own service with your account", PermissionWeight.Low),
        new("ttsEngine", "Read all text spoken by speech synthesis", PermissionWeight.Low),
        new("fontSettings", "Change your font settings", PermissionWeight.Low),
        new("search", "Run searches with your default search engine", PermissionWeight.Low),
        new("tabGroups", "Read and change your tab groups", PermissionWeight.Low),
        new("contextualIdentities", "Manage container tabs", PermissionWeight.Low),
        new("dns", "Look up website addresses", PermissionWeight.Low),
        new("documentScan", "Use attached document scanners", PermissionWeight.Low),
        new("printing", "Send pages to your printers", PermissionWeight.Low),
        new("printerProvider", "Provide printers", PermissionWeight.Low),
        new("wallpaper", "Change your wallpaper", PermissionWeight.Low),
        new("loginState", "Know whether you are signed in", PermissionWeight.Low),
        new("processes", "See the browser's processes", PermissionWeight.Low),
        new("audio", "Use audio devices", PermissionWeight.Low),
        new("accessibilityFeatures.read", "Read your accessibility settings", PermissionWeight.Low),
        new("system.cpu", "Read processor information", PermissionWeight.Low),
        new("system.memory", "Read memory information", PermissionWeight.Low),
        new("system.storage", "Read storage device information", PermissionWeight.Low),
        new("system.display", "Read display information", PermissionWeight.Low),
        new("enterprise.deviceAttributes", "Read device attributes", PermissionWeight.Low),
        new("enterprise.platformKeys", "Use enterprise certificates", PermissionWeight.Low),

        // Info
        new("storage", "Store its own data", PermissionWeight.Info),
        new("unlimitedStorage", "Store unlimited data on this computer", PermissionWeight.Info),
        new("alarms", "Schedule tasks", PermissionWeight.Info),
        new("contextMenus", "Add items to the right-click menu", PermissionWeight.Info),
        new("menus", "Add items to the right-click menu", PermissionWeight.Info),
        new("sidePanel", "Show a side panel", PermissionWeight.Info),
        new("offscreen", "Use a hidden page for background work", PermissionWeight.Info),
        new("idle", "Know when you are away from the computer", PermissionWeight.Info),
        new("power", "Keep the computer awake", PermissionWeight.Info),
        new("tts", "Speak text aloud", PermissionWeight.Info),
        new("favicon", "Show website icons", PermissionWeight.Info),
        new("gcm", "Receive push messages", PermissionWeight.Info),
        new("runtime", "Basic extension runtime", PermissionWeight.Info),
        new("theme", "Change the browser theme", PermissionWeight.Info),
        new("captivePortal", "Detect Wi-Fi sign-in pages", PermissionWeight.Info),
        new("commands", "Use keyboard shortcuts", PermissionWeight.Info),
        new("devtools", "Add panels to the developer tools", PermissionWeight.Info),
        new("declarativeContent", "Show its button only on certain pages", PermissionWeight.Info),
        new("webRequestAuthProvider", "Answer sign-in prompts for websites", PermissionWeight.Medium),
        new("system.network", "Read network information", PermissionWeight.Low),
        new("unsafe", "Use unsafe internal APIs", PermissionWeight.Info),
    ];

    /// <summary>Known API permissions keyed by name (case-insensitive).</summary>
    public static IReadOnlyDictionary<string, PermissionInfo> Known { get; } =
        Table.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the catalog entry for a permission, or a generic Low-weight entry when unknown.</summary>
    public static PermissionInfo Describe(string permission)
    {
        if (Known.TryGetValue(permission, out var info))
            return info;

        return new PermissionInfo(permission, $"Uses the \"{permission}\" feature", PermissionWeight.Low);
    }

    /// <summary>True when a host match pattern covers every website.</summary>
    public static bool IsAllSites(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        if (pattern.Equals("<all_urls>", StringComparison.OrdinalIgnoreCase) ||
            pattern.Equals("*://*/*", StringComparison.OrdinalIgnoreCase) ||
            pattern.Equals("http://*/*", StringComparison.OrdinalIgnoreCase) ||
            pattern.Equals("https://*/*", StringComparison.OrdinalIgnoreCase) ||
            pattern.Equals("*://*", StringComparison.OrdinalIgnoreCase))
            return true;

        var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;

        var scheme = pattern[..schemeEnd];
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
            scheme != "*")
            return false;

        var rest = pattern[(schemeEnd + 3)..];
        var slash = rest.IndexOf('/');
        var host = slash < 0 ? rest : rest[..slash];
        return host == "*";
    }

    /// <summary>Plain-English summary of the websites a set of host patterns can access.</summary>
    public static PermissionInfo DescribeHosts(IReadOnlyList<string> patterns)
    {
        if (patterns is null || patterns.Count == 0)
            return new PermissionInfo("hosts", "No access to websites", PermissionWeight.Info);

        if (patterns.Any(IsAllSites))
            return new PermissionInfo("hosts", "Read and change all your data on all websites", PermissionWeight.High);

        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in patterns)
        {
            var host = ExtractHost(pattern);
            if (host is not null && seen.Add(host))
                hosts.Add(host);
        }

        var hasFile = patterns.Any(p => p.StartsWith("file://", StringComparison.OrdinalIgnoreCase));
        var count = hosts.Count;

        string text;
        if (count == 0)
        {
            text = "No access to websites";
        }
        else
        {
            var shown = string.Join(", ", hosts.Take(3));
            text = count == 1
                ? $"Read and change your data on 1 site: {shown}"
                : $"Read and change your data on {count} sites: {shown}";

            if (count > 3)
                text += $" and {count - 3} more";
        }

        var weight = count == 0 ? PermissionWeight.Info : PermissionWeight.Medium;

        if (hasFile)
        {
            text = count == 0 ? "Read your local files" : text + " (and your local files)";
            if (weight < PermissionWeight.Medium)
                weight = PermissionWeight.Medium;
        }

        return new PermissionInfo("hosts", text, weight);
    }

    static readonly HashSet<string> WebSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "*", "ws", "wss", "ftp" };

    /// <summary>Extracts the bare host from a match pattern, or null if there is none.</summary>
    private static string? ExtractHost(string pattern)
    {
        var schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return null;

        // Only website patterns count as sites (not chrome://favicon/, chrome-extension://, …).
        var scheme = pattern[..schemeEnd];
        if (!WebSchemes.Contains(scheme))
            return null;

        var rest = pattern[(schemeEnd + 3)..];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];

        var colon = authority.IndexOf(':');
        if (colon >= 0)
            authority = authority[..colon];

        if (authority.StartsWith("*.", StringComparison.Ordinal))
            authority = authority[2..];

        // A bare wildcard host is not a named site; skip it.
        if (authority.Length == 0 || authority == "*")
            return null;

        return authority;
    }
}
