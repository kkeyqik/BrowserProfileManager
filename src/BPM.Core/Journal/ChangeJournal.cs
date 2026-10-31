using System.Text.Json;
using System.Text.Json.Serialization;
using BPM.Core.Util;

namespace BPM.Core.Journal;

public enum ChangeKind { ExtensionSwitch, ExtensionRemoval, Policy, ExtensionData, Shortcut, Sideload, NativeHost, ProfileCreated }

/// <summary>One change the app made, with what's needed to undo it.</summary>
public sealed class ChangeRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime Time { get; init; } = DateTime.Now;
    public ChangeKind Kind { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string? BrowserId { get; init; }
    public string? ProfilePath { get; init; }
    public string? ExtensionId { get; init; }

    /// <summary>Undo data, interpreted per <see cref="Kind"/> (e.g. registry key + previous value, backup folder).</summary>
    public Dictionary<string, string?> Undo { get; init; } = [];

    public bool CanUndo { get; init; } = true;
    public bool Undone { get; set; }
    public DateTime? UndoneAt { get; set; }
}

/// <summary>Append-only log of changes (JSON lines in the app data folder).</summary>
public static class ChangeJournal
{
    static readonly Lock Gate = new();
    static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static event Action? Changed;

    public static void Add(ChangeRecord record)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            // If a previous write was cut off, start on a fresh line so this record stays readable.
            var prefix = EndsWithNewline(AppPaths.JournalFile) ? "" : Environment.NewLine;
            File.AppendAllText(AppPaths.JournalFile, prefix + JsonSerializer.Serialize(record, Options) + Environment.NewLine);
        }
        Changed?.Invoke();
    }

    static bool EndsWithNewline(string path)
    {
        if (!File.Exists(path)) return true;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return true;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() == '\n';
    }

    /// <summary>All records, newest first. Unreadable lines are skipped.</summary>
    public static List<ChangeRecord> Load()
    {
        lock (Gate)
        {
            return ReadAll().OrderByDescending(r => r.Time).ToList();
        }
    }

    /// <summary>Marks one record undone. Every other line, including any it can't read, is kept exactly as it was.</summary>
    public static void MarkUndone(string id)
    {
        lock (Gate)
        {
            if (!File.Exists(AppPaths.JournalFile)) return;
            var lines = File.ReadAllLines(AppPaths.JournalFile);
            bool found = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (TryParse(lines[i]) is not { } record || record.Id != id) continue;
                record.Undone = true;
                record.UndoneAt = DateTime.Now;
                lines[i] = JsonSerializer.Serialize(record, Options);
                found = true;
            }
            if (!found) return;
            var temp = AppPaths.JournalFile + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, AppPaths.JournalFile, overwrite: true);
        }
        Changed?.Invoke();
    }

    static ChangeRecord? TryParse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<ChangeRecord>(line, Options); }
        catch (JsonException) { return null; }
    }

    static List<ChangeRecord> ReadAll()
    {
        var list = new List<ChangeRecord>();
        if (!File.Exists(AppPaths.JournalFile)) return list;
        // A damaged line shouldn't hide the rest of the history.
        foreach (var line in File.ReadAllLines(AppPaths.JournalFile))
            if (TryParse(line) is { } record) list.Add(record);
        return list;
    }
}
