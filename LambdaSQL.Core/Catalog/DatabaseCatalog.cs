using System.Text.Json;
using LambdaSQL.Core.Storage;
using LambdaSQL.Core.Storage.PagedStorage;

namespace LambdaSQL.Core.Catalog;

/// <summary>
/// Manages all tables. Supports both in-memory and persistent modes.
/// Persistent mode: each table gets its own .tbl file; schema stored in catalog.json.
/// </summary>
public sealed class DatabaseCatalog : IDisposable
{
    private readonly Dictionary<string, Table> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _dataDir;
    private WalWriter? _wal;

    // In-memory mode
    public DatabaseCatalog() { }

    // Persistent mode
    public DatabaseCatalog(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        _wal = new WalWriter(Path.Combine(dataDir, "wal.log"));
        LoadSchema();
    }

    // ── DDL ───────────────────────────────────────────────────────────────────

    public void CreateTable(Table table)
    {
        if (_tables.ContainsKey(table.Name))
            throw new CatalogException($"Table '{table.Name}' already exists");
        _tables[table.Name] = table;
        if (_dataDir != null) SaveSchema();
    }

    public void DropTable(string name, bool ifExists = false)
    {
        if (!_tables.TryGetValue(name, out var table))
        {
            if (!ifExists) throw new CatalogException($"Table '{name}' does not exist");
            return;
        }
        table.Dispose();
        _tables.Remove(name);

        if (_dataDir != null)
        {
            var tblPath = TablePath(name);
            if (File.Exists(tblPath)) File.Delete(tblPath);
            SaveSchema();
        }
    }

    public Table GetTable(string name)
    {
        if (!_tables.TryGetValue(name, out var table))
            throw new CatalogException($"Table '{name}' does not exist");
        return table;
    }

    public bool TableExists(string name) => _tables.ContainsKey(name);
    public IEnumerable<string> TableNames => _tables.Keys;

    // ── Build a Table with persistence wired up ───────────────────────────────

    public Table BuildTable(string name, IEnumerable<Column> columns)
    {
        if (_dataDir == null)
            return new Table(name, columns);

        var file = new TableFile(TablePath(name));
        var table = new Table(name, columns, file, _wal);
        table.LoadFromFile();
        return table;
    }

    // ── Schema persistence ────────────────────────────────────────────────────

    private void SaveSchema()
    {
        if (_dataDir == null) return;
        var schema = _tables.Values.Select(t => new TableSchema
        {
            Name    = t.Name,
            Columns = t.Columns.Select(c => new ColumnSchema
            {
                Name       = c.Name,
                Type       = DataTypeHelper.TypeName(c.Type),
                NotNull    = c.NotNull,
                PrimaryKey = c.PrimaryKey,
                Default    = c.Default?.ToString()
            }).ToList()
        }).ToList();

        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SchemaPath(), json);
    }

    private void LoadSchema()
    {
        if (_dataDir == null) return;
        var path = SchemaPath();
        if (!File.Exists(path)) return;

        var json   = File.ReadAllText(path);
        var schema = JsonSerializer.Deserialize<List<TableSchema>>(json);
        if (schema == null) return;

        foreach (var ts in schema)
        {
            var columns = ts.Columns.Select(cs => new Column(
                cs.Name,
                DataTypeHelper.Parse(cs.Type),
                cs.NotNull,
                cs.PrimaryKey,
                cs.Default
            ));
            var table = BuildTable(ts.Name, columns);
            _tables[ts.Name] = table;
        }
    }

    private string TablePath(string name)  => Path.Combine(_dataDir!, $"{name.ToLowerInvariant()}.tbl");
    private string SchemaPath()            => Path.Combine(_dataDir!, "catalog.json");

    public void Dispose()
    {
        foreach (var t in _tables.Values) t.Dispose();
        _wal?.Dispose();
    }

    // ── Schema DTOs ───────────────────────────────────────────────────────────

    private sealed class TableSchema
    {
        public string Name { get; set; } = "";
        public List<ColumnSchema> Columns { get; set; } = new();
    }

    private sealed class ColumnSchema
    {
        public string  Name       { get; set; } = "";
        public string  Type       { get; set; } = "";
        public bool    NotNull    { get; set; }
        public bool    PrimaryKey { get; set; }
        public string? Default    { get; set; }
    }
}

public sealed class CatalogException(string message) : Exception(message);
