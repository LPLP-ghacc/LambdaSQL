using System.Buffers.Binary;
using System.Text;

namespace LambdaSQL.Core.Storage.PagedStorage;

/// <summary>
/// Serializes/deserializes a Row to/from a compact binary format.
///
/// Format per row:
///   [2B] column count
///   For each column:
///     [1B] type tag  (0=null, 1=int32, 2=int64, 3=float64, 4=text, 5=bool)
///     [payload]
///       null   → nothing
///       int32  → 4 bytes LE
///       int64  → 8 bytes LE
///       float64→ 8 bytes LE
///       text   → [2B length][UTF-8 bytes]
///       bool   → 1 byte (0/1)
/// </summary>
public static class RowSerializer
{
    private const byte TagNull   = 0;
    private const byte TagInt32  = 1;
    private const byte TagInt64  = 2;
    private const byte TagFloat  = 3;
    private const byte TagText   = 4;
    private const byte TagBool   = 5;

    public static byte[] Serialize(Row row, IReadOnlyList<Column> columns)
    {
        // Pre-calculate size
        int size = 2; // column count
        foreach (var col in columns)
        {
            size += 1; // tag
            var val = row.Get(col.Name);
            size += val switch
            {
                null    => 0,
                int     => 4,
                long    => 8,
                double  => 8,
                float   => 8,
                bool    => 1,
                string s => 2 + Encoding.UTF8.GetByteCount(s),
                _ => 0
            };
        }

        var buf = new byte[size];
        var span = buf.AsSpan();
        int pos = 0;

        BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)columns.Count);
        pos += 2;

        foreach (var col in columns)
        {
            var val = row.Get(col.Name);
            switch (val)
            {
                case null:
                    buf[pos++] = TagNull;
                    break;
                case int i:
                    buf[pos++] = TagInt32;
                    BinaryPrimitives.WriteInt32LittleEndian(span[pos..], i);
                    pos += 4;
                    break;
                case long l:
                    buf[pos++] = TagInt64;
                    BinaryPrimitives.WriteInt64LittleEndian(span[pos..], l);
                    pos += 8;
                    break;
                case double d:
                    buf[pos++] = TagFloat;
                    BinaryPrimitives.WriteDoubleLittleEndian(span[pos..], d);
                    pos += 8;
                    break;
                case float f:
                    buf[pos++] = TagFloat;
                    BinaryPrimitives.WriteDoubleLittleEndian(span[pos..], f);
                    pos += 8;
                    break;
                case bool b:
                    buf[pos++] = TagBool;
                    buf[pos++] = b ? (byte)1 : (byte)0;
                    break;
                case string s:
                    buf[pos++] = TagText;
                    var strBytes = Encoding.UTF8.GetBytes(s);
                    BinaryPrimitives.WriteUInt16LittleEndian(span[pos..], (ushort)strBytes.Length);
                    pos += 2;
                    strBytes.CopyTo(span[pos..]);
                    pos += strBytes.Length;
                    break;
            }
        }

        return buf;
    }

    public static Row Deserialize(ReadOnlySpan<byte> data, IReadOnlyList<Column> columns)
    {
        int pos = 0;
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;

        var dict = new Dictionary<string, object?>(colCount, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < colCount && i < columns.Count; i++)
        {
            byte tag = data[pos++];
            object? value = tag switch
            {
                TagNull  => null,
                TagInt32 => (object?)ReadInt32(data, ref pos),
                TagInt64 => ReadInt64(data, ref pos),
                TagFloat => ReadDouble(data, ref pos),
                TagBool  => data[pos++] == 1,
                TagText  => ReadString(data, ref pos),
                _ => null
            };
            dict[columns[i].Name] = value;
        }

        return new Row(dict);
    }

    private static int    ReadInt32(ReadOnlySpan<byte> d, ref int p)  { var v = BinaryPrimitives.ReadInt32LittleEndian(d[p..]); p += 4; return v; }
    private static long   ReadInt64(ReadOnlySpan<byte> d, ref int p)  { var v = BinaryPrimitives.ReadInt64LittleEndian(d[p..]); p += 8; return v; }
    private static double ReadDouble(ReadOnlySpan<byte> d, ref int p) { var v = BinaryPrimitives.ReadDoubleLittleEndian(d[p..]); p += 8; return v; }

    private static string ReadString(ReadOnlySpan<byte> d, ref int p)
    {
        int len = BinaryPrimitives.ReadUInt16LittleEndian(d[p..]); p += 2;
        var s = Encoding.UTF8.GetString(d.Slice(p, len)); p += len;
        return s;
    }
}
