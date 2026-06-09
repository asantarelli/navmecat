using System.Windows.Controls;
using System.Windows.Input;
using NavMeCat.ViewModels;

namespace NavMeCat.Views;

public partial class ObjectListView : UserControl
{
    public ObjectListView()
    {
        InitializeComponent();
    }

    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ObjectListViewModel vm && vm.SelectedItem is not null && vm.OpenCommand.CanExecute(null))
            vm.OpenCommand.Execute(null);
    }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || DataContext is not ObjectListViewModel vm) return;
        if (e.Key == Key.C) { vm.CopyCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.V) { vm.PasteCommand.Execute(null); e.Handled = true; }
    }
}
