using System.Windows;
using BPM.App.Services;
using BPM.App.ViewModels;
using BPM.Core.Actions;

namespace BPM.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Elevated helper mode: started by ElevatedRunner behind a UAC prompt, does one registry job and exits without a window.
        if (e.Args.Length == 2 && e.Args[0] == "--elevated")
        {
            Shutdown(ElevatedExecutor.RunFromCommandLine(e.Args[1]));
            return;
        }

        // Demo mode: made-up profiles and extensions for screenshots and videos; nothing on this PC is listed or changed.
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) DemoMode.Start();

        // One window at a time: a second launch brings the running one to the front.
        singleInstance = new Mutex(true, @"Local\BrowserProfileManager.App", out bool firstInstance);
        if (!firstInstance)
        {
            ActivateRunningInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            Notifier.Error("Something went wrong", args.Exception.Message);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            args.SetObserved();
        };

        var settings = AppSettings.Load();
        var window = new MainWindow(new MainViewModel(settings));
        MainWindow = window;
        window.Show();
    }

    Mutex? singleInstance;

    static void ActivateRunningInstance()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
}
