using System.Windows;
using BPM.App.ViewModels;
using Wpf.Ui.Controls;

namespace BPM.App.Views;

/// <summary>"Install an extension": pick what to install and which profiles get it (DataContext: InstallViewModel).</summary>
public partial class InstallDialog : FluentWindow
{
    public InstallDialog(InstallViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    void Install_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
