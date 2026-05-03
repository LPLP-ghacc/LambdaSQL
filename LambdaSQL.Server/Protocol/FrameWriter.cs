using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using LambdaSQL.Core.Executor;

namespace LambdaSQL.Server.Protocol;

public static class FrameWriter
{
    // ── Write a QueryResult as Ok frame ──────────────────────────────────────

    public static void WriteOk(Stream stream, QueryResult result)
    {
        // Estimate buffer size
        var buf = new ArrayBufferWriter<byte>(4096);
        var writer = new BinaryWriter(new MemoryStream(), Encoding.UTF8, leaveOpen: false);

        // Build payload
        using var ms = new MemoryStream();
        WriteResultSet(ms, result);
        var payload = ms.ToArray();

        WriteFrame(stream, FrameType.Ok, payload);
    }

    public static void WriteError(Stream stream, string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        WriteFrame(stream, FrameType.Error, payload);
    }

    public static void WritePong(Stream stream)
    {
        WriteFrame(stream, FrameType.Pong, Array.Empty<byte>());
    }

    // ── Frame: [4B length][1B type][payload] ─────────────────────────────────

    private static void WriteFrame(Stream stream, byte type, byte[] payload)
    {
        Span<byte> header = stackalloc byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length + 1);
        header[4] = type;
        stream.Write(header);
        if (payload.Length > 0)
            stream.Write(payload);
        stream.Flush();
    }

    // ── ResultSet serialization ───────────────────────────────────────────────

    private static void WriteResultSet(Stream s, QueryResult result)
    {
        using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);

        // Message-only result (DDL / DML)
        if (!result.IsResultSet)
        {
            w.Write((ushort)0); // 0 columns
            w.Write(result.RowsAffected);
            var msg = result.Message ?? $"{result.RowsAffected} row(s) affected";
            var msgBytes = Encoding.UTF8.GetBytes(msg);
            w.Write((ushort)msgBytes.Length);
            w.Write(msgBytes);
            return;
        }

        // Column headers
        w.Write((ushort)result.Columns.Length);
        foreach (var col in result.Columns)
        {
            var colBytes = Encoding.UTF8.GetBytes(col);
            w.Write((ushort)colBytes.Length);
            w.Write(colBytes);
        }

        // Rows
        w.Write(result.Rows.Count);
        foreach (var row in result.Rows)
        {
            foreach (var val in row)
                WriteValue(w, val);
        }
    }

    private static void WriteValue(BinaryWriter w, object? val)
    {
        switch (val)
        {
            case null:
                w.Write((byte)0);
                break;
            case int i:
                w.Write((byte)1);
                w.Write((long)i);
                break;
            case long l:
                w.Write((byte)1);
                w.Write(l);
                break;
            case double d:
                w.Write((byte)2);
                w.Write(d);
                break;
            case float f:
                w.Write((byte)2);
                w.Write((double)f);
                break;
            case bool b:
                w.Write((byte)4);
                w.Write(b);
                break;
            default:
                var s = val.ToString() ?? "";
                var bytes = Encoding.UTF8.GetBytes(s);
                w.Write((byte)3);
                w.Write((ushort)bytes.Length);
                w.Write(bytes);
                break;
        }
    }
}
