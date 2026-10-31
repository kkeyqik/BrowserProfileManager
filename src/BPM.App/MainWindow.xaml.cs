using System.Windows.Threading;
using BPM.App.Services;
using BPM.App.ViewModels;
using Wpf.Ui.Controls;

namespace BPM.App;

public partial class MainWindow : FluentWindow
{
    readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public MainWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        FitToScreen();
        ThemeService.Apply(viewModel.AppSettings.Theme, this);
        Notifier.Initialize(SnackbarPresenter);

        liveTimer.Tick += async (_, _) => await viewModel.RefreshLiveStateAsync();
        Loaded += async (_, _) =>
        {
            if (viewModel.AppSettings.AutoScanOnStart) await viewModel.ScanCommand.ExecuteAsync(null);
            liveTimer.Start();
        };
        Closed += (_, _) => liveTimer.Stop();
        SizeChanged += (_, _) =>
        {
            viewModel.IsNavCompact = ActualWidth < 1300;
            NavColumn.Width = new System.Windows.GridLength(viewModel.IsNavCompact ? 68 : 220);
        };
    }

    /// <summary>Keeps the default size inside small or heavily scaled screens; very small screens start maximized.</summary>
    void FitToScreen()
    {
        var area = System.Windows.SystemParameters.WorkArea;
        if (area.Width < MinWidth || area.Height < MinHeight)
        {
            WindowState = System.Windows.WindowState.Maximized;
            return;
        }
        Width = Math.Min(Width, area.Width * 0.92);
        Height = Math.Min(Height, area.Height * 0.92);
    }
}
