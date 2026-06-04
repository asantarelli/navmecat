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
    [ObservableProperty] private bool showClarionTypes = true;

    /// <summary>Columns detected as Clarion dates/times in the current data, by kind.</summary>
    public Dictionary<string, ClarionKind> ClarionColumns { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Manual per-column overrides set via the header right-click menu.
    /// A present key wins over detection; a null value forces "plain number".
    /// </summary>
    public Dictionary<string, ClarionKind?> ClarionOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool HasClarionTypes => ClarionColumns.Count > 0;

    /// <summary>Resolves how a column should be displayed: date, time, or plain (null).</summary>
    public ClarionKind? GetEffectiveKind(string column)
    {
        if (!ShowClarionTypes) return null;
        if (ClarionOverrides.TryGetValue(column, out var ov)) return ov;
        return ClarionColumns.TryGetValue(column, out var k) ? k : null;
    }

    public bool HasOverride(string column) => ClarionOverrides.ContainsKey(column);

    public void SetClarionOverride(string column, ClarionKind? kind)
    {
        ClarionOverrides[column] = kind;
        RefreshView();
    }

    public void ClearClarionOverride(string column)
    {
        if (ClarionOverrides.Remove(column))
            RefreshView();
    }

    public string ClarionToggleLabel
    {
        get
        {
            if (!HasClarionTypes) return "Clarion dates/times";
            var dates = ClarionColumns.Values.Count(k => k == ClarionKind.Date);
            var times = ClarionColumns.Values.Count(k => k == ClarionKind.Time);
            if (dates > 0 && times > 0) return $"Clarion dates/times ({dates}+{times})";
            return dates > 0 ? $"Clarion dates ({dates})" : $"Clarion times ({times})";
        }
    }

    public event Action<TableTabViewModel>? CloseRequested;

    partial void OnShowClarionTypesChanged(bool value) => RefreshView();

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

            // Detect Clarion date/time columns before the grid generates its columns.
            ClarionColumns = ClarionDetector.Detect(_session.Data);
            OnPropertyChanged(nameof(HasClarionTypes));
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
