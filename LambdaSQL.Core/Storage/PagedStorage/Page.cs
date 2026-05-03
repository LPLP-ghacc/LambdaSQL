using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LambdaSQL.Core.Storage.PagedStorage;

/// <summary>
/// Fixed-size 8KB page. Layout:
///   [0..3]   magic (0xLSQL)
///   [4..7]   page id
///   [8..9]   slot count
///   [10..11] free space offset (grows from end)
///   [12..N]  slot directory: each slot = [offset:2][length:2]
///   [N..end] row data (packed from end toward start)
/// </summary>
public sealed class Page
{
    public const int PageSize    = 8192;
    public const int HeaderSize  = 12;
    public const int SlotSize    = 4;   // offset(2) + length(2)
    public const uint Magic      = 0x4C53514C; // "LSQL"

    private readonly byte[] _data;

    public int PageId { get; }

    public Page(int pageId)
    {
        PageId = pageId;
        _data  = new byte[PageSize];
        WriteUInt32(0, Magic);
        WriteInt32(4, pageId);
        WriteUInt16(8, 0);                    // slot count
        WriteUInt16(10, (ushort)PageSize);    // free space starts at end
    }

    public Page(int pageId, byte[] data)
    {
        PageId = pageId;
        _data  = data;
    }

    // ── Accessors ────────────────────────────────────────────────────────────

    public ushort SlotCount      => ReadUInt16(8);
    public ushort FreeOffset     => ReadUInt16(10);
    public int    FreeSpace      => FreeOffset - HeaderSize - SlotCount * SlotSize;

    // ── Write a row, returns slot index or -1 if no space ────────────────────

    public int WriteRow(ReadOnlySpan<byte> rowData)
    {
        int needed = rowData.Length + SlotSize;
        if (FreeSpace < needed) return -1;

        // Place data at end
        ushort newOffset = (ushort)(FreeOffset - rowData.Length);
        rowData.CopyTo(_data.AsSpan(newOffset, rowData.Length));

        // Add slot
        int slotIdx = SlotCount;
        int slotPos = HeaderSize + slotIdx * SlotSize;
        WriteUInt16(slotPos,     newOffset);
        WriteUInt16(slotPos + 2, (ushort)rowData.Length);

        WriteUInt16(8,  (ushort)(slotIdx + 1));
        WriteUInt16(10, newOffset);

        return slotIdx;
    }

    // ── Read a row by slot index ──────────────────────────────────────────────

    public ReadOnlySpan<byte> ReadRow(int slotIdx)
    {
        if (slotIdx >= SlotCount) return ReadOnlySpan<byte>.Empty;
        int slotPos = HeaderSize + slotIdx * SlotSize;
        int offset  = ReadUInt16(slotPos);
        int length  = ReadUInt16(slotPos + 2);
        if (length == 0) return ReadOnlySpan<byte>.Empty; // deleted
        return _data.AsSpan(offset, length);
    }

    // ── Mark slot as deleted (zero length) ───────────────────────────────────

    public void DeleteRow(int slotIdx)
    {
        if (slotIdx >= SlotCount) return;
        int slotPos = HeaderSize + slotIdx * SlotSize;
        WriteUInt16(slotPos + 2, 0);
    }

    // ── Raw page bytes ────────────────────────────────────────────────────────

    public ReadOnlySpan<byte> AsSpan() => _data;
    public byte[] ToArray() => _data;

    // ── Helpers ───────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort ReadUInt16(int offset) =>
        MemoryMarshal.Read<ushort>(_data.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadUInt32(int offset) =>
        MemoryMarshal.Read<uint>(_data.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadInt32(int offset) =>
        MemoryMarshal.Read<int>(_data.AsSpan(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUInt16(int offset, ushort value) =>
        MemoryMarshal.Write(_data.AsSpan(offset), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUInt32(int offset, uint value) =>
        MemoryMarshal.Write(_data.AsSpan(offset), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteInt32(int offset, int value) =>
        MemoryMarshal.Write(_data.AsSpan(offset), value);
}
