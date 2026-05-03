using LambdaSQL.Core.Catalog;
using LambdaSQL.Core.Parser.Ast;
using LambdaSQL.Core.Storage;

namespace LambdaSQL.Core.Executor;

public sealed class Executor
{
    private readonly DatabaseCatalog _catalog;

    public Executor(DatabaseCatalog catalog)
    {
        _catalog = catalog;
    }

    public QueryResult Execute(Statement stmt) => stmt switch
    {
        SelectStatement sel    => ExecuteSelect(sel),
        InsertStatement ins    => ExecuteInsert(ins),
        UpdateStatement upd    => ExecuteUpdate(upd),
        DeleteStatement del    => ExecuteDelete(del),
        CreateTableStatement c => ExecuteCreate(c),
        DropTableStatement d   => ExecuteDrop(d),
        _ => throw new ExecutorException($"Unsupported statement: {stmt.GetType().Name}")
    };

    // ── CREATE TABLE ─────────────────────────────────────────────────────────

    private QueryResult ExecuteCreate(CreateTableStatement stmt)
    {
        var columns = stmt.Columns.Select(cd => new Column(
            cd.Name,
            DataTypeHelper.Parse(cd.DataType),
            cd.NotNull,
            cd.PrimaryKey,
            cd.Default
        ));

        var table = _catalog.BuildTable(stmt.Table, columns);
        _catalog.CreateTable(table);
        return new QueryResult($"Table '{stmt.Table}' created.");
    }

    // ── DROP TABLE ───────────────────────────────────────────────────────────

    private QueryResult ExecuteDrop(DropTableStatement stmt)
    {
        _catalog.DropTable(stmt.Table, stmt.IfExists);
        return new QueryResult($"Table '{stmt.Table}' dropped.");
    }

    // ── INSERT ───────────────────────────────────────────────────────────────

    private QueryResult ExecuteInsert(InsertStatement stmt)
    {
        var table = _catalog.GetTable(stmt.Table);
        var count = 0;

        foreach (var rowExprs in stmt.Rows)
        {
            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (stmt.Columns != null)
            {
                if (stmt.Columns.Count != rowExprs.Count)
                    throw new ExecutorException("Column count does not match value count");

                for (var i = 0; i < stmt.Columns.Count; i++)
                    data[stmt.Columns[i]] = ExprEvaluator.Eval(rowExprs[i], null);
            }
            else
            {
                if (table.Columns.Count != rowExprs.Count)
                    throw new ExecutorException("Value count does not match table column count");

                for (var i = 0; i < table.Columns.Count; i++)
                    data[table.Columns[i].Name] = ExprEvaluator.Eval(rowExprs[i], null);
            }

            table.Insert(new Row(data));
            count++;
        }

        return new QueryResult(count, $"{count} row(s) inserted.");
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────

    private QueryResult ExecuteUpdate(UpdateStatement stmt)
    {
        var table = _catalog.GetTable(stmt.Table);

        var count = table.Update(
            row => stmt.Where == null || ExprEvaluator.IsTruthy(ExprEvaluator.Eval(stmt.Where, row)),
            row =>
            {
                foreach (var set in stmt.Sets)
                    row.Set(set.Column, ExprEvaluator.Eval(set.Value, row));
            }
        );

        return new QueryResult(count, $"{count} row(s) updated.");
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    private QueryResult ExecuteDelete(DeleteStatement stmt)
    {
        var table = _catalog.GetTable(stmt.Table);

        var count = table.Delete(
            row => stmt.Where == null || ExprEvaluator.IsTruthy(ExprEvaluator.Eval(stmt.Where, row))
        );

        return new QueryResult(count, $"{count} row(s) deleted.");
    }

    // ── SELECT ───────────────────────────────────────────────────────────────

    private QueryResult ExecuteSelect(SelectStatement stmt)
    {
        // 1. FROM + JOIN
        var rows = stmt.From != null
            ? ResolveTableSource(stmt.From)
            : [new Row(new Dictionary<string, object?>())];

        // 2. WHERE
        if (stmt.Where != null)
            rows = rows.Where(r => ExprEvaluator.IsTruthy(ExprEvaluator.Eval(stmt.Where, r)));

        // 3. GROUP BY / aggregates
        var hasAggregates = stmt.Columns.Any(ContainsAggregate);

        List<Row> resultRows;

        if (stmt.GroupBy != null || hasAggregates)
        {
            resultRows = ExecuteGroupBy(rows.ToList(), stmt);
        }
        else
        {
            // 4. Project columns
            resultRows = rows.Select(r => ProjectRow(r, stmt.Columns)).ToList();
        }

        // 5. DISTINCT (non-aggregate path)
        if (stmt is { Distinct: true, GroupBy: null } && !hasAggregates)
            resultRows = DistinctRows(resultRows);

        // 6. ORDER BY
        if (stmt.OrderBy != null)
            resultRows = ApplyOrderBy(resultRows, stmt.OrderBy);

        // 7. LIMIT
        if (stmt.Limit.HasValue)
            resultRows = resultRows.Take(stmt.Limit.Value).ToList();

        // 8. Build result
        var colNames = ResolveColumnNames(stmt.Columns, resultRows);
        var data = resultRows.Select(r => colNames.Select(r.Get).ToArray()).ToList();

        return new QueryResult(colNames, data);
    }

    // ── GROUP BY ─────────────────────────────────────────────────────────────

    private List<Row> ExecuteGroupBy(List<Row> rows, SelectStatement stmt)
    {
        IEnumerable<IGrouping<string, Row>> groups;

        if (stmt.GroupBy is { Count: > 0 })
        {
            groups = rows.GroupBy(r =>
                string.Join("|", stmt.GroupBy.Select(g => ExprEvaluator.Eval(g, r)?.ToString() ?? "NULL")));
        }
        else
        {
            // Single group for bare aggregates
            groups = rows.GroupBy(_ => "");
        }

        var result = new List<Row>();

        foreach (var group in groups)
        {
            var groupRows = group.ToList();
            var firstRow  = groupRows[0];
            var data      = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var colExpr in stmt.Columns)
            {
                var (name, value) = EvalSelectColumn(colExpr, firstRow, groupRows);
                data[name] = value;
            }

            var row = new Row(data);

            // HAVING
            if (stmt.Having != null && !ExprEvaluator.IsTruthy(EvalAggregate(stmt.Having, groupRows, firstRow)))
                continue;

            result.Add(row);
        }

        return result;
    }

    private (string name, object? value) EvalSelectColumn(Expr expr, Row firstRow, List<Row> groupRows)
    {
        return expr switch
        {
            AliasExpr alias => (alias.Alias, EvalAggregate(alias.Inner, groupRows, firstRow)),
            FunctionExpr fn => (fn.Name, EvalAggregate(fn, groupRows, firstRow)),
            WildcardExpr    => ("*", null),
            ColumnExpr col  => (col.Name, ExprEvaluator.Eval(col, firstRow)),
            _ => ("expr", EvalAggregate(expr, groupRows, firstRow))
        };
    }

    private object? EvalAggregate(Expr expr, List<Row> groupRows, Row firstRow)
    {
        if (expr is FunctionExpr fn)
        {
            return fn.Name.ToLowerInvariant() switch
            {
                "count" => fn.Args is [WildcardExpr]
                    ? (object?)groupRows.Count
                    : groupRows.Count(r => ExprEvaluator.Eval(fn.Args[0], r) != null),

                "sum" => groupRows
                    .Select(r => ExprEvaluator.Eval(fn.Args[0], r))
                    .Where(v => v != null)
                    .Aggregate((object?)0.0, (acc, v) => ExprEvaluator.ToDouble(acc) + ExprEvaluator.ToDouble(v)),

                "avg" => groupRows
                    .Select(r => ExprEvaluator.Eval(fn.Args[0], r))
                    .Where(v => v != null)
                    .Select(ExprEvaluator.ToDouble)
                    .DefaultIfEmpty(0)
                    .Average(),

                "min" => groupRows
                    .Select(r => ExprEvaluator.Eval(fn.Args[0], r))
                    .Where(v => v != null)
                    .OrderBy(v => v, Comparer<object?>.Create((a, b) =>
                        ExprEvaluator.IsNumeric(a) ? ExprEvaluator.ToDouble(a).CompareTo(ExprEvaluator.ToDouble(b))
                        : string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal)))
                    .FirstOrDefault(),

                "max" => groupRows
                    .Select(r => ExprEvaluator.Eval(fn.Args[0], r))
                    .Where(v => v != null)
                    .OrderByDescending(v => v, Comparer<object?>.Create((a, b) =>
                        ExprEvaluator.IsNumeric(a) ? ExprEvaluator.ToDouble(a).CompareTo(ExprEvaluator.ToDouble(b))
                        : string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal)))
                    .FirstOrDefault(),

                _ => throw new ExecutorException($"Unknown aggregate function: {fn.Name}")
            };
        }

        return ExprEvaluator.Eval(expr, firstRow);
    }

    // ── Projection ───────────────────────────────────────────────────────────

    private static Row ProjectRow(Row source, List<Expr> columns)
    {
        // Wildcard: return all columns
        if (columns is [WildcardExpr])
            return source.Clone();

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in columns)
        {
            var (name, value) = col switch
            {
                AliasExpr alias => (alias.Alias, ExprEvaluator.Eval(alias.Inner, source)),
                ColumnExpr c    => (c.Name, ExprEvaluator.Eval(c, source)),
                WildcardExpr    => ("*", null),
                _               => ("expr", ExprEvaluator.Eval(col, source))
            };
            data[name] = value;
        }

        return new Row(data);
    }

    // ── ORDER BY ─────────────────────────────────────────────────────────────

    private static List<Row> ApplyOrderBy(List<Row> rows, List<OrderByClause> orderBy)
    {
        IOrderedEnumerable<Row>? ordered = null;

        for (var i = 0; i < orderBy.Count; i++)
        {
            var clause = orderBy[i];
            Func<Row, object?> key = r => ExprEvaluator.Eval(clause.Expr, r);
            var comparer = Comparer<object?>.Create((a, b) =>
            {
                switch (a)
                {
                    case null when b is null:
                        return 0;
                    case null:
                        return -1;
                }

                if (b is null) return 1;
                if (ExprEvaluator.IsNumeric(a) && ExprEvaluator.IsNumeric(b))
                    return ExprEvaluator.ToDouble(a).CompareTo(ExprEvaluator.ToDouble(b));
                return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
            });

            if (i == 0)
                ordered = clause.Ascending ? rows.OrderBy(key, comparer) : rows.OrderByDescending(key, comparer);
            else
                ordered = clause.Ascending ? ordered!.ThenBy(key, comparer) : ordered!.ThenByDescending(key, comparer);
        }

        return ordered?.ToList() ?? rows;
    }

    // ── DISTINCT ─────────────────────────────────────────────────────────────

    private static List<Row> DistinctRows(List<Row> rows)
    {
        var seen = new HashSet<string>();
        return (from row in rows let key = string.Join("|", row.Data.Values.Select(v => v?.ToString() ?? "NULL")) where seen.Add(key) select row).ToList();
    }

    // ── Table source resolution ───────────────────────────────────────────────

    private IEnumerable<Row> ResolveTableSource(TableSource source)
    {
        return source switch
        {
            SimpleTable st => _catalog.GetTable(st.Name).Scan()
                .Select(r => st.Alias != null ? AliasRow(r, st.Alias) : r),

            JoinTable jt => ExecuteJoin(jt),

            _ => throw new ExecutorException($"Unknown table source: {source.GetType().Name}")
        };
    }

    private static Row AliasRow(Row row, string alias)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in row.Data)
        {
            data[k] = v;
            data[$"{alias}.{k}"] = v;
        }
        return new Row(data);
    }

    private IEnumerable<Row> ExecuteJoin(JoinTable jt)
    {
        var leftRows  = ResolveTableSource(jt.Left).ToList();
        var rightRows = ResolveTableSource(jt.Right).ToList();

        var leftAlias  = (jt.Left  as SimpleTable)?.Alias;
        var rightAlias = (jt.Right as SimpleTable)?.Alias;

        foreach (var left in leftRows)
        {
            var matched = false;
            foreach (var merged in rightRows
                         .Select(right => Row.Merge(left, leftAlias, right, rightAlias))
                         .Where(merged => ExprEvaluator.IsTruthy(ExprEvaluator.Eval(jt.Condition, merged))))
            {
                yield return merged;
                matched = true;
            }

            // LEFT JOIN: emit left row with NULLs if no match
            if (matched || jt.Type != JoinType.Left) continue;
            var nullRight = new Row(rightRows.FirstOrDefault()?.Data.Keys
                                        .ToDictionary(k => k, object? (_) => null, StringComparer.OrdinalIgnoreCase)
                                    ?? new Dictionary<string, object?>());
            yield return Row.Merge(left, leftAlias, nullRight, rightAlias);
        }
    }

    // ── Column name resolution ────────────────────────────────────────────────

    private static string[] ResolveColumnNames(List<Expr> selectExprs, List<Row> rows)
    {
        if (selectExprs is [WildcardExpr])
        {
            return rows.Count > 0 ? rows[0].Data.Keys.ToArray() : Array.Empty<string>();
        }

        return selectExprs.Select((e, i) => e switch
        {
            AliasExpr alias => alias.Alias,
            ColumnExpr col  => col.Name,
            FunctionExpr fn => fn.Name,
            WildcardExpr    => "*",
            _               => $"col{i}"
        }).ToArray();
    }

    // ── Aggregate detection ───────────────────────────────────────────────────

    private static bool ContainsAggregate(Expr expr)
    {
        return expr switch
        {
            FunctionExpr fn => fn.Name.ToLowerInvariant() is "count" or "sum" or "avg" or "min" or "max",
            AliasExpr alias => ContainsAggregate(alias.Inner),
            BinaryExpr b    => ContainsAggregate(b.Left) || ContainsAggregate(b.Right),
            UnaryExpr u     => ContainsAggregate(u.Operand),
            _ => false
        };
    }
}
