using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.ViewModels;

public enum NodeType { Server, Database, Schema, Table, Message }

/// <summary>
/// A node in the connection tree. Children are loaded lazily the first time
/// the node is expanded. Server → Database → Schema → Table.
/// </summary>
public partial class DbTreeNode : ObservableObject
{
    public NodeType Type { get; private init; }
    public string Name { get; private init; } = "";
    public ConnectionProfile Connection { get; private init; } = null!;
    public string? Database { get; private init; }
    public string? Schema { get; private init; }

    public ObservableCollection<DbTreeNode> Children { get; } = new();

    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasError;

    private bool _loaded;
    public bool IsLeaf => Type is NodeType.Table or NodeType.Message;

    // ---- factory helpers -------------------------------------------------

    public static DbTreeNode Server(ConnectionProfile c) =>
        WithPlaceholder(new DbTreeNode { Type = NodeType.Server, Name = c.Name, Connection = c });

    private static DbTreeNode DatabaseNode(ConnectionProfile c, string db) =>
        WithPlaceholder(new DbTreeNode { Type = NodeType.Database, Name = db, Connection = c, Database = db });

    private static DbTreeNode SchemaNode(ConnectionProfile c, string db, string schema) =>
        WithPlaceholder(new DbTreeNode { Type = NodeType.Schema, Name = schema, Connection = c, Database = db, Schema = schema });

    private static DbTreeNode TableNode(ConnectionProfile c, string db, string schema, string table) =>
        new() { Type = NodeType.Table, Name = table, Connection = c, Database = db, Schema = schema };

    private static DbTreeNode Message(string text) =>
        new() { Type = NodeType.Message, Name = text };

    private static DbTreeNode WithPlaceholder(DbTreeNode node)
    {
        // A dummy child makes the expander arrow appear before real children load.
        node.Children.Add(Message("Loading…"));
        return node;
    }

    // ---- lazy loading ----------------------------------------------------

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && !IsLeaf)
            _ = LoadChildrenAsync();
    }

    public void Reset()
    {
        _loaded = false;
        HasError = false;
        Children.Clear();
        if (!IsLeaf) Children.Add(Message("Loading…"));
    }

    public async Task LoadChildrenAsync()
    {
        if (IsLeaf) return;
        _loaded = true;
        IsLoading = true;
        HasError = false;
        try
        {
            var connStr = Connection.BuildConnectionString();
            var items = new List<DbTreeNode>();

            switch (Type)
            {
                case NodeType.Server:
                    foreach (var db in await SqlServerService.GetDatabasesAsync(connStr))
                        items.Add(DatabaseNode(Connection, db));
                    break;
                case NodeType.Database:
                    foreach (var schema in await SqlServerService.GetSchemasAsync(connStr, Database!))
                        items.Add(SchemaNode(Connection, Database!, schema));
                    break;
                case NodeType.Schema:
                    foreach (var table in await SqlServerService.GetTablesAsync(connStr, Database!, Schema!))
                        items.Add(TableNode(Connection, Database!, Schema!, table));
                    break;
            }

            Children.Clear();
            if (items.Count == 0)
                Children.Add(Message("(empty)"));
            else
                foreach (var n in items) Children.Add(n);
        }
        catch (Exception ex)
        {
            HasError = true;
            _loaded = false;
            Children.Clear();
            Children.Add(Message(ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }
}
