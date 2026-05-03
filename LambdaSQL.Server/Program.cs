using LambdaSQL.Server;

var config = ServerConfig.FromArgs(args);

await using var server = new TcpServer(config);

// Graceful shutdown on Ctrl+C / SIGTERM
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    server.Stop();
    Console.WriteLine("\n[LambdaSQL] Shutting down...");
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => server.Stop();

await server.RunAsync();
