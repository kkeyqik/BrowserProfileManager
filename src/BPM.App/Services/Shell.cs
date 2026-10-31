using System.Diagnostics;
using System.Windows;

namespace BPM.App.Services;

/// <summary>Small Windows shell helpers used by buttons (open link, show in Explorer, copy).</summary>
public static class Shell
{
    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Notifier.Error("Couldn't open the link", e.Message); }
    }

    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
        else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        else Notifier.Error("Folder not found", path);
    }

    public static void Copy(string text, string confirmation)
    {
        try
        {
            Clipboard.SetText(text);
            Notifier.Info(confirmation, text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Notifier.Error("Couldn't use the clipboard", "Another program is using it. Try again.");
        }
    }
}
