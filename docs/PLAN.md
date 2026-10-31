# Plan: Browser Profile Manager (working name), a Windows app with a setup file

## Context
Claude in Chrome would not connect because the Claude extension was ON in 3 Chrome profiles at once. That was hard to see: this PC has 33 Chrome profiles plus Edge, Brave, Opera and Comet. The quick `ChromeClaudeDoctor.exe` proved the scanning works. Now we turn it into a proper Windows app with an installer that the team can put on any PC. It will find every browser, show every profile, and let you manage each profile's extensions (on/off, remove, reset), plus clean-up and diagnostic tools.

**Decisions made:**
- **Platforms:** Windows 10/11 only.
- **Users:** you and your team. It ships unsigned at first, and signing can come later.
- **No helper extension:** nothing is installed in any browser.
- **On/Off/Remove** works three ways (details below):
  - all profiles at once through browser policy,
  - opening the exact page for one profile,
  - an experimental auto-click.
- **Project folder:** `G:\Projects\BrowserProfileManager`, a new folder next to ListMe. The name can change before we start.

## The key limitation
Chrome-based browsers lock each profile's extension switches with a tamper check (*Secure Preferences*). If an outside program edits them, the browser resets them, and adware does exactly that, so antivirus flags it. **The app never edits those files.** It changes extensions only through these routes:

| Action | How the app does it | Works when |
|---|---|---|
| Off / On / Remove in **all profiles** of a browser | Browser policy in the registry (`ExtensionSettings`: blocked / allowed / removed) | Browser open or closed. Applies within seconds. No admin needed for your own Windows user. Shows "Managed by your organization" |
| Off / On / Remove in **one profile** | Opens that profile at `chrome://extensions/?id=…` and you click | Always |
| Same, **auto-click** (experimental) | Opens the same page and flips the switch with Windows accessibility automation (UI Automation), visibly on screen | When the browser's page layout matches. Falls back to you clicking |
| **Reset** (clear the extension's saved data, e.g. sign it out) | Moves its data folders into a backup, which you can restore | That profile is closed (checked through its database lock files) |
| **Reinstall** | Remove, then open its Web Store page in that profile | Always |

## Features

### v1: what you asked for
1. **Scan button** (also runs at start-up). It finds every installed browser through Windows' browser list, App Paths, known folders and installed Store apps:
   - Chrome Stable/Beta/Dev/Canary, Edge (all channels), Brave, Opera / Opera GX, Vivaldi, Chromium, Comet, Arc, Coc Coc, Yandex.
   - Firefox and its forks (Zen, LibreWolf, Waterfox) are listed and use the open-page route.
   - For each browser it shows the version, install path and whether it's running.
   - The list refreshes live when a browser changes its files.
2. **Profiles per browser:**
   - Name, avatar, signed-in account, and whether it's open or was last used.
   - Folder, disk size and extension count.
   - Buttons: **Open profile**, **Open extensions page**, **Create desktop shortcut** (opens straight into that profile).
3. **Extensions per profile:**
   - Icon, name, version, ON/OFF with the plain-English reason (e.g. "needs approval", "blocked by policy", "corrupted").
   - Where it came from (Web Store / developer / installed by another program / policy) and a risk badge.
   - Actions: Off, On or Remove (this profile, or all profiles), Reset data, Reinstall, Open in store, Copy ID, Show folder.
   - Details panel: permissions in plain English, Manifest V2/V3, install date, size, update source.
   - Every change goes in a log and can be undone in one click.

### v1: extra features
4. **Dashboard:** browsers, profiles and extensions at a glance, plus a list of "problems found".
5. **All extensions view:** each extension listed once, with every profile it's in. Bulk actions, and spotting the same extension at different versions in different profiles (e.g. Claude 1.0.99 vs 1.0.102).
6. **Risk flags:**
   - Powerful permissions (cookies, all websites, debugger, proxy, native apps, managing other extensions).
   - Developer/unpacked extensions, extensions not from the store, outdated Manifest V2, and extensions waiting for approval.
7. **Sideload sources:** shows which programs push extensions into browsers through the registry. On this PC that's Claude, Adobe Acrobat, NordVPN, IDM and Power Automate, which is why they ask for approval in every profile. Each entry can be removed, with a backup. Machine-wide entries need admin (a UAC prompt).
8. **Connection doctor:**
   - The Claude in Chrome checks from `ChromeClaudeDoctor`, plus a health check of every browser's native-app links (Chrome, Edge, Brave, Opera, Arc…).
   - Fixes: open the right profile, warn when Claude is ON in several profiles, remove links whose program is missing.
9. **Policies view:** every admin policy set on each browser, which explains "Managed by your organization". Policies the app created can be removed, and so can other per-user ones.
10. **Export report** as HTML, CSV or JSON, with a "hide emails" option, for comparing PCs or sharing with the team.
11. **Settings:** light/dark/system theme, auto-scan on start, backup folder, log viewer.

### Later (v2)
- Alerts when a new extension appears since the last scan (tray icon).
- Compare two PCs' reports.
- Copy one profile's extension set to another (the app opens each store page).
- A command-line `bpm.exe --scan --json`.
- Scan all Windows users (admin).
- In-app update check.
- Code signing.
- Auto-click on Firefox's add-ons page.

### What it will never do
- Read cookies, passwords, history or sign-in tokens.
- Edit the browsers' tamper-protected settings.
- Install or turn anything on silently.
- Send any data anywhere (no telemetry). It works fully offline.

## Tech stack
- **C# on .NET 10 (LTS) with WPF**, using:
  - the **WPF-UI** library for a Windows 11 look (Mica, dark/light),
  - **CommunityToolkit.Mvvm** to structure the UI code,
  - **FlaUI** for the auto-click (UI Automation),
  - **System.Text.Json** and **System.Management**.
- **Self-contained single .exe** (about 70 MB), so other PCs need nothing else installed.
- **Inno Setup 6** builds `BrowserProfileManager-Setup-1.0.0.exe`:
  - installs for the current user by default, with no admin needed,
  - adds Start menu and desktop shortcuts and an uninstaller in Apps & features,
  - offers "Launch now" at the end.
  - There is also a portable `.zip`.
- **To install on this PC** (I'll ask before each one): .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`) and Inno Setup 6 (`winget install JRSoftware.InnoSetup`). Git is already installed.
- **Admin-only actions** (machine-wide policies, HKLM sideload entries) run through a one-off elevated copy of the app behind a UAC prompt. The main app runs as a normal user.

## Project layout (`G:\Projects\BrowserProfileManager`)
```
src/BPM.Core/              no UI; all scanning and actions; unit-tested
  Browsers/BrowserCatalog.cs     per browser: exe, data folder, policy key, native-host key, sideload key, extensions URL
  Browsers/BrowserDetector.cs
  Profiles/ChromiumProfileReader.cs, FirefoxProfileReader.cs
  Extensions/ChromiumExtensionReader.cs, FirefoxExtensionReader.cs, RiskAnalyzer.cs
  Actions/PolicyService.cs, GuidedActions.cs, AutoClicker.cs, ExtensionDataReset.cs, SideloadService.cs
  Diagnostics/ConnectionDoctor.cs, PolicyInspector.cs
  Journal/ChangeJournal.cs       every change, its backup and how to undo it
  Reports/ReportExporter.cs
src/BPM.App/               WPF UI: Shell, Dashboard, Browser/Profile tree, Extension table + details, Doctor, Sideloads & Policies, Change log, Settings
tests/BPM.Core.Tests/      xUnit, with fake profile folders (no real emails)
installer/BrowserProfileManager.iss
build.ps1                  test → publish → build setup → zip
```
**Code reused** from `ChromeClaudeDoctor/ChromeClaudeDoctor.cs` (copied into the project when the session moves):
- `CheckChannel` / `ListExtensions` become the Chromium profile and extension readers.
- `DisableReasons` + `ReasonNames`, `Localize`, `VersionComparer` and `LocationLabel` move over as-is.
- `CheckNativeHosts` / `CheckAnthropicHost`, `CheckPolicies` and `Diagnose` become the Connection Doctor.
- `CheckProcessesAndChrome` becomes running-browser detection.

## Main window
```
┌───────────────────────────────────────────────────────────────────────┐
│ Browser Profile Manager                     [⟳ Scan]  [Export]  [⚙]   │
├────────────────┬──────────────────────────────────────────────────────┤
│ Dashboard      │ Chrome ▸ Sam (Profile 3) · sam@example… · ● open │
│ ▾ Chrome   33  │ [Open profile] [Extensions page] [Desktop shortcut]   │
│    Your Chrome │ ┌───┬──────────────────┬─────────┬───────┬─────────┐  │
│    Alex        │ │ ◆ │ Claude           │ 1.0.102 │ ON    │ Medium  │  │
│    Sam   ●     │ │ ◆ │ Get cookies.txt  │ 0.7.2   │ ON    │ High    │  │
│ ▸ Edge      1  │ └───┴──────────────────┴─────────┴───────┴─────────┘  │
│ ▸ Brave     1  │ Claude: [Turn off ▾ this profile / all] [Remove ▾]    │
│ ▸ Opera     1  │         [Reset data] [Reinstall] [Details]            │
│ ▸ Comet     1  │                                                       │
│ All extensions │                                                       │
│ Doctor         │                                                       │
│ Sideloads & policies │                                                 │
│ Change log     │                                                       │
└────────────────┴──────────────────────────────────────────────────────┘
```

## Build order (each step ends with something runnable)
- **M0 Setup:**
  - Move this session into `G:\Projects\BrowserProfileManager` and run `git init`.
  - Install the .NET 10 SDK and Inno Setup (with your OK).
  - Create the solution skeleton and `build.ps1`.
- **M1 Quick tests of the risky parts** (results may adjust features):
  - (a) Do policy block, unblock and remove take effect on this non-company PC in Chrome, Edge and Brave, and how fast?
  - (b) Can auto-click flip the switch on `chrome://extensions/?id=` reliably in Chrome, Edge and Brave?
  - (c) Where do Opera, Comet and Vivaldi keep policies and native-host links, and do they support policies at all? If not, they get the open-page route only.
  - (d) Does the lock-file check reliably tell whether a profile is open?
- **M2 Scanning engine:**
  - Browser catalog and detector, Chromium and Firefox readers, risk analyzer, unit tests.
  - Check results against this PC (33 Chrome profiles, Edge, Brave, Opera, Comet) and against `ChromeClaudeDoctor`'s output.
- **M3 UI:** shell, Scan, browser/profile tree, extension table and details, dashboard, all-extensions view, live refresh, themes.
- **M4 Actions:** open page / store / profile, desktop shortcuts, policy actions with the change log and undo, auto-click, Reset with backup and restore, elevated actions.
- **M5 Doctor and clean-up:** connection doctor, sideload sources, policies view, export reports.
- **M6 Packaging:** app icon, version info, self-contained publish, Inno Setup installer and uninstaller, portable zip, a short user guide.
- **M7 Testing on a fresh machine:**
  - Install on a fresh Windows (Windows Sandbox if you turn it on, or a second PC).
  - Fix what turns up and tag v1.0.

## Testing
- `build.ps1` runs the unit tests (fake profile folders with known extensions, disable reasons and Firefox `extensions.json`), then publishes and builds the setup.
- **Scan accuracy:** compare the app with `chrome://extensions` in 3 real profiles (Default, Profile 1, Profile 3), and with Edge, Brave, Opera and Comet.
- **Actions on a throwaway profile:** create a "BPM Test" Chrome profile with one harmless extension. Test there:
  - policy off/on/remove for all profiles, then undo,
  - auto-click and the open-page fallback,
  - Reset with backup, then Restore,
  - the desktop shortcut,
  - that every change appears in the change log and undoes cleanly.
- **Install/uninstall** on a fresh Windows: Start menu entry, it launches without .NET installed, and the uninstall removes everything except backups the user chooses to keep.
- **Defender** scan of the setup file before sharing it with the team.

## Risks and how we handle them
- **SmartScreen and antivirus:** an unsigned app that reads browser folders can look suspicious.
  - The app never opens cookie or password files and doesn't obfuscate its code.
  - The team clicks "More info → Run anyway" once per PC.
  - Signing can be added later (an OV certificate or Azure Trusted Signing, about $100–400/year, depending on what's available in your country).
- **Policy side effects:**
  - "Managed by your organization" appears in the browser.
  - A blocked extension can only be turned back on from the app.
  - Both are shown in the confirmation dialog.
- **Chrome Sync:** removing an extension in a synced profile also removes it on that Google account's other devices. Synced extension data may come back after a reset. The confirmation dialogs say so.
- **Auto-click may break** when Chrome redesigns its page. It is labelled experimental and always falls back to you clicking.
- **The Claude app may re-add** its registry sideload entry each time it starts. The doctor explains this instead of fighting it.
