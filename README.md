# Browser Profile Manager

A Windows 10/11 app that finds every browser on a PC, every profile in each browser, and every extension in each
profile, then lets you manage them: turn extensions on or off per profile, remove or block them in all profiles at once,
reset an extension's data, create new profiles, and spot profiles and accounts that need attention.

## Download

Get `BrowserProfileManager-Setup-<version>.exe` from the **Releases** page of this repository and run it. It installs for
your Windows user only (no administrator rights needed) and includes everything it needs, so .NET doesn't have to be
installed. A portable `.zip` is there too.

The app isn't code-signed yet, so Windows may say "Windows protected your PC": click **More info**, then **Run anyway**.
Requires Windows 10 or 11, 64-bit.

## Features

- **Scan**: Chrome (all channels), Edge (all channels), Brave, Opera / Opera GX, Vivaldi, Chromium, Comet, Arc, Cốc Cốc,
  Yandex, Thorium, plus Firefox, LibreWolf, Waterfox, Zen and Floorp (listed; changes are made in the browser).
- **Per profile**: name, account, picture, open/last used, size, extensions with plain-English permissions and a risk level.
- **Actions**: on/off and remove in one profile, and remove from every profile at once (the app drives each browser's
  own page, visibly; nothing is blocked, so it can be reinstalled any time); block / unblock in every profile of a
  browser (machine-wide browser policy, one admin prompt); reset an extension's data (backed up, profile must be
  closed); reinstall from its store; desktop shortcut per profile; create a new, named profile.
- **Health**: profiles whose account needs signing in again ("Paused"), profiles that aren't signed in, profiles nobody
  has used in 3 months, broken browser links left by uninstalled programs, and browser policies (why a browser says
  "Managed by your organization"). When Claude in Chrome is installed, its connection checks appear here too.
- **Sideloads & policies**: which programs push extensions into browsers through the registry, and every browser policy set.
- **Change log** with undo for every change, and **reports** as HTML, CSV or JSON (emails can be masked).

It never reads cookies, passwords, history or sign-in data, never edits the browsers' tamper-protected settings files,
and never sends anything anywhere. See [docs/USER-GUIDE.md](docs/USER-GUIDE.md) for how to use it.

## Build

Requirements: .NET 10 SDK and Inno Setup 6 (`winget install Microsoft.DotNet.SDK.10`, `winget install JRSoftware.InnoSetup`).

```powershell
.\build.ps1                  # tests, self-contained exe, portable zip, installer → artifacts\
.\build.ps1 -Version 1.1.0
```

During development: `dotnet build BrowserProfileManager.slnx` and `dotnet test tests\BPM.Core.Tests`.
The developer command line `src\BPM.Cli\bin\Debug\net10.0-windows\bpm.exe` runs the app's code without the window:
`scan` (text dump, `--json` for everything), `doctor` (connection checks), `newprofile <browser> <name>`, and
`page` / `uninstall <browser> <profile folder> <extension id>` (the app's browser automation).

## Layout

| Path | What |
|---|---|
| `src/BPM.Core` | Scanning, analysis, actions, diagnostics, reports (no UI) |
| `src/BPM.App` | WPF app (WPF-UI, CommunityToolkit.Mvvm) |
| `src/BPM.Cli` | `bpm.exe` developer command line |
| `tests/BPM.Core.Tests` | xUnit tests with fake profile folders |
| `installer/` | Inno Setup script |
| `docs/` | Plan, M1 test findings, user guide |

## How changes are made (and why)

Chromium browsers protect each profile's extension switches with a tamper check, so the app never edits those files.
It changes extensions only through routes the browsers support, all tested on a real Chrome install
(see [docs/M1-FINDINGS.md](docs/M1-FINDINGS.md)):

1. **One profile**: opens a new window of that profile, puts `chrome://extensions/?id=…` in its address bar (browsers refuse
   internal pages from the command line), presses Enter only when the address bar has focus, then flips the switch
   (`enableToggle`) through Windows UI Automation.
2. **All profiles**: the browser's `ExtensionSettings` policy in HKLM (per-user policies are ignored on PCs that aren't
   company-managed), written by a one-off elevated copy of the app that may only touch browser policy and extension keys.
3. **Reset**: moves the extension's storage folders into a backup while the profile is closed (checked via database locks).

## Licence

[MIT](LICENSE).
