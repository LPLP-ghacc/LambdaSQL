namespace LambdaSQL.Core.Storage;

public sealed class Row
{
    private readonly Dictionary<string, object?> _data;

    public Row(Dictionary<string, object?> data)
    {
        _data = data;
    }

    public object? Get(string column) =>
        _data.TryGetValue(column, out var v) ? v : null;

    public bool Has(string column) => _data.ContainsKey(column);

    public void Set(string column, object? value) => _data[column] = value;

    public IReadOnlyDictionary<string, object?> Data => _data;

    public Row Clone() => new(new Dictionary<string, object?>(_data));

    // Merge two rows (for JOINs), prefixing with table alias if provided
    public static Row Merge(Row left, string? leftAlias, Row right, string? rightAlias)
    {
        var merged = new Dictionary<string, object?>();

        foreach (var (k, v) in left.Data)
        {
            merged[k] = v;
            if (leftAlias != null) merged[$"{leftAlias}.{k}"] = v;
        }

        foreach (var (k, v) in right.Data)
        {
            merged[k] = v;
            if (rightAlias != null) merged[$"{rightAlias}.{k}"] = v;
        }

        return new Row(merged);
    }
}
