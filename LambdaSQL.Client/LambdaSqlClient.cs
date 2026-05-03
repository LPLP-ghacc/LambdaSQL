using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace LambdaSQL.Client;

/// <summary>
/// TCP client for LambdaSQL server.
/// Usage:
///   var client = new LambdaSqlClient("localhost", 5464);
///   await client.ConnectAsync();
///   var result = await client.QueryAsync("select * from users");
///   result.Print();
/// </summary>
public sealed class LambdaSqlClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int    _port;
    private TcpClient?      _tcp;
    private NetworkStream?  _stream;

    public bool IsConnected => _tcp?.Connected ?? false;

    public LambdaSqlClient(string host = "localhost", int port = 5464)
    {
        _host = host;
        _port = port;
    }

    // ── Connect ───────────────────────────────────────────────────────────────

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(_host, _port, ct);
        _stream = _tcp.GetStream();
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public async Task<ClientResult> QueryAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();
        await SendFrameAsync(0x01, Encoding.UTF8.GetBytes(sql), ct);
        return await ReadResultAsync(ct);
    }

    // ── Ping ──────────────────────────────────────────────────────────────────

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        await SendFrameAsync(0x02, Array.Empty<byte>(), ct);
        var (type, _) = await ReadFrameAsync(ct);
        return type == 0x12; // Pong
    }

    // ── Frame I/O ─────────────────────────────────────────────────────────────

    private async Task SendFrameAsync(byte type, byte[] payload, CancellationToken ct)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length + 1);
        header[4] = type;
        await _stream!.WriteAsync(header, ct);
        if (payload.Length > 0)
            await _stream.WriteAsync(payload, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<(byte type, byte[] payload)> ReadFrameAsync(CancellationToken ct)
    {
        var header = new byte[5];
        await ReadExactAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        byte type  = header[4];
        int payLen = length - 1;
        var payload = new byte[payLen];
        if (payLen > 0) await ReadExactAsync(payload, ct);
        return (type, payload);
    }

    private async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(read, buf.Length - read), ct);
            if (n == 0) throw new EndOfStreamException("Server disconnected");
            read += n;
        }
    }

    // ── Result parsing ────────────────────────────────────────────────────────

    private async Task<ClientResult> ReadResultAsync(CancellationToken ct)
    {
        var (type, payload) = await ReadFrameAsync(ct);

        if (type == 0x11) // Error
            return ClientResult.FromError(Encoding.UTF8.GetString(payload));

        if (type != 0x10) // Ok
            return ClientResult.FromError($"Unexpected frame type: {type}");

        return ParseResultSet(payload);
    }

    private static ClientResult ParseResultSet(byte[] payload)
    {
        using var ms = new MemoryStream(payload);
        using var r  = new BinaryReader(ms, Encoding.UTF8);

        ushort colCount = r.ReadUInt16();

        // DML / DDL result
        if (colCount == 0)
        {
            int rowsAffected = r.ReadInt32();
            ushort msgLen    = r.ReadUInt16();
            var msg          = Encoding.UTF8.GetString(r.ReadBytes(msgLen));
            return ClientResult.FromMessage(rowsAffected, msg);
        }

        // SELECT result
        var columns = new string[colCount];
        for (int i = 0; i < colCount; i++)
        {
            ushort len = r.ReadUInt16();
            columns[i] = Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        int rowCount = r.ReadInt32();
        var rows = new object?[rowCount][];

        for (int i = 0; i < rowCount; i++)
        {
            rows[i] = new object?[colCount];
            for (int j = 0; j < colCount; j++)
                rows[i][j] = ReadValue(r);
        }

        return ClientResult.FromResultSet(columns, rows);
    }

    private static object? ReadValue(BinaryReader r)
    {
        byte tag = r.ReadByte();
        return tag switch
        {
            0 => null,
            1 => r.ReadInt64(),
            2 => r.ReadDouble(),
            3 => Encoding.UTF8.GetString(r.ReadBytes(r.ReadUInt16())),
            4 => r.ReadBoolean(),
            _ => null
        };
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected. Call ConnectAsync() first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream != null) await _stream.DisposeAsync();
        _tcp?.Dispose();
    }
}
