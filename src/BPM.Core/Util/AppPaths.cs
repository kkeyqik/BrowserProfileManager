namespace BPM.Core.Util;

/// <summary>Where the app keeps its own files (settings, change log, backups, icon cache).</summary>
public static class AppPaths
{
    public static string DataDir { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrowserProfileManager");

    /// <summary>Points the app's own files somewhere else (demo mode keeps them apart from the real ones).</summary>
    public static void UseDataDir(string dir) => DataDir = dir;

    public static string BackupsDir => Path.Combine(DataDir, "Backups");
    public static string JournalFile => Path.Combine(DataDir, "changes.jsonl");
    public static string IconCacheDir => Path.Combine(DataDir, "IconCache");
}
