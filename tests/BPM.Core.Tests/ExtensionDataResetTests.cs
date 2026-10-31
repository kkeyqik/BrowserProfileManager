using BPM.Core.Actions;
using BPM.Core.Models;
using BPM.Core.Util;

namespace BPM.Core.Tests;

[Collection("AppPaths")]
public class ExtensionDataResetTests : AppDataTestBase
{
    const string Id = "abcdefghijklmnopabcdefghijklmnop";
    const string LogRel = @"Local Extension Settings\" + Id + @"\000003.log";
    const string IdbRel = @"IndexedDB\chrome-extension_" + Id + @"_0.indexeddb.leveldb\CURRENT";
    const string SyncRel = @"Sync Extension Settings\" + Id + @"\x";

    readonly string userData;
    readonly string profilePath;
    readonly ExtensionInfo extension = new() { Id = Id, Name = "Test extension" };

    public ExtensionDataResetTests()
    {
        userData = Temp.Combine("User Data");
        profilePath = Path.Combine(userData, "Profile 1");
        Directory.CreateDirectory(profilePath);
    }

    ProfileInfo ChromiumProfile() => new()
    {
        Browser = TestFixtures.BrowserFor("chrome", userData),
        Key = "Profile 1", Path = profilePath, Name = "Work",
    };

    void WriteExtensionData()
    {
        File.WriteAllText(Write(LogRel), "log-data");
        File.WriteAllText(Write(IdbRel), "idb-data");
        File.WriteAllText(Write(SyncRel), "sync-data");
    }

    string Write(string relative)
    {
        var full = Path.Combine(profilePath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    [Fact]
    public void CheckCanReset_is_null_for_a_closed_profile_with_data()
    {
        WriteExtensionData();

        Assert.Null(ExtensionDataReset.CheckCanReset(ChromiumProfile(), extension));
    }

    [Fact]
    public void CheckCanReset_explains_when_there_is_no_data()
    {
        var problem = ExtensionDataReset.CheckCanReset(ChromiumProfile(), extension);

        Assert.NotNull(problem);
        Assert.Contains("no saved data", problem);
    }

    [Fact]
    public void CheckCanReset_refuses_firefox_profiles()
    {
        var profile = new ProfileInfo
        {
            Browser = TestFixtures.BrowserFor("firefox", userData),
            Key = "Profiles/abc.work", Path = profilePath, Name = "work",
        };
        WriteExtensionData();

        var problem = ExtensionDataReset.CheckCanReset(profile, extension);

        Assert.NotNull(problem);
        Assert.Contains("Chromium", problem);
    }

    [Fact]
    public void Reset_moves_the_data_into_a_backup_and_describes_it()
    {
        WriteExtensionData();

        var backup = ExtensionDataReset.Reset(ChromiumProfile(), extension);

        Assert.StartsWith(AppPaths.BackupsDir, backup.BackupDir);
        Assert.Equal(3, backup.Items.Count);
        Assert.False(Directory.Exists(Path.Combine(profilePath, "Local Extension Settings", Id)));
        Assert.False(Directory.Exists(Path.Combine(profilePath, "IndexedDB", $"chrome-extension_{Id}_0.indexeddb.leveldb")));
        Assert.False(Directory.Exists(Path.Combine(profilePath, "Sync Extension Settings", Id)));
        Assert.Equal("log-data", File.ReadAllText(Path.Combine(backup.BackupDir, "data", LogRel)));
        Assert.Equal("idb-data", File.ReadAllText(Path.Combine(backup.BackupDir, "data", IdbRel)));
        Assert.Equal("sync-data", File.ReadAllText(Path.Combine(backup.BackupDir, "data", SyncRel)));

        var saved = System.Text.Json.JsonSerializer.Deserialize<DataBackup>(File.ReadAllText(Path.Combine(backup.BackupDir, "backup.json")));
        Assert.NotNull(saved);
        Assert.Equal(3, saved!.Items.Count);
        Assert.Equal(Id, saved.ExtensionId);
        Assert.Equal(profilePath, saved.ProfilePath);
        Assert.Equal("chrome", saved.BrowserId);
    }

    [Fact]
    public void Reset_only_moves_folders_that_exist()
    {
        File.WriteAllText(Write(LogRel), "log-data");

        var backup = ExtensionDataReset.Reset(ChromiumProfile(), extension);

        Assert.Equal([@"Local Extension Settings\" + Id], backup.Items);
    }

    [Fact]
    public void Restore_puts_the_data_back_with_identical_contents()
    {
        WriteExtensionData();
        var backup = ExtensionDataReset.Reset(ChromiumProfile(), extension);

        ExtensionDataReset.Restore(backup.BackupDir);

        Assert.Equal("log-data", File.ReadAllText(Path.Combine(profilePath, LogRel)));
        Assert.Equal("idb-data", File.ReadAllText(Path.Combine(profilePath, IdbRel)));
        Assert.Equal("sync-data", File.ReadAllText(Path.Combine(profilePath, SyncRel)));
        Assert.True(File.Exists(Path.Combine(backup.BackupDir, "restored.txt")));
    }

    [Fact]
    public void Restore_moves_data_created_since_the_reset_into_replaced()
    {
        WriteExtensionData();
        var backup = ExtensionDataReset.Reset(ChromiumProfile(), extension);
        File.WriteAllText(Write(LogRel), "fresh-data");

        ExtensionDataReset.Restore(backup.BackupDir);

        Assert.Equal("log-data", File.ReadAllText(Path.Combine(profilePath, LogRel)));
        Assert.Equal("fresh-data", File.ReadAllText(Path.Combine(backup.BackupDir, "replaced", LogRel)));
    }

    [Fact]
    public void Reset_of_an_open_profile_throws_and_moves_nothing()
    {
        WriteExtensionData();
        var lockFile = Write(@"Extension State\LOCK");
        File.WriteAllText(lockFile, "");
        using var held = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        held.Lock(0, 1);

        Assert.Throws<InvalidOperationException>(() => ExtensionDataReset.Reset(ChromiumProfile(), extension));

        Assert.True(File.Exists(Path.Combine(profilePath, LogRel)));
        Assert.True(File.Exists(Path.Combine(profilePath, IdbRel)));
        Assert.True(File.Exists(Path.Combine(profilePath, SyncRel)));
        Assert.False(Directory.Exists(AppPaths.BackupsDir));
    }

    [Fact]
    public void Reset_of_a_firefox_profile_throws()
    {
        var profile = new ProfileInfo
        {
            Browser = TestFixtures.BrowserFor("firefox", userData),
            Key = "Profiles/abc.work", Path = profilePath, Name = "work",
        };
        WriteExtensionData();

        Assert.Throws<InvalidOperationException>(() => ExtensionDataReset.Reset(profile, extension));
        Assert.True(File.Exists(Path.Combine(profilePath, LogRel)));
    }
}
