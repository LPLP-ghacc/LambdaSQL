namespace LambdaSQL.Core.Executor;

public sealed class QueryResult
{
    public static readonly QueryResult Empty = new(Array.Empty<string>(), Array.Empty<object?[]>());

    public string[] Columns { get; }
    public IReadOnlyList<object?[]> Rows { get; }
    public int RowsAffected { get; }
    public string? Message { get; }

    // Result set (SELECT)
    public QueryResult(string[] columns, IEnumerable<object?[]> rows)
    {
        Columns = columns;
        Rows = rows.ToList();
        RowsAffected = Rows.Count;
    }

    // DML result (INSERT / UPDATE / DELETE)
    public QueryResult(int rowsAffected, string message)
    {
        Columns = [];
        Rows = [];
        RowsAffected = rowsAffected;
        Message = message;
    }

    // DDL result (CREATE / DROP)
    public QueryResult(string message)
    {
        Columns = [];
        Rows = [];
        RowsAffected = 0;
        Message = message;
    }

    public bool IsResultSet => Columns.Length > 0;

    public void Print()
    {
        if (Message != null)
        {
            Console.WriteLine(Message);
            return;
        }

        if (!IsResultSet)
        {
            Console.WriteLine($"({RowsAffected} row(s) affected)");
            return;
        }

        // Calculate column widths
        var widths = Columns.Select(c => c.Length).ToArray();
        foreach (var row in Rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                var len = FormatValue(row[i]).Length;
                if (len > widths[i]) widths[i] = len;
            }
        }

        // Header
        PrintSeparator(widths);
        PrintRow(Columns.Select(c => (object?)c).ToArray(), widths);
        PrintSeparator(widths);

        // Rows
        foreach (var row in Rows)
            PrintRow(row, widths);

        PrintSeparator(widths);
        Console.WriteLine($"({Rows.Count} row(s))");
    }

    private static void PrintSeparator(int[] widths)
    {
        Console.WriteLine("+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+");
    }

    private static void PrintRow(object?[] values, int[] widths)
    {
        var cells = values.Select((v, i) =>
        {
            var s = FormatValue(v);
            return " " + s.PadRight(widths[i]) + " ";
        });
        Console.WriteLine("|" + string.Join("|", cells) + "|");
    }

    private static string FormatValue(object? v) => v switch
    {
        null    => "NULL",
        bool b  => b ? "true" : "false",
        double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        float f  => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "NULL"
    };
}
