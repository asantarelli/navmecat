using System.Data;

namespace NavMeCat.Services;

/// <summary>
/// Heuristically identifies integer columns that most likely hold Clarion dates.
///
/// A column is flagged when either:
///  - its name looks like a date (contains "date" or ends with "dt") and most of its
///    non-zero values fall in the plausible Clarion-date window, or
///  - every non-zero value falls in that window and there is a reasonable spread of
///    distinct values (catches date columns with non-obvious names while excluding
///    small sequential IDs, which fall below the window).
/// </summary>
public static class ClarionDateDetector
{
    public static HashSet<string> Detect(DataTable table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataColumn col in table.Columns)
        {
            if (!IsCandidateType(col.DataType)) continue;

            var nameIsDate = NameLooksLikeDate(col.ColumnName);
            int nonNull = 0, inRange = 0, positiveOutOfRange = 0;
            var distinct = new HashSet<long>();
            var disqualified = false;

            foreach (DataRow row in table.Rows)
            {
                var raw = row[col];
                if (raw is null || raw == DBNull.Value) continue;
                if (!TryGetIntegral(raw, out var n)) { disqualified = true; break; }

                nonNull++;
                if (n == 0) continue; // empty Clarion date
                distinct.Add(n);
                if (n >= ClarionDate.MinPlausible && n <= ClarionDate.MaxPlausible) inRange++;
                else positiveOutOfRange++;
            }

            if (disqualified) continue;

            var nonZero = inRange + positiveOutOfRange;
            if (nonZero == 0)
            {
                // All values empty/zero — only trust the name in that case.
                if (nameIsDate && nonNull > 0) result.Add(col.ColumnName);
                continue;
            }

            var fractionInRange = (double)inRange / nonZero;
            var isDate =
                (nameIsDate && fractionInRange >= 0.8) ||
                (fractionInRange >= 0.999 && distinct.Count >= 5);

            if (isDate) result.Add(col.ColumnName);
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
