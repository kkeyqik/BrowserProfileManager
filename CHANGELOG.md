# Changelog

## 1.1.1 (November 2026)

- **Edge and the new Brave extensions page now work** for installing, switching on and off, and removing. Before,
  Edge's Developer mode switch, Remove button and question boxes weren't recognised, and Brave's redesigned page
  (1.98) hid its switches. Tested end to end on Chrome 156, Edge 151 and Brave 1.98.
- Store installs wait for the store's button to be ready instead of giving up while the page loads.
- New profiles never reuse a folder the browser plans to use next, or one the app made before. A profile deleted in
  Chrome left a folder name that Chrome silently refused to open again.
- Licensed under Apache 2.0, with a NOTICE file. The installer and portable zip include both.
- README: fixed garbled characters, and shows how to get past "Windows protected your PC".

## 1.1.0 (October 2026)

- **Install extensions in the profiles you pick**: from a Chrome Web Store link or ID, an extension folder, or a
  `.crx` file. Developer mode is turned on where a folder needs it, logged in the change log with Undo.

## 1.0.1 (October 2026)

- Demo mode (`--demo`) with made-up data for screenshots and videos.
- New README, screenshots and issue templates.

## 1.0.0 (October 2026)

- First release: every browser, profile and extension on the PC in one window. Switch extensions on or off and remove
  them in one profile or every profile, block them by policy, reset their data, create profiles, check profile health,
  see programs that add extensions, export reports, and undo changes from the change log.
