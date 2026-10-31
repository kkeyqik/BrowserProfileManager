# Browser Profile Manager: User Guide

Browser Profile Manager shows you every web browser on this PC, every profile in each browser, and every extension in each profile. You can use it to turn extensions on or off, remove them, and fix common problems.

## 1. Install

1. Double-click `BrowserProfileManager-Setup-1.0.0.exe`.
2. Windows may show "Windows protected your PC." This happens because the app is not signed yet. Click **More info**, then click **Run anyway**.
3. The app installs just for you. You do not need administrator rights.

Prefer not to install? Use the portable `.zip` file. Unzip it and run the app from that folder.

To uninstall, go to **Settings > Apps** in Windows and choose Browser Profile Manager. The uninstaller asks whether to delete your settings, change log and backups. Choose what you want.

## 2. The left side

- **Scan this PC** looks for browsers, profiles and extensions again.
- **Dashboard** shows a summary.
- **Browsers & profiles** shows each browser and its profiles.
- **All extensions** shows every extension once.
- **Health** shows accounts, profiles, browser links and policies that need attention.
- **Sideloads & policies** shows extensions that other programs installed.
- **Change log** shows every change, with undo.
- **Settings** holds the app options.

The app scans when it opens. You can turn this off in Settings.

On narrow windows, the left side shows icons only.

Scanning only reads files. It does not change anything in your browsers.

## 3. Dashboard

The top row shows counters:

- Browsers and profiles, and how many profiles are open.
- Extensions, and how many are turned on.
- High-risk extensions that are turned on.
- Extensions waiting for approval.

**Problems found** lists issues the app noticed. Click **Show** next to an item to go to it.

Below that are browser cards, one for each browser.

Click **Export report...** to save a report. You can save it as a web page, a spreadsheet or JSON. To hide email addresses in reports, go to **Settings** and turn on **Hide email addresses in exported reports**.

## 4. Browsers & profiles

This page shows a list: each browser, with its profiles under it. A green dot means the profile is open now.

Click a profile to see its buttons:

- **Open profile** opens that profile in its browser.
- **Extensions page** opens the browser's extensions page for that profile.
- **Desktop shortcut** puts a shortcut on your desktop that opens this profile.
- **New profile...** creates another, empty profile in the same browser. Type its name and it opens in a new window. Sign in there if you want to. This works for Chrome, Edge, Brave, Opera and the other Chromium browsers. To delete a profile, use the browser's own profile menu.

The extension list shows the extensions in that profile. Use the search box to find one by name. Use the filter to show only:

- All extensions
- On
- Off
- Waiting for approval
- High risk
- Not from a store

Click an extension to open its details panel. It shows:

- **Risk level** and the reason for it.
- **What it can do** lists its permissions in plain English.
- **Details** shows its ID, version, where it came from, its format and its dates.

## 5. Managing an extension

The details panel has five groups of buttons.

### In this profile

- **Turn off** or **Turn on** opens that extension's page in the right profile and flips the switch for you. A browser window appears for a moment. Do not type or click while it works.
- **Open its page** opens the extension's page so you can change it yourself.
- **Remove...** uninstalls it from this profile. The app clicks Remove and answers the browser's "Remove?" question for you.

If a profile syncs its extensions to a Google or Microsoft account, removing an extension also removes it on that account's other devices.

### In every profile

- **Remove from every profile...** uninstalls the extension from every profile that has it, one profile at a time. You also find this button on the **All extensions** page.

Nothing is blocked, and no administrator permission is needed. You can install the extension again from its store whenever you like. Closed profiles open for a moment while the app works. If a profile is set to reopen its last tabs, those windows stay open.

### Block in all <browser> profiles (asks for administrator permission)

- **Block everywhere...** keeps the extension installed but turns it off in all profiles, and stops anyone turning it back on.
- **Unblock...** lifts the block. Profiles where it was off only because of the block turn it back on.

Windows asks for administrator permission first. The block applies to every Windows user on this PC. While a block is set, the browser shows "Managed by your organization."

A blocked extension can only be turned back on with **Unblock** in this app.

### Data

- **Reset data...** clears the extension's saved settings and sign-in. Use it to sign the extension out. Close the profile first. The data is backed up, and you can restore it from the Change log.
- **Reinstall** opens the extension's store page in that profile.

### Quick links

At the bottom of the panel you will find **Copy ID**, **Store page**, **Website** and **Show files**.

## 6. All extensions

This page lists each extension once, even if it is in several profiles. It shows which profiles have it and whether the versions differ.

Use the filters to show:

- All extensions
- In several profiles
- Different versions
- High risk
- Waiting for approval
- Not from a store

## 7. Health

Health lists things worth a look, grouped by topic. Nothing changes until you use a button.

- **Accounts**: profiles whose account needs you to sign in again (the browser's profile button says "Paused" or
  "Verify that it's you"), and profiles that aren't signed in to an account. **Open profile** opens it so you can sign
  in. Only open profiles can be checked for sign-in problems; closed ones are checked the next time they're open.
- **Profiles**: profiles nobody has used in 3 months. **Show** takes you to them. To delete one, use the browser's own
  profile menu.
- **Browser links**: registrations that let an extension talk to a program on this PC, such as a password manager or
  download manager. **Remove link...** removes leftovers from programs you have uninstalled.
- **Policies**: rules set on this PC that make a browser say "Managed by your organization". **Show policies** lists them.
- **Claude in Chrome**: only shown when the Claude extension is installed. It checks that Claude is on in a profile, that
  the Claude app is running, and that its browser link works. If Claude can't use your browser, open the profile you use
  with Claude, open its Claude side panel, and check it's signed in with the same account as the Claude app.

Click **Run checks again** after you change something.

## 8. Sideloads & policies

Some programs install extensions into your browsers without asking each time. Examples are Adobe, NordVPN, IDM, Power Automate and the Claude app. They do this through a part of Windows called the registry.

Those extensions show as **waiting for approval**. Every profile asks you before it uses them.

- **Remove entry...** stops new profiles from asking. The browser may also uninstall copies that came from that entry. The program may add the entry back when it updates.

The **Policies** list shows rules that are set on this PC. **Managed by your organization** means a rule is in place.

On a PC that is not managed by a company, Chrome ignores policies named "You only...".

## 9. Change log and undo

The **Change log** lists every change the app has made.

To undo a change, click **Undo** next to it.

Click **Open backups folder** to see the saved backups.

## 10. Settings

- **Theme**: choose System, Light or Dark.
- **Scan when the app opens**: turn this on or off.
- **Hide email addresses in exported reports**: turn this on or off.
- **App data folder**: click **Open** to see where settings and backups are kept.

## 11. Troubleshooting

**The app could not flip the switch for me.**
The app copies the page address. Click the browser's address bar, press Ctrl+V, then press Enter. Then flip the switch yourself.

**An action is taking too long (for example removing from many profiles).**
Click **Stop** in the bar at the bottom of the window. The app stops after the profile it is working on. Changes already made stay made and are listed in the Change log.

**"Couldn't bring the window to the front."**
Click the app once, then try again.

**A blocked extension's switch is grey.**
Click **Unblock**. Only this app can turn a blocked extension back on.

**Reset says the profile is open.**
Close all windows of that profile. Check the green dot on the Browsers & profiles page. Then try again.

**A policy change is not showing yet.**
Restart the browser.

## 12. Privacy and safety

- Scanning only reads files.
- The app never reads cookies, passwords, browsing history or sign-in data.
- The app never edits your browsers' protected settings files.
- The app never sends anything anywhere.
- The app works offline.
