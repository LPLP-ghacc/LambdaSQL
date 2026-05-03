using System.Net;
using System.Net.Sockets;
using LambdaSQL.Core.Engine;
using LambdaSQL.Server.Protocol;

namespace LambdaSQL.Server;

/// <summary>
/// Async TCP server. Each connection handled in its own Task.
/// Uses a semaphore to cap concurrent connections.
/// </summary>
public sealed class TcpServer : IAsyncDisposable
{
    private readonly ServerConfig _config;
    private readonly DatabaseEngine _engine;
    private readonly TcpListener _listener;
    private readonly SemaphoreSlim _connLimit;
    private readonly CancellationTokenSource _cts = new();

    private long _totalConnections;
    private long _totalQueries;

    public TcpServer(ServerConfig config)
    {
        _config     = config;
        _engine     = new DatabaseEngine(config.DataDir);
        _listener   = new TcpListener(IPAddress.Parse(config.Host), config.Port);
        _connLimit  = new SemaphoreSlim(config.MaxConns, config.MaxConns);
    }

    public async Task RunAsync()
    {
        _listener.Start();
        Console.WriteLine($"[LambdaSQL] Listening on {_config.Host}:{_config.Port}");
        Console.WriteLine($"[LambdaSQL] Data dir: {Path.GetFullPath(_config.DataDir)}");
        Console.WriteLine($"[LambdaSQL] Max connections: {_config.MaxConns}");

        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[Accept error] {ex.Message}"); continue; }

            // Non-blocking: fire and forget per connection
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        await _connLimit.WaitAsync(ct);
        long connId = Interlocked.Increment(ref _totalConnections);

        var remote = client.Client.RemoteEndPoint;
        Console.WriteLine($"[+] Connection #{connId} from {remote}");

        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();

            while (!ct.IsCancellationRequested)
            {
                (byte type, byte[] payload) frame;
                try { frame = await FrameReader.ReadFrameAsync(stream, ct); }
                catch (EndOfStreamException) { break; }
                catch (IOException) { break; }

                switch (frame.type)
                {
                    case FrameType.Ping:
                        FrameWriter.WritePong(stream);
                        break;

                    case FrameType.Query:
                        var sql = FrameReader.ReadQueryPayload(frame.payload);
                        Interlocked.Increment(ref _totalQueries);
                        await HandleQueryAsync(stream, sql);
                        break;

                    default:
                        FrameWriter.WriteError(stream, $"Unknown frame type: {frame.type}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Connection #{connId} error] {ex.Message}");
        }
        finally
        {
            client.Dispose();
            _connLimit.Release();
            Console.WriteLine($"[-] Connection #{connId} closed");
        }
    }

    private Task HandleQueryAsync(Stream stream, string sql)
    {
        try
        {
            // ExecuteAll handles multiple statements
            var results = _engine.ExecuteAll(sql).ToList();
            // Send last result (or first if single)
            var result = results.LastOrDefault();
            if (result != null)
                FrameWriter.WriteOk(stream, result);
            else
                FrameWriter.WriteError(stream, "No result");
        }
        catch (Exception ex)
        {
            FrameWriter.WriteError(stream, ex.Message);
        }
        return Task.CompletedTask;
    }

    public void Stop() => _cts.Cancel();

    public (long connections, long queries) Stats =>
        (Interlocked.Read(ref _totalConnections), Interlocked.Read(ref _totalQueries));

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _engine.Dispose();
        _connLimit.Dispose();
        _cts.Dispose();
    }
}
