using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NavMeCat.ViewModels;

public enum JoinType { Inner, Left, Right, Full }
public enum BoolConnector { And, Or }

public partial class BuilderColumn : ObservableObject
{
    public string Table { get; init; } = "";
    public string Name { get; init; } = "";
    public string Reference => $"[{Table}].[{Name}]";
    [ObservableProperty] private bool included;
}

public partial class BuilderTable : ObservableObject
{
    public string Schema { get; init; } = "";
    public string Table { get; init; } = "";
    public string Display => $"{Schema}.{Table}";
    public string FromClause => $"[{Schema}].[{Table}]";
    public ObservableCollection<BuilderColumn> Columns { get; } = new();
}

public partial class JoinRow : ObservableObject
{
    [ObservableProperty] private string? leftColumn;
    [ObservableProperty] private JoinType joinType;
    [ObservableProperty] private string? rightColumn;
}

public partial class FilterRow : ObservableObject
{
    [ObservableProperty] private BoolConnector connector;
    [ObservableProperty] private string? column;
    [ObservableProperty] private FilterOperator @operator;
    [ObservableProperty] private string? value;
}

public partial class BuilderSort : ObservableObject
{
    [ObservableProperty] private string? column;
    [ObservableProperty] private SortDirection direction;
}
