using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NavMeCat.ViewModels;

namespace NavMeCat;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        Vm.SelectedNode = e.NewValue as DbTreeNode;
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedNode is { Type: NodeType.Table } node &&
            Vm.OpenTableCommand.CanExecute(node))
        {
            Vm.OpenTableCommand.Execute(node);
        }
    }

    /// <summary>Drag handle above a docked pane resizes it (dragging up makes it taller).</summary>
    private void PaneThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not TableTabViewModel tab) return;

        static double Clamp(double h) => Math.Max(120, Math.Min(900, h));

        if ((string)thumb.Tag == "sql")
            tab.SqlPaneHeight = Clamp(tab.SqlPaneHeight - e.VerticalChange);
        else
            tab.DetailPaneHeight = Clamp(tab.DetailPaneHeight - e.VerticalChange);
    }
}
