using System.Text.Json.Nodes;
using BPM.Core.Actions;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Tests;

public sealed class ProfileCreatorTests
{
    static BrowserInfo Chrome(TempDir dir)
    {
        var browser = TestFixtures.BrowserFor("chrome", dir.Root);
        return new BrowserInfo { Definition = browser.Definition, DataDir = dir.Root, ExePath = @"C:\chrome.exe" };
    }

    [Fact]
    public void Next_folder_is_past_existing_folders_and_local_state()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Default"));
        Directory.CreateDirectory(dir.Combine("Profile 3"));
        dir.WriteJson("Local State", JsonNode.Parse("""{"profile":{"info_cache":{"Default":{},"Profile 7":{}},"profiles_created":5}}""")!);
        Assert.Equal("Profile 8", ProfileCreator.NextFolderName(dir.Root));
    }

    [Fact]
    public void Next_folder_keeps_ahead_of_the_browsers_own_counter()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Profile 2"));
        dir.WriteJson("Local State", JsonNode.Parse("""{"profile":{"profiles_created":12}}""")!);
        Assert.Equal("Profile 12", ProfileCreator.NextFolderName(dir.Root));
    }

    [Fact]
    public void Next_folder_with_nothing_yet_is_profile_1()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Default"));
        Assert.Equal("Profile 1", ProfileCreator.NextFolderName(dir.Root));
    }

    [Fact]
    public void Prepare_writes_only_a_starter_preferences_file_with_the_name()
    {
        using var dir = new TempDir();
        var created = ProfileCreator.Prepare(Chrome(dir), "  BPM Test  ");
        Assert.Equal("Profile 1", created.Key);
        Assert.Equal("BPM Test", created.Name);
        Assert.Equal(["Preferences"], Directory.GetFiles(created.Path).Select(Path.GetFileName));
        var prefs = JsonUtil.Load(Path.Combine(created.Path, "Preferences"));
        Assert.Equal("BPM Test", prefs.At("profile", "name").AsString());
        Assert.False(prefs.At("profile", "using_default_name").AsBool());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\there")]
    public void Bad_names_are_refused(string name)
    {
        using var dir = new TempDir();
        Assert.NotNull(ProfileCreator.CheckCanCreate(Chrome(dir), name));
        Assert.Throws<InvalidOperationException>(() => ProfileCreator.Prepare(Chrome(dir), name));
    }

    [Fact]
    public void Firefox_and_missing_programs_are_refused()
    {
        using var dir = new TempDir();
        Assert.NotNull(ProfileCreator.CheckCanCreate(TestFixtures.BrowserFor("firefox", dir.Root), "Work"));
        Assert.NotNull(ProfileCreator.CheckCanCreate(TestFixtures.BrowserFor("chrome", dir.Root), "Work")); // no ExePath
    }

    [Fact]
    public void Name_in_use_ignores_case_and_spaces()
    {
        using var dir = new TempDir();
        var browser = Chrome(dir);
        browser.Profiles.Add(new ProfileInfo { Key = "Default", Path = "d", Name = "Work", Browser = browser });
        Assert.True(ProfileCreator.NameInUse(browser, " work "));
        Assert.False(ProfileCreator.NameInUse(browser, "Home"));
    }
}
