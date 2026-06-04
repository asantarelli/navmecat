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
        }
        else
        {
            grid.AutoGeneratingColumn -= OnAutoGeneratingColumn;
            grid.PreviewMouseRightButtonUp -= OnHeaderRightClick;
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
