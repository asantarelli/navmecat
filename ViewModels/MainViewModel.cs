using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavMeCat.Models;
using NavMeCat.Services;
using NavMeCat.Views;

namespace NavMeCat.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConnectionStore _store = new();

    public ObservableCollection<DbTreeNode> Roots { get; } = new();
    public ObservableCollection<TableTabViewModel> OpenTabs { get; } = new();

    [ObservableProperty] private DbTreeNode? selectedNode;
    [ObservableProperty] private TableTabViewModel? selectedTab;
    [ObservableProperty] private string treeFilter = "";
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isBusy;

    /// <summary>Row limit applied when opening a new tab.</summary>

    public MainViewModel()
    {
        foreach (var profile in _store.Load())
            Roots.Add(DbTreeNode.Server(profile));
    }

    private void Persist() =>
        _store.Save(Roots.Where(r => r.Type == NodeType.Server).Select(r => r.Connection));

    partial void OnTreeFilterChanged(string value)
    {
        DbTreeNode.ActiveFilter = value;
        foreach (var root in Roots) root.ApplyFilter(value);
    }

    // ---- connection management ------------------------------------------

    [RelayCommand]
    private void OpenQuery()
    {
        var node = SelectedNode;
        var connection = node?.Connection
            ?? Roots.FirstOrDefault(r => r.Type == NodeType.Server)?.Connection;
        if (connection is null)
        {
            StatusText = "Add a connection first.";
            return;
        }
        new Views.QueryWindow(connection, node?.Database).Show();
        StatusText = $"Opened a query window for '{connection.Name}'.";
    }

    [RelayCommand]
    private void OpenQueryBuilder()
    {
        var node = SelectedNode;
        var connection = node?.Connection
            ?? Roots.FirstOrDefault(r => r.Type == NodeType.Server)?.Connection;
        if (connection is null)
        {
            StatusText = "Add a connection first.";
            return;
        }
        new Views.QueryBuilderWindow(connection, node?.Database).Show();
        StatusText = $"Opened the query builder for '{connection.Name}'.";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (new Views.SettingsDialog().ShowDialog() == true)
            StatusText = "Settings saved (applies to tables opened from now on).";
    }

    private const string RepoUrl = "https://github.com/robertorenz/navmecat";

    [RelayCommand]
    private static void ExitApp() => System.Windows.Application.Current?.Shutdown();

    [RelayCommand]
    private void ShowAbout()
    {
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        var version = v is null ? "" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        Dialogs.ShowMessage("About NavMeCat",
            $"NavMeCat — SQL Server Manager\n{version}\n\nA Navicat-style database manager for SQL Server.\n{RepoUrl}");
    }

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(RepoUrl);

    [RelayCommand]
    private void OpenReleases() => OpenUrl(RepoUrl + "/releases/latest");

    [RelayCommand]
    private void OpenDocs() => OpenUrl(RepoUrl + "#readme");

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* no browser available */ }
    }

    [RelayCommand]
    private void AddConnection()
    {
        var profile = new ConnectionProfile();
        if (Dialogs.EditConnection(profile))
        {
            Roots.Add(DbTreeNode.Server(profile));
            Persist();
            StatusText = $"Added connection '{profile.Name}'.";
        }
    }

    [RelayCommand]
    private void EditConnection(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Server }) return;

        var edited = node.Connection.Clone();
        if (Dialogs.EditConnection(edited))
        {
            // Copy edited values back into the live profile and rebuild the node.
            var idx = Roots.IndexOf(node);
            CopyInto(edited, node.Connection);
            Roots[idx] = DbTreeNode.Server(node.Connection);
            Persist();
            StatusText = $"Updated connection '{node.Connection.Name}'.";
        }
    }

    [RelayCommand]
    private void RemoveConnection(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Server }) return;

        if (Dialogs.Confirm("Remove connection",
                $"Remove the connection '{node.Connection.Name}'?"))
        {
            Roots.Remove(node);
            Persist();
            StatusText = "Connection removed.";
        }
    }

    [RelayCommand]
    private async Task RefreshNode(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is null || node.IsLeaf) return;
        node.Reset();
        node.IsExpanded = true;
        await node.LoadChildrenAsync();
        StatusText = $"Refreshed '{node.Name}'.";
    }

    // ---- tabbed data viewing / editing ----------------------------------

    [RelayCommand]
    private async Task OpenTable(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is null || !node.IsOpenable) return;

        // Already open? Just switch to it.
        var key = TableTabViewModel.MakeKey(node);
        var existing = OpenTabs.FirstOrDefault(t => t.Key == key);
        if (existing is not null)
        {
            SelectedTab = existing;
            StatusText = $"Switched to {existing.Identifier}.";
            return;
        }

        var tab = new TableTabViewModel(node, SettingsStore.Current.DefaultRowLimit,
            s => StatusText = s, b => IsBusy = b);
        tab.CloseRequested += CloseTab;
        OpenTabs.Add(tab);
        SelectedTab = tab;

        if (!await tab.LoadAsync())
            CloseTab(tab); // load failed — don't leave an empty tab behind
    }

    private static string RoutineKind(NodeType type) => type switch
    {
        NodeType.Procedure => "Procedure",
        NodeType.View => "View",
        _ => "Function"
    };

    private static string RoutineKeyword(NodeType type) => type switch
    {
        NodeType.Procedure => "PROCEDURE",
        NodeType.View => "VIEW",
        _ => "FUNCTION"
    };

    [RelayCommand]
    private void DesignTable(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Table }) return;
        new Views.TableDesignerWindow(node.Connection, node.Database, node.Schema!, node.Name, isNew: false).Show();
        StatusText = $"Designing {node.Schema}.{node.Name}.";
    }

    [RelayCommand]
    private async Task GenerateInserts(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Table or NodeType.View }) return;
        StatusText = $"Generating INSERT script for {node.Schema}.{node.Name}…";
        try
        {
            var script = await ScriptService.GenerateInsertsAsync(
                node.Connection.BuildConnectionString(), node.Database ?? "", node.Schema!, node.Name,
                SettingsStore.Current.DefaultRowLimit);
            new Views.ScriptViewerWindow($"INSERT script — {node.Schema}.{node.Name}", script, $"{node.Name}_inserts").Show();
            StatusText = $"Generated INSERT script for {node.Schema}.{node.Name}.";
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Generate INSERT failed", ex.Message);
        }
    }

    [RelayCommand]
    private void ImportData(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Table }) return;
        new Views.ImportDialog(node.Connection, node.Database ?? "", node.Schema!, node.Name).ShowDialog();
    }

    [RelayCommand]
    private void NewTable(DbTreeNode? category)
    {
        category ??= SelectedNode;
        // Accept the Tables category or a schema node.
        var schema = category?.Schema ?? "dbo";
        var connection = category?.Connection ?? Roots.FirstOrDefault(r => r.Type == NodeType.Server)?.Connection;
        if (connection is null) { StatusText = "Add a connection first."; return; }
        new Views.TableDesignerWindow(connection, category?.Database, schema, "NewTable", isNew: true).Show();
    }

    private static async Task<bool> ConfirmDrop(DbTreeNode node, string keyword, string extra = "")
    {
        var dependents = new List<string>();
        try
        {
            dependents = await SqlServerService.GetDependentsAsync(
                node.Connection.BuildConnectionString(), node.Database ?? "", node.Schema!, node.Name);
        }
        catch { /* dependency check is best-effort */ }

        var msg = $"Permanently drop {keyword.ToLowerInvariant()} {node.Schema}.{node.Name}?{extra}";
        if (dependents.Count > 0)
            msg += "\n\n⚠ These objects reference it:\n• " + string.Join("\n• ", dependents.Take(15)) +
                   (dependents.Count > 15 ? $"\n…and {dependents.Count - 15} more" : "");
        return Dialogs.Confirm("Drop " + keyword.ToLowerInvariant(), msg);
    }

    [RelayCommand]
    private async Task DropTable(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Table }) return;
        if (!await ConfirmDrop(node, "TABLE", " All its data will be lost."))
            return;
        try
        {
            await SqlServerService.ExecuteAsync(node.Connection.BuildConnectionString(), node.Database ?? "",
                $"DROP TABLE [{node.Schema}].[{node.Name}]");
            StatusText = $"Dropped table {node.Schema}.{node.Name}.";
            if (node.Parent is not null) await RefreshNode(node.Parent);
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Drop failed", ex.Message);
        }
    }

    [RelayCommand]
    private void EditRoutine(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Function or NodeType.Procedure or NodeType.View }) return;
        var kind = RoutineKind(node.Type);
        new Views.RoutineEditorWindow(node.Connection, node.Database, node.Schema!, node.Name, kind).Show();
        StatusText = $"Editing {kind.ToLowerInvariant()} {node.Schema}.{node.Name}.";
    }

    /// <summary>Opens the routine editor with a template (node is a Functions/Procedures/Views category).</summary>
    [RelayCommand]
    private void NewRoutine(DbTreeNode? category)
    {
        category ??= SelectedNode;
        if (category is not { Type: NodeType.Category } c) return;
        var schema = c.Schema ?? "dbo";
        var (name, kind, template) = c.CategoryChildType switch
        {
            NodeType.Procedure => ("NewProcedure", "Procedure",
                $"CREATE PROCEDURE [{schema}].[NewProcedure]\n    @Param1 int = 0\nAS\nBEGIN\n    SET NOCOUNT ON;\n    SELECT @Param1 AS Result;\nEND"),
            NodeType.View => ("NewView", "View",
                $"CREATE VIEW [{schema}].[NewView]\nAS\nSELECT 1 AS Col1"),
            _ => ("NewFunction", "Function",
                $"CREATE FUNCTION [{schema}].[NewFunction] (@Param1 int)\nRETURNS int\nAS\nBEGIN\n    RETURN @Param1;\nEND")
        };
        new Views.RoutineEditorWindow(c.Connection, c.Database, schema, name, kind, template).Show();
    }

    [RelayCommand]
    private void ExecuteRoutine(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Function or NodeType.Procedure }) return;
        var qualified = $"[{node.Schema}].[{node.Name}]";
        var sql = node.Type == NodeType.Procedure
            ? $"EXEC {qualified} "
            : $"-- Scalar function: SELECT {qualified}(/* args */)\n-- Table function: SELECT * FROM {qualified}(/* args */)\nSELECT {qualified}()";
        new Views.QueryWindow(node.Connection, node.Database, sql).Show();
    }

    [RelayCommand]
    private async Task DropRoutine(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Function or NodeType.Procedure or NodeType.View }) return;
        var keyword = RoutineKeyword(node.Type);
        if (!await ConfirmDrop(node, keyword))
            return;
        try
        {
            await SqlServerService.ExecuteAsync(node.Connection.BuildConnectionString(), node.Database ?? "",
                $"DROP {keyword} [{node.Schema}].[{node.Name}]");
            StatusText = $"Dropped {keyword.ToLowerInvariant()} {node.Schema}.{node.Name}.";
            if (node.Parent is not null) await RefreshNode(node.Parent);
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Drop failed", ex.Message);
        }
    }

    private void CloseTab(TableTabViewModel tab)
    {
        if (tab.HasUnsavedChangesNow &&
            !Dialogs.Confirm("Close tab",
                $"'{tab.Identifier}' has unsaved changes. Close anyway?"))
            return;

        tab.CloseRequested -= CloseTab;
        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        tab.Dispose();

        if (SelectedTab == tab)
            SelectedTab = OpenTabs.Count > 0
                ? OpenTabs[Math.Min(index, OpenTabs.Count - 1)]
                : null;
    }

    private static void CopyInto(ConnectionProfile from, ConnectionProfile to)
    {
        to.Name = from.Name;
        to.Server = from.Server;
        to.Database = from.Database;
        to.IntegratedSecurity = from.IntegratedSecurity;
        to.Username = from.Username;
        to.Password = from.Password;
        to.Encrypt = from.Encrypt;
        to.TrustServerCertificate = from.TrustServerCertificate;
        to.UseRawConnectionString = from.UseRawConnectionString;
        to.RawConnectionString = from.RawConnectionString;
    }
}
