using System.Windows;
using System.Windows.Controls;
using BPM.App.ViewModels;

namespace BPM.App.Views;

public partial class BrowsersView : UserControl
{
    public BrowsersView() => InitializeComponent();

    // TreeView.SelectedItem is read-only, so selection is pushed to the view-model here.
    void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is BrowsersViewModel vm && e.NewValue != null) vm.SelectedNode = e.NewValue;
    }
}
