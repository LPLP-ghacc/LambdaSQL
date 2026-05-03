using LambdaSQL.Core.Parser.Ast;
using LambdaSQL.Core.Storage;

namespace LambdaSQL.Core.Executor;

/// <summary>
/// Evaluates an expression against a row context.
/// </summary>
public static class ExprEvaluator
{
    public static object? Eval(Expr expr, Row? row)
    {
        return expr switch
        {
            LiteralExpr lit => lit.Value,

            ColumnExpr col => row?.Get(col.TableAlias != null
                ? $"{col.TableAlias}.{col.Name}"
                : col.Name) ?? row?.Get(col.Name),

            WildcardExpr => throw new ExecutorException("Wildcard (*) cannot be evaluated as a scalar"),

            UnaryExpr u => EvalUnary(u, row),

            BinaryExpr b => EvalBinary(b, row),

            IsNullExpr isn =>
                isn.IsNot ? Eval(isn.Operand, row) is not null
                           : Eval(isn.Operand, row) is null,

            InExpr inExpr => EvalIn(inExpr, row),

            LikeExpr like => EvalLike(like, row),

            FunctionExpr fn => throw new ExecutorException(
                $"Aggregate function '{fn.Name}' cannot be evaluated as scalar here"),

            AliasExpr alias => Eval(alias.Inner, row),

            _ => throw new ExecutorException($"Unknown expression type: {expr.GetType().Name}")
        };
    }

    // ── Unary ────────────────────────────────────────────────────────────────

    private static object? EvalUnary(UnaryExpr u, Row? row)
    {
        var val = Eval(u.Operand, row);
        return u.Op switch
        {
            "-" => val switch
            {
                int i    => -i,
                long l   => -l,
                double d => -d,
                float f  => -f,
                _ => throw new ExecutorException($"Cannot negate {val}")
            },
            "not" => val is bool b ? !b : throw new ExecutorException("NOT requires boolean"),
            _ => throw new ExecutorException($"Unknown unary op: {u.Op}")
        };
    }

    // ── Binary ───────────────────────────────────────────────────────────────

    private static object? EvalBinary(BinaryExpr b, Row? row)
    {
        // Short-circuit for AND / OR
        if (b.Op == "and")
        {
            var l = Eval(b.Left, row);
            if (l is false) return false;
            return Eval(b.Right, row);
        }
        if (b.Op == "or")
        {
            var l = Eval(b.Left, row);
            if (l is true) return true;
            return Eval(b.Right, row);
        }

        var left  = Eval(b.Left, row);
        var right = Eval(b.Right, row);

        return b.Op switch
        {
            "="  => CompareEqual(left, right),
            "!=" or "<>" => !CompareEqual(left, right),
            "<"  => Compare(left, right) < 0,
            "<=" => Compare(left, right) <= 0,
            ">"  => Compare(left, right) > 0,
            ">=" => Compare(left, right) >= 0,
            "+"  => ArithAdd(left, right),
            "-"  => ArithSub(left, right),
            "*"  => ArithMul(left, right),
            "/"  => ArithDiv(left, right),
            "%"  => ArithMod(left, right),
            _ => throw new ExecutorException($"Unknown binary op: {b.Op}")
        };
    }

    // ── Comparison helpers ───────────────────────────────────────────────────

    private static bool CompareEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is string sa && b is string sb)
            return string.Equals(sa, sb, StringComparison.Ordinal);
        if (IsNumeric(a) && IsNumeric(b))
            return Math.Abs(ToDouble(a) - ToDouble(b)) < 0.00000001;
        return a.Equals(b);
    }

    private static int Compare(object? a, object? b)
    {
        switch (a)
        {
            case null when b is null:
                return 0;
            case null:
                return -1;
        }

        if (b is null) return 1;

        if (IsNumeric(a) && IsNumeric(b))
            return ToDouble(a).CompareTo(ToDouble(b));

        return a switch
        {
            string sa when b is string sb => string.Compare(sa, sb, StringComparison.Ordinal),
            bool ba when b is bool bb => ba.CompareTo(bb),
            _ => throw new ExecutorException($"Cannot compare {a.GetType().Name} with {b.GetType().Name}")
        };
    }

    // ── Arithmetic ───────────────────────────────────────────────────────────

    private static object? ArithAdd(object? a, object? b)
    {
        if (a is string sa) return sa + Convert.ToString(b);
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double) return ToDouble(a) + ToDouble(b);
            return ToLong(a) + ToLong(b);
        }
        throw new ExecutorException($"Cannot add {a} and {b}");
    }

    private static object? ArithSub(object? a, object? b)
    {
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double) return ToDouble(a) - ToDouble(b);
            return ToLong(a) - ToLong(b);
        }
        throw new ExecutorException($"Cannot subtract {a} and {b}");
    }

    private static object? ArithMul(object? a, object? b)
    {
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double) return ToDouble(a) * ToDouble(b);
            return ToLong(a) * ToLong(b);
        }
        throw new ExecutorException($"Cannot multiply {a} and {b}");
    }

    private static object? ArithDiv(object? a, object? b)
    {
        if (IsNumeric(a) && IsNumeric(b))
        {
            var d = ToDouble(b);
            if (d == 0) throw new ExecutorException("Division by zero");
            return ToDouble(a) / d;
        }
        throw new ExecutorException($"Cannot divide {a} and {b}");
    }

    private static object? ArithMod(object? a, object? b)
    {
        if (IsNumeric(a) && IsNumeric(b))
            return ToLong(a) % ToLong(b);
        throw new ExecutorException($"Cannot mod {a} and {b}");
    }

    // ── IN ───────────────────────────────────────────────────────────────────

    private static object? EvalIn(InExpr inExpr, Row? row)
    {
        var val = Eval(inExpr.Operand, row);
        var found = inExpr.Values.Any(v => CompareEqual(val, Eval(v, row)));
        return inExpr.IsNot ? !found : found;
    }

    // ── LIKE ─────────────────────────────────────────────────────────────────

    private static object? EvalLike(LikeExpr like, Row? row)
    {
        var val     = Eval(like.Operand, row)?.ToString() ?? "";
        var pattern = Eval(like.Pattern, row)?.ToString() ?? "";
        var match  = LikeMatch(val, pattern);
        return like.IsNot ? !match : match;
    }

    private static bool LikeMatch(string input, string pattern)
    {
        // Convert SQL LIKE pattern to simple matching
        // % = any sequence, _ = any single char
        int i = 0, p = 0;
        int starIdx = -1, matchIdx = 0;

        while (i < input.Length)
        {
            if (p < pattern.Length && (pattern[p] == '_' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(input[i])))
            { i++; p++; }
            else if (p < pattern.Length && pattern[p] == '%')
            { starIdx = p++; matchIdx = i; }
            else if (starIdx != -1)
            { p = starIdx + 1; i = ++matchIdx; }
            else return false;
        }

        while (p < pattern.Length && pattern[p] == '%') p++;
        return p == pattern.Length;
    }

    // ── Type helpers ─────────────────────────────────────────────────────────

    public static bool IsNumeric(object? v) =>
        v is int or long or double or float or decimal or short or byte;

    public static double ToDouble(object? v) => Convert.ToDouble(v);
    public static long   ToLong(object? v)   => Convert.ToInt64(v);

    public static bool IsTruthy(object? v) => v switch
    {
        null    => false,
        bool b  => b,
        int i   => i != 0,
        long l  => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true
    };
}

public sealed class ExecutorException(string message) : Exception(message);
