using LambdaSQL.Core.Storage;

namespace LambdaSQL.Core.Catalog;

public sealed class DatabaseCatalog
{
    private readonly Dictionary<string, Table> _tables =
        new(StringComparer.OrdinalIgnoreCase);

    public void CreateTable(Table table)
    {
        if (!_tables.TryAdd(table.Name, table))
            throw new CatalogException($"Table '{table.Name}' already exists");
    }

    public void DropTable(string name, bool ifExists = false)
    {
        if (!_tables.Remove(name) && !ifExists)
            throw new CatalogException($"Table '{name}' does not exist");
    }

    public Table GetTable(string name)
    {
        if (!_tables.TryGetValue(name, out var table))
            throw new CatalogException($"Table '{name}' does not exist");
        return table;
    }

    public bool TableExists(string name) => _tables.ContainsKey(name);

    public IEnumerable<string> TableNames => _tables.Keys;
}

public sealed class CatalogException(string message) : Exception(message);
