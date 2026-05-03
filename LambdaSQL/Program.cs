using LambdaSQL.Core.Executor;
using LambdaSQL.Engine;

var db = new DatabaseEngine();

Console.WriteLine("LambdaSQL — in-memory SQL engine");
Console.WriteLine("Type SQL queries, or 'exit' to quit.");
Console.WriteLine("Tip: statements can be lowercase, end with ; or not.\n");

// ── Demo: run a quick smoke test on startup ──────────────────────────────────
RunDemo(db);

// ── REPL ─────────────────────────────────────────────────────────────────────
Console.WriteLine("\n--- Interactive mode ---\n");

var buffer = new System.Text.StringBuilder();

while (true)
{
    Console.Write(buffer.Length == 0 ? "sql> " : "  -> ");
    var line = Console.ReadLine();

    if (line is null || line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    if (line.Trim().Equals("tables", StringComparison.OrdinalIgnoreCase))
    {
        var tables = db.Tables.ToList();
        Console.WriteLine(tables.Count == 0 ? "(no tables)" : string.Join(", ", tables));
        continue;
    }

    buffer.AppendLine(line);

    // Execute when we see a semicolon or a non-empty single line
    var sql = buffer.ToString().Trim();
    if (!sql.EndsWith(';') && !IsSingleStatement(sql))
        continue;

    buffer.Clear();

    if (string.IsNullOrWhiteSpace(sql)) continue;

    try
    {
        foreach (var result in db.ExecuteAll(sql))
            result.Print();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {ex.Message}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

Console.WriteLine("Bye!");

// ── Demo helper ──────────────────────────────────────────────────────────────

static void RunDemo(DatabaseEngine db)
{
    Console.WriteLine("=== Demo ===\n");

    var queries = new[]
    {
        """
        create table users (
            id   int  primary key,
            name text not null,
            age  int,
            city text
        )
        """,

        "insert into users (id, name, age, city) values (1, 'Alice', 30, 'Moscow')",
        "insert into users (id, name, age, city) values (2, 'Bob',   25, 'London')",
        "insert into users (id, name, age, city) values (3, 'Carol', 35, 'Moscow')",
        "insert into users (id, name, age, city) values (4, 'Dave',  28, 'London')",
        "insert into users (id, name, age, city) values (5, 'Eve',   22, 'Paris')",

        "select * from users",

        "select name, age from users where age > 25 order by age desc",

        "select city, count(*) as cnt, avg(age) as avg_age from users group by city order by cnt desc",

        "update users set age = 31 where name = 'Alice'",

        "select * from users where city = 'Moscow'",

        "delete from users where age < 25",

        "select count(*) as total from users",
    };

    foreach (var q in queries)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"> {q.Trim()}");
        Console.ResetColor();

        try
        {
            db.Execute(q).Print();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}

static bool IsSingleStatement(string sql)
{
    // Heuristic: if it starts with a known keyword and has no semicolons, treat as complete
    var first = sql.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
    return first is "select" or "insert" or "update" or "delete" or "create" or "drop";
}
