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
    private void OpenSettings()
    {
        if (new Views.SettingsDialog().ShowDialog() == true)
            StatusText = "Settings saved (applies to tables opened from now on).";
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
