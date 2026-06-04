using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavMeCat.Models;
using NavMeCat.Services;
using NavMeCat.Views;

namespace NavMeCat.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConnectionStore _store = new();
    private EditableTableSession? _session;
    private DbTreeNode? _openNode;

    public ObservableCollection<DbTreeNode> Roots { get; } = new();

    [ObservableProperty] private DbTreeNode? selectedNode;
    [ObservableProperty] private DataView? gridData;
    [ObservableProperty] private string? currentTableLabel;
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private int rowLimit = 1000;

    public MainViewModel()
    {
        foreach (var profile in _store.Load())
            Roots.Add(DbTreeNode.Server(profile));
    }

    private void Persist() =>
        _store.Save(Roots.Where(r => r.Type == NodeType.Server).Select(r => r.Connection));

    // ---- connection management ------------------------------------------

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

    // ---- data viewing / editing -----------------------------------------

    [RelayCommand]
    private async Task OpenTable(DbTreeNode? node)
    {
        node ??= SelectedNode;
        if (node is not { Type: NodeType.Table }) return;

        IsBusy = true;
        StatusText = $"Loading {node.Database}.{node.Schema}.{node.Name}…";
        try
        {
            DetachSession();
            _openNode = node;
            _session = await EditableTableSession.OpenAsync(
                node.Connection.BuildConnectionString(),
                node.Database!, node.Schema!, node.Name, RowLimit);

            _session.Data.RowChanged += OnDataChanged;
            _session.Data.RowDeleted += OnDataChanged;

            GridData = _session.Data.DefaultView;
            CurrentTableLabel = _session.Identifier;
            HasUnsavedChanges = false;
            StatusText = $"Loaded {_session.Data.Rows.Count} row(s) from {_session.Identifier} (limit {RowLimit}).";
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Could not open table", ex.Message);
            StatusText = "Failed to open table.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadData()
    {
        if (_openNode is not null)
            await OpenTable(_openNode);
    }

    [RelayCommand]
    private async Task SaveChanges()
    {
        if (_session is null || !_session.HasChanges) return;

        IsBusy = true;
        try
        {
            var affected = await _session.SaveAsync();
            HasUnsavedChanges = false;
            StatusText = $"Saved {affected} change(s) to {_session.Identifier}.";
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Save failed", ex.Message +
                "\n\nIn-place editing requires the table to have a primary key.");
            StatusText = "Save failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnDataChanged(object? sender, DataRowChangeEventArgs e)
        => HasUnsavedChanges = _session?.HasChanges ?? false;

    private void DetachSession()
    {
        if (_session is null) return;
        _session.Data.RowChanged -= OnDataChanged;
        _session.Data.RowDeleted -= OnDataChanged;
        _session.Dispose();
        _session = null;
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
