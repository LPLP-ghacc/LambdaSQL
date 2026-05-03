using LambdaSQL.Core.Catalog;
using LambdaSQL.Core.Executor;
using LambdaSQL.Core.Lexer;
using LambdaSQL.Core.Parser;

namespace LambdaSQL.Engine;

/// <summary>
/// Top-level entry point. Accepts SQL strings and returns QueryResult.
/// </summary>
public sealed class DatabaseEngine
{
    private readonly DatabaseCatalog _catalog = new();
    private readonly Executor _executor;

    public DatabaseEngine()
    {
        _executor = new Executor(_catalog);
    }

    /// <summary>Execute a single SQL statement.</summary>
    public QueryResult Execute(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        var stmt   = new Parser(tokens).ParseOne();
        return _executor.Execute(stmt);
    }

    /// <summary>Execute multiple statements separated by semicolons.</summary>
    public IEnumerable<QueryResult> ExecuteAll(string sql)
    {
        var tokens = new Lexer(sql).Tokenize();
        var stmts  = new Parser(tokens).ParseAll();
        return stmts.Select(s => _executor.Execute(s));
    }

    public IEnumerable<string> Tables => _catalog.TableNames;
}
