using System.Data;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NavMeCat.Services;
using NavMeCat.ViewModels;

namespace NavMeCat.Behaviors;

/// <summary>
/// Spreadsheet-friendly clipboard copy for the data grid. Writes HTML (a real table),
/// tab-separated text, and CSV — all properly quoted — so Excel and Google Sheets paste
/// each value into its own cell even when a value contains tabs or line breaks.
/// Clarion date/time columns are copied as their displayed value.
/// </summary>
public static class GridClipboard
{
    public static void Copy(DataGrid grid, bool includeHeaders)
    {
        var (headers, rows) = Gather(grid);
        if (rows.Count == 0) return;

        try
        {
            var data = new DataObject();
            data.SetText(BuildDelimited(headers, rows, '\t', includeHeaders), TextDataFormat.UnicodeText);
            data.SetData(DataFormats.CommaSeparatedValue, BuildDelimited(headers, rows, ',', includeHeaders));
            data.SetData(DataFormats.Html, BuildCfHtml(BuildHtmlTable(headers, rows, includeHeaders)));
            Clipboard.SetDataObject(data, true);
        }
        catch
        {
            // Clipboard can be transiently locked by another app — ignore.
        }
    }

    private static (List<string> Headers, List<List<string>> Rows) Gather(DataGrid grid)
    {
        var tab = grid.DataContext as TableTabViewModel;

        var columns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToList();

        // If individual cells are selected, restrict to those columns.
        if (grid.SelectedCells.Count > 0)
        {
            var selectedCols = new HashSet<DataGridColumn>(grid.SelectedCells.Select(c => c.Column));
            columns = columns.Where(selectedCols.Contains).ToList();
        }

        var headers = columns.Select(GetColumnName).ToList();

        var selectedRows = grid.SelectedCells.Count > 0
            ? new HashSet<object>(grid.SelectedCells.Select(c => c.Item))
            : new HashSet<object>(grid.SelectedItems.Cast<object>());

        var rows = new List<List<string>>();
        foreach (var item in grid.Items) // preserves display order
        {
            if (!selectedRows.Contains(item) || item is not DataRowView rowView) continue;

            var cells = new List<string>(columns.Count);
            foreach (var col in columns)
            {
                var name = GetColumnName(col);
                object? raw = !string.IsNullOrEmpty(name) && rowView.Row.Table.Columns.Contains(name)
                    ? rowView[name]
                    : null;
                cells.Add(FormatValue(tab, name, raw));
            }
            rows.Add(cells);
        }

        return (headers, rows);
    }

    private static string FormatValue(TableTabViewModel? tab, string name, object? raw)
    {
        if (raw is null || raw == DBNull.Value) return "";

        var kind = tab?.GetEffectiveKind(name);
        if (kind == ClarionKind.Date && TryLong(raw, out var d))
        {
            var date = ClarionDate.FromClarion(d);
            if (date is not null) return date.Value.ToString("yyyy-MM-dd");
        }
        if (kind == ClarionKind.Time && TryLong(raw, out var t))
        {
            var s = ClarionTime.Format(t);
            if (s is not null) return s;
        }
        return raw.ToString() ?? "";
    }

    private static string BuildDelimited(List<string> headers, List<List<string>> rows, char delim, bool includeHeaders)
    {
        var sb = new StringBuilder();
        if (includeHeaders)
            sb.Append(string.Join(delim, headers.Select(h => Field(h, delim)))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(delim, row.Select(c => Field(c, delim)))).Append("\r\n");
        return sb.ToString();
    }

    private static string Field(string value, char delim)
    {
        if (value.IndexOf(delim) >= 0 || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string BuildHtmlTable(List<string> headers, List<List<string>> rows, bool includeHeaders)
    {
        var sb = new StringBuilder();
        sb.Append("<table>");
        if (includeHeaders)
        {
            sb.Append("<tr>");
            foreach (var h in headers) sb.Append("<th>").Append(Encode(h)).Append("</th>");
            sb.Append("</tr>");
        }
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var c in row) sb.Append("<td>").Append(Encode(c)).Append("</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Encode(string value) =>
        WebUtility.HtmlEncode(value).Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");

    /// <summary>Wraps an HTML fragment in the CF_HTML clipboard format with byte offsets.</summary>
    private static string BuildCfHtml(string fragment)
    {
        const string header =
            "Version:0.9\r\nStartHTML:{0:00000000}\r\nEndHTML:{1:00000000}\r\nStartFragment:{2:00000000}\r\nEndFragment:{3:00000000}\r\n";
        const string pre = "<html><body><!--StartFragment-->";
        const string post = "<!--EndFragment--></body></html>";

        var headerLength = Encoding.UTF8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        var startFragment = headerLength + Encoding.UTF8.GetByteCount(pre);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(post);

        return string.Format(header, headerLength, endHtml, startFragment, endFragment) + pre + fragment + post;
    }

    private static string GetColumnName(DataGridColumn column)
    {
        if (column is DataGridBoundColumn { Binding: Binding b } && !string.IsNullOrEmpty(b.Path?.Path))
            return b.Path.Path;
        return column.SortMemberPath ?? "";
    }

    private static bool TryLong(object value, out long result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case decimal d when d == Math.Truncate(d): result = (long)d; return true;
            default: result = 0; return false;
        }
    }
}
