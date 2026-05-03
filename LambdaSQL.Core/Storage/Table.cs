using System.Collections.Concurrent;
using LambdaSQL.Core.Storage.PagedStorage;

namespace LambdaSQL.Core.Storage;

/// <summary>
/// In-memory table with optional on-disk persistence via TableFile.
/// Rows are stored in a slot array; deleted slots are marked null.
/// Primary key index: Dictionary for O(1) lookup.
/// </summary>
public sealed class Table : IDisposable
{
    public string Name { get; }
    public IReadOnlyList<Column> Columns { get; }

    // In-memory row store: index → row (null = deleted)
    private readonly List<Row?> _rows = new();

    // Primary key index: pkValue → row index
    private readonly Dictionary<object, int> _pkIndex = new();
    private readonly int _pkColumnIndex = -1;

    // Column name → index for fast lookup
    private readonly Dictionary<string, int> _colIndex;

    // On-disk storage (null = in-memory only)
    private TableFile? _file;
    private WalWriter? _wal;

    // Row location map: row index → (pageId, slotId)
    private readonly List<(int pageId, int slotId)> _rowLocations = new();

    private long _autoId = 0;

    public Table(string name, IEnumerable<Column> columns, TableFile? file = null, WalWriter? wal = null)
    {
        Name    = name;
        Columns = columns.ToList();
        _file   = file;
        _wal    = wal;

        _colIndex = Columns
            .Select((c, i) => (c.Name, i))
            .ToDictionary(x => x.Name, x => x.i, StringComparer.OrdinalIgnoreCase);

        // Find primary key column
        for (int i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].PrimaryKey) { _pkColumnIndex = i; break; }
        }
    }

    // ── Load from disk ────────────────────────────────────────────────────────

    public void LoadFromFile()
    {
        if (_file == null) return;
        foreach (var (pageId, slotId, data) in _file.ScanAll())
        {
            var row = RowSerializer.Deserialize(data.Span, Columns);
            int idx = _rows.Count;
            _rows.Add(row);
            _rowLocations.Add((pageId, slotId));
            IndexPk(row, idx);
        }
    }

    // ── Insert ────────────────────────────────────────────────────────────────

    public void Insert(Row row)
    {
        ValidateAndCoerce(row);
        CheckPkUnique(row);

        int idx = _rows.Count;
        _rows.Add(row);

        if (_file != null)
        {
            var bytes = RowSerializer.Serialize(row, Columns);
            var (pageId, slotId) = _file.WriteRow(bytes);
            _rowLocations.Add((pageId, slotId));

            _wal?.Append(WalWriter.OpInsert, Name, bytes);
        }
        else
        {
            _rowLocations.Add((-1, -1));
        }

        IndexPk(row, idx);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public int Update(Func<Row, bool> predicate, Action<Row> mutate)
    {
        int count = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row == null || !predicate(row)) continue;

            // Remove old PK index
            RemovePkIndex(row);

            mutate(row);
            ValidateAndCoerce(row);
            CheckPkUnique(row, i);

            // Re-index PK
            IndexPk(row, i);

            // Persist: delete old slot, write new
            if (_file != null)
            {
                var (pageId, slotId) = _rowLocations[i];
                if (pageId >= 0) _file.DeleteRow(pageId, slotId);

                var bytes = RowSerializer.Serialize(row, Columns);
                var (np, ns) = _file.WriteRow(bytes);
                _rowLocations[i] = (np, ns);

                _wal?.Append(WalWriter.OpUpdate, Name, bytes);
            }

            count++;
        }
        return count;
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public int Delete(Func<Row, bool> predicate)
    {
        int count = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row == null || !predicate(row)) continue;

            RemovePkIndex(row);

            if (_file != null)
            {
                var (pageId, slotId) = _rowLocations[i];
                if (pageId >= 0) _file.DeleteRow(pageId, slotId);

                var bytes = RowSerializer.Serialize(row, Columns);
                _wal?.Append(WalWriter.OpDelete, Name, bytes);
            }

            _rows[i] = null;
            count++;
        }
        return count;
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    /// <summary>Full table scan, skips deleted rows.</summary>
    public IEnumerable<Row> Scan()
    {
        foreach (var row in _rows)
            if (row != null) yield return row;
    }

    // ── PK lookup O(1) ────────────────────────────────────────────────────────

    public Row? FindByPk(object pkValue)
    {
        if (_pkColumnIndex < 0) return null;
        return _pkIndex.TryGetValue(pkValue, out var idx) ? _rows[idx] : null;
    }

    // ── Schema helpers ────────────────────────────────────────────────────────

    public Column? GetColumn(string name) =>
        _colIndex.TryGetValue(name, out var idx) ? Columns[idx] : null;

    public bool HasColumn(string name) => _colIndex.ContainsKey(name);

    public long NextId() => Interlocked.Increment(ref _autoId);

    // ── Validation ────────────────────────────────────────────────────────────

    private void ValidateAndCoerce(Row row)
    {
        foreach (var col in Columns)
        {
            var val = row.Get(col.Name);

            if (val is null)
            {
                if (col.Default != null) { row.Set(col.Name, col.Default); continue; }
                if (col.NotNull)
                    throw new StorageException($"Column '{col.Name}' cannot be null");
                continue;
            }

            try { row.Set(col.Name, DataTypeHelper.Coerce(val, col.Type)); }
            catch { throw new StorageException($"Cannot coerce '{val}' to {DataTypeHelper.TypeName(col.Type)} for column '{col.Name}'"); }
        }
    }

    private void CheckPkUnique(Row row, int skipIdx = -1)
    {
        if (_pkColumnIndex < 0) return;
        var pkVal = row.Get(Columns[_pkColumnIndex].Name);
        if (pkVal == null) return;
        if (_pkIndex.TryGetValue(pkVal, out var existing) && existing != skipIdx)
            throw new StorageException($"Duplicate primary key value: {pkVal}");
    }

    private void IndexPk(Row row, int idx)
    {
        if (_pkColumnIndex < 0) return;
        var pkVal = row.Get(Columns[_pkColumnIndex].Name);
        if (pkVal != null) _pkIndex[pkVal] = idx;
    }

    private void RemovePkIndex(Row row)
    {
        if (_pkColumnIndex < 0) return;
        var pkVal = row.Get(Columns[_pkColumnIndex].Name);
        if (pkVal != null) _pkIndex.Remove(pkVal);
    }

    public void Dispose() => _file?.Dispose();

    public override string ToString() =>
        $"Table({Name}, [{string.Join(", ", Columns)}])";
}

public sealed class StorageException(string message) : Exception(message);
