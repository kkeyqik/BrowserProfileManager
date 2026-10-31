using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using BPM.Core.Actions;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Automation;

public enum AutomationOutcome { Done, AlreadyInState, NeedsUser, Blocked, Failed }

public sealed record AutomationResult(AutomationOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome is AutomationOutcome.Done or AutomationOutcome.AlreadyInState;
}

/// <summary>Drives a Chromium browser's own extensions page through Windows UI Automation, visibly, like a person would.
/// Proven in M1 on Chrome: browsers refuse chrome:// pages from the command line, so the app opens a new window of the
/// profile, types the address into its address bar (pressing Enter only once that address bar has focus), then flips the
/// switch whose page id is "enableToggle".
/// Safety rules: it only uses a brand-new, blank window whose profile button names the exact profile; it only touches a
/// page that shows that one extension's details; it only closes windows that hold just the tab it opened. Anything
/// unexpected stops and asks the user instead. All work runs off the UI thread.</summary>
public static class BrowserAutomation
{
    const string WindowClass = "Chrome_WidgetWin_1";

    /// <summary>Address bar and profile button classes: Chrome's, and Brave's own (seen on Brave 1.98, Oct 2026).</summary>
    static readonly string[] AddressBarClasses = ["OmniboxViewViews", "BraveOmniboxViewViews"];
    static readonly string[] AvatarButtonClasses = ["AvatarToolbarButton", "BraveAvatarToolbarButton"];

    static Condition ClassIs(string[] classes) => new OrCondition(classes
        .Select(c => (Condition)new PropertyCondition(AutomationElement.ClassNameProperty, c)).ToArray());

    public static Task<AutomationResult> SetEnabledAsync(ExtensionInfo extension, bool enable, CancellationToken ct = default) =>
        RunSafe(async () =>
        {
            var (window, problem) = await OpenInternalPageAsync(extension.Profile, ExtensionPage(extension), ct);
            if (window == null) return problem!;

            var (toggle, notThere) = await FindExtensionSwitchAsync(window, extension, ct);
            if (toggle == null) return notThere!;
            if (!toggle.Current.IsEnabled)
                return new(AutomationOutcome.Blocked, "The switch is locked: the extension is blocked by policy or needs repair.");

            var pattern = (TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
            if (IsOn(pattern) == enable)
            {
                CloseIfOnlyOurTab(window);
                return new(AutomationOutcome.AlreadyInState, $"{extension.Name} was already {(enable ? "on" : "off")} in {extension.Profile.Name}.");
            }

            pattern.Toggle();
            // The page updates the switch a moment later (seen in M1), so wait for the new state.
            for (int i = 0; i < 25; i++)
            {
                await Task.Delay(200, ct);
                if (IsOn(pattern) == enable)
                {
                    CloseIfOnlyOurTab(window);
                    return new(AutomationOutcome.Done, $"Turned {extension.Name} {(enable ? "on" : "off")} in {extension.Profile.Name}.");
                }
            }
            return new(AutomationOutcome.Failed, "The switch didn't change. Check the browser window that opened.");
        });

    /// <summary>Uninstalls the extension from its profile the way a person would: opens its page, clicks "Remove extension",
    /// then confirms the browser's own "Remove 'name'?" question (seen in M4: a small window with Remove / Cancel).
    /// Nothing is blocked, so it can be installed again any time.</summary>
    public static Task<AutomationResult> UninstallAsync(ExtensionInfo extension, CancellationToken ct = default) =>
        RunSafe(async () =>
        {
            var browserName = extension.Profile.Browser.Name;
            var (window, problem) = await OpenInternalPageAsync(extension.Profile, ExtensionPage(extension), ct);
            if (window == null) return problem!;
            var (_, notThere) = await FindExtensionSwitchAsync(window, extension, ct);
            if (notThere != null) return notThere;

            var remove = await WaitForAsync(window, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
                new PropertyCondition(AutomationElement.NameProperty, "Remove extension")), ct);
            if (remove == null)
                return new(AutomationOutcome.NeedsUser, $"There's no “Remove extension” on its page in {extension.Profile.Name} (it may be installed by policy). Check the {browserName} window.");
            ((InvokePattern)remove.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

            // Confirm only a question that names this extension (a small "Remove 'name'?" box inside the window).
            var questionCondition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                new PropertyCondition(AutomationElement.ClassNameProperty, "RootView"));
            var question = await WaitForAsync(window, questionCondition, ct);
            if (question == null || !question.Current.Name.Contains(extension.Name, StringComparison.CurrentCultureIgnoreCase))
                return new(AutomationOutcome.NeedsUser, $"Confirm the removal in the {browserName} window.");

            // Chrome ignores a press that comes the instant the box appears (protection against accidental clicks), and
            // says nothing. So wait a moment, press Remove, and check the box really closed; try again if not.
            bool confirmed = false;
            for (int attempt = 0; attempt < 4 && !confirmed; attempt++)
            {
                await Task.Delay(attempt == 0 ? 800 : 1000, ct);
                var confirm = question.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, "Remove")));
                if (confirm == null) break;
                ((InvokePattern)confirm.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                for (int i = 0; i < 10 && !confirmed; i++)
                {
                    await Task.Delay(150, ct);
                    confirmed = window.FindFirst(TreeScope.Descendants, questionCondition) == null;
                }
            }
            if (!confirmed)
                return new(AutomationOutcome.NeedsUser, $"Press “Remove” in the {browserName} window to finish.");

            // Removed once the page no longer shows the extension's name. After removing, the browser goes back to the full
            // list, which still has switches for the other extensions, so "no switch" isn't a sign (seen Oct 2026).
            var nameShown = new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "name"),
                new PropertyCondition(AutomationElement.NameProperty, extension.Name));
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250, ct);
                if (window.FindFirst(TreeScope.Descendants, nameShown) == null)
                {
                    CloseIfOnlyOurTab(window);
                    return new(AutomationOutcome.Done, $"Removed {extension.Name} from {extension.Profile.Name}.");
                }
            }
            return new(AutomationOutcome.Failed, $"{browserName} didn't confirm the removal. Check its window.");
        });

    /// <summary>Opens the extension's own page in its profile without changing anything.</summary>
    public static Task<AutomationResult> ShowExtensionPageAsync(ExtensionInfo extension, CancellationToken ct = default) =>
        RunSafe(async () =>
        {
            var (window, problem) = await OpenInternalPageAsync(extension.Profile, ExtensionPage(extension), ct);
            return window != null ? new(AutomationOutcome.Done, "Opened the extension's page.") : problem!;
        });

    public static Task<AutomationResult> ShowExtensionsPageAsync(ProfileInfo profile, CancellationToken ct = default) =>
        RunSafe(async () =>
        {
            var (window, problem) = await OpenInternalPageAsync(profile, profile.Browser.Definition.ExtensionsPageUrl, ct);
            return window != null ? new(AutomationOutcome.Done, "Opened the extensions page.") : problem!;
        });

    /// <summary>Makes the browser re-read its policies now ("Reload policies" on its policy page).</summary>
    public static Task<AutomationResult> ReloadPoliciesAsync(ProfileInfo profile, CancellationToken ct = default) =>
        RunSafe(async () =>
        {
            var page = profile.Browser.Definition.ExtensionsPageUrl.Replace("://extensions", "://policy", StringComparison.Ordinal);
            var (window, problem) = await OpenInternalPageAsync(profile, page, ct);
            if (window == null) return problem!;
            var reload = await WaitForAsync(window, new PropertyCondition(AutomationElement.AutomationIdProperty, "reload-policies"), ct);
            if (reload == null) return new(AutomationOutcome.NeedsUser, "Click “Reload policies” on the page that opened.");
            ((InvokePattern)reload.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            await Task.Delay(1500, ct);
            CloseIfOnlyOurTab(window);
            return new(AutomationOutcome.Done, "The browser reloaded its policies.");
        });

    static Task<AutomationResult> RunSafe(Func<Task<AutomationResult>> work) => Task.Run(async () =>
    {
        try
        {
            return await work();
        }
        catch (Exception e) when (e is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException
                                       or COMException or ArgumentException)
        {
            return new AutomationResult(AutomationOutcome.Failed, "The browser window changed while the app was working: " + e.Message);
        }
    });

    static string ExtensionPage(ExtensionInfo extension) =>
        $"{extension.Profile.Browser.Definition.ExtensionsPageUrl}/?id={extension.Id}";

    /// <summary>The page's on/off switch, but only when the page is that extension's details view: exactly one switch
    /// (the list view has one per extension) and the extension's name in the window title or the details header.</summary>
    static async Task<(AutomationElement? Switch, AutomationResult? Problem)> FindExtensionSwitchAsync(AutomationElement window, ExtensionInfo extension, CancellationToken ct)
    {
        var condition = new PropertyCondition(AutomationElement.AutomationIdProperty, "enableToggle");
        if (await WaitForAsync(window, condition, ct) == null)
            return (null, new(AutomationOutcome.NeedsUser, $"The page opened, but its switch wasn't found. Make the change yourself in the {extension.Profile.Browser.Name} window."));
        await Task.Delay(400, ct); // let the page finish rendering before counting switches

        var switches = window.FindAll(TreeScope.Descendants, condition);
        // The details view shows the extension's name as its title (page id "name"); fall back to the window title.
        var title = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "name"));
        bool named = title != null
            ? title.Current.Name.Trim().Equals(extension.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)
            : window.Current.Name.Contains(extension.Name, StringComparison.CurrentCultureIgnoreCase);
        if (switches.Count != 1 || !named)
            return (null, new(AutomationOutcome.NeedsUser, $"The page didn't show {extension.Name}'s details, so nothing was changed. Check the {extension.Profile.Browser.Name} window."));
        return (switches[0], null);
    }

    /// <summary>Opens a new window of the profile and navigates it to an internal page. Returns the window, or why not.</summary>
    static async Task<(AutomationElement? Window, AutomationResult? Problem)> OpenInternalPageAsync(ProfileInfo profile, string url, CancellationToken ct)
    {
        var browser = profile.Browser;
        if (browser.Family != BrowserFamily.Chromium)
        {
            BrowserLauncher.OpenProfile(profile, url.StartsWith("about:", StringComparison.Ordinal) ? url : null);
            return (null, new(AutomationOutcome.NeedsUser, $"{browser.Name} opened. Use its add-ons page (about:addons) to make the change."));
        }
        if (browser.ExePath == null)
            return (null, new(AutomationOutcome.Failed, $"{browser.Name}'s program wasn't found on this PC."));

        var exeDir = Path.GetDirectoryName(browser.ExePath)!;
        var before = BrowserWindows(exeDir).ToHashSet();
        AllowSetForegroundWindow(AsfwAny);
        BrowserLauncher.OpenProfile(profile, newWindow: true);

        // Our window: one that appeared after the launch and still shows an empty new tab (restored session windows
        // and windows the user opens show web addresses instead).
        AutomationElement? window = null, addressBar = null;
        for (int i = 0; i < 50 && window == null; i++)
        {
            await Task.Delay(300, ct);
            foreach (var hwnd in BrowserWindows(exeDir).Where(h => !before.Contains(h)))
            {
                var element = AutomationElement.FromHandle(hwnd);
                var bar = element.FindFirst(TreeScope.Descendants, ClassIs(AddressBarClasses));
                if (bar == null || ((ValuePattern)bar.GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Length > 0) continue;
                window = element;
                addressBar = bar;
                break;
            }
        }
        if (window == null || addressBar == null)
            return (null, CopyAndAsk(url, $"{browser.Name} didn't open a new window in time."));

        // Make sure it's the right profile before touching anything. With one profile there is nothing to mix up (Brave's
        // button just says "You"). Otherwise:
        //  1. The window's taskbar ID names the profile folder ("Chrome.UserData.Profile23"; plain "Chrome" for Default).
        //     It's exact and unaffected by account states, so it decides whenever the browser sets one.
        //  2. Else the profile button, e.g. "Alex (Your Chrome)" or "Omar". A profile that just opened greets with
        //     "Hi, Omar" for a few seconds, and an account problem shows "Paused" or "Verify that it's you" instead
        //     of the name (all seen Oct 2026), so wait for a label that names it.
        if (browser.Profiles.Count > 1)
        {
            bool confirmed = false;
            for (int i = 0; i < 30 && !confirmed; i++)
            {
                var byTaskbarId = TaskbarIdNamesProfile(WindowAppId(new IntPtr(window.Current.NativeWindowHandle)), profile);
                if (byTaskbarId == false)
                    return (null, CopyAndAsk(url, $"The window that opened belongs to another profile, not “{profile.Name}”, so nothing was changed."));
                var label = window.FindFirst(TreeScope.Descendants, ClassIs(AvatarButtonClasses))?.Current.Name;
                confirmed = byTaskbarId == true || (label != null && NamesProfile(label, profile, browser.Profiles));
                if (!confirmed) await Task.Delay(300, ct);
            }
            if (!confirmed)
                return (null, CopyAndAsk(url, $"The window that opened couldn't be confirmed as “{profile.Name}”, so nothing was changed."));
        }

        var value = (ValuePattern)addressBar.GetCurrentPattern(ValuePattern.Pattern);
        value.SetValue(url);
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            SetForegroundWindow(handle);
            addressBar.SetFocus();
            await Task.Delay(250, ct);
            // Safety: press Enter only when the browser's own address bar has the keyboard focus and still holds our address.
            if (GetForegroundWindow() == handle
                && AddressBarClasses.Contains(AutomationElement.FocusedElement?.Current.ClassName)
                && value.Current.Value == url)
            {
                PressEnter();
                await Task.Delay(800, ct);
                return (window, null);
            }
        }
        return (null, CopyAndAsk(url, $"Couldn't bring the {browser.Name} window to the front."));
    }

    /// <summary>True when the profile button's label names <paramref name="profile"/>: "Alex (Your Chrome)", the profile's own
    /// name ("Omar"), or the account's first or full name. Chrome shows "First (Profile)" when the two differ, so a bare
    /// label that is another profile's own name belongs to that profile; and a first or full name that another profile
    /// could also show doesn't count, so two profiles signed in as the same person can't be mixed up.</summary>
    internal static bool NamesProfile(string label, ProfileInfo profile, IEnumerable<ProfileInfo> allProfiles)
    {
        label = label.Trim();
        if (label.Length == 0) return false;
        if (label.EndsWith($"({profile.Name.Trim()})", StringComparison.CurrentCultureIgnoreCase)) return true;
        var others = allProfiles.Where(other => other != profile).ToList();
        if (Same(label, profile.Name)) return !others.Any(o => Same(label, o.Name));
        if (!Same(label, profile.GivenName) && !Same(label, profile.FullName)) return false;
        return !others.Any(o => Same(label, o.Name) || Same(label, o.GivenName) || Same(label, o.FullName));
    }

    static bool Same(string label, string? name) =>
        name != null && label.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>What the profile button says in each open profile of a Chromium browser, keyed by profile folder, e.g.
    /// "Alex" or "Paused". Read-only: it only looks at windows that are already open.</summary>
    public static Dictionary<string, string> ReadProfileButtonLabels(BrowserInfo browser)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (browser.Family != BrowserFamily.Chromium || browser.ExePath == null || browser.Profiles.Count == 0) return labels;
        foreach (var hwnd in BrowserWindows(Path.GetDirectoryName(browser.ExePath)!))
        {
            try
            {
                var appId = WindowAppId(hwnd);
                var profile = browser.Profiles.Count == 1
                    ? browser.Profiles[0]
                    : browser.Profiles.FirstOrDefault(p => TaskbarIdNamesProfile(appId, p) == true);
                if (profile == null || labels.ContainsKey(profile.Path)) continue;
                var label = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, ClassIs(AvatarButtonClasses))?.Current.Name;
                if (!string.IsNullOrWhiteSpace(label)) labels[profile.Path] = label.Trim();
            }
            catch (Exception e) when (e is ElementNotAvailableException or COMException or InvalidOperationException)
            {
                // A window that closed or changed while being read: skip it.
            }
        }
        return labels;
    }

    /// <summary>Whether a window's taskbar ID (AppUserModelID) belongs to <paramref name="profile"/>, or null when it can't
    /// tell. Chromium browsers give each profile its own ID so profiles group separately on the taskbar:
    /// "Chrome.UserData.Profile23" for "User Data\Profile 23" (spaces dropped) and the bare "Chrome" for Default.</summary>
    internal static bool? TaskbarIdNamesProfile(string? appId, ProfileInfo profile)
    {
        if (string.IsNullOrWhiteSpace(appId) || profile.Browser?.DataDir is not { } dataDir || profile.Key.Length == 0) return null;
        static string Compact(string s) => s.Replace(" ", "");
        var marker = "." + Compact(Path.GetFileName(PathUtil.Normalize(dataDir))) + ".";
        int at = appId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return appId[(at + marker.Length)..].Equals(Compact(profile.Key), StringComparison.OrdinalIgnoreCase);
        if (!appId.Contains('.')) return profile.Key.Equals("Default", StringComparison.OrdinalIgnoreCase);
        return null; // some other form: fall back to the profile button
    }

    /// <summary>The window's taskbar ID (System.AppUserModel.ID), or null.</summary>
    static string? WindowAppId(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return null;
        try
        {
            var key = new PropertyKey { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
            var value = new PropVariant();
            if (store.GetValue(ref key, value) != 0) return null;
            try { return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(value); }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Sequential)]
    sealed class PropVariant { public ushort Type; ushort reserved1, reserved2, reserved3; public IntPtr Pointer; public int Extra; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, [Out] PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, [In] PropVariant value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(PropVariant value);

    static AutomationResult CopyAndAsk(string url, string why)
    {
        var copied = TrySetClipboard(url);
        return new(AutomationOutcome.NeedsUser, copied
            ? $"{why} The page address is copied: click the browser's address bar, press Ctrl+V and Enter."
            : $"{why} Open {url} in the browser's address bar.");
    }

    static async Task<AutomationElement?> WaitForAsync(AutomationElement window, Condition condition, CancellationToken ct)
    {
        for (int i = 0; i < 40; i++)
        {
            var found = window.FindFirst(TreeScope.Descendants, condition);
            if (found != null) return found;
            await Task.Delay(250, ct);
        }
        return null;
    }

    static bool IsOn(TogglePattern pattern) => pattern.Current.ToggleState == ToggleState.On;

    /// <summary>Closes the window only when it holds a single tab (the one the app opened).</summary>
    static void CloseIfOnlyOurTab(AutomationElement window)
    {
        try
        {
            var tabs = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            if (tabs.Count != 1) return;
            ((WindowPattern)window.GetCurrentPattern(WindowPattern.Pattern)).Close();
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException) { }
    }

    /// <summary>Visible top-level browser windows whose process runs from <paramref name="exeDir"/>.</summary>
    static List<IntPtr> BrowserWindows(string exeDir)
    {
        var result = new List<IntPtr>();
        var imageByPid = new Dictionary<uint, string?>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != WindowClass) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!imageByPid.TryGetValue(pid, out var image)) imageByPid[pid] = image = ProcessUtil.GetImagePath((int)pid);
            if (image != null && PathUtil.IsUnder(image, exeDir)) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    static void PressEnter()
    {
        const byte VkReturn = 0x0D;
        const uint KeyUp = 0x0002;
        keybd_event(VkReturn, 0, 0, UIntPtr.Zero);
        keybd_event(VkReturn, 0, KeyUp, UIntPtr.Zero);
    }

    static bool TrySetClipboard(string text)
    {
        bool ok = false;
        var thread = new Thread(() =>
        {
            try { System.Windows.Clipboard.SetText(text); ok = true; }
            catch (COMException) { }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(2000) && ok;
    }

    const int AsfwAny = -1;
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
