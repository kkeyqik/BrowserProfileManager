using System.Windows.Automation;
using BPM.Core.Actions;
using BPM.Core.Automation;
using BPM.Core.Browsers;
using BPM.Core.Journal;
using BPM.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.Services;

/// <summary>Runs every change the user asks for: confirms first, runs one action at a time, records it in the
/// change log with what's needed to undo it, then refreshes the affected browser.</summary>
public sealed partial class ActionService : ObservableObject
{
    public static ActionService Instance { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyText = "";

    /// <summary>Latest scan (set by the main view-model).</summary>
    public Func<ScanResult?> CurrentScan { get; set; } = () => null;

    /// <summary>Re-reads one browser after a change (set by the main view-model).</summary>
    public Func<string, Task> RefreshBrowser { get; set; } = _ => Task.CompletedTask;

    static string AppExe => Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the app's own program file.");

    // ---------- one profile (the browser's own page, auto-clicked) ----------

    public Task SetEnabledHereAsync(ExtensionInfo extension, bool enable) =>
        RunAsync(enable ? $"Turning on {extension.Name}…" : $"Turning off {extension.Name}…", async ct =>
        {
            var result = await BrowserAutomation.SetEnabledAsync(extension, enable, ct);
            Report(result);
            if (result.Succeeded) RefreshUntilSaved(extension, enable);
            if (result.Outcome != AutomationOutcome.Done) return;
            Record(new ChangeRecord
            {
                Kind = ChangeKind.ExtensionSwitch,
                Title = $"Turned {extension.Name} {(enable ? "on" : "off")}",
                Detail = $"{extension.Profile.Browser.Name} · {extension.Profile.Name}",
                BrowserId = extension.Profile.Browser.Id,
                ProfilePath = extension.Profile.Path,
                ExtensionId = extension.Id,
                Undo = { ["enable"] = (!enable).ToString() },
            });
        });

    public async Task RemoveHereAsync(ExtensionInfo extension)
    {
        if (RefuseIfBusy()) return;
        var synced = extension.Profile.Email != null
            ? $"\n\n“{extension.Profile.Name}” is signed in as {extension.Profile.Email}; if it syncs extensions, it's removed on that account's other devices too."
            : "";
        if (!await Dialogs.ConfirmAsync($"Remove {extension.Name} from {extension.Profile.Name}?",
                $"The app opens {extension.Name}'s page in “{extension.Profile.Name}”, clicks “Remove extension” and confirms {extension.Profile.Browser.Name}'s question. You can install it again from its store any time.{synced}",
                "Remove", danger: true)) return;
        await RunAsync($"Removing {extension.Name}…", async ct =>
        {
            var result = await BrowserAutomation.UninstallAsync(extension, ct);
            Report(result);
            if (result.Outcome != AutomationOutcome.Done) return;
            RecordRemoval(extension);
            RefreshUntilSaved(extension, enabled: null);
        });
    }

    /// <summary>Uninstalls an extension from every profile that has it, one profile at a time, through each browser's own
    /// page. Nothing is blocked: it can be installed again from its store whenever you like.</summary>
    public async Task RemoveEverywhereAsync(ExtensionInfo sample)
    {
        if (RefuseIfBusy()) return;
        var plan = RemovalPlanner.Plan(CurrentScan(), sample.Id);
        var skipped = plan.Skipped.Count == 0 ? "" :
            "\n\nSkipped:\n" + string.Join("\n", plan.Skipped.Select(s => $"• {s.Extension.Profile.Name} ({s.Extension.Profile.Browser.Name}): {s.Reason}"));
        if (plan.Targets.Count == 0)
        {
            await Dialogs.InfoAsync($"Nothing to remove", $"No profile has {sample.Name} in a browser the app can remove it from.{skipped}");
            return;
        }
        var where = string.Join("\n", plan.Targets.GroupBy(t => t.Profile.Browser.Name)
            .Select(g => $"• {g.Key}: {string.Join(", ", g.Select(t => t.Profile.Name))}"));
        if (!await Dialogs.ConfirmAsync($"Remove {sample.Name} from {Profiles(plan.Targets.Count)}?",
                $"The app uninstalls it from each profile in turn: it opens the profile for a moment, clicks “Remove extension” and confirms the browser's question. Nothing is blocked, so you can install it again from its store any time.\n\n{where}{skipped}\n\nProfiles that sync extensions may also remove it on that account's other devices. Closed profiles open briefly; a profile set to reopen its last tabs keeps those windows open.",
                "Remove from all", danger: true)) return;

        await RunAsync($"Removing {sample.Name}…", async ct =>
        {
            var problems = new List<string>();
            var removedCopies = new List<ExtensionInfo>();
            try
            {
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    var copy = plan.Targets[i];
                    BusyText = $"Removing {sample.Name} from {copy.Profile.Name} ({i + 1} of {plan.Targets.Count})…";
                    var result = await BrowserAutomation.UninstallAsync(copy, ct);
                    if (result.Outcome == AutomationOutcome.Done)
                    {
                        RecordRemoval(copy);
                        removedCopies.Add(copy);
                    }
                    else problems.Add($"• {copy.Profile.Name} ({copy.Profile.Browser.Name}): {result.Message}");
                }
            }
            finally
            {
                // Also after "Stop": the copies removed so far should disappear from the lists.
                foreach (var browser in removedCopies.GroupBy(c => c.Profile.Browser.Id))
                    RefreshUntil(browser.Key, scan => browser.All(c => Find(scan, c) == null));
            }
            int removed = plan.Targets.Count - problems.Count;
            if (problems.Count == 0)
                Notifier.Success("Removed everywhere", $"{sample.Name} was removed from {Profiles(removed)}.");
            else
                await Dialogs.InfoAsync($"Removed from {removed} of {plan.Targets.Count} profiles",
                    $"These still need a look:\n{string.Join("\n", problems)}");
        });
    }

    void RecordRemoval(ExtensionInfo extension) => Record(new ChangeRecord
    {
        Kind = ChangeKind.ExtensionRemoval,
        Title = $"Removed {extension.Name}",
        Detail = $"{extension.Profile.Browser.Name} · {extension.Profile.Name}" + (extension.StoreUrl != null ? " · reinstall from its store page" : ""),
        BrowserId = extension.Profile.Browser.Id,
        ProfilePath = extension.Profile.Path,
        ExtensionId = extension.Id,
        CanUndo = false,
    });

    static string Profiles(int count) => count == 1 ? "1 profile" : $"{count} profiles";

    public Task ShowPageAsync(ExtensionInfo extension) =>
        RunAsync("Opening the extension's page…", async ct => Report(await BrowserAutomation.ShowExtensionPageAsync(extension, ct), quietOnSuccess: true));

    // ---------- all profiles of a browser (machine-wide policy, admin) ----------

    public async Task SetPolicyAsync(ExtensionInfo extension, string? mode)
    {
        if (RefuseIfBusy()) return;
        var browser = extension.Profile.Browser;
        var definition = browser.Definition;
        // Chrome's channels share one policy key, so a block in "Chrome Beta" also applies to Chrome, Dev and Canary.
        var sharing = ExtensionSettingsPolicy.SharingPolicy(definition);
        var everywhere = sharing.Count > 1 ? $"every profile of {string.Join(", ", sharing.Select(b => b.Name))}" : $"every {browser.Name} profile";
        var (title, effect, button) = mode switch
        {
            ExtensionSettingsPolicy.Blocked => ($"Block {extension.Name} in {everywhere}?",
                $"It's turned off in {everywhere}, for every Windows user on this PC, and can only be turned back on here with “Unblock”.", "Block"),
            _ => ($"Unblock {extension.Name}?",
                $"The app's block is lifted for {everywhere}. Profiles where it was only off because of the block turn it back on; removed copies need reinstalling from the store.", "Unblock"),
        };
        if (!await Dialogs.ConfirmAsync(title,
                $"{effect}\n\nThis uses {browser.Name}'s administrator settings (policy), so Windows asks for administrator permission and {browser.Name} shows “Managed by your organization” while any app policy is set.",
                button, danger: mode != null)) return;

        await RunAsync("Waiting for administrator permission…", async ct =>
        {
            var previous = ExtensionSettingsPolicy.ReadMachine(definition);
            var next = ExtensionSettingsPolicy.Apply(previous, extension.Id, mode);
            if (ExtensionSettingsPolicy.SameJson(previous, next))
            {
                Notifier.Info("Nothing to change", $"{browser.Name}'s policy already says that.");
                return;
            }
            if (!await WriteMachineAsync(definition, next)) return;
            Record(new ChangeRecord
            {
                Kind = ChangeKind.Policy,
                Title = mode == ExtensionSettingsPolicy.Blocked
                    ? $"Blocked {extension.Name} in all {browser.Name} profiles"
                    : $"Unblocked {extension.Name} in {browser.Name}",
                Detail = $"Machine-wide browser policy (ExtensionSettings) for {string.Join(", ", sharing.Select(b => b.Name))}",
                BrowserId = browser.Id,
                ExtensionId = extension.Id,
                // Undo restores only this extension's previous setting, so other extensions' blocks survive.
                Undo = { ["policyKey"] = definition.PolicyKey, ["mode"] = ExtensionSettingsPolicy.ModeFor(previous, extension.Id) },
            });
            await ApplyPoliciesNowAsync(browser, extension.Id, blocked: mode == ExtensionSettingsPolicy.Blocked, ct);
            Notifier.Success("Done", $"{browser.Name}'s policy now says: {mode ?? "no block"} for {extension.Name}.");
        });
    }

    async Task<bool> WriteMachineAsync(BrowserDefinition definition, string? json)
    {
        BusyText = "Waiting for administrator permission…";
        var response = await ElevatedRunner.RunAsync(AppExe, new ElevatedRequest
        {
            Operations = [ExtensionSettingsPolicy.Write(definition, json)],
            RefreshMachinePolicy = true,
        });
        PolicyCache.Clear();
        if (response.Ok) return true;
        if (response.Cancelled) Notifier.Info("Nothing changed", response.Error ?? "Cancelled.");
        else Notifier.Error("Couldn't change the policy", response.Error ?? "Unknown error.");
        return false;
    }

    /// <summary>Browsers re-read registry policies on a timer; when one is running, press its "Reload policies" so the change
    /// applies now. The lists refresh once the browser has saved the new state.</summary>
    async Task ApplyPoliciesNowAsync(BrowserInfo browser, string extensionId, bool blocked, CancellationToken ct)
    {
        var open = browser.Profiles.FirstOrDefault(p => p.IsOpen) ?? (browser.IsRunning ? browser.Profiles.FirstOrDefault(p => p.IsLastUsed) : null);
        if (open != null && browser.Family == BrowserFamily.Chromium)
        {
            BusyText = $"Asking {browser.Name} to apply it…";
            var reload = await BrowserAutomation.ReloadPoliciesAsync(open, ct);
            if (!reload.Succeeded) Notifier.Info("Almost done", $"Restart {browser.Name} to apply the change. {reload.Message}");
        }
        RefreshUntil(browser.Id, scan => scan.Browsers.Where(b => b.Id == browser.Id).SelectMany(b => b.Profiles)
            .SelectMany(p => p.Extensions).Where(e => e.Id == extensionId).All(e => e.BlockedByPolicy == blocked));
    }

    // ---------- data ----------

    public async Task ResetDataAsync(ExtensionInfo extension)
    {
        if (RefuseIfBusy()) return;
        if (ExtensionDataReset.CheckCanReset(extension.Profile, extension) is { } problem)
        {
            await Dialogs.InfoAsync("Can't reset right now", problem);
            return;
        }
        if (!await Dialogs.ConfirmAsync($"Reset {extension.Name}?",
                $"{extension.Name} starts fresh in “{extension.Profile.Name}”: its saved settings and sign-in are cleared. The data is moved to a backup first, so you can undo this from the Change log.\n\nIf the profile syncs extension data, some settings may come back from the account.",
                "Reset", danger: true)) return;

        await RunAsync($"Resetting {extension.Name}…", async ct =>
        {
            var backup = await Task.Run(() => ExtensionDataReset.Reset(extension.Profile, extension), ct);
            Record(new ChangeRecord
            {
                Kind = ChangeKind.ExtensionData,
                Title = $"Reset {extension.Name}'s data",
                Detail = $"{extension.Profile.Browser.Name} · {extension.Profile.Name} · {backup.Items.Count} folder(s) backed up",
                BrowserId = extension.Profile.Browser.Id,
                ProfilePath = extension.Profile.Path,
                ExtensionId = extension.Id,
                Undo = { ["backupDir"] = backup.BackupDir },
            });
            Notifier.Success("Reset done", $"{extension.Name} will start fresh next time “{extension.Profile.Name}” opens.");
        });
    }

    public Task ReinstallAsync(ExtensionInfo extension) => RunAsync("Opening the store page…", _ =>
    {
        if (extension.StoreUrl == null) throw new InvalidOperationException("This extension isn't from a store, so it can't be reinstalled from one.");
        BrowserLauncher.OpenProfile(extension.Profile, extension.StoreUrl);
        Notifier.Info("Store page opened", $"In “{extension.Profile.Name}”: remove it there if needed, then add it again.");
        return Task.CompletedTask;
    });

    // ---------- profiles ----------

    public Task OpenProfileAsync(ProfileInfo profile) => RunAsync($"Opening {profile.Name}…", _ =>
    {
        BrowserLauncher.OpenProfile(profile);
        return Task.CompletedTask;
    });

    public Task ShowExtensionsPageAsync(ProfileInfo profile) =>
        RunAsync("Opening the extensions page…", async ct => Report(await BrowserAutomation.ShowExtensionsPageAsync(profile, ct), quietOnSuccess: true));

    public Task CreateShortcutAsync(ProfileInfo profile) => RunAsync("Creating the shortcut…", _ =>
    {
        var path = BrowserLauncher.CreateDesktopShortcut(profile);
        Record(new ChangeRecord
        {
            Kind = ChangeKind.Shortcut,
            Title = $"Created a desktop shortcut for {profile.Name}",
            Detail = path,
            BrowserId = profile.Browser.Id,
            ProfilePath = profile.Path,
            Undo = { ["path"] = path, ["written"] = File.GetLastWriteTimeUtc(path).Ticks.ToString() },
        });
        Notifier.Success("Shortcut created", Path.GetFileName(path));
        return Task.CompletedTask;
    });

    /// <summary>Creates a new, empty profile with the name the user types, opens it, and shows it in the list once the
    /// browser has registered it. Sign-in is left to the user in the browser.</summary>
    public async Task CreateProfileAsync(BrowserInfo browser)
    {
        if (RefuseIfBusy()) return;
        if (ProfileCreator.CheckCanCreate(browser, "x") is { } unavailable)
        {
            await Dialogs.InfoAsync("Can't create a profile", unavailable);
            return;
        }
        var name = await Dialogs.AskTextAsync($"New {browser.Name} profile",
            $"Type a name for the new profile. It opens in a new {browser.Name} window, empty: no account, extensions or bookmarks. Sign in there if you want to.",
            "e.g. Work", "Create", ProfileCreator.MaxNameLength);
        if (name == null) return;
        if (ProfileCreator.CheckCanCreate(browser, name) is { } problem)
        {
            await Dialogs.InfoAsync("Can't create that profile", problem);
            return;
        }
        if (ProfileCreator.NameInUse(browser, name)
            && !await Dialogs.ConfirmAsync("That name is taken", $"Another {browser.Name} profile is already called “{name}”. Create a second one anyway?", "Create anyway"))
            return;

        await RunAsync($"Creating “{name}”…", _ =>
        {
            // Never reuse a folder this app made before: the profile may have been deleted in the browser since.
            var usedBefore = ChangeJournal.Load()
                .Where(r => r.Kind == ChangeKind.ProfileCreated && r.BrowserId == browser.Id && r.ProfilePath != null)
                .Select(r => Path.GetFileName(r.ProfilePath!));
            var created = ProfileCreator.Create(browser, name, usedBefore);
            Record(new ChangeRecord
            {
                Kind = ChangeKind.ProfileCreated,
                Title = $"Created the {browser.Name} profile “{created.Name}”",
                Detail = $"Folder {created.Key}. To delete it, use the profile menu in {browser.Name}.",
                BrowserId = browser.Id,
                ProfilePath = created.Path,
                CanUndo = false, // deleting a profile deletes its data: left to the browser's own "Delete" with its warning
            });
            Notifier.Success("Profile created", $"“{created.Name}” is opening in {browser.Name}.");
            RefreshUntil(browser.Id, scan => scan.AllProfiles.Any(p => p.Browser.Id == browser.Id && p.Key == created.Key));
            return Task.CompletedTask;
        });
    }

    // ---------- installing extensions ----------

    /// <summary>Opens the "Install an extension" window, optionally with the extension and/or a profile already chosen,
    /// then installs into the profiles picked there.</summary>
    public async Task ShowInstallAsync(InstallSource? source = null, ProfileInfo? preselect = null)
    {
        // The window opens in demo mode too, to show it; installing is refused there like every action.
        if ((!DemoMode.IsOn && RefuseIfBusy()) || CurrentScan() is not { } scan) return;
        var model = new ViewModels.InstallViewModel(scan, source, preselect);
        var window = new Views.InstallDialog(model) { Owner = System.Windows.Application.Current?.MainWindow };
        if (window.ShowDialog() != true || model.Source is not { } chosen || model.SelectedProfiles.Count == 0) return;
        await InstallAsync(chosen, model.SelectedProfiles);
    }

    /// <summary>What to offer for "Install in other profiles…" on an extension: its store listing, or for a developer
    /// (unpacked) extension its folder; null when neither can be installed again.</summary>
    public static InstallSource? SourceOf(ExtensionInfo extension)
    {
        if (extension.StoreUrl?.Contains("chromewebstore.google.com", StringComparison.OrdinalIgnoreCase) == true)
            return ExtensionInstall.ParseStore(extension.Id, _ => extension.Name);
        if (extension.Source == ExtensionSource.Developer && extension.FolderPath is { } folder && Directory.Exists(folder))
        {
            try { return ExtensionInstall.FromFolder(folder); }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { return null; }
        }
        return null;
    }

    /// <summary>Installs one extension in each target profile, one at a time, through the browser's own pages: the store page
    /// for store extensions, "Load unpacked" (turning Developer mode on where needed) for folders.</summary>
    public async Task InstallAsync(InstallSource source, IReadOnlyList<ProfileInfo> targets)
    {
        if (RefuseIfBusy()) return;
        var where = string.Join("\n", targets.GroupBy(t => t.Browser.Name).Select(g => $"• {g.Key}: {string.Join(", ", g.Select(t => t.Name))}"));
        var how = source.Kind == InstallSourceKind.Store
            ? "The app opens its store page in each profile in turn, clicks “Add” and confirms the browser's question."
            : "The app opens each profile's extensions page in turn, turns on Developer mode if it's off, clicks “Load unpacked” and picks a copy of the folder that it keeps in its own data folder. Developer mode has to stay on for the extension to keep working; turning it on is recorded in the Change log, where you can undo it.";
        if (!await Dialogs.ConfirmAsync($"Install {source.Name} in {Profiles(targets.Count)}?",
                $"{how}\n\n{where}\n\nProfiles that sync extensions may also add it on that account's other devices. Closed profiles open briefly.",
                "Install")) return;

        await RunAsync($"Installing {source.Name}…", async ct =>
        {
            var install = source;
            if (source.Kind == InstallSourceKind.Folder)
            {
                BusyText = "Copying the extension folder…";
                var copy = await Task.Run(() => ExtensionInstall.CopyToLibrary(source), ct);
                install = source with { FolderPath = copy };
            }

            var problems = new List<string>();
            var installed = new List<ProfileInfo>();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var profile = targets[i];
                    BusyText = $"Installing {source.Name} in {profile.Name} ({i + 1} of {targets.Count})…";
                    AutomationResult result;
                    if (install.Kind == InstallSourceKind.Store)
                    {
                        result = await BrowserAutomation.InstallFromStoreAsync(profile, install.StoreId!, ct);
                    }
                    else
                    {
                        var loaded = await BrowserAutomation.LoadUnpackedAsync(profile, install.FolderPath!, install.Name, ct);
                        result = loaded.Result;
                        if (loaded.TurnedOnDeveloperMode) RecordDeveloperMode(profile, install.Name);
                    }
                    if (result.Outcome == AutomationOutcome.Done)
                    {
                        installed.Add(profile);
                        Record(new ChangeRecord
                        {
                            Kind = ChangeKind.ExtensionInstall,
                            Title = $"Installed {source.Name}",
                            Detail = $"{profile.Browser.Name} · {profile.Name}" + (install.Kind == InstallSourceKind.Folder ? $" · loaded from {install.FolderPath}" : " · from the Chrome Web Store"),
                            BrowserId = profile.Browser.Id,
                            ProfilePath = profile.Path,
                            ExtensionId = install.StoreId,
                            CanUndo = false, // remove it like any other extension
                        });
                    }
                    else if (result.Outcome != AutomationOutcome.AlreadyInState)
                    {
                        problems.Add($"• {profile.Name} ({profile.Browser.Name}): {result.Message}");
                    }
                }
            }
            finally
            {
                // Also after "Stop": show what was installed so far.
                foreach (var browser in installed.GroupBy(p => p.Browser.Id))
                    RefreshUntil(browser.Key, scan => browser.All(p => scan.AllProfiles.FirstOrDefault(n => n.Browser.Id == p.Browser.Id && n.Path == p.Path) is { } now
                        && now.Extensions.Any(e => install.Kind == InstallSourceKind.Store
                            ? e.Id == install.StoreId
                            : e.FolderPath != null && Core.Util.PathUtil.SamePath(e.FolderPath, install.FolderPath!))));
            }

            if (problems.Count == 0)
                Notifier.Success("Installed", $"{source.Name} is in {Profiles(targets.Count)}.");
            else
                await Dialogs.InfoAsync($"Installed in {targets.Count - problems.Count} of {targets.Count} profiles",
                    $"These still need a look:\n{string.Join("\n", problems)}");
        });
    }

    void RecordDeveloperMode(ProfileInfo profile, string forExtension) => Record(new ChangeRecord
    {
        Kind = ChangeKind.DeveloperMode,
        Title = $"Turned Developer mode on in {profile.Name}",
        Detail = $"{profile.Browser.Name} · {profile.Name} · to load {forExtension}. Undo turns it off, which also turns off extensions loaded from folders.",
        BrowserId = profile.Browser.Id,
        ProfilePath = profile.Path,
        Undo = { ["on"] = "False" },
    });

    // ---------- doctor / clean-up ----------

    /// <summary>Optional tidy-up: keep Claude on in the chosen profile and turn it off in the others. Claude can connect to
    /// every open profile that has it on (it asks which browser to use), so this is a preference, not a repair.</summary>
    public async Task KeepOneClaudeProfileAsync(string browserId)
    {
        if (RefuseIfBusy()) return;
        var on = CurrentScan()?.AllExtensions
            .Where(e => e.Profile.Browser.Id == browserId && Core.Analysis.ProblemFinder.ClaudeIds.Contains(e.Id) && e.Enabled)
            .ToList() ?? [];
        if (on.Count < 2) return;
        var choice = await Dialogs.ChooseAsync("Which profile do you use with Claude?",
            "Claude stays on in the profile you pick and is turned off in the others (the app opens each one's extension page briefly to flip the switch).",
            on.Select(e => e.Profile.Email is { } mail ? $"{e.Profile.Name} · {mail}" : e.Profile.Name).ToList(), "Keep only this one");
        if (choice < 0) return;

        await RunAsync("Turning Claude off in the other profiles…", async ct =>
        {
            var others = on.Where((_, i) => i != choice).ToList();
            try
            {
                foreach (var extension in others)
                {
                    BusyText = $"Turning Claude off in {extension.Profile.Name}…";
                    var result = await BrowserAutomation.SetEnabledAsync(extension, false, ct);
                    if (result.Outcome == AutomationOutcome.Done)
                        Record(new ChangeRecord
                        {
                            Kind = ChangeKind.ExtensionSwitch,
                            Title = $"Turned {extension.Name} off",
                            Detail = $"{extension.Profile.Browser.Name} · {extension.Profile.Name} (kept on in {on[choice].Profile.Name})",
                            BrowserId = browserId,
                            ProfilePath = extension.Profile.Path,
                            ExtensionId = extension.Id,
                            Undo = { ["enable"] = "True" },
                        });
                    else if (!result.Succeeded)
                    {
                        Report(result);
                        return;
                    }
                }
                Notifier.Success("Done", $"Claude is now on only in {on[choice].Profile.Name}. Open its Claude side panel and sign in.");
            }
            finally
            {
                RefreshUntil(browserId, scan => others.All(e => Find(scan, e)?.Enabled != true));
            }
        });
    }

    public async Task RemoveSideloadAsync(Core.Diagnostics.Sideload sideload, string displayName)
    {
        if (RefuseIfBusy()) return;
        if (!await Dialogs.ConfirmAsync($"Remove the sideload entry for {displayName}?",
                $"Programs use this registry entry to push {displayName} into {string.Join(", ", sideload.Browsers)}. Removing it stops new profiles from asking to approve it.\n\nThe browser may also uninstall copies that came from this entry. The program that added it might add it again when it updates.\n\n{sideload.DisplayKey}",
                "Remove entry", danger: true)) return;
        await RunAsync("Removing the sideload entry…", async _ =>
        {
            if (!await DeleteRegistryKeyAsync(sideload.Scope, sideload.Key)) return;
            Record(new ChangeRecord
            {
                Kind = ChangeKind.Sideload,
                Title = $"Removed the sideload entry for {displayName}",
                Detail = sideload.DisplayKey,
                ExtensionId = sideload.ExtensionId,
                Undo = RegistryUndo(sideload.Scope, sideload.Key, sideload.Values),
            });
            Notifier.Success("Removed", $"{displayName} won't be pushed into new profiles any more.");
        });
    }

    public async Task RemoveNativeHostAsync(Core.Diagnostics.NativeHost host)
    {
        if (RefuseIfBusy()) return;
        if (!await Dialogs.ConfirmAsync($"Remove the browser link {host.Name}?",
                $"{host.StatusText}, so it can't work. Removing the leftover registry entry is safe; reinstalling the program that owned it registers it again.\n\n{host.DisplayKey}",
                "Remove link", danger: true)) return;
        await RunAsync("Removing the browser link…", async _ =>
        {
            if (!await DeleteRegistryKeyAsync(host.Scope, host.Key)) return;
            var values = new Dictionary<string, string>();
            if (host.ManifestPath != null) values[""] = host.ManifestPath;
            Record(new ChangeRecord
            {
                Kind = ChangeKind.NativeHost,
                Title = $"Removed the broken browser link {host.Name}",
                Detail = host.DisplayKey,
                Undo = RegistryUndo(host.Scope, host.Key, values),
            });
            Notifier.Success("Removed", host.Name);
        });
    }

    async Task<bool> DeleteRegistryKeyAsync(Core.Diagnostics.RegistryScope scope, string key) =>
        await ApplyRegistryAsync(scope, new RegistryOperation(RegistryOpKind.DeleteKey, key, Wow64_32: scope.IsWow32));

    async Task<bool> ApplyRegistryAsync(Core.Diagnostics.RegistryScope scope, RegistryOperation op)
    {
        if (!scope.IsMachine)
        {
            ElevatedExecutor.ApplyForCurrentUser(op);
            return true;
        }
        BusyText = "Waiting for administrator permission…";
        var response = await ElevatedRunner.RunAsync(AppExe, new ElevatedRequest { Operations = [op] });
        if (response.Ok) return true;
        if (response.Cancelled) Notifier.Info("Nothing changed", response.Error ?? "Cancelled.");
        else Notifier.Error("Couldn't change the registry", response.Error ?? "Unknown error.");
        return false;
    }

    static Dictionary<string, string?> RegistryUndo(Core.Diagnostics.RegistryScope scope, string key, IReadOnlyDictionary<string, string> values)
    {
        var undo = new Dictionary<string, string?>
        {
            ["machine"] = scope.IsMachine.ToString(),
            ["wow32"] = scope.IsWow32.ToString(),
            ["key"] = key,
        };
        foreach (var (name, value) in values) undo["value:" + name] = value;
        return undo;
    }

    async Task<bool> RestoreRegistryKeyAsync(ChangeRecord record)
    {
        var key = record.Undo.GetValueOrDefault("key") ?? throw new InvalidOperationException("The registry key wasn't recorded.");
        var values = record.Undo.Where(kv => kv.Key.StartsWith("value:", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[6..], kv => kv.Value ?? "");
        bool machine = bool.Parse(record.Undo.GetValueOrDefault("machine") ?? "false");
        bool wow32 = bool.Parse(record.Undo.GetValueOrDefault("wow32") ?? "false");
        var scope = machine ? (wow32 ? Core.Diagnostics.RegistryScope.Machine32 : Core.Diagnostics.RegistryScope.Machine) : Core.Diagnostics.RegistryScope.User;
        return await ApplyRegistryAsync(scope, new RegistryOperation(RegistryOpKind.CreateKey, key, Wow64_32: wow32, Values: values));
    }

    // ---------- undo ----------

    public async Task UndoAsync(ChangeRecord record)
    {
        if (!record.CanUndo || record.Undone || RefuseIfBusy()) return;
        if (!await Dialogs.ConfirmAsync("Undo this change?", record.Title, "Undo")) return;

        await RunAsync("Undoing…", async ct =>
        {
            bool undone = false;
            switch (record.Kind)
            {
                case ChangeKind.ExtensionSwitch:
                    var extension = CurrentScan()?.AllExtensions.FirstOrDefault(e =>
                        e.Id == record.ExtensionId && e.Profile.Path == record.ProfilePath && e.Profile.Browser.Id == record.BrowserId);
                    if (extension == null) throw new InvalidOperationException("That extension isn't in the latest scan any more.");
                    bool enable = !bool.TryParse(record.Undo.GetValueOrDefault("enable"), out var parsed) || parsed;
                    var result = await BrowserAutomation.SetEnabledAsync(extension, enable, ct);
                    Report(result);
                    undone = result.Succeeded;
                    if (undone) RefreshUntilSaved(extension, enable);
                    break;
                case ChangeKind.Policy:
                    var definition = BrowserCatalog.Find(record.BrowserId ?? "") ?? throw new InvalidOperationException("Unknown browser.");
                    var id = record.ExtensionId ?? throw new InvalidOperationException("The extension wasn't recorded.");
                    var current = ExtensionSettingsPolicy.ReadMachine(definition);
                    var restored = ExtensionSettingsPolicy.ApplyExact(current, id, record.Undo.GetValueOrDefault("mode"));
                    undone = ExtensionSettingsPolicy.SameJson(current, restored) || await WriteMachineAsync(definition, restored);
                    if (!undone) break;
                    ChangeJournal.MarkUndone(record.Id); // the policy is back: record it before the slower apply step
                    if (CurrentScan()?.Browsers.FirstOrDefault(b => b.Id == definition.Id) is { } browser)
                        await ApplyPoliciesNowAsync(browser, id, blocked: record.Undo.GetValueOrDefault("mode") == ExtensionSettingsPolicy.Blocked, ct);
                    Notifier.Success("Undone", record.Title);
                    return;
                case ChangeKind.ExtensionData:
                    var dir = record.Undo.GetValueOrDefault("backupDir") ?? throw new InvalidOperationException("The backup location wasn't recorded.");
                    await Task.Run(() => ExtensionDataReset.Restore(dir), ct);
                    undone = true;
                    break;
                case ChangeKind.Shortcut:
                    // Delete only the shortcut the app made: skip it if the file was changed or replaced since.
                    if (record.Undo.GetValueOrDefault("path") is { } path && File.Exists(path))
                    {
                        if (record.Undo.GetValueOrDefault("written") != File.GetLastWriteTimeUtc(path).Ticks.ToString())
                            throw new InvalidOperationException($"{Path.GetFileName(path)} was changed after the app made it, so it wasn't deleted. Delete it yourself if you don't need it.");
                        File.Delete(path);
                    }
                    undone = true;
                    break;
                case ChangeKind.DeveloperMode:
                    var devProfile = CurrentScan()?.AllProfiles.FirstOrDefault(p => p.Path == record.ProfilePath && p.Browser.Id == record.BrowserId)
                        ?? throw new InvalidOperationException("That profile isn't in the latest scan any more.");
                    var devResult = await BrowserAutomation.SetDeveloperModeAsync(devProfile, false, ct);
                    Report(devResult);
                    undone = devResult.Succeeded;
                    break;
                case ChangeKind.Sideload or ChangeKind.NativeHost:
                    undone = await RestoreRegistryKeyAsync(record);
                    break;
                default:
                    throw new NotSupportedException("This kind of change can't be undone from here.");
            }
            if (!undone) return;
            ChangeJournal.MarkUndone(record.Id);
            Notifier.Success("Undone", record.Title);
            if (record.BrowserId != null) await RefreshBrowser(record.BrowserId);
        });
    }

    // ---------- plumbing ----------

    CancellationTokenSource? running;

    /// <summary>True (and says so) while another action runs. Called before asking anything, so the user isn't asked
    /// to confirm a change that would then be refused.</summary>
    bool RefuseIfBusy()
    {
        if (DemoMode.IsOn)
        {
            Notifier.Info("Demo mode", "This is made-up data, so actions are turned off. Start the app normally to manage your browsers.");
            return true;
        }
        if (!IsBusy) return false;
        Notifier.Info("One moment", "Another action is still running. Wait for it, or press Stop.");
        return true;
    }

    /// <summary>Stops the running action at its next step (a change already made in a profile stays made).</summary>
    [RelayCommand]
    void Cancel()
    {
        if (running is not { IsCancellationRequested: false } cts) return;
        BusyText = "Stopping…";
        cts.Cancel();
    }

    async Task RunAsync(string busy, Func<CancellationToken, Task> work)
    {
        if (RefuseIfBusy()) return;
        using var cts = running = new CancellationTokenSource();
        IsBusy = true;
        BusyText = busy;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Notifier.Info("Stopped", "The action was stopped. Anything already changed is in the Change log.");
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or InvalidDataException or System.ComponentModel.Win32Exception
                                       or ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            Notifier.Error("That didn't work", e.Message);
        }
        catch (Exception e)
        {
            // Anything unexpected is logged and reported instead of closing the app.
            ErrorLog.Write(e);
            Notifier.Error("Something went wrong", e.Message);
        }
        finally
        {
            running = null;
            IsBusy = false;
            BusyText = "";
        }
    }

    // ---------- refreshing after a change ----------

    /// <summary>The copy of <paramref name="extension"/> in <paramref name="scan"/> (same browser, profile and id), if any.</summary>
    static ExtensionInfo? Find(ScanResult? scan, ExtensionInfo extension) => scan?.AllExtensions.FirstOrDefault(e =>
        e.Id == extension.Id && e.Profile.Path == extension.Profile.Path && e.Profile.Browser.Id == extension.Profile.Browser.Id);

    /// <summary>Refreshes the extension's browser until it shows the new state: on/off (<paramref name="enabled"/>) or gone (null).</summary>
    void RefreshUntilSaved(ExtensionInfo extension, bool? enabled) =>
        RefreshUntil(extension.Profile.Browser.Id, scan => Find(scan, extension) is not { } now ? enabled == null : now.Enabled == enabled);

    /// <summary>Browsers save a change to their settings files a few seconds after it happens (seen in M1: up to ~10 s), so
    /// one re-read right away still shows the old state. This re-reads the browser in the background until
    /// <paramref name="done"/> holds or about 15 s pass; re-reads where nothing changed don't touch the screen.</summary>
    void RefreshUntil(string browserId, Func<ScanResult, bool> done) => _ = RefreshUntilAsync(browserId, done);

    async Task RefreshUntilAsync(string browserId, Func<ScanResult, bool> done)
    {
        try
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                await RefreshBrowser(browserId);
                if (CurrentScan() is { } scan && done(scan)) return;
                await Task.Delay(1500);
            }
        }
        catch (Exception e)
        {
            ErrorLog.Write(e); // a failed background refresh only leaves the lists stale until the next one
        }
    }

    /// <summary>Logs a change. If the log can't be written, the change still happened, so say so instead of failing.</summary>
    static void Record(ChangeRecord record)
    {
        try
        {
            ChangeJournal.Add(record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(e);
            Notifier.Error("Done, but not logged", $"{record.Title}. It won't appear in the Change log: {e.Message}");
        }
    }

    static void Report(AutomationResult result, bool quietOnSuccess = false)
    {
        switch (result.Outcome)
        {
            case AutomationOutcome.Done or AutomationOutcome.AlreadyInState:
                if (!quietOnSuccess) Notifier.Success("Done", result.Message);
                break;
            case AutomationOutcome.NeedsUser:
                Notifier.Info("Over to you", result.Message);
                break;
            default:
                Notifier.Error("Couldn't finish", result.Message);
                break;
        }
    }
}
