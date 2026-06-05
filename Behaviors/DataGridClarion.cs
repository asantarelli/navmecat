using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using NavMeCat.Converters;
using NavMeCat.Services;
using NavMeCat.ViewModels;

namespace NavMeCat.Behaviors;

/// <summary>
/// Attached behavior: when enabled on an auto-generating DataGrid whose DataContext is a
/// <see cref="TableTabViewModel"/>, columns that resolve to a Clarion date/time get a converter
/// that displays them as real dates (📅) or times (🕒) while keeping the integer editable.
/// Right-clicking a column header lets the user force date / time / plain number, or auto-detect.
/// </summary>
public static class DataGridClarion
{
    private static readonly ClarionDateConverter DateConverter = new();
    private static readonly ClarionTimeConverter TimeConverter = new();

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(DataGridClarion),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        if ((bool)e.NewValue)
        {
            grid.AutoGeneratingColumn += OnAutoGeneratingColumn;
            grid.PreviewMouseRightButtonUp += OnHeaderRightClick;
            grid.CurrentCellChanged += OnCurrentCellChanged;

            grid.PreviewTextInput += OnPreviewTextInput;
            grid.BeginningEdit += OnBeginningEdit;
            grid.CellEditEnding += OnCellEditEnding;

            // Spreadsheet-friendly copy/paste: replace the built-in copy and add a context menu.
            grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
            grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
                (_, ev) => { GridClipboard.Copy(grid, false); ev.Handled = true; }));
            grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
                (_, ev) => { GridClipboard.Paste(grid); ev.Handled = true; }));
            grid.ContextMenu = BuildContextMenu(grid);
        }
        else
        {
            grid.AutoGeneratingColumn -= OnAutoGeneratingColumn;
            grid.PreviewMouseRightButtonUp -= OnHeaderRightClick;
            grid.CurrentCellChanged -= OnCurrentCellChanged;
            grid.PreviewTextInput -= OnPreviewTextInput;
            grid.BeginningEdit -= OnBeginningEdit;
            grid.CellEditEnding -= OnCellEditEnding;
        }
    }

    private static void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not TableTabViewModel tab) return;

        var kind = tab.GetEffectiveKind(e.PropertyName);
        if (kind is null) return;

        if (e.Column is DataGridTextColumn column && column.Binding is Binding binding)
        {
            binding.Converter = kind == ClarionKind.Date ? DateConverter : TimeConverter;
            binding.ConverterParameter = e.PropertyType; // numeric type for ConvertBack
            column.Header = e.PropertyName + (kind == ClarionKind.Date ? "  📅" : "  🕒");
        }
    }

    private static ContextMenu BuildContextMenu(DataGrid grid)
    {
        var menu = new ContextMenu();

        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => GridClipboard.Copy(grid, false);
        var copyWithHeaders = new MenuItem { Header = "Copy with headers" };
        copyWithHeaders.Click += (_, _) => GridClipboard.Copy(grid, true);
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V" };
        paste.Click += (_, _) => GridClipboard.Paste(grid);

        menu.Items.Add(copy);
        menu.Items.Add(copyWithHeaders);
        menu.Items.Add(new Separator());
        menu.Items.Add(paste);
        return menu;
    }

    // Selection captured before an edit starts, so type-fill survives the edit collapsing/moving it.
    private static List<(DataRowView Row, DataGridColumn Col)>? _fillSnapshot;

    private static List<(DataRowView Row, DataGridColumn Col)>? SnapshotSelection(DataGrid grid) =>
        grid.SelectedCells.Count > 1
            ? grid.SelectedCells
                .Where(c => c.Item is DataRowView)
                .Select(c => ((DataRowView)c.Item, c.Column))
                .ToList()
            : null;

    // Typing a character is the earliest signal — selection is still intact here.
    private static void OnPreviewTextInput(object? sender, TextCompositionEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedCells.Count > 1)
            _fillSnapshot = SnapshotSelection(grid);
    }

    private static void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        // Fallback (e.g. F2 then type) — don't clobber a snapshot already taken on text input.
        if (_fillSnapshot is null && sender is DataGrid grid)
            _fillSnapshot = SnapshotSelection(grid);
    }

    /// <summary>When several cells were selected, typing into one fills them all.</summary>
    private static void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        var snapshot = _fillSnapshot;
        _fillSnapshot = null;

        if (e.EditAction != DataGridEditAction.Commit) return;
        if (sender is not DataGrid grid) return;
        if (snapshot is null || snapshot.Count <= 1) return;
        if (e.EditingElement is not TextBox box) return;

        var value = box.Text;
        grid.Dispatcher.BeginInvoke(
            new Action(() => GridClipboard.FillCells(grid, snapshot, value)),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---- current cell -> detail panel -----------------------------------

    private static void OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not TableTabViewModel tab) return;

        var cell = grid.CurrentCell;
        if (cell.Column is null || cell.Item is not DataRowView rowView) return;

        var name = GetColumnName(cell.Column);
        object? value = !string.IsNullOrEmpty(name) && rowView.Row.Table.Columns.Contains(name)
            ? rowView[name]
            : null;

        tab.SetDetail(rowView, name, value);
    }

    // ---- header right-click menu ----------------------------------------

    private static void OnHeaderRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not TableTabViewModel tab) return;

        var header = FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
        if (header?.Column is not { } column) return;

        var name = GetColumnName(column);
        if (string.IsNullOrEmpty(name)) return;

        var effective = tab.GetEffectiveKind(name);
        var menu = new ContextMenu();
        menu.Items.Add(MakeItem("Show as date 📅", effective == ClarionKind.Date,
            () => tab.SetClarionOverride(name, ClarionKind.Date)));
        menu.Items.Add(MakeItem("Show as time 🕒", effective == ClarionKind.Time,
            () => tab.SetClarionOverride(name, ClarionKind.Time)));
        menu.Items.Add(MakeItem("Show as number", effective is null,
            () => tab.SetClarionOverride(name, null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Auto-detect", !tab.HasOverride(name),
            () => tab.ClearClarionOverride(name)));

        menu.PlacementTarget = header;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem MakeItem(string header, bool isChecked, Action onClick)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static string GetColumnName(DataGridColumn column)
    {
        if (column is DataGridBoundColumn { Binding: Binding b } && !string.IsNullOrEmpty(b.Path?.Path))
            return b.Path.Path;
        return column.SortMemberPath ?? "";
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = VisualTreeHelper.GetParent(current);
        return current as T;
    }
}
