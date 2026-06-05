using System.Windows;
using NavMeCat.ViewModels;

namespace NavMeCat.Views;

public enum PaneKind { Detail, Sql }

/// <summary>
/// Pops a tab's pane (cell detail or SQL preview) out into a floating window that can be
/// moved anywhere on screen, and docks it back when the window closes. The floating content
/// is a fresh view bound to the same tab view-model, so it stays in sync with the grid.
/// </summary>
public static class PaneService
{
    private static readonly Dictionary<(TableTabViewModel, PaneKind), FloatingPaneWindow> Open = new();

    /// <summary>Pops the pane out if docked, or docks it back (closes the window) if already floating.</summary>
    public static void TogglePopOut(TableTabViewModel tab, PaneKind kind)
    {
        var key = (tab, kind);
        if (Open.TryGetValue(key, out var existing))
        {
            existing.Activate();
            existing.Close();
            return;
        }

        FrameworkElement body = kind == PaneKind.Detail
            ? new CellDetailView { DataContext = tab }
            : new SqlPreviewView { DataContext = tab };

        var window = new FloatingPaneWindow
        {
            Title = (kind == PaneKind.Detail ? "Cell detail — " : "SQL preview — ") + tab.PaneTitleSuffix,
            Owner = Application.Current?.MainWindow
        };
        window.SetBody(body);
        window.Closed += (_, _) =>
        {
            Open.Remove(key);
            SetPopped(tab, kind, false);
        };

        Open[key] = window;
        SetPopped(tab, kind, true);
        window.Show();
    }

    /// <summary>Closes the floating window for a pane if one is open (used when hiding the pane).</summary>
    public static void ClosePopOut(TableTabViewModel tab, PaneKind kind)
    {
        if (Open.TryGetValue((tab, kind), out var window))
            window.Close();
    }

    private static void SetPopped(TableTabViewModel tab, PaneKind kind, bool value)
    {
        if (kind == PaneKind.Detail) tab.DetailPopped = value;
        else tab.SqlPopped = value;
    }
}
