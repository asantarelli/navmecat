using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
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

    [ObservableProperty] private bool showDetailPanel;
    [ObservableProperty] private string? detailColumn;
    [ObservableProperty] private string detailText = "";
    [ObservableProperty] private string detailHex = "";
    [ObservableProperty] private System.Windows.Media.ImageSource? detailImage;
    [ObservableProperty] private string detailHtml = "";
    [ObservableProperty] private bool viewMenuOpen;
    [ObservableProperty] private CellViewMode viewMode = CellViewMode.Auto;

    [ObservableProperty] private bool detailPopped;
    [ObservableProperty] private bool showSqlPanel;
    [ObservableProperty] private string sqlPreview = "";
    [ObservableProperty] private bool sqlPopped;

    /// <summary>Resizable heights of the docked panes (pixels), driven by their drag handles.</summary>
    [ObservableProperty] private double detailPaneHeight = 240;
    [ObservableProperty] private double sqlPaneHeight = 240;

    private bool _sqlRefreshQueued;

    public string PaneTitleSuffix => Identifier;

    private CellViewMode _effectiveViewMode = CellViewMode.Text;
    public bool IsTextMode => _effectiveViewMode == CellViewMode.Text;
    public bool IsHexMode => _effectiveViewMode == CellViewMode.Hex;
    public bool IsImageMode => _effectiveViewMode == CellViewMode.Image;
    public bool IsWebMode => _effectiveViewMode == CellViewMode.Web;
    public string ViewModeLabel => _effectiveViewMode.ToString();
    /// <summary>Apply is only meaningful for editable text on a string column.</summary>
    public bool CanApplyDetail => IsTextMode && _detailIsString;

    private DataRowView? _detailRow;
    private string? _detailColumnName;
    private byte[]? _detailBytes;
    private bool _detailIsString;

    private readonly RowIdentityStore _identityStore = new();
    private string? _identityKey;

    // ---- structure inspector ---------------------------------------------
    [ObservableProperty] private bool showInspector;
    [ObservableProperty] private bool inspectorPopped;
    [ObservableProperty] private double inspectorWidth = 400;
    [ObservableProperty] private string inspectorContent = "";
    [ObservableProperty] private InspectorSection inspectorSection = InspectorSection.Ddl;

    private TableStructure? _structure;

    public bool IsInfoSection => InspectorSection == InspectorSection.Info;
    public bool IsDdlSection => InspectorSection == InspectorSection.Ddl;
    public bool IsRelSection => InspectorSection == InspectorSection.Relationships;
    public string PaneTitle => Identifier;

    /// <summary>Shown only for tables without a primary key / unique index.</summary>
    public bool CanPickRowIdentity => _session is not null && !_session.HasNaturalKey;

    // ---- filter / sort ---------------------------------------------------
    public ObservableCollection<string> ColumnNames { get; } = new();
    public ObservableCollection<SortLevel> SortLevels { get; } = new();
    public ObservableCollection<FilterCondition> FilterConditions { get; } = new();
    [ObservableProperty] private bool filterMatchAll = true;
    [ObservableProperty] private bool hasActiveSort;
    [ObservableProperty] private bool hasActiveFilter;

    private string _sortExpression = "";
    private string _filterExpression = "";

    public Array SortDirections { get; } = Enum.GetValues(typeof(SortDirection));
    public Array FilterOperators { get; } = Enum.GetValues(typeof(FilterOperator));

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

    /// <summary>Re-projects the same data (applying current filter+sort) so the grid refreshes.</summary>
    private void RefreshView()
    {
        ProjectView();
        _detailRow = null; // old view's row handle is stale after re-projection
    }

    /// <summary>Builds the bound DataView from the session, applying the current filter and sort.</summary>
    private void ProjectView()
    {
        if (_session is null) return;
        var view = new DataView(_session.Data);
        if (!string.IsNullOrEmpty(_filterExpression))
        {
            try { view.RowFilter = _filterExpression; } catch { /* keep unfiltered */ }
        }
        if (!string.IsNullOrEmpty(_sortExpression))
        {
            try { view.Sort = _sortExpression; } catch { /* keep unsorted */ }
        }
        GridData = view;
    }

    // ---- cell detail panel ----------------------------------------------

    /// <summary>Called by the grid when the current cell changes.</summary>
    public void SetDetail(DataRowView? row, string? column, object? value)
    {
        _detailRow = row;
        _detailColumnName = column;
        DetailColumn = column;

        var actual = value is DBNull ? null : value;
        if (actual is byte[] bytes)
        {
            _detailBytes = bytes;
            _detailIsString = false;
            DetailText = $"(binary — {bytes.Length:N0} byte(s))";
        }
        else
        {
            var text = actual?.ToString() ?? "";
            DetailText = text;
            _detailBytes = System.Text.Encoding.UTF8.GetBytes(text);
            _detailIsString = actual is string || actual is null;
        }

        UpdateDerived();
    }

    [RelayCommand]
    private void SetViewMode(CellViewMode mode)
    {
        ViewMode = mode;
        ShowDetailPanel = true;
        ViewMenuOpen = false;
    }

    partial void OnViewModeChanged(CellViewMode value) => UpdateDerived();

    partial void OnDetailTextChanged(string value)
    {
        // Keep the Web view in sync while the user is in text/web on a string.
        if (IsWebMode) DetailHtml = value;
    }

    private CellViewMode ResolveMode()
    {
        if (ViewMode != CellViewMode.Auto) return ViewMode;
        if (CellContent.LooksLikeImage(_detailBytes)) return CellViewMode.Image;
        if (_detailIsString && CellContent.LooksLikeHtml(DetailText)) return CellViewMode.Web;
        if (!_detailIsString) return CellViewMode.Hex;
        return CellViewMode.Text;
    }

    private void UpdateDerived()
    {
        _effectiveViewMode = ResolveMode();

        DetailHex = _effectiveViewMode == CellViewMode.Hex ? CellContent.BuildHexDump(_detailBytes) : "";
        DetailImage = _effectiveViewMode == CellViewMode.Image ? CellContent.TryLoadImage(_detailBytes) : null;
        DetailHtml = _effectiveViewMode == CellViewMode.Web ? DetailText : "";

        OnPropertyChanged(nameof(IsTextMode));
        OnPropertyChanged(nameof(IsHexMode));
        OnPropertyChanged(nameof(IsImageMode));
        OnPropertyChanged(nameof(IsWebMode));
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(CanApplyDetail));
    }

    [RelayCommand]
    private void HideDetailPanel()
    {
        PaneService.ClosePopOut(this, PaneKind.Detail);
        ShowDetailPanel = false;
        ViewMenuOpen = false;
    }

    [RelayCommand]
    private void PinDetail() => PaneService.TogglePopOut(this, PaneKind.Detail);

    [RelayCommand]
    private void PinSql() => PaneService.TogglePopOut(this, PaneKind.Sql);

    // ---- structure inspector --------------------------------------------

    [RelayCommand]
    private void HideInspector()
    {
        PaneService.ClosePopOut(this, PaneKind.Inspector);
        ShowInspector = false;
    }

    [RelayCommand]
    private void PinInspector() => PaneService.TogglePopOut(this, PaneKind.Inspector);

    [RelayCommand]
    private void SetInspectorSection(InspectorSection section)
    {
        InspectorSection = section;
        ShowInspector = true;
    }

    partial void OnShowInspectorChanged(bool value)
    {
        if (value && _structure is null) _ = LoadStructureAsync();
    }

    partial void OnInspectorSectionChanged(InspectorSection value)
    {
        UpdateInspectorContent();
        OnPropertyChanged(nameof(IsInfoSection));
        OnPropertyChanged(nameof(IsDdlSection));
        OnPropertyChanged(nameof(IsRelSection));
    }

    private async Task LoadStructureAsync()
    {
        InspectorContent = "Loading…";
        try
        {
            _structure = await TableMetadataService.GetAsync(
                Node.Connection.BuildConnectionString(), Node.Database!, Node.Schema!, Node.Name);
            UpdateInspectorContent();
        }
        catch (Exception ex)
        {
            InspectorContent = "-- Error loading structure: " + ex.Message;
        }
    }

    /// <summary>Opens the panels the user has chosen to show by default (after the table loads).</summary>
    private void ApplyDefaults()
    {
        var s = SettingsStore.Current;
        ShowClarionTypes = s.ShowClarionTypesByDefault;
        if (s.ShowStructureByDefault)
        {
            InspectorSection = s.DefaultStructureSection;
            ShowInspector = true;
        }
        if (s.ShowSqlByDefault) ShowSqlPanel = true;
        if (s.ShowCellDetailByDefault) ShowDetailPanel = true;
    }

    private void UpdateInspectorContent()
    {
        if (_structure is null) return;
        InspectorContent = InspectorSection switch
        {
            InspectorSection.Info => _structure.Info,
            InspectorSection.Relationships => _structure.Relationships,
            _ => _structure.Ddl
        };
    }

    [RelayCommand]
    private void PickRowIdentity()
    {
        if (_session is null) return;
        var dialog = new RowIdentityDialog(_session);
        if (dialog.ShowDialog() != true) return;

        _session.SetRowIdentity(dialog.SelectedColumns);
        if (_identityKey is not null) _identityStore.Set(_identityKey, dialog.SelectedColumns);
        RefreshSqlPreview();
        _setStatus($"Row identity for {Identifier}: {_session.KeyDescription}.");
    }

    // ---- SQL preview pane ------------------------------------------------

    [RelayCommand]
    private void RefreshSqlPreview()
    {
        if (_session is null) { SqlPreview = ""; return; }
        try
        {
            var list = _session.BuildChangePreview();
            SqlPreview = list.Count == 0
                ? "-- No pending changes."
                : string.Join(";\n\n", list) + ";";
        }
        catch (Exception ex)
        {
            SqlPreview = "-- Error generating preview: " + ex.Message;
        }
    }

    /// <summary>Refreshes the preview after edits settle, so a burst of changes only rebuilds once.</summary>
    private void QueueSqlRefresh()
    {
        if (!ShowSqlPanel || _sqlRefreshQueued) return;
        _sqlRefreshQueued = true;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _sqlRefreshQueued = false;
            RefreshSqlPreview();
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            _sqlRefreshQueued = false;
            if (ShowSqlPanel) RefreshSqlPreview();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    [RelayCommand]
    private void HideSqlPanel()
    {
        PaneService.ClosePopOut(this, PaneKind.Sql);
        ShowSqlPanel = false;
    }

    [RelayCommand]
    private async Task ExecuteSql()
    {
        await SaveChanges();
        RefreshSqlPreview();
    }

    partial void OnShowSqlPanelChanged(bool value)
    {
        if (value) RefreshSqlPreview();
    }

    [RelayCommand]
    private void ApplyDetail()
    {
        if (_detailRow is null || string.IsNullOrEmpty(_detailColumnName)) return;
        try
        {
            var table = _detailRow.Row.Table;
            if (!table.Columns.Contains(_detailColumnName)) return;
            var col = table.Columns[_detailColumnName]!;

            object newValue = col.DataType == typeof(string)
                ? DetailText
                : string.IsNullOrEmpty(DetailText)
                    ? DBNull.Value
                    : Convert.ChangeType(DetailText, col.DataType);

            _detailRow[_detailColumnName] = newValue;
            _setStatus($"Updated '{_detailColumnName}' for the selected row.");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Could not update cell", ex.Message);
        }
    }

    // ---- sort ------------------------------------------------------------

    [RelayCommand]
    private void AddSortLevel() =>
        SortLevels.Add(new SortLevel { Column = ColumnNames.FirstOrDefault() });

    [RelayCommand]
    private void RemoveSortLevel(SortLevel? level)
    {
        if (level is not null) SortLevels.Remove(level);
    }

    [RelayCommand]
    private void ApplySort()
    {
        var parts = SortLevels
            .Where(s => !string.IsNullOrEmpty(s.Column))
            .Select(s => $"{Bracket(s.Column!)} {(s.Direction == SortDirection.Desc ? "DESC" : "ASC")}");
        _sortExpression = string.Join(", ", parts);
        HasActiveSort = _sortExpression.Length > 0;
        ProjectView();
        _setStatus(HasActiveSort ? $"Sorted by {_sortExpression}." : "Sort cleared.");
    }

    [RelayCommand]
    private void ClearSort()
    {
        SortLevels.Clear();
        _sortExpression = "";
        HasActiveSort = false;
        ProjectView();
        _setStatus("Sort cleared.");
    }

    // ---- filter ----------------------------------------------------------

    [RelayCommand]
    private void AddFilterCondition() =>
        FilterConditions.Add(new FilterCondition { Column = ColumnNames.FirstOrDefault() });

    [RelayCommand]
    private void RemoveFilterCondition(FilterCondition? condition)
    {
        if (condition is not null) FilterConditions.Remove(condition);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        var expr = BuildFilterExpression();
        try
        {
            // Validate against a throwaway view first so a bad expression doesn't blank the grid.
            var rows = new DataView(_session!.Data) { RowFilter = expr }.Count;
            _filterExpression = expr;
            HasActiveFilter = expr.Length > 0;
            ProjectView();
            _setStatus(HasActiveFilter ? $"Filter applied — {rows} row(s) match." : "Filter cleared.");
        }
        catch (Exception ex)
        {
            Dialogs.ShowError("Invalid filter", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FilterConditions.Clear();
        _filterExpression = "";
        HasActiveFilter = false;
        ProjectView();
        _setStatus("Filter cleared.");
    }

    private string BuildFilterExpression()
    {
        var clauses = FilterConditions
            .Where(c => !string.IsNullOrEmpty(c.Column))
            .Select(BuildClause)
            .Where(c => c.Length > 0)
            .ToList();
        if (clauses.Count == 0) return "";
        var joiner = FilterMatchAll ? " AND " : " OR ";
        return string.Join(joiner, clauses.Select(c => $"({c})"));
    }

    private string BuildClause(FilterCondition c)
    {
        var col = Bracket(c.Column!);
        var isString = _session is not null
            && _session.Data.Columns.Contains(c.Column!)
            && _session.Data.Columns[c.Column!]!.DataType == typeof(string);
        var raw = c.Value ?? "";

        string Literal(string v) => isString ? $"'{v.Replace("'", "''")}'" : v;
        string Like(string pattern) => $"{col} LIKE '{pattern.Replace("'", "''")}'";

        return c.Operator switch
        {
            FilterOperator.Contains => Like($"%{raw}%"),
            FilterOperator.StartsWith => Like($"{raw}%"),
            FilterOperator.EndsWith => Like($"%{raw}"),
            FilterOperator.Equals => $"{col} = {Literal(raw)}",
            FilterOperator.NotEquals => $"{col} <> {Literal(raw)}",
            FilterOperator.GreaterThan => $"{col} > {Literal(raw)}",
            FilterOperator.LessThan => $"{col} < {Literal(raw)}",
            FilterOperator.GreaterOrEqual => $"{col} >= {Literal(raw)}",
            FilterOperator.LessOrEqual => $"{col} <= {Literal(raw)}",
            FilterOperator.IsEmpty => isString ? $"{col} IS NULL OR {col} = ''" : $"{col} IS NULL",
            FilterOperator.IsNotEmpty => isString ? $"{col} IS NOT NULL AND {col} <> ''" : $"{col} IS NOT NULL",
            _ => ""
        };
    }

    private static string Bracket(string column) => "[" + column.Replace("]", "]]") + "]";

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

            ColumnNames.Clear();
            foreach (DataColumn c in _session.Data.Columns)
                ColumnNames.Add(c.ColumnName);

            // Apply a saved row-identity choice (keyless tables).
            _identityKey = RowIdentityStore.MakeKey(Node.Connection.Id, Node.Database, Node.Schema, Node.Name);
            if (!_session.HasNaturalKey)
            {
                var saved = _identityStore.Get(_identityKey);
                if (saved is not null) _session.SetRowIdentity(saved);
            }
            OnPropertyChanged(nameof(CanPickRowIdentity));

            ProjectView(); // applies any active filter/sort
            HasUnsavedChanges = false;
            ApplyDefaults();

            var keyNote = _session.HasReliableKey
                ? ""
                : $"  ⚠ No primary key — edits/deletes match on {_session.KeyDescription} (one row at a time).";
            _setStatus($"Loaded {_session.Data.Rows.Count} row(s) from {Identifier} (limit {RowLimit}).{keyNote}");
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
    {
        // Cheap dirty flag — DataTable.GetChanges() here would be O(rows) on every edit.
        HasUnsavedChanges = true;
        QueueSqlRefresh(); // live-update the SQL preview if it's open (debounced)
    }

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
