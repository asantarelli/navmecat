using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.Views;

public partial class QueryWindow : Window
{
    private readonly ConnectionProfile _connection;
    private readonly string? _database;

    public QueryWindow(ConnectionProfile connection, string? database, string? initialSql = null)
    {
        InitializeComponent();
        _connection = connection;
        _database = database;
        Owner = Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

        Title = $"Query — {connection.Name}" + (string.IsNullOrEmpty(database) ? "" : " / " + database);
        TargetLabel.Text = Title;
        if (!string.IsNullOrEmpty(initialSql)) Editor.Text = initialSql;

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
            var table = new DataTable();
            int affected;
            var truncated = false;
            var sw = Stopwatch.StartNew();

            await using (var conn = CreateConnection())
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                try { cmd.CommandTimeout = 0; } catch { /* not all providers allow 0 */ }
                await using var reader = await cmd.ExecuteReaderAsync();
                if (reader.FieldCount > 0) (table, truncated) = await ResultReader.LoadAsync(reader);
                affected = reader.RecordsAffected;
            }
            sw.Stop();

            if (table.Columns.Count > 0)
            {
                ResultsGrid.ItemsSource = table.DefaultView;
                Messages.Text = $"{table.Rows.Count:N0} row(s)  ·  {sw.ElapsedMilliseconds} ms" +
                                (truncated ? $"  (first {ResultReader.DefaultRowCap:N0})" : "");
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

    private DbConnection CreateConnection()
    {
        var cs = _connection.BuildConnectionString();
        return _connection.Engine switch
        {
            DatabaseEngine.Sqlite => new SqliteConnection(cs),
            DatabaseEngine.Firebird => new FbConnection(cs),
            DatabaseEngine.MySql or DatabaseEngine.MariaDb =>
                new MySqlConnection(string.IsNullOrEmpty(_database) ? cs : MySqlService.WithDatabase(cs, _database)),
            _ => new SqlConnection(string.IsNullOrEmpty(_database) ? cs : SqlServerService.WithDatabase(cs, _database))
        };
    }
}
