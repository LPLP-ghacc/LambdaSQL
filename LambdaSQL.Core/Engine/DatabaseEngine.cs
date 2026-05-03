using LambdaSQL.Core.Catalog;
using LambdaSQL.Core.Executor;
using LambdaSQL.Core.Storage;
using CoreExecutor = LambdaSQL.Core.Executor.Executor;
using CoreLexer   = LambdaSQL.Core.Lexer.Lexer;
using CoreParser  = LambdaSQL.Core.Parser.Parser;

namespace LambdaSQL.Core.Engine;

/// <summary>
/// Top-level entry point for the SQL engine.
/// Supports both in-memory and persistent modes.
/// Thread-safe: uses a reader-writer lock for concurrent reads.
/// </summary>
public sealed class DatabaseEngine : IDisposable
{
    private readonly DatabaseCatalog _catalog;
    private readonly CoreExecutor _executor;
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);

    /// <summary>In-memory mode — no persistence.</summary>
    public DatabaseEngine()
    {
        _catalog  = new DatabaseCatalog();
        _executor = new CoreExecutor(_catalog);
    }

    /// <summary>Persistent mode — data stored in <paramref name="dataDir"/>.</summary>
    public DatabaseEngine(string dataDir)
    {
        _catalog  = new DatabaseCatalog(dataDir);
        _executor = new CoreExecutor(_catalog);
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    /// <summary>Execute a single SQL statement. Thread-safe.</summary>
    public QueryResult Execute(string sql)
    {
        var tokens = new CoreLexer(sql).Tokenize();
        var stmt   = new CoreParser(tokens).ParseOne();

        bool isRead = stmt is Parser.Ast.SelectStatement;

        if (isRead)
        {
            _rwLock.EnterReadLock();
            try   { return _executor.Execute(stmt); }
            finally { _rwLock.ExitReadLock(); }
        }
        else
        {
            _rwLock.EnterWriteLock();
            try   { return _executor.Execute(stmt); }
            finally { _rwLock.ExitWriteLock(); }
        }
    }

    /// <summary>Execute multiple semicolon-separated statements.</summary>
    public IEnumerable<QueryResult> ExecuteAll(string sql)
    {
        var tokens = new CoreLexer(sql).Tokenize();
        var stmts  = new CoreParser(tokens).ParseAll();
        return stmts.Select(s =>
        {
            bool isRead = s is Parser.Ast.SelectStatement;
            if (isRead)
            {
                _rwLock.EnterReadLock();
                try   { return _executor.Execute(s); }
                finally { _rwLock.ExitReadLock(); }
            }
            else
            {
                _rwLock.EnterWriteLock();
                try   { return _executor.Execute(s); }
                finally { _rwLock.ExitWriteLock(); }
            }
        });
    }

    public IEnumerable<string> Tables => _catalog.TableNames;

    public object GetTableInfo(string name)
    {
        var table = _catalog.GetTable(name);
        return new
        {
            name = table.Name,
            columns = table.Columns.Select(c => new
            {
                name       = c.Name,
                type       = DataTypeHelper.TypeName(c.Type),
                notNull    = c.NotNull,
                primaryKey = c.PrimaryKey
            })
        };
    }

    public void Dispose()
    {
        _rwLock.Dispose();
        _catalog.Dispose();
    }
}
