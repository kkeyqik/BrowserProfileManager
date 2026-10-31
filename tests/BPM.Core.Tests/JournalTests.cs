using BPM.Core.Journal;
using BPM.Core.Util;

namespace BPM.Core.Tests;

[CollectionDefinition("AppPaths", DisableParallelization = true)]
public class AppPathsCollection;

/// <summary>Points AppPaths.DataDir at a temp folder for the test and restores it afterwards.</summary>
public abstract class AppDataTestBase : IDisposable
{
    readonly string originalDataDir = AppPaths.DataDir;
    protected TempDir Temp { get; } = new();
    protected string DataDir => Temp.Combine("appdata");

    protected AppDataTestBase() => AppPaths.DataDir = DataDir;

    public void Dispose()
    {
        AppPaths.DataDir = originalDataDir;
        Temp.Dispose();
    }
}

[Collection("AppPaths")]
public class ChangeJournalTests : AppDataTestBase
{
    static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void Load_with_no_file_returns_empty_list()
    {
        Assert.Empty(ChangeJournal.Load());
    }

    [Fact]
    public void Load_returns_newest_first_with_all_fields_intact()
    {
        var older = new ChangeRecord
        {
            Time = Base, Kind = ChangeKind.Policy, Title = "Blocked X", Detail = "d1",
            BrowserId = "chrome", ProfilePath = @"C:\p", ExtensionId = "abc",
            Undo = new Dictionary<string, string?> { ["key"] = @"SOFTWARE\Policies\Google\Chrome", ["previous"] = null },
        };
        var newer = new ChangeRecord { Time = Base.AddHours(1), Kind = ChangeKind.ExtensionData, Title = "Reset Y", CanUndo = false };

        ChangeJournal.Add(older);
        ChangeJournal.Add(newer);
        var loaded = ChangeJournal.Load();

        Assert.Equal([newer.Id, older.Id], loaded.Select(r => r.Id));
        var back = loaded[1];
        Assert.Equal(ChangeKind.Policy, back.Kind);
        Assert.Equal("Blocked X", back.Title);
        Assert.Equal("d1", back.Detail);
        Assert.Equal("chrome", back.BrowserId);
        Assert.Equal(@"C:\p", back.ProfilePath);
        Assert.Equal("abc", back.ExtensionId);
        Assert.Equal(Base, back.Time);
        Assert.Equal(@"SOFTWARE\Policies\Google\Chrome", back.Undo["key"]);
        Assert.Null(back.Undo["previous"]);
        Assert.False(loaded[0].CanUndo);
    }

    [Fact]
    public void MarkUndone_sets_flags_and_persists()
    {
        var record = new ChangeRecord { Time = Base, Kind = ChangeKind.Shortcut, Title = "Shortcut" };
        var other = new ChangeRecord { Time = Base.AddMinutes(1), Kind = ChangeKind.Sideload, Title = "Other" };
        ChangeJournal.Add(record);
        ChangeJournal.Add(other);

        ChangeJournal.MarkUndone(record.Id);
        var loaded = ChangeJournal.Load();

        var undone = loaded.Single(r => r.Id == record.Id);
        Assert.True(undone.Undone);
        Assert.NotNull(undone.UndoneAt);
        Assert.True((DateTime.Now - undone.UndoneAt!.Value).Duration() < TimeSpan.FromMinutes(1));
        Assert.False(loaded.Single(r => r.Id == other.Id).Undone);
    }

    [Fact]
    public void MarkUndone_with_unknown_id_changes_nothing()
    {
        ChangeJournal.Add(new ChangeRecord { Kind = ChangeKind.Policy, Title = "T" });

        ChangeJournal.MarkUndone("missing");

        Assert.False(ChangeJournal.Load().Single().Undone);
    }

    [Fact]
    public void Corrupted_line_is_skipped_without_losing_other_records()
    {
        var first = new ChangeRecord { Time = Base, Kind = ChangeKind.Policy, Title = "First" };
        var last = new ChangeRecord { Time = Base.AddHours(1), Kind = ChangeKind.NativeHost, Title = "Last" };
        ChangeJournal.Add(first);
        File.AppendAllText(AppPaths.JournalFile, "{this is not json" + Environment.NewLine);
        ChangeJournal.Add(last);

        var loaded = ChangeJournal.Load();

        Assert.Equal([last.Id, first.Id], loaded.Select(r => r.Id));
    }

    [Fact]
    public void MarkUndone_still_works_when_the_file_contains_a_corrupted_line()
    {
        var record = new ChangeRecord { Time = Base, Kind = ChangeKind.Policy, Title = "First" };
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(AppPaths.JournalFile, "garbage" + Environment.NewLine);
        ChangeJournal.Add(record);

        ChangeJournal.MarkUndone(record.Id);

        Assert.True(ChangeJournal.Load().Single().Undone);
    }
}
