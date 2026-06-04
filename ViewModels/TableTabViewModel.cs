using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NavMeCat.Services;
using NavMeCat.Views;

namespace NavMeCat.ViewModels;

/// <summary>
/// One open table, shown as a tab. Owns its own editable session and data,
/// independent of any other open tab.
/// </summary>
public partial class TableTabViewModel : ObservableObject, IDisposable
{
    private EditableTableSession? _session;
    private readonly Action<string> _setStatus;
    private readonly Action<bool> _setBusy;

    public DbTreeNode Node { get; }
    /// <summary>Uniquely identifies the table so duplicate tabs aren't opened.</summary>
    public string Key { get; }
    /// <summary>Tab caption.</summary>
    public string Header { get; }
    /// <summary>Fully-qualified name shown in the tab's toolbar.</summary>
    public string Identifier { get; }

    [ObservableProperty] private DataView? gridData;
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private int rowLimit;
    [ObservableProperty] private bool showClarionDates = true;

    /// <summary>Column names detected as Clarion dates in the current data.</summary>
    public HashSet<string> ClarionDateColumns { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasClarionDates => ClarionDateColumns.Count > 0;
    public string ClarionToggleLabel =>
        HasClarionDates ? $"Clarion dates ({ClarionDateColumns.Count})" : "Clarion dates";

    public event Action<TableTabViewModel>? CloseRequested;

    partial void OnShowClarionDatesChanged(bool value) => RefreshView();

    /// <summary>Re-projects the same data so the grid regenerates columns (no DB round-trip).</summary>
    private void RefreshView()
    {
        if (_session is not null)
            GridData = new DataView(_session.Data);
    }

    public TableTabViewModel(DbTreeNode node, int rowLimit,
        Action<string> setStatus, Action<bool> setBusy)
    {
        Node = node;
        this.rowLimit = rowLimit;
        _setStatus = setStatus;
        _setBusy = setBusy;
        Key = MakeKey(node);
        Identifier = $"{node.Database}.{node.Schema}.{node.Name}";
        Header = node.Name;
    }

    public static string MakeKey(DbTreeNode n) =>
        $"{n.Connection.Id}|{n.Database}|{n.Schema}|{n.Name}";

    public async Task<bool> LoadAsync()
    {
        _setBusy(true);
        _setStatus($"Loading {Identifier}…");
        try
        {
            Detach();
            _session = await EditableTableSession.OpenAsync(
                Node.Connection.BuildConnectionString(),
                Node.Database!, Node.Schema!, Node.Name, RowLimit);

            _session.Data.RowChanged += OnDataChanged;
            _session.Data.RowDeleted += OnDataChanged;

            // Detect Clarion-date columns before the grid generates its columns.
            ClarionDateColumns = ClarionDateDetector.Detect(_session.Data);
            OnPropertyChanged(nameof(HasClarionDates));
            OnPropertyChanged(nameof(ClarionToggleLabel));

            GridData = _session.Data.DefaultView;
            HasUnsavedChanges = false;
            _setStatus($"Loaded {_session.Data.Rows.Count} row(s) from {Identifier} (limit {RowLimit}).");
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Could not open table", ex.Message);
            _setStatus("Failed to open table.");
            return false;
        }
        finally
        {
            _setBusy(false);
        }
    }

    [RelayCommand]
    private async Task Reload() => await LoadAsync();

    [RelayCommand]
    private async Task SaveChanges()
    {
        if (_session is null || !_session.HasChanges) return;
        _setBusy(true);
        try
        {
            var affected = await _session.SaveAsync();
            HasUnsavedChanges = false;
            _setStatus($"Saved {affected} change(s) to {Identifier}.");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Save failed", ex.Message +
                "\n\nIn-place editing requires the table to have a primary key.");
            _setStatus("Save failed.");
        }
        finally
        {
            _setBusy(false);
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this);

    public bool HasUnsavedChangesNow => _session?.HasChanges ?? false;

    private void OnDataChanged(object? sender, DataRowChangeEventArgs e)
        => HasUnsavedChanges = _session?.HasChanges ?? false;

    private void Detach()
    {
        if (_session is null) return;
        _session.Data.RowChanged -= OnDataChanged;
        _session.Data.RowDeleted -= OnDataChanged;
        _session.Dispose();
        _session = null;
    }

    public void Dispose() => Detach();
}
