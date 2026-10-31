using System.IO.Compression;
using System.Text.Json.Nodes;
using BPM.Core.Firefox;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public sealed class FirefoxReaderTests : IDisposable
{
    readonly TempDir temp = new();
    readonly List<ProfileInfo> profiles;
    readonly string xpiPath;

    public FirefoxReaderTests()
    {
        var dataDir = temp.Combine("Firefox");
        temp.WriteFile(@"Firefox\profiles.ini", """
            [Install4F96D1932A9F858E]
            Default=Profiles/work.dev-edition
            Locked=1

            ; comment
            [Profile1]
            Name=work
            IsRelative=1
            Path=Profiles/work.dev-edition

            [Profile0]
            Name=default-release
            IsRelative=1
            Path=Profiles/abc.default-release
            Default=1

            [Profile2]
            Name=gone
            IsRelative=1
            Path=Profiles/missing

            [General]
            StartWithLastProfile=1
            """);

        var main = Path.Combine(dataDir, "Profiles", "abc.default-release");
        Directory.CreateDirectory(Path.Combine(dataDir, "Profiles", "work.dev-edition"));
        temp.WriteFile(@"Firefox\Profiles\abc.default-release\prefs.js", """
            // Mozilla User Preferences
            user_pref("browser.startup.homepage", "about:home");
            user_pref("services.sync.username", "alice@example.com");
            """);

        xpiPath = Path.Combine(main, "extensions", "{amo-addon}.xpi");
        WriteXpi(xpiPath);
        var tempAddonFolder = temp.Combine("temp-addon");
        Directory.CreateDirectory(tempAddonFolder);

        temp.WriteJson(@"Firefox\Profiles\abc.default-release\extensions.json", new JsonObject
        {
            ["addons"] = new JsonArray(
                Addon("{amo-addon}", "extension", "app-profile", active: true, name: "Page Helper", path: xpiPath, extra: a =>
                {
                    a["sourceURI"] = "https://addons.mozilla.org/firefox/downloads/file/1/x.xpi";
                    a["version"] = "3.1";
                    a["manifestVersion"] = 2;
                    a["installDate"] = 1700000000000;
                    a["defaultLocale"]!["description"] = "Helps";
                    a["defaultLocale"]!["homepageURL"] = "https://example.com";
                    a["userPermissions"] = new JsonObject
                    {
                        ["permissions"] = new JsonArray("storage", "tabs"),
                        ["origins"] = new JsonArray("https://example.com/*", "HTTPS://EXAMPLE.COM/*"),
                    };
                }),
                Addon("off@example.com", "extension", "app-profile", active: false, name: "Quiet one", extra: a =>
                {
                    a["userDisabled"] = true;
                    a["signedState"] = 2;
                }),
                Addon("dark@example.com", "theme", "app-profile", active: true, name: "Dark theme"),
                Addon("langpack-de@example.com", "locale", "app-profile", active: true, name: "Deutsch"),
                Addon("builtin@example.com", "extension", "app-builtin", active: true, name: "Built in"),
                Addon("dev@example.com", "extension", "temporary-install", active: true, name: "Dev add-on", path: tempAddonFolder))
        });

        profiles = FirefoxProfileReader.Read(TestFixtures.BrowserFor("firefox", dataDir));
    }

    public void Dispose()
    {
        if (profiles.SelectMany(p => p.Extensions).Select(e => e.IconPath).FirstOrDefault(p => p != null) is { } icon)
            File.Delete(icon);
        temp.Dispose();
    }

    static JsonObject Addon(string id, string type, string location, bool active, string name, string? path = null, Action<JsonObject>? extra = null)
    {
        var addon = new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["location"] = location,
            ["active"] = active,
            ["version"] = "1.0",
            ["defaultLocale"] = new JsonObject { ["name"] = name },
        };
        if (path != null) addon["path"] = path;
        extra?.Invoke(addon);
        return addon;
    }

    static void WriteXpi(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write("""{"name":"x", /* comment */ "icons":{"16":"icons/16.png","48":"icons/48.png"}}""");
        using var icon = zip.CreateEntry("icons/48.png").Open();
        icon.Write([0x89, 0x50, 0x4E, 0x47]);
    }

    ProfileInfo Profile(string name) => profiles.Single(p => p.Name == name);
    ExtensionInfo Extension(string id) => profiles.SelectMany(p => p.Extensions).Single(e => e.Id == id);

    [Fact]
    public void Lists_only_profiles_whose_folder_exists()
    {
        Assert.Equal(["default-release", "work"], profiles.Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void Profile_key_is_the_path_as_written_in_profiles_ini()
    {
        var profile = Profile("default-release");
        Assert.Equal("Profiles/abc.default-release", profile.Key);
        Assert.EndsWith(@"Profiles\abc.default-release", profile.Path);
    }

    [Fact]
    public void Profile_marked_default_or_chosen_by_install_section_is_default()
    {
        Assert.True(Profile("default-release").IsDefault);
        Assert.True(Profile("default-release").IsLastUsed);
        Assert.True(Profile("work").IsDefault);
    }

    [Fact]
    public void Sync_account_comes_from_prefs_js()
    {
        Assert.Equal("alice@example.com", Profile("default-release").Email);
        Assert.Null(Profile("work").Email);
        Assert.NotNull(Profile("default-release").LastActive);
    }

    [Fact]
    public void Extensions_include_add_ons_and_themes_but_not_locales_or_built_ins()
    {
        Assert.Equal(["Dark theme", "Dev add-on", "Page Helper", "Quiet one"],
            Profile("default-release").Extensions.Select(e => e.Name).ToArray());
        Assert.Equal(ExtensionKind.Theme, Extension("dark@example.com").Kind);
    }

    [Fact]
    public void Store_extension_has_details_permissions_and_amo_link()
    {
        var ext = Extension("{amo-addon}");
        Assert.True(ext.Enabled);
        Assert.Equal(ExtensionSource.Store, ext.Source);
        Assert.Equal("3.1", ext.Version);
        Assert.Equal(2, ext.ManifestVersion);
        Assert.Equal("Helps", ext.Description);
        Assert.Equal("https://example.com", ext.HomepageUrl);
        Assert.Equal(["storage", "tabs"], ext.ApiPermissions);
        Assert.Single(ext.HostPermissions);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).LocalDateTime, ext.InstalledAt);
        Assert.Equal("https://addons.mozilla.org/firefox/addon/%7Bamo-addon%7D/", ext.StoreUrl);
        Assert.True(ext.FilesPresent);
        Assert.Equal(0, ext.DisableReasonBits);
    }

    [Fact]
    public void User_disabled_extension_explains_why()
    {
        var ext = Extension("off@example.com");
        Assert.False(ext.Enabled);
        Assert.Equal(["turned off by you"], ext.DisableReasons);
        Assert.Equal(ExtensionSource.Store, ext.Source);
    }

    [Fact]
    public void Temporary_install_is_a_developer_extension_without_a_store_link()
    {
        var ext = Extension("dev@example.com");
        Assert.Equal(ExtensionSource.Developer, ext.Source);
        Assert.Null(ext.StoreUrl);
        Assert.True(ext.FilesPresent);
    }

    [Fact]
    public void Icon_is_extracted_from_the_xpi_into_the_cache()
    {
        var icon = Extension("{amo-addon}").IconPath;
        Assert.NotNull(icon);
        Assert.True(File.Exists(icon));
        Assert.EndsWith(".png", icon);
        Assert.Equal(4, new FileInfo(icon).Length);
    }
}
