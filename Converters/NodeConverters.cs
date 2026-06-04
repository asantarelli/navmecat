using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NavMeCat.ViewModels;

namespace NavMeCat.Converters;

/// <summary>Maps a node type to a small outline icon (16x16 design space).</summary>
public class NodeTypeToGeometryConverter : IValueConverter
{
    private static readonly Dictionary<NodeType, Geometry> Icons = new()
    {
        // Two stacked server bars.
        [NodeType.Server] = Geometry.Parse(
            "M2,3 H14 V6.5 H2 Z M2,9.5 H14 V13 H2 Z M4,4.75 H4.01 M4,11.25 H4.01"),
        // Classic database cylinder.
        [NodeType.Database] = Geometry.Parse(
            "M8,2 C10.8,2 13,2.7 13,3.6 C13,4.5 10.8,5.2 8,5.2 C5.2,5.2 3,4.5 3,3.6 C3,2.7 5.2,2 8,2 Z " +
            "M3,3.6 L3,12.4 C3,13.3 5.2,14 8,14 C10.8,14 13,13.3 13,12.4 L13,3.6"),
        // Folder for schema.
        [NodeType.Schema] = Geometry.Parse(
            "M2,4.5 C2,3.95 2.45,3.5 3,3.5 L6,3.5 L7.5,5 L13,5 C13.55,5 14,5.45 14,6 " +
            "L14,12 C14,12.55 13.55,13 13,13 L3,13 C2.45,13 2,12.55 2,12 Z"),
        // Grid for table.
        [NodeType.Table] = Geometry.Parse(
            "M2.5,3.5 H13.5 V12.5 H2.5 Z M2.5,6.5 H13.5 M2.5,9.5 H13.5 M6.17,3.5 V12.5 M9.83,3.5 V12.5"),
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is NodeType t && Icons.TryGetValue(t, out var g) ? g : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Color-codes each node type, like a real database tool.</summary>
public class NodeTypeToBrushConverter : IValueConverter
{
    private static readonly Brush Server = Freeze("#2D7FE0");
    private static readonly Brush Database = Freeze("#1B9E8B");
    private static readonly Brush Schema = Freeze("#E0A52D");
    private static readonly Brush Table = Freeze("#4C6275");
    private static readonly Brush Default = Freeze("#9AA7B4");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            NodeType.Server => Server,
            NodeType.Database => Database,
            NodeType.Schema => Schema,
            NodeType.Table => Table,
            _ => Default,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>
/// Visible when the value is null. Pass ConverterParameter="inv" to invert
/// (visible when the value is NOT null).
/// </summary>
public class IsNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null;
        if (string.Equals(parameter as string, "inv", StringComparison.OrdinalIgnoreCase))
            isNull = !isNull;
        return isNull ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverse of the built-in BooleanToVisibilityConverter.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
