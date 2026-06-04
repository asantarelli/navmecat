using System.Data;

namespace NavMeCat.Services;

public enum ClarionKind { Date, Time }

/// <summary>
/// Heuristically classifies integer columns that most likely hold Clarion dates or times.
///
/// Dates are flagged by name ("date"/"…dt") with most non-zero values in the plausible
/// date window, or purely by values when every non-zero value sits in that window with a
/// reasonable spread (which excludes small sequential IDs).
///
/// Times are flagged primarily by name ("time"/"…tm"), because the Clarion time range
/// (1..8,640,001) overlaps far too much ordinary integer data to trust values alone.
/// </summary>
public static class ClarionDetector
{
    public static Dictionary<string, ClarionKind> Detect(DataTable table)
    {
        var result = new Dictionary<string, ClarionKind>(StringComparer.OrdinalIgnoreCase);

        foreach (DataColumn col in table.Columns)
        {
            if (!IsCandidateType(col.DataType)) continue;

            var nameDate = NameLooksLikeDate(col.ColumnName);
            var nameTime = NameLooksLikeTime(col.ColumnName);

            int nonNull = 0, nonZero = 0, dateInRange = 0, timeInRange = 0;
            var distinct = new HashSet<long>();
            var disqualified = false;

            foreach (DataRow row in table.Rows)
            {
                var raw = row[col];
                if (raw is null || raw == DBNull.Value) continue;
                if (!TryGetIntegral(raw, out var n)) { disqualified = true; break; }

                nonNull++;
                if (n == 0) continue;
                nonZero++;
                if (n > 0) distinct.Add(n);

                if (n >= ClarionDate.MinPlausible && n <= ClarionDate.MaxPlausible) dateInRange++;
                if (n >= 1 && n <= ClarionTime.MaxValue) timeInRange++;
            }

            if (disqualified) continue;

            if (nonZero == 0)
            {
                // No values to range-check — trust the name only.
                if (nameDate && nonNull > 0) result[col.ColumnName] = ClarionKind.Date;
                else if (nameTime && nonNull > 0) result[col.ColumnName] = ClarionKind.Time;
                continue;
            }

            var dateFrac = (double)dateInRange / nonZero;
            var timeFrac = (double)timeInRange / nonZero;

            var isDate = (nameDate && dateFrac >= 0.8) || (dateFrac >= 0.999 && distinct.Count >= 5);
            var isTime = nameTime && timeFrac >= 0.8;

            if (isDate && isTime)
                result[col.ColumnName] = dateInRange >= timeInRange ? ClarionKind.Date : ClarionKind.Time;
            else if (isDate)
                result[col.ColumnName] = ClarionKind.Date;
            else if (isTime)
                result[col.ColumnName] = ClarionKind.Time;
        }

        return result;
    }

    private static bool IsCandidateType(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(decimal);

    private static bool NameLooksLikeDate(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("date") || lower.EndsWith("dt");
    }

    private static bool NameLooksLikeTime(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("time") || lower.EndsWith("tm");
    }

    private static bool TryGetIntegral(object value, out long result)
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
