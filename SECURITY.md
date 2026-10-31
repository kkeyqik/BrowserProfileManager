# Security

Browser Profile Manager reads browser profiles and can change extensions and browser policies, so security problems
matter.

## Reporting a problem

Please don't open a public issue. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/kkeyqik/BrowserProfileManager/security/advisories/new)
(the **Report a vulnerability** button on the Security tab).

Include what an attacker could do, the steps to reproduce it, and the app version (Settings shows it). You'll get a
reply within a week.

## Supported versions

Only the [latest release](https://github.com/kkeyqik/BrowserProfileManager/releases/latest) gets fixes.

## What the app does and doesn't touch

- It never reads cookies, passwords, sign-in tokens or browsing history.
- It never edits the browsers' tamper-protected settings files.
- Machine-wide policy changes run in a separate elevated copy of the app that may only touch browser policy and
  extension registry keys, and only after Windows asks you for permission.
- It sends nothing over the internet. Store installs open the Chrome Web Store in your browser, like you would.
