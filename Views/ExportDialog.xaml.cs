using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NavMeCat.Services;

namespace NavMeCat.Views;

public partial class ExportDialog : Window
{
    private readonly DataView _view;
    private readonly Func<string, object?, string?>? _display;
    private readonly string _suggestedName;
    private ExportFormat _format = ExportFormat.Csv;

    public ObservableCollection<ColumnChoice> Columns { get; } = new();

    public ExportDialog(DataView view, string suggestedName, Func<string, object?, string?>? display = null)
    {
        InitializeComponent();
        _view = view;
        _display = display;
        _suggestedName = suggestedName;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

        foreach (ExportFormat f in Enum.GetValues(typeof(ExportFormat)))
        {
            var rb = new RadioButton
            {
                Content = ExportService.Label(f),
                GroupName = "Format",
                Tag = f,
                Margin = new Thickness(4, 4, 4, 4),
                IsChecked = f == _format,
            };
            rb.Checked += (_, _) => _format = (ExportFormat)rb.Tag;
            FormatList.Children.Add(rb);
        }

        foreach (DataColumn c in view.Table!.Columns)
            Columns.Add(new ColumnChoice { Name = c.ColumnName, Enabled = true, IsChecked = true });
        ColumnList.ItemsSource = Columns;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var c in Columns) c.IsChecked = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var c in Columns) c.IsChecked = false; }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var cols = Columns.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        if (cols.Count == 0)
        {
            Dialogs.ShowError("No columns", "Select at least one column to export.");
            return;
        }
        var format = _format;

        var ext = ExportService.Extension(format);
        var dialog = new SaveFileDialog
        {
            FileName = $"{Sanitize(_suggestedName)}.{ext}",
            DefaultExt = ext,
            Filter = $"{ExportService.Label(format)}|*.{ext}|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var tableName = _suggestedName.Split('.').Last();
            ExportService.Export(_view, cols, format, dialog.FileName, HeadersCheck.IsChecked == true,
                _display, tableName);
            DialogResult = true;
            Close();
            Dialogs.ShowSuccess("Export complete", $"Exported {_view.Count:N0} row(s) to:\n{dialog.FileName}");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Export failed", ex.Message);
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
