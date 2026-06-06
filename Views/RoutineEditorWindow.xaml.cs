using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.Views;

public partial class RoutineEditorWindow : Window
{
    private readonly ConnectionProfile _connection;
    private readonly string? _database;
    private readonly string _schema;
    private readonly string _name;

    public RoutineEditorWindow(ConnectionProfile connection, string? database, string schema, string name, string kind)
    {
        InitializeComponent();
        _connection = connection;
        _database = database;
        _schema = schema;
        _name = name;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

        Title = $"Edit {kind} — {schema}.{name}";
        TitleLabel.Text = Title;

        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; await SaveAsync(); }
        };

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Messages.Text = "Loading…";
        try
        {
            var def = await SqlServerService.GetObjectDefinitionAsync(
                _connection.BuildConnectionString(), _database ?? "", _schema, _name);
            if (string.IsNullOrEmpty(def))
            {
                Editor.Text = "-- Definition not available (the object may be encrypted).";
                Messages.Text = "No definition available.";
            }
            else
            {
                Editor.Text = def;
                Messages.Text = "Loaded. Edit and press Ctrl+S to save.";
            }
        }
        catch (Exception ex)
        {
            Editor.Text = "";
            Messages.Text = "Error: " + ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        var text = Editor.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        SaveButton.IsEnabled = false;
        Messages.Text = "Saving…";
        try
        {
            await SqlServerService.ExecuteAsync(_connection.BuildConnectionString(), _database ?? "", MakeAlterable(text));
            Messages.Text = "Saved successfully.";
        }
        catch (Exception ex)
        {
            Messages.Text = "Error: " + ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>Turns a leading CREATE into CREATE OR ALTER so the routine updates in place.</summary>
    private static string MakeAlterable(string text)
    {
        if (Regex.IsMatch(text, @"\bCREATE\s+OR\s+ALTER\b", RegexOptions.IgnoreCase))
            return text;
        var rx = new Regex(@"\bCREATE\b(\s+)(PROCEDURE|PROC|FUNCTION|VIEW|TRIGGER)\b", RegexOptions.IgnoreCase);
        return rx.Replace(text, "CREATE OR ALTER$1$2", 1);
    }
}
