using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BPM.Core.Chromium;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Actions;

public enum InstallSourceKind { Store, Folder }

/// <summary>What to install: a Chrome Web Store extension (by ID) or an extension folder on this PC (loaded unpacked).</summary>
public sealed record InstallSource(InstallSourceKind Kind, string Name, string? StoreId = null, string? FolderPath = null, string? Version = null)
{
    public string StoreUrl => $"https://chromewebstore.google.com/detail/{StoreId}";
    public string Description => Kind == InstallSourceKind.Store
        ? $"{Name} from the Chrome Web Store"
        : $"{Name}{(Version != null ? $" {Version}" : "")} from a folder on this PC";
}

/// <summary>Works out what to install and where it can go. Store extensions install through each browser's store page;
/// folders through the extensions page's "Load unpacked" (Developer mode). Nothing here touches a browser.</summary>
public static partial class ExtensionInstall
{
    [GeneratedRegex(@"^[a-p]{32}$")]
    private static partial Regex ExtensionId();

    [GeneratedRegex(@"(?:^|[/=])([a-p]{32})(?:[/?#&]|$)")]
    private static partial Regex IdInText();

    /// <summary>A store extension from a Chrome Web Store link (old or new address) or a bare 32-letter ID; null when the
    /// text holds none. <paramref name="knownName"/> names it when the extension is already installed somewhere.</summary>
    public static InstallSource? ParseStore(string? text, Func<string, string?>? knownName = null)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        string? id = ExtensionId().IsMatch(text) ? text : null;
        if (id == null && (text.Contains("chromewebstore.google.com", StringComparison.OrdinalIgnoreCase)
                           || text.Contains("chrome.google.com/webstore", StringComparison.OrdinalIgnoreCase)))
            id = IdInText().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
        return id == null ? null : new InstallSource(InstallSourceKind.Store, knownName?.Invoke(id) ?? id, StoreId: id);
    }

    /// <summary>An unpacked extension folder; throws when there's no readable manifest.json in it.</summary>
    public static InstallSource FromFolder(string folder)
    {
        var manifestFile = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifestFile))
            throw new InvalidDataException("That folder isn't an extension: it has no manifest.json. Pick the folder that contains manifest.json.");
        var manifest = JsonUtil.Load(manifestFile) ?? throw new InvalidDataException("The extension's manifest.json couldn't be read.");
        var locale = manifest.At("default_locale").AsString();
        var name = ChromiumExtensionReader.Localize(manifest.At("name").AsString(), folder, locale);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("The extension's manifest.json has no name.");
        return new InstallSource(InstallSourceKind.Folder, name.Trim(), FolderPath: Path.GetFullPath(folder), Version: manifest.At("version").AsString());
    }

    /// <summary>Unpacks a .crx file (a zip with a signed header) into <paramref name="destination"/> and returns that folder.
    /// Browsers on Windows won't install a .crx from outside a store, but the unpacked folder can be loaded.</summary>
    public static string UnpackCrx(string crxPath, string destination)
    {
        var bytes = File.ReadAllBytes(crxPath);
        if (bytes.Length < 16 || bytes[0] != 'C' || bytes[1] != 'r' || bytes[2] != '2' || bytes[3] != '4')
            throw new InvalidDataException("That file isn't a .crx extension package.");
        uint version = BitConverter.ToUInt32(bytes, 4);
        long zipStart = version switch
        {
            3 => 12L + BitConverter.ToUInt32(bytes, 8),                                   // magic, version, header size, header
            2 => 16L + BitConverter.ToUInt32(bytes, 8) + BitConverter.ToUInt32(bytes, 12), // magic, version, key size, signature size
            _ => throw new InvalidDataException($"This .crx format (version {version}) isn't supported."),
        };
        if (zipStart >= bytes.Length) throw new InvalidDataException("The .crx file is damaged.");

        Directory.CreateDirectory(destination);
        using var zip = new ZipArchive(new MemoryStream(bytes, (int)zipStart, bytes.Length - (int)zipStart), ZipArchiveMode.Read);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the folder
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
        return destination;
    }

    /// <summary>Where the app keeps extensions it loads from folders. Browsers use a loaded folder in place, so the app loads
    /// its own copy: moving or deleting the original then can't break the extension in every profile.</summary>
    public static string LibraryDir => Path.Combine(AppPaths.DataDir, "Extensions");

    /// <summary>Copies an extension folder into the library ("Name 1.2.0", "Name 1.2.0 (2)", …) and returns the copy.</summary>
    public static string CopyToLibrary(InstallSource source)
    {
        if (source.Kind != InstallSourceKind.Folder || source.FolderPath == null) throw new InvalidOperationException("Only folders are copied.");
        if (PathUtil.IsUnder(source.FolderPath, LibraryDir)) return source.FolderPath; // already the app's copy
        var baseName = string.Concat($"{source.Name} {source.Version}".Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var target = Path.Combine(LibraryDir, baseName);
        for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(LibraryDir, $"{baseName} ({n})");
        CopyDirectory(source.FolderPath, target);
        return target;
    }

    static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from))
        {
            if (Path.GetFileName(dir).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }

    /// <summary>Why <paramref name="source"/> can't be installed in <paramref name="profile"/> by the app, or null when it can.</summary>
    public static string? CheckTarget(ProfileInfo profile, InstallSource source)
    {
        var browser = profile.Browser;
        if (browser.Family == BrowserFamily.Firefox)
            return source.Kind == InstallSourceKind.Store
                ? $"{browser.Name} installs from its own add-ons site"
                : $"{browser.Name} can't keep an extension folder loaded";
        if (browser.ExePath == null) return $"{browser.Name} can't be opened by other programs: install it in {browser.Name} itself";
        if (source.Kind == InstallSourceKind.Store && profile.Extensions.Any(e => e.Id == source.StoreId)) return "Already installed";
        return null;
    }
}
