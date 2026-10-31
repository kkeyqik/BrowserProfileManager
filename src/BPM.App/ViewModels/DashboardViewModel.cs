using System.Collections.ObjectModel;
using BPM.App.Services;
using BPM.Core.Analysis;
using BPM.Core.Models;
using BPM.Core.Reports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BPM.App.ViewModels;

/// <summary>Dashboard: counts, "problems found" and a card per browser.</summary>
public sealed partial class DashboardViewModel(MainViewModel main) : ObservableObject
{
    public ObservableCollection<Problem> Problems { get; } = [];
    public ObservableCollection<BrowserNode> BrowserCards { get; } = [];

    [ObservableProperty] private bool hasScan;
    [ObservableProperty] private int browserCount;
    [ObservableProperty] private int profileCount;
    [ObservableProperty] private int openProfileCount;
    [ObservableProperty] private int extensionCount;
    [ObservableProperty] private int enabledCount;
    [ObservableProperty] private int highRiskCount;
    [ObservableProperty] private int waitingCount;
    [ObservableProperty] private string otherBrowsersText = "";

    public bool HasNoProblems => HasScan && Problems.Count == 0;

    public void Load(ScanResult scan)
    {
        var extensions = scan.AllExtensions.ToList();
        HasScan = true;
        BrowserCount = scan.Browsers.Count;
        ProfileCount = scan.AllProfiles.Count();
        OpenProfileCount = scan.AllProfiles.Count(p => p.IsOpen);
        ExtensionCount = extensions.Count;
        EnabledCount = extensions.Count(e => e.Enabled);
        HighRiskCount = extensions.Count(e => e.Enabled && e.Risk.Level == RiskLevel.High);
        WaitingCount = extensions.Count(e => e.NeedsApproval);
        OtherBrowsersText = scan.OtherBrowsers.Count == 0 ? "" :
            $"Also installed but not managed: {string.Join(", ", scan.OtherBrowsers.Select(o => o.Name))}";

        Problems.Clear();
        foreach (var problem in ProblemFinder.Find(scan)) Problems.Add(problem);
        BrowserCards.Clear();
        foreach (var browser in scan.Browsers) BrowserCards.Add(new BrowserNode(browser));
        OnPropertyChanged(nameof(HasNoProblems));
    }

    [RelayCommand]
    void OpenBrowser(BrowserNode? card)
    {
        if (card == null) return;
        var target = main.Browsers.Browsers.FirstOrDefault(b => b.Info.Id == card.Info.Id);
        if (target != null) main.Browsers.SelectedNode = target;
        if (target != null) target.IsSelected = true;
        main.Navigate(main.Browsers);
    }

    [RelayCommand]
    void Export()
    {
        if (main.Scan is not { } scan) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export report",
            Filter = "Web page (*.html)|*.html|Spreadsheet (*.csv)|*.csv|Data (*.json)|*.json",
            FileName = ReportExporter.SuggestedFileName(scan, ReportFormat.Html),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;
        var format = dialog.FilterIndex switch { 2 => ReportFormat.Csv, 3 => ReportFormat.Json, _ => ReportFormat.Html };
        try
        {
            ReportExporter.Export(scan, Problems.ToList(), dialog.FileName, format, main.AppSettings.HideEmailsInReports);
            Notifier.Success("Report saved", Path.GetFileName(dialog.FileName));
            if (format == ReportFormat.Html) Shell.OpenUrl(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notifier.Error("Couldn't save the report", e.Message);
        }
    }

    [RelayCommand]
    void ShowProblem(Problem? problem)
    {
        if (problem?.ExtensionId == null) return;
        main.AllExtensions.Show(problem.ExtensionId);
        main.Navigate(main.AllExtensions);
    }
}
