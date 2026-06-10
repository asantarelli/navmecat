using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NavMeCat.Services;

namespace NavMeCat.Views;

/// <summary>
/// Lets the user review (and tweak) the SQL type chosen for each column when copying a Clarion
/// file (.tps / .dat) into a SQL database, before the table is created and the rows are copied.
/// </summary>
public partial class ColumnMappingDialog : Window
{
    private readonly List<TableCopyService.ClarionColumnMap> _suggested;

    public ObservableCollection<ColumnMapRow> Rows { get; } = new();

    /// <summary>The confirmed mapping (null until the user clicks Apply).</summary>
    public List<TableCopyService.ClarionColumnMap>? Result { get; private set; }

    public ColumnMappingDialog(string sourceName, string targetDescription,
        IEnumerable<TableCopyService.ClarionColumnMap> proposed)
    {
        InitializeComponent();
        _suggested = proposed.ToList();
        SubtitleText.Text = $"'{sourceName}'  →  {targetDescription}";
        foreach (var m in _suggested)
            Rows.Add(new ColumnMapRow { Name = m.Name, SourceType = m.SourceType, TargetType = m.TargetType });
        RowList.ItemsSource = Rows;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < Rows.Count && i < _suggested.Count; i++)
            Rows[i].TargetType = _suggested[i].TargetType;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var blank = Rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.TargetType));
        if (blank is not null)
        {
            Dialogs.ShowError("Missing type", $"Please enter a SQL type for column '{blank.Name}'.");
            return;
        }
        Result = Rows
            .Select(r => new TableCopyService.ClarionColumnMap(r.Name, r.SourceType, r.TargetType.Trim()))
            .ToList();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}

/// <summary>A single editable row in the column-mapping dialog.</summary>
public sealed class ColumnMapRow : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";

    private string _targetType = "";
    public string TargetType
    {
        get => _targetType;
        set { if (_targetType != value) { _targetType = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetType))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
