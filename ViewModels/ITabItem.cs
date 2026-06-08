namespace NavMeCat.ViewModels;

/// <summary>A tab in the content area (the Objects list, or an open table).</summary>
public interface ITabItem
{
    string Header { get; }
    bool CanClose { get; }
}
