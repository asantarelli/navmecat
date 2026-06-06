using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        var node = Vm.SelectedNode;
        if (node is null) return;

        if (node.IsOpenable && Vm.OpenTableCommand.CanExecute(node))
            Vm.OpenTableCommand.Execute(node);
        else if (node.Type is NodeType.Function or NodeType.Procedure && Vm.EditRoutineCommand.CanExecute(node))
            Vm.EditRoutineCommand.Execute(node);
    }

    private void Tree_RightClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        item.IsSelected = true;
        item.Focus();
        if (item.DataContext is not DbTreeNode node) return;

        var menu = BuildNodeMenu(node);
        if (menu is null) return;
        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu? BuildNodeMenu(DbTreeNode node)
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }

        switch (node.Type)
        {
            case NodeType.Table:
                menu.Items.Add(Item("Open", () => Run(Vm.OpenTableCommand, node)));
                break;

            case NodeType.View:
                menu.Items.Add(Item("Open", () => Run(Vm.OpenTableCommand, node)));
                menu.Items.Add(Item("Edit", () => Run(Vm.EditRoutineCommand, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Drop…", () => Run(Vm.DropRoutineCommand, node)));
                break;

            case NodeType.Function or NodeType.Procedure:
                menu.Items.Add(Item("Edit", () => Run(Vm.EditRoutineCommand, node)));
                menu.Items.Add(Item("Execute…", () => Run(Vm.ExecuteRoutineCommand, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Drop…", () => Run(Vm.DropRoutineCommand, node)));
                break;

            case NodeType.Category when node.CategoryChildType is NodeType.Function:
                menu.Items.Add(Item("New Function…", () => Run(Vm.NewRoutineCommand, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Refresh", () => Run(Vm.RefreshNodeCommand, node)));
                break;

            case NodeType.Category when node.CategoryChildType is NodeType.Procedure:
                menu.Items.Add(Item("New Procedure…", () => Run(Vm.NewRoutineCommand, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Refresh", () => Run(Vm.RefreshNodeCommand, node)));
                break;

            case NodeType.Category when node.CategoryChildType is NodeType.View:
                menu.Items.Add(Item("New View…", () => Run(Vm.NewRoutineCommand, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Refresh", () => Run(Vm.RefreshNodeCommand, node)));
                break;

            case NodeType.Server or NodeType.Database or NodeType.Schema or NodeType.Category:
                menu.Items.Add(Item("Refresh", () => Run(Vm.RefreshNodeCommand, node)));
                break;

            default:
                return null;
        }
        return menu;
    }

    private static void Run(System.Windows.Input.ICommand command, DbTreeNode node)
    {
        if (command.CanExecute(node)) command.Execute(node);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }

    /// <summary>Drag handle above a docked pane resizes it (dragging up makes it taller).</summary>
    private void PaneThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not TableTabViewModel tab) return;

        static double Clamp(double v) => Math.Max(120, Math.Min(900, v));

        switch ((string)thumb.Tag)
        {
            case "sql":
                tab.SqlPaneHeight = Clamp(tab.SqlPaneHeight - e.VerticalChange);
                break;
            case "inspector":
                tab.InspectorWidth = Math.Max(220, Math.Min(1000, tab.InspectorWidth - e.HorizontalChange));
                break;
            default:
                tab.DetailPaneHeight = Clamp(tab.DetailPaneHeight - e.VerticalChange);
                break;
        }
    }
}
