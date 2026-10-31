using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using BPM.App.Services;
using BPM.Core.Journal;
using BPM.Core.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace BPM.App.ViewModels;

/// <summary>"Change log": everything the app changed, newest first, with Undo.</summary>
public sealed partial class ChangeLogViewModel : ObservableObject
{
    public ChangeLogViewModel()
    {
        Reload();
        ChangeJournal.Changed += () => Application.Current?.Dispatcher.BeginInvoke(Reload);
    }

    public ObservableCollection<ChangeRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;

    void Reload()
    {
        Rows.Clear();
        foreach (var record in ChangeJournal.Load()) Rows.Add(new ChangeRow(record));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    Task Undo(ChangeRow? row) => row == null ? Task.CompletedTask : ActionService.Instance.UndoAsync(row.Record);

    [RelayCommand]
    void OpenBackups()
    {
        Directory.CreateDirectory(AppPaths.BackupsDir);
        Process.Start("explorer.exe", $"\"{AppPaths.BackupsDir}\"");
    }
}

public sealed class ChangeRow(ChangeRecord record)
{
    public ChangeRecord Record { get; } = record;
    public string Title => Record.Title;
    public string Detail => Record.Detail;
    public string TimeText => Record.Time.ToString("d MMM yyyy, HH:mm");
    public bool CanUndo => Record.CanUndo && !Record.Undone;
    public string StatusText => Record.Undone ? $"Undone {Record.UndoneAt:d MMM, HH:mm}" : "";

    public SymbolRegular Icon => Record.Kind switch
    {
        ChangeKind.ExtensionSwitch => SymbolRegular.ToggleRight24,
        ChangeKind.ExtensionRemoval => SymbolRegular.Delete24,
        ChangeKind.Policy => SymbolRegular.ShieldKeyhole24,
        ChangeKind.ExtensionData => SymbolRegular.ArrowReset24,
        ChangeKind.Shortcut => SymbolRegular.Desktop24,
        ChangeKind.ProfileCreated => SymbolRegular.PersonAdd24,
        _ => SymbolRegular.Wrench24,
    };
}
