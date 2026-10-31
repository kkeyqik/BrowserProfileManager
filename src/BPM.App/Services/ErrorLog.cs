using BPM.Core.Util;

namespace BPM.App.Services;

/// <summary>Appends unexpected errors to errors.log in the app data folder (for troubleshooting; never sent anywhere).</summary>
public static class ErrorLog
{
    public static void Write(Exception e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(Path.Combine(AppPaths.DataDir, "errors.log"), $"{DateTime.Now:O} {e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Logging must never cause a second failure.
        }
    }
}
