using LambdaSQL.Core.Engine;
using LambdaSQL.Web;

var builder = WebApplication.CreateBuilder(args);

// Config
var dataDir = args.FirstOrDefault(a => a.StartsWith("--data="))?.Split('=')[1]
           ?? builder.Configuration["DataDir"]
           ?? "data";

// Register engine as singleton
builder.Services.AddSingleton<DatabaseEngine>(_ =>
    Directory.Exists(dataDir) || !string.IsNullOrEmpty(dataDir)
        ? new DatabaseEngine(dataDir)
        : new DatabaseEngine());

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// ── API ───────────────────────────────────────────────────────────────────────

// POST /api/query  { "sql": "select * from users" }
app.MapPost("/api/query", (QueryRequest req, DatabaseEngine db) =>
{
    if (string.IsNullOrWhiteSpace(req.Sql))
        return Results.BadRequest(new { error = "SQL is empty" });

    try
    {
        var results = db.ExecuteAll(req.Sql).ToList();
        var responses = results.Select(ApiResult.From).ToList();
        return Results.Ok(responses);
    }
    catch (Exception ex)
    {
        return Results.Ok(new[] { new ApiResult { Error = ex.Message } });
    }
});

// GET /api/tables
app.MapGet("/api/tables", (DatabaseEngine db) =>
    Results.Ok(db.Tables.OrderBy(t => t)));

// GET /api/tables/{name}
app.MapGet("/api/tables/{name}", (string name, DatabaseEngine db) =>
{
    try
    {
        var table = db.GetTableInfo(name);
        return Results.Ok(table);
    }
    catch (Exception ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// GET /api/tables/{name}/rows?limit=100&offset=0
app.MapGet("/api/tables/{name}/rows", (string name, int limit, int offset, DatabaseEngine db) =>
{
    try
    {
        var sql = $"select * from {name} limit {limit}";
        var results = db.ExecuteAll(sql).ToList();
        return Results.Ok(ApiResult.From(results.First()));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
