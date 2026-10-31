using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BPM.Core.Util;

public static class ProcessUtil
{
    const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    /// <summary>Full executable path of a process, or null when it can't be read (e.g. elevated or exited).</summary>
    public static string? GetImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Image paths of all running processes whose file name is in <paramref name="exeNames"/> (e.g. "chrome.exe").</summary>
    public static List<string> GetRunningImages(IEnumerable<string> exeNames)
    {
        var result = new List<string>();
        foreach (var name in exeNames.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    if (GetImagePath(p.Id) is { } path) result.Add(path);
                }
            }
        }
        return result;
    }
}
