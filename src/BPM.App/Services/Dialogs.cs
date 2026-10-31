using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace BPM.App.Services;

public static class Dialogs
{
    /// <summary>Asks before a change. Returns true when the user picked the primary button.</summary>
    public static async Task<bool> ConfirmAsync(string title, string message, string primary, bool danger = false)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary,
            Owner = Application.Current?.MainWindow,
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    /// <summary>Asks the user to pick one option. Returns its index, or -1 when cancelled.</summary>
    public static async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options, string primary)
    {
        var picker = new ComboBox { ItemsSource = options, SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 0), MinWidth = 320 };
        var content = new StackPanel();
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
        content.Children.Add(picker);
        var box = new MessageBox
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            Owner = Application.Current?.MainWindow,
        };
        return await box.ShowDialogAsync() == MessageBoxResult.Primary ? picker.SelectedIndex : -1;
    }

    /// <summary>Asks for a line of text. Returns it (trimmed), or null when cancelled.</summary>
    public static async Task<string?> AskTextAsync(string title, string message, string placeholder, string primary, int maxLength)
    {
        var input = new Wpf.Ui.Controls.TextBox { PlaceholderText = placeholder, MaxLength = maxLength, Margin = new Thickness(0, 12, 0, 0), MinWidth = 320 };
        var content = new StackPanel();
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
        content.Children.Add(input);
        var box = new MessageBox
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            Owner = Application.Current?.MainWindow,
        };
        box.Loaded += (_, _) => input.Focus();
        return await box.ShowDialogAsync() == MessageBoxResult.Primary ? input.Text.Trim() : null;
    }

    public static async Task InfoAsync(string title, string message)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            CloseButtonText = "OK",
            Owner = Application.Current?.MainWindow,
        };
        await box.ShowDialogAsync();
    }
}
