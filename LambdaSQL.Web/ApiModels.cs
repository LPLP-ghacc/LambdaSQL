using LambdaSQL.Core.Engine;
using LambdaSQL.Core.Executor;

namespace LambdaSQL.Web;

public record QueryRequest(string Sql);

public sealed class ApiResult
{
    public bool     Success      { get; init; }
    public string?  Error        { get; init; }
    public string?  Message      { get; init; }
    public int      RowsAffected { get; init; }
    public string[] Columns      { get; init; } = Array.Empty<string>();
    public List<Dictionary<string, object?>> Rows { get; init; } = new();

    public static ApiResult From(QueryResult r)
    {
        if (!r.IsResultSet)
            return new ApiResult
            {
                Success      = true,
                Message      = r.Message,
                RowsAffected = r.RowsAffected
            };

        var rows = r.Rows.Select(row =>
        {
            var dict = new Dictionary<string, object?>();
            for (int i = 0; i < r.Columns.Length; i++)
                dict[r.Columns[i]] = FormatValue(row[i]);
            return dict;
        }).ToList();

        return new ApiResult
        {
            Success      = true,
            Columns      = r.Columns,
            Rows         = rows,
            RowsAffected = r.RowsAffected
        };
    }

    private static object? FormatValue(object? v) => v switch
    {
        null    => null,
        bool b  => b,
        double d => d,
        float f  => (double)f,
        long l   => l,
        int i    => (long)i,
        _ => v.ToString()
    };
}

public sealed class TableInfo
{
    public string Name { get; init; } = "";
    public List<ColumnInfo> Columns { get; init; } = new();
}

public sealed class ColumnInfo
{
    public string Name       { get; init; } = "";
    public string Type       { get; init; } = "";
    public bool   NotNull    { get; init; }
    public bool   PrimaryKey { get; init; }
}
