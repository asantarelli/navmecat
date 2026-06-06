using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace NavMeCat.Services;

public enum ExportFormat { Csv, Tsv, Json, Xml, Html, Xlsx }

/// <summary>Exports a DataView's rows (respecting its filter/sort) to a file in various formats.</summary>
public static class ExportService
{
    public static string Extension(ExportFormat f) => f switch
    {
        ExportFormat.Csv => "csv",
        ExportFormat.Tsv => "tsv",
        ExportFormat.Json => "json",
        ExportFormat.Xml => "xml",
        ExportFormat.Html => "html",
        ExportFormat.Xlsx => "xlsx",
        _ => "txt"
    };

    /// <param name="display">Optional per-cell display override (e.g. Clarion date/time); null = use raw value.</param>
    public static void Export(DataView view, IReadOnlyList<string> columns, ExportFormat format, string path,
        bool includeHeaders, Func<string, object?, string?>? display = null)
    {
        var rows = view.Cast<DataRowView>().ToList();

        switch (format)
        {
            case ExportFormat.Csv: WriteDelimited(path, columns, rows, ',', includeHeaders, display); break;
            case ExportFormat.Tsv: WriteDelimited(path, columns, rows, '\t', includeHeaders, display); break;
            case ExportFormat.Json: WriteJson(path, columns, rows, display); break;
            case ExportFormat.Xml: WriteXml(path, columns, rows, display); break;
            case ExportFormat.Html: WriteHtml(path, columns, rows, includeHeaders, display); break;
            case ExportFormat.Xlsx: WriteXlsx(path, columns, rows, includeHeaders, display); break;
        }
    }

    private static string Cell(DataRowView row, string col, Func<string, object?, string?>? display)
    {
        var raw = row[col];
        var over = display?.Invoke(col, raw is DBNull ? null : raw);
        if (over is not null) return over;
        return raw is DBNull ? "" : raw.ToString() ?? "";
    }

    private static void WriteDelimited(string path, IReadOnlyList<string> cols, List<DataRowView> rows,
        char delim, bool headers, Func<string, object?, string?>? display)
    {
        var sb = new StringBuilder();
        string Q(string v) =>
            v.IndexOf(delim) >= 0 || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
                ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;

        if (headers) sb.AppendLine(string.Join(delim, cols.Select(Q)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(delim, cols.Select(c => Q(Cell(r, c, display)))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static void WriteJson(string path, IReadOnlyList<string> cols, List<DataRowView> rows,
        Func<string, object?, string?>? display)
    {
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        w.WriteStartArray();
        foreach (var r in rows)
        {
            w.WriteStartObject();
            foreach (var c in cols)
            {
                var raw = r[c];
                var over = display?.Invoke(c, raw is DBNull ? null : raw);
                if (over is not null) { w.WriteString(c, over); continue; }
                WriteJsonValue(w, c, raw);
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteJsonValue(Utf8JsonWriter w, string name, object value)
    {
        switch (value)
        {
            case null or DBNull: w.WriteNull(name); break;
            case bool b: w.WriteBoolean(name, b); break;
            case byte or sbyte or short or ushort or int or uint or long:
                w.WriteNumber(name, Convert.ToInt64(value)); break;
            case ulong ul: w.WriteNumber(name, ul); break;
            case float or double or decimal:
                w.WriteNumber(name, Convert.ToDecimal(value)); break;
            case DateTime dt: w.WriteString(name, dt.ToString("o", CultureInfo.InvariantCulture)); break;
            case Guid g: w.WriteString(name, g.ToString()); break;
            case byte[] bytes: w.WriteString(name, Convert.ToBase64String(bytes)); break;
            default: w.WriteString(name, value.ToString()); break;
        }
    }

    private static void WriteXml(string path, IReadOnlyList<string> cols, List<DataRowView> rows,
        Func<string, object?, string?>? display)
    {
        var root = new XElement("rows");
        foreach (var r in rows)
        {
            var rowEl = new XElement("row");
            foreach (var c in cols)
                rowEl.Add(new XElement(XmlName(c), Cell(r, c, display)));
            root.Add(rowEl);
        }
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);
    }

    private static string XmlName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static void WriteHtml(string path, IReadOnlyList<string> cols, List<DataRowView> rows,
        bool headers, Func<string, object?, string?>? display)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" +
                      "table{border-collapse:collapse;font-family:Segoe UI,Arial,sans-serif;font-size:13px}" +
                      "th,td{border:1px solid #ccc;padding:4px 8px;text-align:left}th{background:#f1f4f7}" +
                      "</style></head><body><table>");
        if (headers)
            sb.Append("<tr>").Append(string.Concat(cols.Select(c => $"<th>{WebUtility.HtmlEncode(c)}</th>"))).AppendLine("</tr>");
        foreach (var r in rows)
            sb.Append("<tr>").Append(string.Concat(cols.Select(c => $"<td>{WebUtility.HtmlEncode(Cell(r, c, display))}</td>"))).AppendLine("</tr>");
        sb.AppendLine("</table></body></html>");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static void WriteXlsx(string path, IReadOnlyList<string> cols, List<DataRowView> rows,
        bool headers, Func<string, object?, string?>? display)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Data");
        var row = 1;

        if (headers)
        {
            for (var c = 0; c < cols.Count; c++)
                ws.Cell(row, c + 1).Value = cols[c];
            ws.Row(row).Style.Font.Bold = true;
            row++;
        }

        foreach (var r in rows)
        {
            for (var c = 0; c < cols.Count; c++)
            {
                var raw = r[cols[c]];
                var over = display?.Invoke(cols[c], raw is DBNull ? null : raw);
                var cell = ws.Cell(row, c + 1);
                if (over is not null) cell.Value = over;
                else SetXlsxCell(cell, raw);
            }
            row++;
        }

        ws.Columns().AdjustToContents();
        if (headers) ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    private static void SetXlsxCell(IXLCell cell, object value)
    {
        switch (value)
        {
            case null or DBNull: break;
            case bool b: cell.Value = b; break;
            case byte or sbyte or short or ushort or int or uint or long or ulong
                 or float or double or decimal: cell.Value = Convert.ToDouble(value); break;
            case DateTime dt: cell.Value = dt; break;
            default: cell.Value = value.ToString(); break;
        }
    }
}
