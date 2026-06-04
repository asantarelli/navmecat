using System.Windows;
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
}
