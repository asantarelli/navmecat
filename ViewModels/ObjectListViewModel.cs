using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavMeCat.Models;
using NavMeCat.Services;

namespace NavMeCat.ViewModels;

/// <summary>Navicat-style object list for a container (a Tables folder, or a MongoDB database).</summary>
public partial class ObjectListViewModel : ObservableObject
{
    private readonly DbTreeNode _container;
    private readonly Action<string> _open;
    private readonly Action<string> _design;
    private readonly Action<string> _delete;
    private readonly Action _new;

    public ObservableCollection<ObjectListItem> Items { get; } = new();

    [ObservableProperty] private ObjectListItem? selectedItem;
    [ObservableProperty] private string title = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string countText = "";

    private readonly DatabaseEngine _engine;

    /// <summary>Design / New are only meaningful where a designer exists.</summary>
    public bool CanDesign => _engine is DatabaseEngine.SqlServer or DatabaseEngine.Sqlite;
    public bool CanCreate => _engine is DatabaseEngine.SqlServer or DatabaseEngine.Sqlite;

    public ObjectListViewModel(DbTreeNode container,
        Action<string> open, Action<string> design, Action<string> delete, Action @new)
    {
        _container = container;
        _engine = container.Connection.Engine;
        _open = open;
        _design = design;
        _delete = delete;
        _new = @new;
        Title = container.Connection.Engine == DatabaseEngine.MongoDb
            ? $"{container.Name} — collections"
            : $"{container.Database}.{container.Schema} — tables";
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var items = await ObjectListService.LoadTablesAsync(
                _container.Connection, _container.Database ?? _container.Name, _container.Schema ?? "");
            Items.Clear();
            foreach (var i in items) Items.Add(i);
            CountText = $"{Items.Count} object(s)";
        }
        catch (Exception ex)
        {
            CountText = "Error: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Open()
    {
        if (SelectedItem is not null) _open(SelectedItem.Name);
    }

    [RelayCommand]
    private void Design()
    {
        if (SelectedItem is not null) _design(SelectedItem.Name);
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedItem is not null) _delete(SelectedItem.Name);
    }

    [RelayCommand]
    private void New() => _new();

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();
}
