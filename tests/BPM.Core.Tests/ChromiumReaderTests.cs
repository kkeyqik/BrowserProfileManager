using System.Text.Json.Nodes;
using BPM.Core.Chromium;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public sealed class ChromiumReaderTests : IDisposable
{
    const string StoreId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string DisabledId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string SideloadedId = "cccccccccccccccccccccccccccccccc";
    const string ComponentId = "dddddddddddddddddddddddddddddddd";
    const string UnpackedId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    const string GhostId = "ffffffffffffffffffffffffffffffff";

    readonly TempDir temp = new();
    readonly string unpackedFolder;
    readonly List<ProfileInfo> profiles;

    public ChromiumReaderTests()
    {
        unpackedFolder = temp.Combine("dev-extension");
        var userData = temp.Combine("User Data");

        temp.WriteFile(@"User Data\Local State", """
            {"profile":{
              "last_used":"Profile 2",
              "last_active_profiles":["Default"],
              "info_cache":{
                "Default":{"name":"Alice","user_name":"alice@example.com","gaia_name":"Alice Example","active_time":1700000000.5},
                "Profile 2":{"name":"Work","user_name":"work@example.com"},
                "System Profile":{"name":"System"}
              }}}
            """);
        temp.WriteFile(@"User Data\System Profile\Preferences", "{}");
        temp.WriteFile(@"User Data\Profile 9\Preferences", "{}");
        temp.WriteFile(@"User Data\Profile 2\Preferences", "{}");

        WriteStoreExtension(userData);
        WriteOtherExtensions(userData);

        profiles = ChromiumProfileReader.Read(TestFixtures.BrowserFor("chrome", userData));
    }

    public void Dispose() => temp.Dispose();

    ProfileInfo Profile(string key) => profiles.Single(p => p.Key == key);
    ExtensionInfo Extension(string id) => Profile("Default").Extensions.Single(e => e.Id == id);

    void WriteStoreExtension(string userData)
    {
        var root = Path.Combine(userData, "Default", "Extensions", StoreId);
        temp.WriteFile(Path.Combine(root, "1.2.9_0", "manifest.json"),
            """{"name":"Old","version":"1.2.9","manifest_version":3}""");
        temp.WriteFile(Path.Combine(root, "1.2.10_0", "manifest.json"), """
            {"name":"__MSG_appName__","description":"Does things","version":"1.2.10","manifest_version":3,
             "default_locale":"en","icons":{"16":"i16.png","48":"i48.png","128":"i128.png"}}
            """);
        temp.WriteFile(Path.Combine(root, "1.2.10_0", "_locales", "en", "messages.json"),
            """{"appName":{"message":"Page Helper"}}""");
        foreach (var icon in new[] { "i16.png", "i48.png", "i128.png" })
            temp.WriteFile(Path.Combine(root, "1.2.10_0", icon), "png");
    }

    void WriteOtherExtensions(string userData)
    {
        temp.WriteFile(@"User Data\Default\Extensions\" + DisabledId + @"\2.0_0\manifest.json",
            """{"name":"Disabled one","version":"2.0"}""");
        temp.WriteFile(@"User Data\Default\Extensions\" + SideloadedId + @"\1.0_0\manifest.json",
            """{"name":"Sideloaded","version":"1.0"}""");
        temp.WriteFile(Path.Combine(unpackedFolder, "manifest.json"), """{"name":"Dev tool","version":"0.1","manifest_version":3}""");

        var preferences = new JsonObject
        {
            ["extensions"] = new JsonObject
            {
                ["settings"] = new JsonObject
                {
                    [StoreId] = new JsonObject
                    {
                        ["location"] = 1,
                        ["state"] = 1,
                        ["from_webstore"] = true,
                        ["first_install_time"] = "13300000000000000",
                        ["granted_permissions"] = new JsonObject
                        {
                            ["api"] = new JsonArray("storage", "tabs"),
                            ["explicit_host"] = new JsonArray("https://example.com/*"),
                        },
                    },
                    [DisabledId] = new JsonObject { ["location"] = 1, ["state"] = 0, ["disable_reasons"] = 1 },
                    [SideloadedId] = new JsonObject { ["location"] = 3, ["state"] = 0, ["disable_reasons"] = new JsonArray(8192) },
                    [ComponentId] = new JsonObject { ["location"] = 5, ["manifest"] = new JsonObject { ["name"] = "Built in", ["version"] = "1" } },
                    [UnpackedId] = new JsonObject { ["location"] = 4, ["path"] = unpackedFolder, ["state"] = 1 },
                    [GhostId] = new JsonObject { ["location"] = 1, ["state"] = 1 },
                },
            },
        };
        temp.WriteJson(@"User Data\Default\Preferences", preferences);

        var secure = new JsonObject
        {
            ["extensions"] = new JsonObject
            {
                ["settings"] = new JsonObject
                {
                    [StoreId] = new JsonObject { ["active_permissions"] = new JsonObject() },
                },
            },
        };
        temp.WriteJson(@"User Data\Default\Secure Preferences", secure);
    }

    [Fact]
    public void Profiles_use_names_emails_and_last_used_from_local_state()
    {
        var alice = Profile("Default");
        Assert.Equal("Alice", alice.Name);
        Assert.Equal("alice@example.com", alice.Email);
        Assert.Equal("Alice Example", alice.FullName);
        Assert.True(alice.WasOpenLastSession);
        Assert.False(alice.IsLastUsed);
        Assert.NotNull(alice.LastActive);

        var work = Profile("Profile 2");
        Assert.Equal("Work", work.Name);
        Assert.True(work.IsLastUsed);
        Assert.False(work.WasOpenLastSession);
    }

    [Fact]
    public void System_profile_is_hidden()
    {
        Assert.DoesNotContain(profiles, p => p.Key == "System Profile");
    }

    [Fact]
    public void Folder_with_preferences_but_no_info_cache_entry_is_still_listed()
    {
        var orphan = Profile("Profile 9");
        Assert.Equal("Profile 9", orphan.Name);
        Assert.Null(orphan.Email);
    }

    [Fact]
    public void Store_extension_uses_newest_version_folder_and_localized_name()
    {
        var ext = Extension(StoreId);
        Assert.Equal("Page Helper", ext.Name);
        Assert.Equal("1.2.10", ext.Version);
        Assert.EndsWith("1.2.10_0", ext.FolderPath);
        Assert.True(ext.Enabled);
        Assert.Equal(ExtensionSource.Store, ext.Source);
        Assert.Equal(3, ext.ManifestVersion);
        Assert.Equal($"https://chromewebstore.google.com/detail/{StoreId}", ext.StoreUrl);
    }

    [Fact]
    public void Icon_prefers_the_smallest_size_of_at_least_32px()
    {
        Assert.EndsWith("i48.png", Extension(StoreId).IconPath);
    }

    [Fact]
    public void Granted_permissions_are_split_into_api_and_hosts()
    {
        var ext = Extension(StoreId);
        Assert.Equal(["storage", "tabs"], ext.ApiPermissions);
        Assert.Equal(["https://example.com/*"], ext.HostPermissions);
    }

    [Fact]
    public void Install_time_is_converted_from_webkit_microseconds()
    {
        Assert.Equal(2022, Extension(StoreId).InstalledAt?.Year);
    }

    [Fact]
    public void User_disabled_extension_explains_why()
    {
        var ext = Extension(DisabledId);
        Assert.False(ext.Enabled);
        Assert.Equal(["turned off by you"], ext.DisableReasons);
    }

    [Fact]
    public void List_form_disable_reasons_flag_extension_awaiting_approval()
    {
        var ext = Extension(SideloadedId);
        Assert.True(ext.NeedsApproval);
        Assert.Contains("added by another program and waiting for your approval", ext.DisableReasons);
        Assert.Equal(ExtensionSource.OtherProgram, ext.Source);
    }

    [Fact]
    public void Component_extensions_are_skipped()
    {
        Assert.DoesNotContain(Profile("Default").Extensions, e => e.Id == ComponentId);
    }

    [Fact]
    public void Unpacked_extension_is_a_developer_extension()
    {
        var ext = Extension(UnpackedId);
        Assert.Equal(ExtensionSource.Developer, ext.Source);
        Assert.Equal(unpackedFolder, ext.FolderPath);
        Assert.Equal("Dev tool", ext.Name);
    }

    [Fact]
    public void Settings_entry_without_files_or_manifest_is_skipped()
    {
        Assert.DoesNotContain(Profile("Default").Extensions, e => e.Id == GhostId);
    }
}
