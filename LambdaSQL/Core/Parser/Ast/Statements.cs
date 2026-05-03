namespace LambdaSQL.Core.Parser.Ast;

// Base statement
public abstract record Statement;

// ── SELECT ──────────────────────────────────────────────────────────────────
public record SelectStatement(
    bool Distinct,
    List<Expr> Columns,
    TableSource? From,
    Expr? Where,
    List<Expr>? GroupBy,
    Expr? Having,
    List<OrderByClause>? OrderBy,
    int? Limit
) : Statement;

public record OrderByClause(Expr Expr, bool Ascending);

public abstract record TableSource;
public record SimpleTable(string Name, string? Alias) : TableSource;
public record JoinTable(TableSource Left, JoinType Type, TableSource Right, Expr Condition) : TableSource;

public enum JoinType { Inner, Left }

// ── INSERT ───────────────────────────────────────────────────────────────────
public record InsertStatement(
    string Table,
    List<string>? Columns,
    List<List<Expr>> Rows
) : Statement;

// ── UPDATE ───────────────────────────────────────────────────────────────────
public record UpdateStatement(
    string Table,
    List<SetClause> Sets,
    Expr? Where
) : Statement;

public record SetClause(string Column, Expr Value);

// ── DELETE ───────────────────────────────────────────────────────────────────
public record DeleteStatement(
    string Table,
    Expr? Where
) : Statement;

// ── CREATE TABLE ─────────────────────────────────────────────────────────────
public record CreateTableStatement(
    string Table,
    List<ColumnDefinition> Columns
) : Statement;

public record ColumnDefinition(
    string Name,
    string DataType,
    bool NotNull,
    bool PrimaryKey,
    object? Default
);

// ── DROP TABLE ───────────────────────────────────────────────────────────────
public record DropTableStatement(string Table, bool IfExists) : Statement;
