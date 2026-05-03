using System.Buffers.Binary;
using System.Text;

namespace LambdaSQL.Server.Protocol;

public static class FrameReader
{
    /// <summary>
    /// Reads one frame from the stream.
    /// Returns (type, payload) or throws on disconnect.
    /// </summary>
    public static async Task<(byte type, byte[] payload)> ReadFrameAsync(
        Stream stream, CancellationToken ct)
    {
        // Read 5-byte header: [4B length][1B type]
        var header = new byte[5];
        await ReadExactAsync(stream, header, ct);

        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        byte type  = header[4];

        int payloadLen = length - 1;
        var payload = new byte[payloadLen];
        if (payloadLen > 0)
            await ReadExactAsync(stream, payload, ct);

        return (type, payload);
    }

    public static string ReadQueryPayload(byte[] payload) =>
        Encoding.UTF8.GetString(payload);

    private static async Task ReadExactAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, buf.Length - read), ct);
            if (n == 0) throw new EndOfStreamException("Client disconnected");
            read += n;
        }
    }
}
