using LambdaSQL.Client;
using LambdaSQL.Core.Engine;

// ── Parse args ───────────────────────────────────────────────────────────────
// Modes:
//   lambdasql                          → embedded, in-memory
//   lambdasql --data ./mydb            → embedded, persistent
//   lambdasql --host localhost --port 5464  → remote server

bool   remote   = false;
string host     = "localhost";
int    port     = 5464;
string? dataDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":   host = args[++i]; remote = true; break;
        case "--port":   port = int.Parse(args[++i]); remote = true; break;
        case "--data":   dataDir = args[++i]; break;
        case "--remote": remote = true; break;
    }
}

Console.WriteLine("╔══════════════════════════════╗");
Console.WriteLine("║       LambdaSQL CLI          ║");
Console.WriteLine("╚══════════════════════════════╝");

if (remote)
{
    Console.WriteLine($"Mode: remote  →  {host}:{port}");
    await RunRemoteAsync(host, port);
}
else
{
    var mode = dataDir != null ? $"persistent ({dataDir})" : "in-memory";
    Console.WriteLine($"Mode: embedded  [{mode}]");
    RunEmbedded(dataDir);
}

// ── Embedded REPL ─────────────────────────────────────────────────────────────

static void RunEmbedded(string? dataDir)
{
    using var engine = dataDir != null
        ? new DatabaseEngine(dataDir)
        : new DatabaseEngine();

    Console.WriteLine("Type SQL or 'help' / 'exit'\n");

    var buf = new System.Text.StringBuilder();

    while (true)
    {
        Console.Write(buf.Length == 0 ? "sql> " : "  -> ");
        var line = Console.ReadLine();
        if (line is null) break;

        var trimmed = line.Trim();

        if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        { PrintHelp(); continue; }

        if (trimmed.Equals("tables", StringComparison.OrdinalIgnoreCase))
        {
            var tables = engine.Tables.ToList();
            Console.WriteLine(tables.Count == 0 ? "(no tables)" : string.Join(", ", tables));
            Console.WriteLine();
            continue;
        }

        buf.AppendLine(line);
        var sql = buf.ToString().Trim();

        if (!sql.EndsWith(';') && !IsCompleteStatement(sql)) continue;
        buf.Clear();

        if (string.IsNullOrWhiteSpace(sql)) continue;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var result in engine.ExecuteAll(sql))
                result.Print();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
        sw.Stop();
        Console.WriteLine($"  ({sw.ElapsedMilliseconds}ms)\n");
    }

    Console.WriteLine("Bye!");
}

// ── Remote REPL ───────────────────────────────────────────────────────────────

static async Task RunRemoteAsync(string host, int port)
{
    await using var client = new LambdaSqlClient(host, port);

    try
    {
        await client.ConnectAsync();
        Console.WriteLine($"Connected to {host}:{port}\n");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Cannot connect: {ex.Message}");
        Console.ResetColor();
        return;
    }

    var buf = new System.Text.StringBuilder();

    while (true)
    {
        Console.Write(buf.Length == 0 ? "sql> " : "  -> ");
        var line = Console.ReadLine();
        if (line is null) break;

        var trimmed = line.Trim();

        if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        { PrintHelp(); continue; }

        if (trimmed.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            var ok = await client.PingAsync();
            Console.WriteLine(ok ? "pong" : "no response");
            Console.WriteLine();
            continue;
        }

        buf.AppendLine(line);
        var sql = buf.ToString().Trim();

        if (!sql.EndsWith(';') && !IsCompleteStatement(sql)) continue;
        buf.Clear();

        if (string.IsNullOrWhiteSpace(sql)) continue;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await client.QueryAsync(sql);
            result.Print();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }
        sw.Stop();
        Console.WriteLine($"  ({sw.ElapsedMilliseconds}ms)\n");
    }

    Console.WriteLine("Bye!");
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static bool IsCompleteStatement(string sql)
{
    var first = sql.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .FirstOrDefault()?.ToLowerInvariant();
    return first is "select" or "insert" or "update" or "delete" or "create" or "drop";
}

static void PrintHelp()
{
    Console.WriteLine("""
    Commands:
      tables          — list all tables
      exit / quit     — exit
      ping            — ping server (remote mode only)

    SQL:
      create table t (id int primary key, name text not null)
      insert into t (id, name) values (1, 'Alice')
      select * from t where id > 0 order by name limit 10
      update t set name = 'Bob' where id = 1
      delete from t where id = 1
      drop table t
    """);
}
