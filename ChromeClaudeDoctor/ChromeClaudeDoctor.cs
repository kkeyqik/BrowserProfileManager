// ChromeClaudeDoctor - finds every Chrome profile on this PC, lists its extensions,
// and checks why the Claude in Chrome extension may not be connecting.
// Read-only by default. It never reads cookies, passwords or sign-in tokens.
// Build: csc /r:System.Web.Extensions.dll /r:System.Management.dll ChromeClaudeDoctor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class Doctor
{
    const string ClaudeStoreId = "fcoeoabgfenejglbffodgkkbkcdhcgfn";
    const string StoreUrl = "https://chromewebstore.google.com/detail/fcoeoabgfenejglbffodgkkbkcdhcgfn";

    static readonly HashSet<string> ClaudeIds = new HashSet<string> {
        "fcoeoabgfenejglbffodgkkbkcdhcgfn", "dihbgbndebgnbjfmelmegjepbnkhlgni", "dngcpimnedloihjnnfngkgjoidhnaolf"
    };

    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
    static readonly StringBuilder Log = new StringBuilder();
    static readonly List<string> Problems = new List<string>();
    static readonly List<string> Warnings = new List<string>();
    static readonly List<string> Advice = new List<string>();

    class ClaudeInstall
    {
        public string Id, Channel, ProfileDir, ProfileName, Email, Version, Reasons, UserDataDir;
        public bool Enabled, FilesPresent;
    }

    class Proc { public int Pid; public string Name, Cmd; }

    static readonly List<ClaudeInstall> ClaudeInstalls = new List<ClaudeInstall>();
    static readonly List<string> AnthropicHostOrigins = new List<string>();
    static bool AnthropicHostFound, AnthropicHostHealthy, UserLevelHostsBlocked;
    static List<Proc> Procs = new List<Proc>();
    static string ChromeExe;

    static int Main(string[] args)
    {
        bool noMenu = args.Any(a => a.Equals("--no-menu", StringComparison.OrdinalIgnoreCase));
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.Title = "Chrome Claude Doctor";

        Header("Chrome Claude Doctor");
        Info(string.Format("Computer: {0}   User: {1}   Time: {2}", Environment.MachineName, Environment.UserName, DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        Info(string.Format("Windows: {0}", WindowsVersion()));
        Info("Read-only check. No cookies, passwords or sign-in tokens are read.");

        try { CheckProcessesAndChrome(); } catch (Exception e) { Warn("Could not check running programs: " + e.Message); }
        try { CheckPolicies(); } catch (Exception e) { Warn("Could not read Chrome policies: " + e.Message); }
        try { CheckNativeHosts(); } catch (Exception e) { Warn("Could not read native messaging hosts: " + e.Message); }
        try { CheckProfiles(); } catch (Exception e) { Bad("Could not read Chrome profiles: " + e.Message); }
        Diagnose();

        string saved = SaveReport();
        Console.WriteLine();
        if (saved != null) Info("Report saved to: " + saved);

        if (!noMenu && !Console.IsInputRedirected) Menu();
        return Problems.Count > 0 ? 1 : 0;
    }

    static string WindowsVersion()
    {
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                var product = S(k.GetValue("ProductName")) ?? "Windows";
                int build;
                int.TryParse(S(k.GetValue("CurrentBuildNumber")), out build);
                if (build >= 22000) product = product.Replace("Windows 10", "Windows 11"); // registry still says 10 on 11
                return string.Format("{0} {1} (build {2})", product, S(k.GetValue("DisplayVersion")), build);
            }
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    // ---------- output ----------

    static void Write(string text, ConsoleColor color)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
        Log.AppendLine(text);
    }
    static void Header(string t) { Write("", ConsoleColor.Gray); Write("=== " + t + " ===", ConsoleColor.Cyan); }
    static void Info(string t) { Write("       " + t, ConsoleColor.Gray); }
    static void Ok(string t) { Write("  [OK] " + t, ConsoleColor.Green); }
    static void Warn(string t) { Write("  [!!] " + t, ConsoleColor.Yellow); }
    static void Bad(string t) { Write("  [XX] " + t, ConsoleColor.Red); }

    // ---------- json helpers ----------

    static object LoadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return Json.DeserializeObject(sr.ReadToEnd());
        }
        catch { return null; }
    }
    static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
    static object Get(object o, params string[] path)
    {
        foreach (var p in path)
        {
            var d = D(o);
            if (d == null || !d.ContainsKey(p)) return null;
            o = d[p];
        }
        return o;
    }
    static string S(object o) { return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture); }
    static long? N(object o)
    {
        if (o == null) return null;
        try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return null; }
    }

    // ---------- 1. running programs ----------

    static void CheckProcessesAndChrome()
    {
        Header("Chrome and Claude programs");

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using (var k = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"))
            {
                var p = k == null ? null : S(k.GetValue(""));
                if (!string.IsNullOrEmpty(p) && File.Exists(p)) { ChromeExe = p; break; }
            }
        }
        if (ChromeExe == null)
        {
            foreach (var p in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe") })
                if (File.Exists(p)) { ChromeExe = p; break; }
        }
        if (ChromeExe != null)
            Ok(string.Format("Chrome installed: version {0}  ({1})", FileVersionInfo.GetVersionInfo(ChromeExe).ProductVersion, ChromeExe));
        else
            Bad("Google Chrome (stable) was not found on this PC.");

        using (var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name='chrome.exe' OR Name='chrome-native-host.exe' OR Name='claude.exe'"))
        {
            foreach (ManagementObject mo in searcher.Get())
                Procs.Add(new Proc { Pid = Convert.ToInt32(mo["ProcessId"]), Name = S(mo["Name"]), Cmd = S(mo["CommandLine"]) ?? "" });
        }

        var chromeMain = Procs.Where(p => p.Name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) && !p.Cmd.Contains("--type=")).ToList();
        if (chromeMain.Count > 0)
        {
            Ok("Chrome is running.");
            foreach (var p in chromeMain)
            {
                var prof = ArgValue(p.Cmd, "--profile-directory=");
                if (prof != null) Info("Chrome was started with profile: " + prof);
            }
        }
        else Warn("Chrome is not running right now.");

        var claudeApp = Procs.Where(p => p.Name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (claudeApp.Count > 0) Ok(string.Format("Claude app / Claude Code is running ({0} process(es)).", claudeApp.Count));
        else Warn("The Claude desktop app is not running.");

        var hosts = Procs.Where(p => p.Name.Equals("chrome-native-host.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (hosts.Count > 0)
        {
            foreach (var h in hosts)
            {
                var origin = h.Cmd.Split(' ').FirstOrDefault(a => a.Trim('"').StartsWith("chrome-extension://"));
                Ok("Claude native bridge is running (a local Chrome <-> Claude link is open)" + (origin != null ? " for " + origin.Trim('"') : "") + ".");
            }
        }
        else Info("Claude native bridge (chrome-native-host.exe) is not running. That is normal unless the extension is linked locally.");
    }

    static string ArgValue(string cmd, string flag)
    {
        int i = cmd.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var rest = cmd.Substring(i + flag.Length);
        if (rest.StartsWith("\"")) { int e = rest.IndexOf('"', 1); return e > 0 ? rest.Substring(1, e - 1) : rest.Trim('"'); }
        int sp = rest.IndexOf(' ');
        return (sp > 0 ? rest.Substring(0, sp) : rest).Trim('"');
    }

    // ---------- 2. policies ----------

    static void CheckPolicies()
    {
        Header("Chrome policies (company / admin rules)");
        bool any = false;
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using (var k = hive.OpenSubKey(@"SOFTWARE\Policies\Google\Chrome"))
            {
                if (k == null) continue;
                any = true;
                string where = hive == Registry.LocalMachine ? "machine" : "user";
                Info("Chrome policies are set (" + where + " level).");

                var ulh = k.GetValue("NativeMessagingUserLevelHosts");
                if (ulh != null && N(ulh) == 0)
                {
                    UserLevelHostsBlocked = true;
                    Bad("Policy NativeMessagingUserLevelHosts = 0: Chrome ignores per-user native hosts, which is where Claude registers its bridge.");
                }

                var extBlock = ListPolicy(k, "ExtensionInstallBlocklist");
                var extAllow = ListPolicy(k, "ExtensionInstallAllowlist");
                bool allowClaude = extAllow.Any(v => ClaudeIds.Contains(v));
                if (!allowClaude && (extBlock.Contains("*") || extBlock.Any(v => ClaudeIds.Contains(v))))
                    Bad("Policy ExtensionInstallBlocklist blocks the Claude extension (" + string.Join(", ", extBlock) + ").");

                var nmBlock = ListPolicy(k, "NativeMessagingBlocklist");
                var nmAllow = ListPolicy(k, "NativeMessagingAllowlist");
                bool allowHost = nmAllow.Any(v => v.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase));
                if (!allowHost && (nmBlock.Contains("*") || nmBlock.Any(v => v.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase))))
                    Bad("Policy NativeMessagingBlocklist blocks Claude's native bridge (" + string.Join(", ", nmBlock) + ").");

                var settings = S(k.GetValue("ExtensionSettings"));
                if (!string.IsNullOrEmpty(settings))
                {
                    var parsed = D(TryParse(settings));
                    if (parsed != null)
                        foreach (var key in parsed.Keys.Where(x => x == "*" || ClaudeIds.Contains(x)))
                        {
                            var mode = S(Get(parsed[key], "installation_mode"));
                            if (mode == "blocked" || mode == "removed")
                                Bad(string.Format("Policy ExtensionSettings sets '{0}' to {1}.", key, mode));
                        }
                }
            }
        }
        if (!any) Ok("No Chrome policies set. Nothing is blocking extensions here.");
    }

    static object TryParse(string s) { try { return Json.DeserializeObject(s); } catch { return null; } }

    static List<string> ListPolicy(RegistryKey parent, string name)
    {
        var list = new List<string>();
        using (var k = parent.OpenSubKey(name))
            if (k != null)
                foreach (var v in k.GetValueNames()) list.Add(S(k.GetValue(v)));
        return list;
    }

    // ---------- 3. native messaging hosts ----------

    static void CheckNativeHosts()
    {
        Header("Claude native bridge registration");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var others = new List<string>();
        var roots = new[] {
            new { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, Label = "user" },
            new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Label = "machine" },
            new { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, Label = "machine (32-bit)" } };

        foreach (var r in roots)
        {
            using (var b = RegistryKey.OpenBaseKey(r.Hive, r.View))
            using (var k = b.OpenSubKey(@"SOFTWARE\Google\Chrome\NativeMessagingHosts"))
            {
                if (k == null) continue;
                foreach (var name in k.GetSubKeyNames())
                {
                    using (var sub = k.OpenSubKey(name))
                    {
                        var manifestPath = sub == null ? null : S(sub.GetValue(""));
                        if (!seen.Add(name + "|" + manifestPath)) continue;
                        if (!name.StartsWith("com.anthropic.", StringComparison.OrdinalIgnoreCase)) { others.Add(name); continue; }
                        AnthropicHostFound = true;
                        CheckAnthropicHost(name, manifestPath, r.Label);
                    }
                }
            }
        }

        if (!AnthropicHostFound)
            Bad("No Claude native bridge is registered with Chrome. Opening the Claude desktop app normally registers it.");
        if (UserLevelHostsBlocked && AnthropicHostFound)
            Warn("A policy makes Chrome ignore per-user bridges, so the registration above may be ignored.");
        if (others.Count > 0)
            Info("Other native hosts registered: " + string.Join(", ", others.Distinct()));
    }

    static void CheckAnthropicHost(string name, string manifestPath, string label)
    {
        Info(string.Format("{0}  ({1} level)", name, label));
        if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
        {
            Bad("Its manifest file is missing: " + (manifestPath ?? "(empty)"));
            return;
        }
        Info("Manifest: " + manifestPath);
        var m = LoadJson(manifestPath);
        if (m == null) { Bad("Its manifest file could not be read as JSON."); return; }

        var exe = S(Get(m, "path"));
        if (exe != null && !Path.IsPathRooted(exe)) exe = Path.Combine(Path.GetDirectoryName(manifestPath), exe);
        bool exeOk = exe != null && File.Exists(exe);
        if (exeOk) Ok("Bridge program exists: " + exe);
        else Bad("Bridge program is missing: " + (exe ?? "(no path)"));

        var origins = Get(m, "allowed_origins") as object[];
        if (origins != null)
            foreach (var o in origins)
            {
                var id = S(o).Replace("chrome-extension://", "").Trim('/');
                AnthropicHostOrigins.Add(id);
                ClaudeIds.Add(id);
            }
        Info("Allowed extensions: " + string.Join(", ", AnthropicHostOrigins.Distinct()));
        if (exeOk && AnthropicHostOrigins.Count > 0) AnthropicHostHealthy = true;
    }

    // ---------- 4. profiles and extensions ----------

    static void CheckProfiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var channels = new[] {
            new { Name = "Chrome", Dir = Path.Combine(local, @"Google\Chrome\User Data") },
            new { Name = "Chrome Beta", Dir = Path.Combine(local, @"Google\Chrome Beta\User Data") },
            new { Name = "Chrome Dev", Dir = Path.Combine(local, @"Google\Chrome Dev\User Data") },
            new { Name = "Chrome Canary", Dir = Path.Combine(local, @"Google\Chrome SxS\User Data") } };

        bool anyChannel = false;
        foreach (var ch in channels)
        {
            if (!Directory.Exists(ch.Dir)) continue;
            anyChannel = true;
            CheckChannel(ch.Name, ch.Dir);
        }
        if (!anyChannel) Bad("No Chrome profile data found for this Windows user.");
    }

    static void CheckChannel(string channel, string userData)
    {
        var state = LoadJson(Path.Combine(userData, "Local State"));
        var cache = D(Get(state, "profile", "info_cache")) ?? new Dictionary<string, object>();
        var lastActive = (Get(state, "profile", "last_active_profiles") as object[] ?? new object[0]).Select(S).ToList();
        var lastUsed = S(Get(state, "profile", "last_used"));

        var dirs = new List<string>(cache.Keys);
        foreach (var d in Directory.GetDirectories(userData))
        {
            var n = Path.GetFileName(d);
            if (!dirs.Contains(n) && File.Exists(Path.Combine(d, "Preferences"))) dirs.Add(n);
        }
        dirs = dirs.OrderBy(d => d == "Default" ? -1 : ProfileNumber(d)).ToList();

        Header(string.Format("{0} profiles ({1} found)", channel, dirs.Count));
        Info("Data folder: " + userData);

        foreach (var dir in dirs)
        {
            var entry = cache.ContainsKey(dir) ? cache[dir] : null;
            var name = S(Get(entry, "name")) ?? dir;
            var email = S(Get(entry, "user_name"));
            var tags = new List<string>();
            if (lastActive.Contains(dir)) tags.Add("open");
            if (dir == lastUsed) tags.Add("last used");

            Write("", ConsoleColor.Gray);
            Write(string.Format("  > {0}  [{1}]  {2}{3}", name, dir,
                string.IsNullOrEmpty(email) ? "(not signed in to Chrome)" : email,
                tags.Count > 0 ? "   <" + string.Join(", ", tags) + ">" : ""), ConsoleColor.White);

            ListExtensions(channel, userData, dir, name, email);
        }
    }

    static int ProfileNumber(string dir)
    {
        int n;
        return dir.StartsWith("Profile ") && int.TryParse(dir.Substring(8), out n) ? n : 100000;
    }

    static void ListExtensions(string channel, string userData, string dir, string profileName, string email)
    {
        var profilePath = Path.Combine(userData, dir);
        var settings = new Dictionary<string, Dictionary<string, object>>();
        foreach (var file in new[] { "Preferences", "Secure Preferences" })
        {
            var s = D(Get(LoadJson(Path.Combine(profilePath, file)), "extensions", "settings"));
            if (s == null) continue;
            foreach (var kv in s)
            {
                var v = D(kv.Value);
                if (v == null) continue;
                Dictionary<string, object> merged;
                if (!settings.TryGetValue(kv.Key, out merged)) settings[kv.Key] = merged = new Dictionary<string, object>();
                foreach (var f in v) merged[f.Key] = f.Value;
            }
        }

        int shown = 0;
        bool claudeHere = false;
        foreach (var kv in settings.OrderBy(k => k.Key))
        {
            var id = kv.Key;
            var e = kv.Value;
            var location = N(Get(e, "location")) ?? 0;
            if (location == 5 || location == 10) continue; // built-in Chrome components

            string extDir = null;
            var manifest = Get(e, "manifest");
            if (location == 4 || location == 8) extDir = S(Get(e, "path"));
            if (extDir == null)
            {
                var root = Path.Combine(profilePath, "Extensions", id);
                if (Directory.Exists(root))
                    extDir = Directory.GetDirectories(root).OrderByDescending(Path.GetFileName, new VersionComparer()).FirstOrDefault();
            }
            bool filesPresent = extDir != null && File.Exists(Path.Combine(extDir, "manifest.json"));
            if (filesPresent) manifest = LoadJson(Path.Combine(extDir, "manifest.json")) ?? manifest;
            if (manifest == null && !filesPresent && !ClaudeIds.Contains(id)) continue; // leftover entry for a removed extension

            var name = Localize(S(Get(manifest, "name")) ?? "(unknown name)", extDir, S(Get(manifest, "default_locale")));
            var version = S(Get(manifest, "version")) ?? "?";
            var reasons = DisableReasons(Get(e, "disable_reasons"));
            var st = N(Get(e, "state"));
            bool enabled = reasons.Count == 0 && (st == null || st == 1);
            bool isClaude = ClaudeIds.Contains(id);

            var line = string.Format("      [{0}] {1}  v{2}  ({3}){4}{5}",
                enabled ? "ON " : "OFF", name, version, id,
                LocationLabel(location),
                reasons.Count > 0 ? "  - " + string.Join("; ", reasons) : "");
            if (!filesPresent && location != 4) line += "  - extension files missing";
            Write(line, isClaude ? (enabled && filesPresent ? ConsoleColor.Green : ConsoleColor.Red) : ConsoleColor.Gray);
            shown++;

            if (isClaude)
            {
                claudeHere = true;
                ClaudeInstalls.Add(new ClaudeInstall {
                    Id = id, Channel = channel, ProfileDir = dir, ProfileName = profileName, Email = email, UserDataDir = userData,
                    Version = version, Enabled = enabled, FilesPresent = filesPresent || location == 4,
                    Reasons = string.Join("; ", reasons) });
            }
        }
        if (shown == 0) Info("    (no extensions installed)");
        if (claudeHere) Info("    ^ Claude extension is in this profile");
    }

    class VersionComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            Version va, vb;
            var ok1 = Version.TryParse((a ?? "").Split('_')[0], out va);
            var ok2 = Version.TryParse((b ?? "").Split('_')[0], out vb);
            if (ok1 && ok2) return va.CompareTo(vb);
            return string.CompareOrdinal(a, b);
        }
    }

    static string Localize(string text, string extDir, string defaultLocale)
    {
        if (text == null || !text.StartsWith("__MSG_") || extDir == null) return text;
        var key = text.Substring(6).TrimEnd('_');
        foreach (var loc in new[] { defaultLocale, "en", "en_US" })
        {
            if (string.IsNullOrEmpty(loc)) continue;
            var msgs = D(LoadJson(Path.Combine(extDir, "_locales", loc, "messages.json")));
            if (msgs == null) continue;
            var hit = msgs.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return S(Get(msgs[hit], "message")) ?? text;
        }
        return text;
    }

    static readonly Dictionary<long, string> ReasonNames = new Dictionary<long, string> {
        // Chromium extensions/browser/disable_reason.h (checked Oct 2026).
        { 1 << 0, "turned off by user" }, { 1 << 1, "waiting for new permissions to be approved" },
        { 1 << 2, "reloading" }, { 1 << 3, "unsupported requirement" }, { 1 << 4, "sideload wipeout" },
        { 1 << 5, "turned off on another device (sync)" },
        { 1 << 8, "not verified by Web Store" }, { 1 << 9, "greylisted" }, { 1 << 10, "corrupted - needs repair" },
        { 1 << 11, "remote install" }, { 1 << 13, "external install needs approval" },
        { 1 << 14, "update required by policy" }, { 1 << 15, "parent approval required" },
        { 1 << 16, "blocked by policy" }, { 1 << 19, "reinstalling" }, { 1 << 20, "not on allowlist" },
        { 1 << 21, "not allowed on this device" }, { 1 << 22, "must be published in store (policy)" },
        { 1 << 23, "unsupported manifest version (MV2)" }, { 1 << 24, "unsupported developer extension" },
        { 1 << 25, "unknown reason" }, { 1 << 26, "blocked by cloud policy check" },
        { 1 << 27, "turned off by another extension" } };

    static List<string> DisableReasons(object raw)
    {
        var bits = new List<long>();
        var arr = raw as object[];
        if (arr != null) bits.AddRange(arr.Select(N).Where(x => x.HasValue && x.Value != 0).Select(x => x.Value));
        else
        {
            var n = N(raw) ?? 0;
            for (int i = 0; i < 40; i++) if ((n & (1L << i)) != 0) bits.Add(1L << i);
        }
        return bits.Select(b => ReasonNames.ContainsKey(b) ? ReasonNames[b] : "reason code " + b).ToList();
    }

    static string LocationLabel(long loc)
    {
        switch (loc)
        {
            case 4: return "  [developer/unpacked]";
            case 7: case 9: return "  [installed by policy]";
            case 2: case 3: case 6: return "  [installed by another program]";
            default: return "";
        }
    }

    // ---------- 5. diagnosis ----------

    static void Diagnose()
    {
        Header("Claude in Chrome - diagnosis");
        var enabled = ClaudeInstalls.Where(c => c.Enabled && c.FilesPresent).ToList();

        if (ClaudeInstalls.Count == 0)
            Problems.Add("The Claude extension is not installed in any Chrome profile. Install it from " + StoreUrl);
        else if (enabled.Count == 0)
            Problems.Add("The Claude extension is installed but not working in any profile: " +
                string.Join(" | ", ClaudeInstalls.Select(c => c.ProfileName + ": " + (c.FilesPresent ? (c.Reasons == "" ? "off" : c.Reasons) : "files missing"))));

        if (enabled.Count > 1)
            Advice.Add(string.Format("The Claude extension is ON in {0} profiles ({1}). Claude can connect to each one that is open and signed in, and asks which browser to use; turn it off in profiles you don't use with Claude if you like.",
                enabled.Count, string.Join(", ", enabled.Select(c => c.ProfileName + " [" + c.ProfileDir + "]"))));

        var emails = enabled.Select(c => c.Email).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (emails.Count > 0)
            Advice.Add("Chrome-profile accounts that have Claude enabled: " + string.Join(", ", emails) +
                ". The account signed in INSIDE the Claude side panel must match the Claude desktop app's account (this can differ from the Chrome profile account).");

        if (AnthropicHostOrigins.Count > 0)
            foreach (var c in enabled.Where(c => !AnthropicHostOrigins.Contains(c.Id)))
                Problems.Add(string.Format("The native bridge does not allow the Claude extension in {0} ({1}). Reopen the Claude desktop app so it re-registers the bridge.", c.ProfileName, c.Id));

        if (!AnthropicHostFound) Warnings.Add("No Claude native bridge is registered. Open (or reinstall) the Claude desktop app, then restart Chrome.");
        else if (!AnthropicHostHealthy) Problems.Add("The Claude native bridge registration is broken (missing file). Reopen or reinstall the Claude desktop app.");
        if (UserLevelHostsBlocked) Problems.Add("A Chrome policy blocks per-user native bridges (NativeMessagingUserLevelHosts=0). Ask your admin to allow it.");

        bool chromeRunning = Procs.Any(p => p.Name.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase));
        bool claudeRunning = Procs.Any(p => p.Name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase));
        if (!chromeRunning) Warnings.Add("Chrome is closed. Open the profile that has Claude enabled.");
        if (!claudeRunning) Warnings.Add("The Claude desktop app is not running.");

        Advice.Add("In Chrome: open the profile with Claude ON -> click the Claude icon -> open the side panel -> sign in with the same account as the Claude app.");
        Advice.Add("If it still is not connected: chrome://extensions -> Developer mode -> Update, then fully quit Chrome (all windows) and reopen it.");

        if (Problems.Count == 0 && Warnings.Count == 0) Ok("No blocking problems found on this PC.");
        foreach (var p in Problems) Bad(p);
        foreach (var w in Warnings) Warn(w);
        Write("", ConsoleColor.Gray);
        Write("  Next steps:", ConsoleColor.Cyan);
        foreach (var a in Advice) Info("- " + a);
    }

    // ---------- report + menu ----------

    static string SaveReport()
    {
        var file = string.Format("ChromeClaudeReport_{0}_{1}.txt", Environment.MachineName, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        var header = "Contains Chrome profile names and account emails from this PC. Share it only with people you trust.\r\n";
        foreach (var dir in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetTempPath() })
        {
            try
            {
                var path = Path.Combine(dir, file);
                File.WriteAllText(path, header + Log.ToString(), Encoding.UTF8);
                return path;
            }
            catch { }
        }
        return null;
    }

    static void Menu()
    {
        var targets = ClaudeInstalls.Where(c => c.Channel == "Chrome").ToList();
        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("What next? (nothing changes unless you pick an option)");
            Console.ResetColor();
            if (ChromeExe != null && targets.Count > 0)
            {
                Console.WriteLine("  1  Open Chrome in a profile that has Claude (wakes the extension)");
                Console.WriteLine("  2  Open the Claude extension's settings page in that profile (enable / update / errors)");
            }
            if (ChromeExe != null) Console.WriteLine("  3  Open the Claude extension's Chrome Web Store page in a profile (install / update)");
            Console.WriteLine("  Q  Quit");
            Console.Write("> ");
            var choice = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();
            if (choice == "" || choice == "q") return;

            if ((choice == "1" || choice == "2") && targets.Count > 0)
            {
                var t = PickProfile(targets.Select(c => new KeyValuePair<string, string>(c.ProfileDir, c.ProfileName + "  " + (c.Email ?? "") + (c.Enabled ? "" : "  (Claude OFF)"))).ToList());
                if (t == null) continue;
                var args = "--profile-directory=\"" + t + "\"" + (choice == "2" ? " \"chrome://extensions/?id=" + ClaudeStoreId + "\"" : "");
                Launch(args);
            }
            else if (choice == "3" && ChromeExe != null)
            {
                var state = LoadJson(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Local State"));
                var cache = D(Get(state, "profile", "info_cache")) ?? new Dictionary<string, object>();
                var t = PickProfile(cache.Select(kv => new KeyValuePair<string, string>(kv.Key, (S(Get(kv.Value, "name")) ?? kv.Key) + "  " + (S(Get(kv.Value, "user_name")) ?? ""))).ToList());
                if (t == null) continue;
                Launch("--profile-directory=\"" + t + "\" \"" + StoreUrl + "\"");
            }
        }
    }

    static string PickProfile(List<KeyValuePair<string, string>> items)
    {
        if (items.Count == 0) return null;
        for (int i = 0; i < items.Count; i++) Console.WriteLine(string.Format("   {0,2}  {1}  [{2}]", i + 1, items[i].Value, items[i].Key));
        Console.Write("  Profile number> ");
        int n;
        if (!int.TryParse(Console.ReadLine(), out n) || n < 1 || n > items.Count) { Console.WriteLine("  Cancelled."); return null; }
        return items[n - 1].Key;
    }

    static void Launch(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ChromeExe, args) { UseShellExecute = false });
            Console.WriteLine("  Opened Chrome: " + args);
        }
        catch (Exception e) { Console.WriteLine("  Could not open Chrome: " + e.Message); }
    }
}
