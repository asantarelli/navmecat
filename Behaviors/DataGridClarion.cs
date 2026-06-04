using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NavMeCat.Converters;
using NavMeCat.ViewModels;

namespace NavMeCat.Behaviors;

/// <summary>
/// Attached behavior: when enabled on an auto-generating DataGrid whose DataContext is a
/// <see cref="TableTabViewModel"/>, columns detected as Clarion dates get a converter that
/// displays them as real dates (and a 📅 marker), while keeping the underlying integer editable.
/// </summary>
public static class DataGridClarion
{
    private static readonly ClarionDateConverter Converter = new();

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
            grid.AutoGeneratingColumn += OnAutoGeneratingColumn;
        else
            grid.AutoGeneratingColumn -= OnAutoGeneratingColumn;
    }

    private static void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not TableTabViewModel tab) return;
        if (!tab.ShowClarionDates) return;
        if (!tab.ClarionDateColumns.Contains(e.PropertyName)) return;

        if (e.Column is DataGridTextColumn column && column.Binding is Binding binding)
        {
            binding.Converter = Converter;
            binding.ConverterParameter = e.PropertyType; // numeric type for ConvertBack
            column.Header = e.PropertyName + "  📅"; // 📅
        }
    }
}
