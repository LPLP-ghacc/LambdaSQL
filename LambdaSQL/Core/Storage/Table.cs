namespace LambdaSQL.Core.Storage;

public sealed class Table
{
    public string Name { get; }
    public IReadOnlyList<Column> Columns { get; }

    private readonly List<Row> _rows = new();
    private long _autoId = 0;

    // Column index for fast lookup
    private readonly Dictionary<string, int> _colIndex;

    public Table(string name, IEnumerable<Column> columns)
    {
        Name = name;
        Columns = columns.ToList();
        _colIndex = Columns
            .Select((c, i) => (c.Name, i))
            .ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<Row> Rows => _rows;

    public Column? GetColumn(string name) =>
        _colIndex.TryGetValue(name, out var idx) ? Columns[idx] : null;

    public bool HasColumn(string name) => _colIndex.ContainsKey(name);

    // ── Insert ───────────────────────────────────────────────────────────────

    public void Insert(Row row)
    {
        ValidateAndCoerce(row);
        _rows.Add(row);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public int Update(Func<Row, bool> predicate, Action<Row> mutate)
    {
        int count = 0;
        foreach (var row in _rows)
        {
            if (!predicate(row)) continue;
            mutate(row);
            ValidateAndCoerce(row);
            count++;
        }
        return count;
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    public int Delete(Func<Row, bool> predicate)
    {
        int before = _rows.Count;
        _rows.RemoveAll(r => predicate(r));
        return before - _rows.Count;
    }

    // ── Scan ─────────────────────────────────────────────────────────────────

    public IEnumerable<Row> Scan() => _rows;

    // ── Validation ───────────────────────────────────────────────────────────

    private void ValidateAndCoerce(Row row)
    {
        foreach (var col in Columns)
        {
            var val = row.Get(col.Name);

            if (val is null)
            {
                if (col.Default != null)
                { row.Set(col.Name, col.Default); continue; }

                if (col.NotNull)
                    throw new StorageException($"Column '{col.Name}' cannot be null");

                continue;
            }

            // Coerce type
            try
            {
                row.Set(col.Name, DataTypeHelper.Coerce(val, col.Type));
            }
            catch
            {
                throw new StorageException($"Cannot coerce value '{val}' to type {DataTypeHelper.TypeName(col.Type)} for column '{col.Name}'");
            }
        }
    }

    public long NextId() => ++_autoId;

    public override string ToString() =>
        $"Table({Name}, [{string.Join(", ", Columns)}])";
}

public sealed class StorageException(string message) : Exception(message);
