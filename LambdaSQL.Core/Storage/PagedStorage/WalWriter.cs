using System.Buffers.Binary;

namespace LambdaSQL.Core.Storage.PagedStorage;

/// <summary>
/// Write-Ahead Log. Each entry:
///   [4B] sequence number
///   [1B] operation type (1=insert, 2=update, 3=delete, 4=checkpoint)
///   [2B] table name length
///   [N]  table name UTF-8
///   [4B] payload length
///   [N]  payload bytes
///
/// On startup: replay all entries after last checkpoint.
/// </summary>
public sealed class WalWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _lock = new();
    private uint _seq;

    public const byte OpInsert     = 1;
    public const byte OpUpdate     = 2;
    public const byte OpDelete     = 3;
    public const byte OpCheckpoint = 4;
    public const byte OpDdl        = 5;

    public WalWriter(string path)
    {
        _stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Read, bufferSize: 65536, FileOptions.SequentialScan);
        _stream.Seek(0, SeekOrigin.End);
    }

    public void Append(byte op, string table, ReadOnlySpan<byte> payload)
    {
        var tableBytes = System.Text.Encoding.UTF8.GetBytes(table);
        int total = 4 + 1 + 2 + tableBytes.Length + 4 + payload.Length;
        var buf = new byte[total];
        var span = buf.AsSpan();
        int pos = 0;

        lock (_lock)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[pos..], ++_seq); pos += 4;
            buf[pos++] = op;
            BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)tableBytes.Length); pos += 2;
            tableBytes.CopyTo(span[pos..]); pos += tableBytes.Length;
            BinaryPrimitives.WriteInt32LittleEndian(span[pos..], payload.Length); pos += 4;
            payload.CopyTo(span[pos..]);

            _stream.Write(buf);
            _stream.Flush(flushToDisk: false); // OS buffer flush, not fsync (perf)
        }
    }

    public void Checkpoint()
    {
        Append(OpCheckpoint, "", ReadOnlySpan<byte>.Empty);
        lock (_lock) { _stream.Flush(flushToDisk: true); }
    }

    public IEnumerable<WalEntry> ReadAll()
    {
        _stream.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);

        while (_stream.Position < _stream.Length)
        {
            uint seq  = reader.ReadUInt32();
            byte op   = reader.ReadByte();
            int  tLen = reader.ReadUInt16();
            var  tbl  = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(tLen));
            int  pLen = reader.ReadInt32();
            var  pay  = reader.ReadBytes(pLen);
            yield return new WalEntry(seq, op, tbl, pay);
        }
    }

    public void Dispose() => _stream.Dispose();
}

public readonly record struct WalEntry(uint Seq, byte Op, string Table, byte[] Payload);
