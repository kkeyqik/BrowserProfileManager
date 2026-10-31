# M1 findings: tests of the risky parts (29 Oct 2026, Chrome 156, Windows 11, PC not company-managed)

All tests ran on the developer's real Chrome with the harmless WhatFont extension, with the owner's consent, and were
undone afterwards.

## (a) Policy route
- A per-user policy (`HKCU\Software\Policies\Google\Chrome\ExtensionSettings` = blocked) was **ignored**:
  `chrome://policy` kept saying "No policies set", even after "Reload policies".
- The same policy machine-wide (`HKLM\SOFTWARE\Policies\Google\Chrome`, written from an elevated process) **worked**:
  the extension's switch turned Off and grey, Preferences recorded disable reason 65536 (blocked by policy).
  After removing it and reloading policies, the switch was On and usable again.
- Chrome re-reads registry policies on a timer, on a Group Policy refresh, or via "Reload policies". The app asks
  Windows for a machine policy refresh and also clicks "Reload policies" in a running profile.

## (b) Auto-click route
- Chrome refuses `chrome://` pages on the command line (opens a blank New Tab instead).
- Works: open the profile, set the address bar (UIA class `OmniboxViewViews`, ValuePattern) to
  `chrome://extensions/?id=<id>`, give it focus, check the foreground window and focused element, press Enter.
- The window's profile can be confirmed from the avatar button (class `AvatarToolbarButton`, e.g. "Alex (Your Chrome)").
- The extension switch has AutomationId `enableToggle` (language-independent); Developer mode is `devMode`;
  "Reload policies" is `reload-policies`; "Remove extension" is a Hyperlink with that name.
- Toggling worked both ways; Chrome saved the change to Preferences within ~10 s. The switch reports its new
  state a moment late, so the app waits for the change.

## (c) Where browsers keep their settings (read from their own binaries)
| Browser | Policy key | Sideload key | Native-messaging keys |
|---|---|---|---|
| Chrome | `SOFTWARE\Policies\Google\Chrome` | `Software\Google\Chrome\Extensions` | `SOFTWARE\Google\Chrome\NativeMessagingHosts` |
| Edge | `SOFTWARE\Policies\Microsoft\Edge` | `Software\Microsoft\Edge\Extensions` | Edge's, then Chrome's and Chromium's |
| Brave | `SOFTWARE\Policies\BraveSoftware\Brave` | Chrome's | Chrome's, Chromium's |
| Opera | `SOFTWARE\Policies\OperaSoftware\Opera` | Chrome's | Chrome's, Chromium's |
| Comet | `SOFTWARE\Policies\Perplexity\Comet` | `Software\Comet\Browser\Extensions` | Chrome's |

## (d) Is a profile open?
- An open Chrome profile holds LevelDB `LOCK` files (Sync Data, Local Storage, Session Storage, shared_proto_db,
  Extension State, Site Characteristics Database, GCM Store); a closed one holds none. Detected by opening the file
  with ReadWrite + sharing and trying a 1-byte lock.
