namespace LambdaSQL.Core.Parser.Ast;

// Base
public abstract record Expr;

// Literals
public record LiteralExpr(object? Value) : Expr;

// Column reference: name or table.name
public record ColumnExpr(string Name, string? TableAlias = null) : Expr;

// Wildcard *
public record WildcardExpr : Expr;

// Binary operation: left op right
public record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;

// Unary: NOT expr, -expr
public record UnaryExpr(string Op, Expr Operand) : Expr;

// Function call: count(*), sum(price)
public record FunctionExpr(string Name, List<Expr> Args, bool Distinct = false) : Expr;

// IS NULL / IS NOT NULL
public record IsNullExpr(Expr Operand, bool IsNot) : Expr;

// IN (val1, val2, ...)
public record InExpr(Expr Operand, List<Expr> Values, bool IsNot) : Expr;

// LIKE
public record LikeExpr(Expr Operand, Expr Pattern, bool IsNot) : Expr;

// BETWEEN
public record BetweenExpr(Expr Operand, Expr Low, Expr High, bool IsNot) : Expr;

// Alias: expr AS alias
public record AliasExpr(Expr Inner, string Alias) : Expr;
