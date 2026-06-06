using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.ViewModels;

public partial class TableDesignerViewModel : ObservableObject
{
    private readonly ConnectionProfile _connection;
    private readonly string? _database;
    private string _schema;
    private string _table;
    private readonly bool _isNew;
    private List<string> _originalColumns = new();

    public ObservableCollection<DesignColumn> Columns { get; } = new();
    public string[] DataTypes { get; } =
    {
        "int", "bigint", "smallint", "tinyint", "bit",
        "decimal", "numeric", "money", "float", "real",
        "date", "datetime", "datetime2", "time", "datetimeoffset",
        "char", "varchar", "nchar", "nvarchar", "text", "ntext",
        "uniqueidentifier", "varbinary", "binary", "xml"
    };

    [ObservableProperty] private string tableName;
    [ObservableProperty] private string generatedSql = "";
    [ObservableProperty] private string messages = "";

    public TableDesignerViewModel(ConnectionProfile connection, string? database, string schema, string table, bool isNew)
    {
        _connection = connection;
        _database = database;
        _schema = schema;
        _table = table;
        _isNew = isNew;
        tableName = table;

        Columns.CollectionChanged += OnColumnsChanged;

        if (_isNew)
        {
            AddColumn();
            Generate();
        }
        else
        {
            _ = LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var cols = await SqlServerService.GetColumnDetailsAsync(_connection.BuildConnectionString(), _database ?? "", _schema, _table);
            foreach (var c in cols)
            {
                var size = SizeOf(c);
                var dc = new DesignColumn
                {
                    OriginalName = c.Name,
                    OriginalType = c.TypeName,
                    OriginalSize = size,
                    OriginalNullable = c.Nullable,
                    Name = c.Name,
                    Type = c.TypeName,
                    Size = size,
                    Nullable = c.Nullable,
                    Identity = c.Identity,
                    DefaultValue = c.Default,
                    PrimaryKey = c.IsPrimaryKey
                };
                dc.PropertyChanged += OnColumnChanged;
                Columns.Add(dc);
            }
            _originalColumns = cols.Select(c => c.Name).ToList();
            Generate();
        }
        catch (Exception ex)
        {
            Messages = "Could not load columns: " + ex.Message;
        }
    }

    private static string? SizeOf(SqlServerService.ColumnDetail c)
    {
        var t = c.TypeName.ToLowerInvariant();
        return t switch
        {
            "char" or "varchar" or "binary" or "varbinary" => c.MaxLength == -1 ? "max" : c.MaxLength.ToString(),
            "nchar" or "nvarchar" => c.MaxLength == -1 ? "max" : (c.MaxLength / 2).ToString(),
            "decimal" or "numeric" => $"{c.Precision},{c.Scale}",
            _ => null
        };
    }

    [RelayCommand]
    private void AddColumn()
    {
        var dc = new DesignColumn { Name = "Column" + (Columns.Count + 1) };
        dc.PropertyChanged += OnColumnChanged;
        Columns.Add(dc);
    }

    [RelayCommand]
    private void RemoveColumn(DesignColumn? c)
    {
        if (c is null) return;
        c.PropertyChanged -= OnColumnChanged;
        Columns.Remove(c);
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql)) return;
        Messages = "Saving…";
        try
        {
            await SqlServerService.ExecuteAsync(_connection.BuildConnectionString(), _database ?? "", GeneratedSql);
            Messages = "Saved successfully.";
            if (_isNew)
            {
                // Re-target as an existing table so further edits diff correctly.
                Refresh();
            }
        }
        catch (Exception ex)
        {
            Messages = "Error: " + ex.Message;
        }
    }

    private async void Refresh()
    {
        _table = TableName;
        foreach (var c in Columns) c.PropertyChanged -= OnColumnChanged;
        Columns.Clear();
        await LoadAsyncAsExisting();
    }

    private async Task LoadAsyncAsExisting()
    {
        // Reload after creating a new table so subsequent saves use ALTER diff.
        try
        {
            var cols = await SqlServerService.GetColumnDetailsAsync(_connection.BuildConnectionString(), _database ?? "", _schema, _table);
            _originalColumns = cols.Select(c => c.Name).ToList();
        }
        catch { /* ignore */ }
    }

    // ---- SQL generation --------------------------------------------------

    private string Fq => $"[{_schema}].[{TableName}]";

    private static string FullType(DesignColumn c)
    {
        var t = c.Type.ToLowerInvariant();
        if (t is "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary")
            return $"{c.Type}({(string.IsNullOrWhiteSpace(c.Size) ? "50" : c.Size)})";
        if (t is "decimal" or "numeric")
            return $"{c.Type}({(string.IsNullOrWhiteSpace(c.Size) ? "18,0" : c.Size)})";
        return c.Type;
    }

    private void Generate()
    {
        var valid = Columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (valid.Count == 0) { GeneratedSql = ""; return; }

        GeneratedSql = _isNew ? GenerateCreate(valid) : GenerateAlter(valid);
    }

    private string GenerateCreate(List<DesignColumn> cols)
    {
        var sb = new StringBuilder($"CREATE TABLE [{_schema}].[{TableName}] (\n");
        var lines = new List<string>();
        foreach (var c in cols)
        {
            var line = new StringBuilder($"  [{c.Name}] {FullType(c)}");
            if (c.Identity) line.Append(" IDENTITY(1,1)");
            line.Append(c.Nullable ? " NULL" : " NOT NULL");
            if (!string.IsNullOrWhiteSpace(c.DefaultValue)) line.Append($" DEFAULT {c.DefaultValue}");
            lines.Add(line.ToString());
        }
        var pk = cols.Where(c => c.PrimaryKey).Select(c => $"[{c.Name}]").ToList();
        if (pk.Count > 0)
            lines.Add($"  CONSTRAINT [PK_{TableName}] PRIMARY KEY ({string.Join(", ", pk)})");

        sb.Append(string.Join(",\n", lines)).Append("\n)");
        return sb.ToString();
    }

    private string GenerateAlter(List<DesignColumn> cols)
    {
        var statements = new List<string>();
        var currentOriginals = cols.Where(c => c.OriginalName is not null)
            .Select(c => c.OriginalName!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Dropped columns
        foreach (var orig in _originalColumns)
            if (!currentOriginals.Contains(orig))
                statements.Add($"ALTER TABLE {Fq} DROP COLUMN [{orig}]");

        // Existing columns: rename and/or alter
        foreach (var c in cols.Where(c => c.OriginalName is not null))
        {
            if (!string.Equals(c.Name, c.OriginalName, StringComparison.Ordinal))
                statements.Add($"EXEC sp_rename '{_schema}.{TableName}.{c.OriginalName}', '{c.Name}', 'COLUMN'");

            var newType = FullType(c);
            var oldType = FullType(new DesignColumn { Type = c.OriginalType ?? "int", Size = c.OriginalSize });
            if (!string.Equals(newType, oldType, StringComparison.OrdinalIgnoreCase) || c.Nullable != c.OriginalNullable)
                statements.Add($"ALTER TABLE {Fq} ALTER COLUMN [{c.Name}] {newType} {(c.Nullable ? "NULL" : "NOT NULL")}");
        }

        // New columns
        foreach (var c in cols.Where(c => c.OriginalName is null && !string.IsNullOrWhiteSpace(c.Name)))
        {
            var line = new StringBuilder($"ALTER TABLE {Fq} ADD [{c.Name}] {FullType(c)}");
            if (c.Identity) line.Append(" IDENTITY(1,1)");
            line.Append(c.Nullable ? " NULL" : " NOT NULL");
            if (!string.IsNullOrWhiteSpace(c.DefaultValue)) line.Append($" DEFAULT {c.DefaultValue}");
            statements.Add(line.ToString());
        }

        if (statements.Count == 0)
            return "-- No changes. (Primary-key / identity changes on existing tables aren't applied here.)";
        return string.Join(";\n", statements) + ";";
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Generate();
    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e) => Generate();
}
