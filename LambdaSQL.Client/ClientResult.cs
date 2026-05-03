namespace LambdaSQL.Client;

/// <summary>
/// Result returned by LambdaSqlClient.QueryAsync().
/// </summary>
public sealed class ClientResult
{
    public bool     IsError      { get; private init; }
    public bool     IsResultSet  { get; private init; }
    public string?  ErrorMessage { get; private init; }
    public string?  Message      { get; private init; }
    public int      RowsAffected { get; private init; }
    public string[] Columns      { get; private init; } = Array.Empty<string>();
    public object?[][] Rows      { get; private init; } = Array.Empty<object?[]>();

    public static ClientResult FromError(string msg) =>
        new() { IsError = true, ErrorMessage = msg };

    public static ClientResult FromMessage(int rows, string msg) =>
        new() { RowsAffected = rows, Message = msg };

    public static ClientResult FromResultSet(string[] columns, object?[][] rows) =>
        new() { IsResultSet = true, Columns = columns, Rows = rows, RowsAffected = rows.Length };

    // ── Pretty print ─────────────────────────────────────────────────────────

    public void Print()
    {
        if (IsError)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ErrorMessage}");
            Console.ResetColor();
            return;
        }

        if (!IsResultSet)
        {
            Console.WriteLine(Message ?? $"({RowsAffected} row(s) affected)");
            return;
        }

        var widths = Columns.Select(c => c.Length).ToArray();
        foreach (var row in Rows)
            for (int i = 0; i < row.Length; i++)
            {
                var len = Format(row[i]).Length;
                if (len > widths[i]) widths[i] = len;
            }

        PrintSep(widths);
        PrintRow(Columns.Cast<object?>().ToArray(), widths);
        PrintSep(widths);
        foreach (var row in Rows) PrintRow(row, widths);
        PrintSep(widths);
        Console.WriteLine($"({Rows.Length} row(s))");
    }

    private static void PrintSep(int[] w) =>
        Console.WriteLine("+" + string.Join("+", w.Select(x => new string('-', x + 2))) + "+");

    private static void PrintRow(object?[] vals, int[] w)
    {
        var cells = vals.Select((v, i) => " " + Format(v).PadRight(w[i]) + " ");
        Console.WriteLine("|" + string.Join("|", cells) + "|");
    }

    private static string Format(object? v) => v switch
    {
        null    => "NULL",
        bool b  => b ? "true" : "false",
        double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "NULL"
    };
}
