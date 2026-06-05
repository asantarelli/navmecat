using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.ViewModels;

public partial class QueryBuilderViewModel : ObservableObject
{
    private readonly ConnectionProfile _connection;
    private readonly string? _database;

    public ObservableCollection<string> AllTables { get; } = new();          // "schema.table"
    public ObservableCollection<BuilderTable> Tables { get; } = new();
    public ObservableCollection<JoinRow> Joins { get; } = new();
    public ObservableCollection<FilterRow> Filters { get; } = new();
    public ObservableCollection<BuilderSort> Sorts { get; } = new();
    public ObservableCollection<string> AvailableColumns { get; } = new();   // "[table].[col]"

    [ObservableProperty] private string? selectedTableToAdd;
    [ObservableProperty] private string generatedSql = "";
    [ObservableProperty] private DataView? results;
    [ObservableProperty] private string messages = "Add a table to begin.";

    public Array JoinTypes { get; } = Enum.GetValues(typeof(JoinType));
    public Array FilterOperatorValues { get; } = Enum.GetValues(typeof(FilterOperator));
    public Array SortDirections { get; } = Enum.GetValues(typeof(SortDirection));
    public Array Connectors { get; } = Enum.GetValues(typeof(BoolConnector));

    public QueryBuilderViewModel(ConnectionProfile connection, string? database)
    {
        _connection = connection;
        _database = database;

        WireRowCollection(Joins);
        WireRowCollection(Filters);
        WireRowCollection(Sorts);
        Tables.CollectionChanged += (_, _) => { RebuildAvailable(); Regenerate(); };

        _ = LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        try
        {
            var tables = await SqlServerService.GetAllTablesAsync(_connection.BuildConnectionString(), _database ?? "");
            AllTables.Clear();
            foreach (var (s, t) in tables) AllTables.Add($"{s}.{t}");
        }
        catch (Exception ex)
        {
            Messages = "Could not load tables: " + ex.Message;
        }
    }

    // ---- tables ----------------------------------------------------------

    [RelayCommand]
    private async Task AddTable()
    {
        if (string.IsNullOrEmpty(SelectedTableToAdd)) return;
        var dot = SelectedTableToAdd.IndexOf('.');
        if (dot <= 0) return;
        var schema = SelectedTableToAdd[..dot];
        var table = SelectedTableToAdd[(dot + 1)..];

        var bt = new BuilderTable { Schema = schema, Table = table };
        try
        {
            var cols = await SqlServerService.GetColumnNamesAsync(_connection.BuildConnectionString(), _database ?? "", schema, table);
            foreach (var c in cols)
            {
                var bc = new BuilderColumn { Table = table, Name = c };
                bc.PropertyChanged += OnRowChanged;
                bt.Columns.Add(bc);
            }
        }
        catch (Exception ex)
        {
            Messages = "Could not load columns: " + ex.Message;
            return;
        }
        Tables.Add(bt);
    }

    [RelayCommand]
    private void RemoveTable(BuilderTable? t)
    {
        if (t is null) return;
        foreach (var c in t.Columns) c.PropertyChanged -= OnRowChanged;
        Tables.Remove(t);
    }

    // ---- joins / filters / sorts ----------------------------------------

    [RelayCommand] private void AddJoin() => Joins.Add(new JoinRow());
    [RelayCommand] private void RemoveJoin(JoinRow? j) { if (j is not null) Joins.Remove(j); }

    [RelayCommand] private void AddFilter() => Filters.Add(new FilterRow());
    [RelayCommand] private void RemoveFilter(FilterRow? f) { if (f is not null) Filters.Remove(f); }

    [RelayCommand] private void AddSort() => Sorts.Add(new BuilderSort());
    [RelayCommand] private void RemoveSort(BuilderSort? s) { if (s is not null) Sorts.Remove(s); }

    [RelayCommand]
    private async Task DetectJoins()
    {
        try
        {
            var fks = await SqlServerService.GetAllForeignKeysAsync(_connection.BuildConnectionString(), _database ?? "");
            var present = new HashSet<string>(Tables.Select(t => t.Table), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var (ps, pt, pc, rs, rt, rc) in fks)
            {
                if (!present.Contains(pt) || !present.Contains(rt)) continue;
                var left = $"[{pt}].[{pc}]";
                var right = $"[{rt}].[{rc}]";
                if (Joins.Any(j => j.LeftColumn == left && j.RightColumn == right)) continue;
                Joins.Add(new JoinRow { LeftColumn = left, JoinType = JoinType.Inner, RightColumn = right });
                added++;
            }
            Messages = added > 0 ? $"Added {added} join(s) from foreign keys." : "No foreign keys found between the selected tables.";
        }
        catch (Exception ex)
        {
            Messages = "Detect joins failed: " + ex.Message;
        }
    }

    // ---- run -------------------------------------------------------------

    [RelayCommand]
    private async Task Run()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql)) return;
        Messages = "Running…";
        try
        {
            var cs = string.IsNullOrEmpty(_database)
                ? _connection.BuildConnectionString()
                : SqlServerService.WithDatabase(_connection.BuildConnectionString(), _database);

            var data = new DataTable();
            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(GeneratedSql, conn) { CommandTimeout = 0 };
                await using var reader = await cmd.ExecuteReaderAsync();
                if (reader.FieldCount > 0) data.Load(reader);
            }
            Results = data.DefaultView;
            Messages = $"{data.Rows.Count:N0} row(s).";
        }
        catch (Exception ex)
        {
            Results = null;
            Messages = "Error: " + ex.Message;
        }
    }

    // ---- generation ------------------------------------------------------

    private void RebuildAvailable()
    {
        AvailableColumns.Clear();
        foreach (var t in Tables)
            foreach (var c in t.Columns)
                AvailableColumns.Add(c.Reference);
    }

    private void Regenerate()
    {
        if (Tables.Count == 0) { GeneratedSql = ""; return; }

        var included = Tables.SelectMany(t => t.Columns).Where(c => c.Included).Select(c => c.Reference).ToList();
        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(included.Count > 0 ? string.Join(", ", included) : "*").Append('\n');
        sb.Append("FROM ").Append(Tables[0].FromClause);

        var inFrom = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Tables[0].Table };
        foreach (var j in Joins)
        {
            if (string.IsNullOrEmpty(j.LeftColumn) || string.IsNullOrEmpty(j.RightColumn)) continue;
            var rightTable = TableOf(j.RightColumn);
            var bt = Tables.FirstOrDefault(t => t.Table.Equals(rightTable, StringComparison.OrdinalIgnoreCase));
            if (bt is null) continue;
            sb.Append('\n').Append(JoinKeyword(j.JoinType)).Append(' ').Append(bt.FromClause)
              .Append(" ON ").Append(j.LeftColumn).Append(" = ").Append(j.RightColumn);
            inFrom.Add(bt.Table);
        }
        foreach (var t in Tables.Skip(1))
            if (!inFrom.Contains(t.Table)) { sb.Append("\n, ").Append(t.FromClause); inFrom.Add(t.Table); }

        var fs = Filters.Where(f => !string.IsNullOrEmpty(f.Column)).ToList();
        if (fs.Count > 0)
        {
            sb.Append("\nWHERE ");
            for (var i = 0; i < fs.Count; i++)
            {
                if (i > 0) sb.Append(' ').Append(fs[i].Connector == BoolConnector.Or ? "OR" : "AND").Append(' ');
                sb.Append(FilterClause(fs[i]));
            }
        }

        var ss = Sorts.Where(s => !string.IsNullOrEmpty(s.Column)).ToList();
        if (ss.Count > 0)
            sb.Append("\nORDER BY ")
              .Append(string.Join(", ", ss.Select(s => $"{s.Column} {(s.Direction == SortDirection.Desc ? "DESC" : "ASC")}")));

        GeneratedSql = sb.ToString();
    }

    private static string FilterClause(FilterRow f)
    {
        var col = f.Column!;
        var v = f.Value ?? "";
        string Lit(string x) => double.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? x : "'" + x.Replace("'", "''") + "'";
        string Like(string p) => $"{col} LIKE '{p.Replace("'", "''")}'";

        return f.Operator switch
        {
            FilterOperator.Contains => Like($"%{v}%"),
            FilterOperator.StartsWith => Like($"{v}%"),
            FilterOperator.EndsWith => Like($"%{v}"),
            FilterOperator.Equals => $"{col} = {Lit(v)}",
            FilterOperator.NotEquals => $"{col} <> {Lit(v)}",
            FilterOperator.GreaterThan => $"{col} > {Lit(v)}",
            FilterOperator.LessThan => $"{col} < {Lit(v)}",
            FilterOperator.GreaterOrEqual => $"{col} >= {Lit(v)}",
            FilterOperator.LessOrEqual => $"{col} <= {Lit(v)}",
            FilterOperator.IsEmpty => $"{col} IS NULL",
            FilterOperator.IsNotEmpty => $"{col} IS NOT NULL",
            _ => ""
        };
    }

    private static string TableOf(string columnRef)
    {
        var start = columnRef.IndexOf('[');
        var end = columnRef.IndexOf(']');
        return start >= 0 && end > start ? columnRef[(start + 1)..end] : "";
    }

    private static string JoinKeyword(JoinType t) => t switch
    {
        JoinType.Left => "LEFT JOIN",
        JoinType.Right => "RIGHT JOIN",
        JoinType.Full => "FULL JOIN",
        _ => "INNER JOIN"
    };

    // ---- live-update wiring ---------------------------------------------

    private void WireRowCollection(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (INotifyPropertyChanged i in e.NewItems) i.PropertyChanged += OnRowChanged;
            if (e.OldItems is not null)
                foreach (INotifyPropertyChanged i in e.OldItems) i.PropertyChanged -= OnRowChanged;
            Regenerate();
        };
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Regenerate();
}
