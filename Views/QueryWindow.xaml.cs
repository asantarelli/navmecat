using System.Data;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.SqlClient;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.Views;

public partial class QueryWindow : Window
{
    private readonly ConnectionProfile _connection;
    private readonly string? _database;

    public QueryWindow(ConnectionProfile connection, string? database)
    {
        InitializeComponent();
        _connection = connection;
        _database = database;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

        Title = $"Query — {connection.Name}" + (string.IsNullOrEmpty(database) ? "" : " / " + database);
        TargetLabel.Text = Title;

        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5) { e.Handled = true; await RunAsync(); }
        };
        Editor.Focus();
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ResultsGrid.ItemsSource = null;
        Messages.Text = "Cleared.";
    }

    private async Task RunAsync()
    {
        var sql = Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Text;
        if (string.IsNullOrWhiteSpace(sql)) return;

        RunButton.IsEnabled = false;
        Messages.Text = "Running…";
        try
        {
            var cs = string.IsNullOrEmpty(_database)
                ? _connection.BuildConnectionString()
                : SqlServerService.WithDatabase(_connection.BuildConnectionString(), _database);

            var table = new DataTable();
            int affected;
            var sw = Stopwatch.StartNew();

            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
                await using var reader = await cmd.ExecuteReaderAsync();
                if (reader.FieldCount > 0) table.Load(reader);
                affected = reader.RecordsAffected;
            }
            sw.Stop();

            if (table.Columns.Count > 0)
            {
                ResultsGrid.ItemsSource = table.DefaultView;
                Messages.Text = $"{table.Rows.Count:N0} row(s)  ·  {sw.ElapsedMilliseconds} ms";
            }
            else
            {
                ResultsGrid.ItemsSource = null;
                Messages.Text = $"{(affected < 0 ? 0 : affected):N0} row(s) affected  ·  {sw.ElapsedMilliseconds} ms";
            }
        }
        catch (Exception ex)
        {
            ResultsGrid.ItemsSource = null;
            Messages.Text = "Error: " + ex.Message;
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }
}
