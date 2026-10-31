using Wpf.Ui;
using Wpf.Ui.Controls;

namespace BPM.App.Services;

/// <summary>Short toast messages at the bottom of the window.</summary>
public static class Notifier
{
    static readonly SnackbarService Service = new() { DefaultTimeOut = TimeSpan.FromSeconds(4) };

    public static void Initialize(SnackbarPresenter presenter) => Service.SetSnackbarPresenter(presenter);

    public static void Info(string title, string message) => Show(title, message, ControlAppearance.Secondary, SymbolRegular.Info24);
    public static void Success(string title, string message) => Show(title, message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    public static void Error(string title, string message) => Show(title, message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);

    static void Show(string title, string message, ControlAppearance appearance, SymbolRegular icon) =>
        Service.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(4));
}
