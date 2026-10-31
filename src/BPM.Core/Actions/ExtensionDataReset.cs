using System.Text.Json;
using BPM.Core.Browsers;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Actions;

/// <summary>What a reset moved aside, kept as backup.json next to the data. It is written before anything moves and
/// updated after every folder, so a crash midway never leaves data without a record.</summary>
public sealed record DataBackup(
    string BackupDir,
    string BrowserId,
    string ProfilePath,
    string ExtensionId,
    string ExtensionName,
    List<string> Items,
    DateTime CreatedAt);

/// <summary>"Reset" = give an extension a fresh start (e.g. signed out) by moving its saved data into a backup.
/// Only while the profile is closed, so the browser can't be writing the files.</summary>
public static class ExtensionDataReset
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Per-extension storage folders inside a Chromium profile (chrome.storage, IndexedDB, storage partition).</summary>
    public static IReadOnlyList<string> DataFolders(string extensionId) =>
    [
        $@"Local Extension Settings\{extensionId}",
        $@"Sync Extension Settings\{extensionId}",
        $@"IndexedDB\chrome-extension_{extensionId}_0.indexeddb.leveldb",
        $@"IndexedDB\chrome-extension_{extensionId}_0.indexeddb.blob",
        $@"Storage\ext\{extensionId}",
    ];

    public static List<string> ExistingItems(string profilePath, string extensionId) =>
        DataFolders(extensionId).Where(rel => Directory.Exists(Path.Combine(profilePath, rel))).ToList();

    /// <summary>Why a reset can't run right now, or null when it can.</summary>
    public static string? CheckCanReset(ProfileInfo profile, ExtensionInfo extension)
    {
        if (profile.Browser.Family != BrowserFamily.Chromium)
            return "Resetting extension data is available for Chrome, Edge, Brave, Opera and other Chromium browsers.";
        if (ProfileLockProbe.IsChromiumProfileOpen(profile.Path))
            return $"“{profile.Name}” is open. Close all of its {profile.Browser.Name} windows, then try again.";
        if (ExistingItems(profile.Path, extension.Id).Count == 0)
            return $"{extension.Name} has no saved data in this profile.";
        return null;
    }

    public static DataBackup Reset(ProfileInfo profile, ExtensionInfo extension)
    {
        if (CheckCanReset(profile, extension) is { } problem) throw new InvalidOperationException(problem);
        var items = ExistingItems(profile.Path, extension.Id);
        if (items.Any(rel => ProfileLockProbe.AnyLockHeld(Path.Combine(profile.Path, rel))))
            throw new InvalidOperationException($"{profile.Browser.Name} is still using this data. Close the profile and try again.");

        var dir = Path.Combine(AppPaths.BackupsDir,
            $"{DateTime.Now:yyyyMMdd-HHmmss}_{Safe(profile.Browser.Id)}_{Safe(profile.Key)}_{extension.Id}");
        var backup = new DataBackup(dir, profile.Browser.Id, profile.Path, extension.Id, extension.Name, [], DateTime.Now);
        Directory.CreateDirectory(dir);
        Save(backup);

        foreach (var rel in items)
        {
            try
            {
                MoveDirectory(Path.Combine(profile.Path, rel), Path.Combine(dir, "data", rel));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                var stillInBackup = RollBack(backup);
                throw new InvalidOperationException(stillInBackup.Count == 0
                    ? $"Couldn't move the extension's data, so nothing was changed: {e.Message}"
                    : $"Couldn't move all of the extension's data ({e.Message}). Some of it is in {dir}; restore it from the Change log.", e);
            }
            backup.Items.Add(rel);
            Save(backup);
        }
        return backup;
    }

    /// <summary>Puts backed-up data back. Safe to run again after a partial failure: items already restored are skipped.
    /// Data the extension created since the reset is moved into the backup's "replaced" folder.</summary>
    public static void Restore(string backupDir)
    {
        var backup = Load(backupDir);
        if (ProfileLockProbe.IsChromiumProfileOpen(backup.ProfilePath))
            throw new InvalidOperationException("That profile is open. Close all of its windows, then try again.");

        foreach (var rel in backup.Items)
        {
            var source = Path.Combine(backupDir, "data", rel);
            if (!Directory.Exists(source)) continue; // restored earlier
            var target = Path.Combine(backup.ProfilePath, rel);
            if (Directory.Exists(target)) MoveDirectory(target, UniqueReplacedPath(backupDir, rel));
            MoveDirectory(source, target);
        }
        File.WriteAllText(Path.Combine(backupDir, "restored.txt"), DateTime.Now.ToString("O"));
    }

    /// <summary>Moves what was already backed up back into the profile; returns the items it couldn't move back.</summary>
    static List<string> RollBack(DataBackup backup)
    {
        var left = new List<string>();
        foreach (var rel in backup.Items)
        {
            try
            {
                MoveDirectory(Path.Combine(backup.BackupDir, "data", rel), Path.Combine(backup.ProfilePath, rel));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                left.Add(rel);
            }
        }
        backup.Items.Clear();
        backup.Items.AddRange(left);
        Save(backup);
        return left;
    }

    static void Save(DataBackup backup) =>
        File.WriteAllText(Path.Combine(backup.BackupDir, "backup.json"), JsonSerializer.Serialize(backup, Indented));

    static DataBackup Load(string backupDir)
    {
        try
        {
            return JsonSerializer.Deserialize<DataBackup>(File.ReadAllText(Path.Combine(backupDir, "backup.json")))
                ?? throw new InvalidDataException("The backup description is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The backup description is damaged: " + e.Message, e);
        }
    }

    static string UniqueReplacedPath(string backupDir, string rel)
    {
        var path = Path.Combine(backupDir, "replaced", rel);
        return Directory.Exists(path) ? Path.Combine(backupDir, $"replaced-{DateTime.Now:yyyyMMdd-HHmmss}", rel) : path;
    }

    /// <summary>Same-volume rename, or copy-then-delete across volumes. Once the copy is complete the data counts as moved
    /// even if the original can't be fully deleted (the copy is the safe one).</summary>
    static void MoveDirectory(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, destination);
            return;
        }
        CopyDirectory(source, destination);
        try { Directory.Delete(source, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    static string Safe(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));
}
