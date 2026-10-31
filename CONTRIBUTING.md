# Contributing

Thanks for helping make Browser Profile Manager better.

## Reporting a problem

Open an [issue](https://github.com/kkeyqik/BrowserProfileManager/issues/new/choose) and pick **Bug report**. Say which
browser and version you used, what you clicked, what you expected and what happened. A screenshot helps. Hide email
addresses and profile names you don't want to share.

Security problems go through [SECURITY.md](SECURITY.md) instead, not a public issue.

## Suggesting a feature

Open an issue and pick **Feature request**. Describe the problem you want solved, not only the button you'd like.

## Changing the code

1. Install the .NET 10 SDK and, for the installer, Inno Setup 6:
   `winget install Microsoft.DotNet.SDK.10` and `winget install JRSoftware.InnoSetup`.
2. Fork the repository, make a branch, and open `BrowserProfileManager.slnx`.
3. Run the tests before you send a change: `dotnet test tests/BPM.Core.Tests`.
4. Open a pull request that says what changed and why, and how you checked it.

Some ground rules the app keeps:

- **Never edit a browser's tamper-protected files** (`Secure Preferences`). Change extensions only through the
  browser's own pages, its policies, or its command line.
- **Anything that changes a browser is undoable** and goes into the change log, or says clearly why it can't be undone.
- **Browser automation checks before it acts:** the right profile, the right extension, one switch. If anything looks
  different, it stops and asks the user rather than guessing.
- **Plain language** in everything the user sees.

Browser pages change between versions. If automation stops working in a browser, the quickest fix is usually to
compare the page's UI Automation tree (names, ids, control types) with what `src/BPM.Core/Automation` expects. Note the
browser version you checked in a comment.

## Licence

By contributing, you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), like the rest
of the project.
