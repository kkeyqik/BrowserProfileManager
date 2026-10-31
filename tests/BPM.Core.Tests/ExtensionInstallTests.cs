using System.IO.Compression;
using System.Text;
using BPM.Core.Actions;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Tests;

// One test points the app's data folder elsewhere for a moment, so this class mustn't run alongside tests that write
// there (Firefox icons landed in its temp folder and their cleanup failed now and then).
[Collection("AppPaths")]
public sealed class ExtensionInstallTests
{
    const string WhatFont = "jabopobgcpjmedljpbcaablpmlmfcogm";

    [Theory]
    [InlineData("https://chromewebstore.google.com/detail/whatfont/jabopobgcpjmedljpbcaablpmlmfcogm")]
    [InlineData("https://chromewebstore.google.com/detail/jabopobgcpjmedljpbcaablpmlmfcogm?hl=en")]
    [InlineData("https://chrome.google.com/webstore/detail/whatfont/jabopobgcpjmedljpbcaablpmlmfcogm?hl=en-GB")]
    [InlineData("  jabopobgcpjmedljpbcaablpmlmfcogm ")]
    public void Store_links_and_ids_are_recognised(string text)
    {
        var source = ExtensionInstall.ParseStore(text);
        Assert.NotNull(source);
        Assert.Equal(InstallSourceKind.Store, source.Kind);
        Assert.Equal(WhatFont, source.StoreId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://example.com/detail/jabopobgcpjmedljpbcaablpmlmfcogm")] // not a store page
    [InlineData("jabopobgcpjmedljpbcaablpmlmfcogz")] // 'z' can't be in an extension ID
    public void Other_text_is_not_a_store_extension(string text) => Assert.Null(ExtensionInstall.ParseStore(text));

    [Fact]
    public void A_known_extension_gets_its_name()
    {
        var source = ExtensionInstall.ParseStore(WhatFont, id => id == WhatFont ? "WhatFont" : null);
        Assert.Equal("WhatFont", source!.Name);
    }

    [Fact]
    public void A_folder_is_read_from_its_manifest_including_localised_names()
    {
        using var dir = new TempDir();
        dir.WriteFile("ext/manifest.json", """{"manifest_version":3,"name":"__MSG_appName__","version":"1.2.0","default_locale":"en"}""");
        dir.WriteFile("ext/_locales/en/messages.json", """{"appName":{"message":"My Helper"}}""");
        var source = ExtensionInstall.FromFolder(dir.Combine("ext"));
        Assert.Equal(InstallSourceKind.Folder, source.Kind);
        Assert.Equal("My Helper", source.Name);
        Assert.Equal("1.2.0", source.Version);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_refused()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("empty"));
        Assert.Throws<InvalidDataException>(() => ExtensionInstall.FromFolder(dir.Combine("empty")));
    }

    static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        return buffer.ToArray();
    }

    [Fact]
    public void A_crx3_file_unpacks_to_a_loadable_folder()
    {
        using var dir = new TempDir();
        var header = new byte[37];
        var crx = Encoding.ASCII.GetBytes("Cr24").Concat(BitConverter.GetBytes(3u)).Concat(BitConverter.GetBytes((uint)header.Length)).Concat(header)
            .Concat(Zip(("manifest.json", """{"manifest_version":3,"name":"Packed","version":"2.0"}"""), ("js/app.js", "1"))).ToArray();
        File.WriteAllBytes(dir.Combine("packed.crx"), crx);

        var folder = ExtensionInstall.UnpackCrx(dir.Combine("packed.crx"), dir.Combine("out"));
        Assert.True(File.Exists(Path.Combine(folder, "js", "app.js")));
        Assert.Equal("Packed", ExtensionInstall.FromFolder(folder).Name);
    }

    [Fact]
    public void A_crx_never_writes_outside_its_folder()
    {
        using var dir = new TempDir();
        var crx = Encoding.ASCII.GetBytes("Cr24").Concat(BitConverter.GetBytes(3u)).Concat(BitConverter.GetBytes(0u))
            .Concat(Zip(("manifest.json", "{}"), ("../escape.txt", "x"))).ToArray();
        File.WriteAllBytes(dir.Combine("bad.crx"), crx);
        ExtensionInstall.UnpackCrx(dir.Combine("bad.crx"), dir.Combine("out"));
        Assert.False(File.Exists(dir.Combine("escape.txt")));
    }

    [Fact]
    public void A_file_that_isnt_a_crx_is_refused()
    {
        using var dir = new TempDir();
        dir.WriteFile("not.crx", "PK this is a plain zip");
        Assert.Throws<InvalidDataException>(() => ExtensionInstall.UnpackCrx(dir.Combine("not.crx"), dir.Combine("out")));
    }

    [Fact]
    public void Folders_are_copied_into_the_library_under_a_free_name()
    {
        using var dir = new TempDir();
        var old = AppPaths.DataDir;
        AppPaths.UseDataDir(dir.Combine("data"));
        try
        {
            dir.WriteFile("ext/manifest.json", """{"manifest_version":3,"name":"Tool: Pro","version":"1.0"}""");
            dir.WriteFile("ext/.git/HEAD", "ref");
            var source = ExtensionInstall.FromFolder(dir.Combine("ext"));
            var first = ExtensionInstall.CopyToLibrary(source);
            var second = ExtensionInstall.CopyToLibrary(source);
            Assert.Equal("Tool_ Pro 1.0", Path.GetFileName(first));
            Assert.Equal("Tool_ Pro 1.0 (2)", Path.GetFileName(second));
            Assert.True(File.Exists(Path.Combine(first, "manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(first, ".git")));
            Assert.Equal(first, ExtensionInstall.CopyToLibrary(ExtensionInstall.FromFolder(first))); // already the app's copy
        }
        finally
        {
            AppPaths.UseDataDir(old);
        }
    }

    [Fact]
    public void Targets_say_why_they_cant_take_an_extension()
    {
        var store = ExtensionInstall.ParseStore(WhatFont)!;
        var folder = new InstallSource(InstallSourceKind.Folder, "Mine", FolderPath: @"C:\x");

        var chrome = TestFixtures.BrowserFor("chrome", @"C:\chrome");
        var withExe = new BrowserInfo { Definition = chrome.Definition, DataDir = @"C:\chrome", ExePath = @"C:\chrome.exe" };
        var profile = new ProfileInfo { Key = "Default", Path = "p", Name = "Me", Browser = withExe };
        Assert.Null(ExtensionInstall.CheckTarget(profile, store));
        Assert.Null(ExtensionInstall.CheckTarget(profile, folder));

        profile.Extensions.Add(new ExtensionInfo { Id = WhatFont, Name = "WhatFont", Profile = profile });
        Assert.Equal("Already installed", ExtensionInstall.CheckTarget(profile, store));

        var noExe = new ProfileInfo { Key = "Default", Path = "p", Name = "Me", Browser = chrome };
        Assert.NotNull(ExtensionInstall.CheckTarget(noExe, folder));

        var firefox = new ProfileInfo { Key = "x", Path = "f", Name = "default", Browser = TestFixtures.BrowserFor("firefox", @"C:\ff") };
        Assert.NotNull(ExtensionInstall.CheckTarget(firefox, store));
        Assert.NotNull(ExtensionInstall.CheckTarget(firefox, folder));
    }
}
