using BPM.Core.Util;

namespace BPM.Core.Browsers;

/// <summary>Tells whether a profile is open by checking database lock files (proven in M1: an open Chrome profile
/// holds these locks, a closed one doesn't).</summary>
public static class ProfileLockProbe
{
    static readonly string[] ChromiumLocks =
    [
        @"Sync Data\LevelDB\LOCK",
        @"Local Storage\leveldb\LOCK",
        @"Session Storage\LOCK",
        @"shared_proto_db\LOCK",
        @"Extension State\LOCK",
        @"Site Characteristics Database\LOCK",
        @"GCM Store\LOCK",
    ];

    public static bool IsChromiumProfileOpen(string profilePath) =>
        ChromiumLocks.Any(relative => FileLock.IsHeld(Path.Combine(profilePath, relative)));

    public static bool IsFirefoxProfileOpen(string profilePath) =>
        FileLock.IsHeld(Path.Combine(profilePath, "parent.lock"));

    /// <summary>True when any LevelDB LOCK file below <paramref name="folder"/> is held by a running browser.</summary>
    public static bool AnyLockHeld(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        try
        {
            return Directory.EnumerateFiles(folder, "LOCK", SearchOption.AllDirectories).Any(FileLock.IsHeld);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true; // can't inspect it: assume it's in use
        }
    }
}
