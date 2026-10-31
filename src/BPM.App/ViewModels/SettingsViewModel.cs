using System.Diagnostics;
using System.Reflection;
using System.Windows;
using BPM.App.Services;
using BPM.Core.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly AppSettings settings;

    public SettingsViewModel(AppSettings settings)
    {
        this.settings = settings;
        theme = settings.Theme;
        autoScanOnStart = settings.AutoScanOnStart;
        hideEmailsInReports = settings.HideEmailsInReports;
    }

    public IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];
    public string DataFolder => AppPaths.DataDir;
    public string VersionText => $"Version {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}";

    [ObservableProperty] private string theme;
    [ObservableProperty] private bool autoScanOnStart;
    [ObservableProperty] private bool hideEmailsInReports;

    partial void OnThemeChanged(string value)
    {
        settings.Theme = value;
        settings.Save();
        if (Application.Current?.MainWindow is { } window) ThemeService.Apply(value, window);
    }

    partial void OnAutoScanOnStartChanged(bool value)
    {
        settings.AutoScanOnStart = value;
        settings.Save();
    }

    partial void OnHideEmailsInReportsChanged(bool value)
    {
        settings.HideEmailsInReports = value;
        settings.Save();
    }

    [RelayCommand]
    void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        Process.Start("explorer.exe", $"\"{AppPaths.DataDir}\"");
    }
}
