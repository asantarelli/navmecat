using CommunityToolkit.Mvvm.ComponentModel;

namespace NavMeCat.ViewModels;

/// <summary>One column row in the table designer.</summary>
public partial class DesignColumn : ObservableObject
{
    /// <summary>Name as it exists in the DB (null for a newly added column).</summary>
    public string? OriginalName { get; set; }
    public string? OriginalType { get; set; }
    public string? OriginalSize { get; set; }
    public bool OriginalNullable { get; set; }

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string type = "int";
    [ObservableProperty] private string? size;
    [ObservableProperty] private bool nullable = true;
    [ObservableProperty] private bool identity;
    [ObservableProperty] private string? defaultValue;
    [ObservableProperty] private bool primaryKey;
}
