namespace BPM.Core.Util;

public static class FileLock
{
    /// <summary>True when another process has the file open exclusively or holds a byte-range lock on it
    /// (LevelDB "LOCK" files of an open Chromium profile, Firefox's parent.lock).</summary>
    public static bool IsHeld(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                fs.Lock(0, 1);
                fs.Unlock(0, 1);
                return false;
            }
            catch (IOException)
            {
                return true; // lock violation: a running browser owns it
            }
        }
        catch (IOException)
        {
            return true; // sharing violation
        }
        catch (UnauthorizedAccessException)
        {
            return false; // can't tell; treat as free
        }
    }
}
