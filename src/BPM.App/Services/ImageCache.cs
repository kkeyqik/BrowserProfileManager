using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BPM.App.Services;

/// <summary>Loads extension icons, profile pictures and program icons once and shares them (frozen, so any thread can use them).</summary>
public static class ImageCache
{
    static readonly ConcurrentDictionary<string, ImageSource?> Images = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A small bitmap from a PNG/JPG/ICO file; null when the file is missing or unreadable.</summary>
    public static ImageSource? FromFile(string? path, int decodeWidth = 64)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        // The file's write time is part of the key, so a changed picture (e.g. a new profile photo) is loaded again.
        long written;
        try { written = File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        return Images.GetOrAdd($"{decodeWidth}|{written}|{path}", _ =>
        {
            try
            {
                // Read through a stream rather than a Uri: paths with '#' or '%' would be misread as a Uri.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // read it all now, so the stream (and browser file) can close
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        });
    }

    /// <summary>The icon Windows shows for a program (e.g. chrome.exe).</summary>
    public static ImageSource? FromProgram(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        return Images.GetOrAdd("exe|" + exePath, _ =>
        {
            var info = new ShFileInfo();
            var result = SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        });
    }

    const uint ShgfiIcon = 0x100;
    const uint ShgfiLargeIcon = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr icon);
}
