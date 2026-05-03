using LambdaSQL.Core.Lexer;
using LambdaSQL.Core.Parser.Ast;

namespace LambdaSQL.Core.Parser;

public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    // ── Public entry ────────────────────────────────────────────────────────

    public List<Statement> ParseAll()
    {
        var stmts = new List<Statement>();
        while (!IsEof())
        {
            if (Check(TokenType.Semicolon)) { Advance(); continue; }
            stmts.Add(ParseStatement());
        }
        return stmts;
    }

    public Statement ParseOne()
    {
        var stmt = ParseStatement();
        if (Check(TokenType.Semicolon)) Advance();
        return stmt;
    }

    // ── Statement dispatch ───────────────────────────────────────────────────

    private Statement ParseStatement() => Current().Type switch
    {
        TokenType.Select => ParseSelect(),
        TokenType.Insert => ParseInsert(),
        TokenType.Update => ParseUpdate(),
        TokenType.Delete => ParseDelete(),
        TokenType.Create => ParseCreate(),
        TokenType.Drop   => ParseDrop(),
        _ => throw new ParserException($"Unexpected token '{Current().Value}' at position {Current().Position}")
    };

    // ── SELECT ───────────────────────────────────────────────────────────────

    private SelectStatement ParseSelect()
    {
        Expect(TokenType.Select);
        bool distinct = TryConsume(TokenType.Distinct);

        var columns = ParseExprList();

        TableSource? from = null;
        if (TryConsume(TokenType.From))
            from = ParseTableSource();

        Expr? where = null;
        if (TryConsume(TokenType.Where))
            where = ParseOr();

        List<Expr>? groupBy = null;
        if (TryConsume(TokenType.Group))
        {
            Expect(TokenType.By);
            groupBy = ParseExprList();
        }

        Expr? having = null;
        if (TryConsume(TokenType.Having))
            having = ParseOr();

        List<OrderByClause>? orderBy = null;
        if (TryConsume(TokenType.Order))
        {
            Expect(TokenType.By);
            orderBy = ParseOrderByList();
        }

        int? limit = null;
        if (TryConsume(TokenType.Limit))
            limit = int.Parse(Expect(TokenType.Integer).Value);

        return new SelectStatement(distinct, columns, from, where, groupBy, having, orderBy, limit);
    }

    private TableSource ParseTableSource()
    {
        TableSource left = ParseSingleTable();

        while (Check(TokenType.Inner) || Check(TokenType.Left) || Check(TokenType.Join))
        {
            JoinType joinType;
            if (TryConsume(TokenType.Inner))
            {
                Expect(TokenType.Join);
                joinType = JoinType.Inner;
            }
            else if (TryConsume(TokenType.Left))
            {
                Expect(TokenType.Join);
                joinType = JoinType.Left;
            }
            else
            {
                Advance(); // join keyword
                joinType = JoinType.Inner;
            }

            var right = (TableSource)ParseSingleTable();
            Expect(TokenType.On);
            var condition = ParseOr();
            left = new JoinTable(left, joinType, right, condition);
        }

        return left;
    }

    private SimpleTable ParseSingleTable()
    {
        var name = Expect(TokenType.Identifier).Value;
        string? alias = null;
        if (TryConsume(TokenType.As))
            alias = Expect(TokenType.Identifier).Value;
        else if (Check(TokenType.Identifier))
            alias = Advance().Value;
        return new SimpleTable(name, alias);
    }

    private List<OrderByClause> ParseOrderByList()
    {
        var list = new List<OrderByClause>();
        do
        {
            var expr = ParseOr();
            bool asc = true;
            if (TryConsume(TokenType.Desc)) asc = false;
            else TryConsume(TokenType.Asc);
            list.Add(new OrderByClause(expr, asc));
        } while (TryConsume(TokenType.Comma));
        return list;
    }

    // ── INSERT ───────────────────────────────────────────────────────────────

    private InsertStatement ParseInsert()
    {
        Expect(TokenType.Insert);
        Expect(TokenType.Into);
        var table = Expect(TokenType.Identifier).Value;

        List<string>? cols = null;
        if (TryConsume(TokenType.LeftParen))
        {
            cols = new List<string>();
            do { cols.Add(Expect(TokenType.Identifier).Value); } while (TryConsume(TokenType.Comma));
            Expect(TokenType.RightParen);
        }

        Expect(TokenType.Values);
        var rows = new List<List<Expr>>();
        do
        {
            Expect(TokenType.LeftParen);
            var row = new List<Expr>();
            do { row.Add(ParseOr()); } while (TryConsume(TokenType.Comma));
            Expect(TokenType.RightParen);
            rows.Add(row);
        } while (TryConsume(TokenType.Comma));

        return new InsertStatement(table, cols, rows);
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────

    private UpdateStatement ParseUpdate()
    {
        Expect(TokenType.Update);
        var table = Expect(TokenType.Identifier).Value;
        Expect(TokenType.Set);

        var sets = new List<SetClause>();
        do
        {
            var col = Expect(TokenType.Identifier).Value;
            Expect(TokenType.Equals);
            var val = ParseOr();
            sets.Add(new SetClause(col, val));
        } while (TryConsume(TokenType.Comma));

        Expr? where = null;
        if (TryConsume(TokenType.Where))
            where = ParseOr();

        return new UpdateStatement(table, sets, where);
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    private DeleteStatement ParseDelete()
    {
        Expect(TokenType.Delete);
        Expect(TokenType.From);
        var table = Expect(TokenType.Identifier).Value;

        Expr? where = null;
        if (TryConsume(TokenType.Where))
            where = ParseOr();

        return new DeleteStatement(table, where);
    }

    // ── CREATE TABLE ─────────────────────────────────────────────────────────

    private CreateTableStatement ParseCreate()
    {
        Expect(TokenType.Create);
        Expect(TokenType.Table);
        var table = Expect(TokenType.Identifier).Value;
        Expect(TokenType.LeftParen);

        var cols = new List<ColumnDefinition>();
        do
        {
            var colName = Expect(TokenType.Identifier).Value;
            var typeTok = Advance();
            var typeName = typeTok.Value.ToLowerInvariant();

            bool notNull = false;
            bool pk = false;
            object? def = null;

            // parse constraints
            while (!Check(TokenType.Comma) && !Check(TokenType.RightParen) && !IsEof())
            {
                if (Current().Value.Equals("not", StringComparison.OrdinalIgnoreCase) &&
                    _pos + 1 < _tokens.Count &&
                    _tokens[_pos + 1].Value.Equals("null", StringComparison.OrdinalIgnoreCase))
                {
                    Advance(); Advance(); notNull = true;
                }
                else if (Current().Value.Equals("primary", StringComparison.OrdinalIgnoreCase) &&
                         _pos + 1 < _tokens.Count &&
                         _tokens[_pos + 1].Value.Equals("key", StringComparison.OrdinalIgnoreCase))
                {
                    Advance(); Advance(); pk = true; notNull = true;
                }
                else if (Current().Value.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    def = EvalLiteral(ParsePrimary());
                }
                else break;
            }

            cols.Add(new ColumnDefinition(colName, typeName, notNull, pk, def));
        } while (TryConsume(TokenType.Comma));

        Expect(TokenType.RightParen);
        return new CreateTableStatement(table, cols);
    }

    // ── DROP TABLE ───────────────────────────────────────────────────────────

    private DropTableStatement ParseDrop()
    {
        Expect(TokenType.Drop);
        Expect(TokenType.Table);

        bool ifExists = false;
        if (Current().Value.Equals("if", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            if (Current().Value.Equals("exists", StringComparison.OrdinalIgnoreCase))
            { Advance(); ifExists = true; }
        }

        var table = Expect(TokenType.Identifier).Value;
        return new DropTableStatement(table, ifExists);
    }

    // ── Expression parsing (Pratt-style) ─────────────────────────────────────

    private List<Expr> ParseExprList()
    {
        var list = new List<Expr>();
        do { list.Add(ParseAliasExpr()); } while (TryConsume(TokenType.Comma));
        return list;
    }

    private Expr ParseAliasExpr()
    {
        var expr = ParseOr();
        if (TryConsume(TokenType.As))
        {
            var alias = Advance().Value;
            return new AliasExpr(expr, alias);
        }
        return expr;
    }

    // OR
    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (TryConsume(TokenType.Or))
            left = new BinaryExpr(left, "or", ParseAnd());
        return left;
    }

    // AND
    private Expr ParseAnd()
    {
        var left = ParseNot();
        while (TryConsume(TokenType.And))
            left = new BinaryExpr(left, "and", ParseNot());
        return left;
    }

    // NOT
    private Expr ParseNot()
    {
        if (TryConsume(TokenType.Not))
            return new UnaryExpr("not", ParseNot());
        return ParseComparison();
    }

    // Comparison: =, !=, <, <=, >, >=, IS NULL, IN, LIKE, BETWEEN
    private Expr ParseComparison()
    {
        var left = ParseAddSub();

        // IS [NOT] NULL
        if (TryConsume(TokenType.Is))
        {
            bool isNot = TryConsume(TokenType.Not);
            Expect(TokenType.Null);
            return new IsNullExpr(left, isNot);
        }

        // [NOT] IN (...)
        bool notIn = false;
        if (Check(TokenType.Not) && PeekNext().Type == TokenType.In)
        { Advance(); notIn = true; }
        if (TryConsume(TokenType.In))
        {
            Expect(TokenType.LeftParen);
            var vals = new List<Expr>();
            do { vals.Add(ParseOr()); } while (TryConsume(TokenType.Comma));
            Expect(TokenType.RightParen);
            return new InExpr(left, vals, notIn);
        }

        // [NOT] LIKE
        bool notLike = false;
        if (Check(TokenType.Not) && PeekNext().Type == TokenType.Like)
        { Advance(); notLike = true; }
        if (TryConsume(TokenType.Like))
            return new LikeExpr(left, ParseAddSub(), notLike);

        // Standard comparison operators
        if (Check(TokenType.Equals) || Check(TokenType.NotEquals) ||
            Check(TokenType.Less) || Check(TokenType.LessOrEqual) ||
            Check(TokenType.Greater) || Check(TokenType.GreaterOrEqual))
        {
            var op = Advance().Value;
            return new BinaryExpr(left, op, ParseAddSub());
        }

        return left;
    }

    // + -
    private Expr ParseAddSub()
    {
        var left = ParseMulDiv();
        while (Check(TokenType.Plus) || Check(TokenType.Minus))
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseMulDiv());
        }
        return left;
    }

    // * / %
    private Expr ParseMulDiv()
    {
        var left = ParseUnary();
        while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Percent))
        {
            var op = Advance().Value;
            left = new BinaryExpr(left, op, ParseUnary());
        }
        return left;
    }

    // Unary -
    private Expr ParseUnary()
    {
        if (TryConsume(TokenType.Minus))
            return new UnaryExpr("-", ParsePrimary());
        return ParsePrimary();
    }

    // Primary: literal, identifier, function call, (expr), *
    private Expr ParsePrimary()
    {
        var tok = Current();

        // Wildcard *
        if (tok.Type == TokenType.Star)
        {
            Advance();
            return new WildcardExpr();
        }

        // Literals
        if (tok.Type == TokenType.Integer)
        { Advance(); return new LiteralExpr(long.Parse(tok.Value)); }

        if (tok.Type == TokenType.Float)
        { Advance(); return new LiteralExpr(double.Parse(tok.Value, System.Globalization.CultureInfo.InvariantCulture)); }

        if (tok.Type == TokenType.String)
        { Advance(); return new LiteralExpr(tok.Value); }

        if (tok.Type == TokenType.Bool)
        { Advance(); return new LiteralExpr(tok.Value == "true"); }

        if (tok.Type == TokenType.Null)
        { Advance(); return new LiteralExpr(null); }

        // Grouped expression
        if (tok.Type == TokenType.LeftParen)
        {
            Advance();
            var inner = ParseOr();
            Expect(TokenType.RightParen);
            return inner;
        }

        // Identifier or function call
        if (tok.Type == TokenType.Identifier)
        {
            Advance();
            // function call
            if (Check(TokenType.LeftParen))
            {
                Advance();
                bool distinct = TryConsume(TokenType.Distinct);
                var args = new List<Expr>();
                if (!Check(TokenType.RightParen))
                {
                    if (Check(TokenType.Star))
                    { Advance(); args.Add(new WildcardExpr()); }
                    else
                    { do { args.Add(ParseOr()); } while (TryConsume(TokenType.Comma)); }
                }
                Expect(TokenType.RightParen);
                return new FunctionExpr(tok.Value.ToLowerInvariant(), args, distinct);
            }

            // table.column
            if (Check(TokenType.Dot))
            {
                Advance();
                var col = Advance().Value;
                return new ColumnExpr(col, tok.Value);
            }

            return new ColumnExpr(tok.Value);
        }

        throw new ParserException($"Unexpected token '{tok.Value}' ({tok.Type}) at position {tok.Position}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static object? EvalLiteral(Expr e) => e switch
    {
        LiteralExpr l => l.Value,
        UnaryExpr { Op: "-", Operand: LiteralExpr l2 } => l2.Value switch
        {
            long v   => -v,
            double v => -v,
            _ => null
        },
        _ => null
    };

    private Token Current() => _tokens[_pos];
    private Token PeekNext() => _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : _tokens[^1];
    private bool IsEof() => Current().Type == TokenType.Eof;
    private bool Check(TokenType t) => Current().Type == t;

    private Token Advance()
    {
        var t = _tokens[_pos];
        if (!IsEof()) _pos++;
        return t;
    }

    private Token Expect(TokenType t)
    {
        if (!Check(t))
            throw new ParserException($"Expected {t} but got '{Current().Value}' ({Current().Type}) at position {Current().Position}");
        return Advance();
    }

    private bool TryConsume(TokenType t)
    {
        if (!Check(t)) return false;
        Advance();
        return true;
    }
}

public sealed class ParserException(string message) : Exception(message);
