namespace LambdaSQL.Server.Protocol;

/// <summary>
/// Binary protocol frame types.
///
/// Request frame:  [4B length][1B type][payload]
/// Response frame: [4B length][1B type][payload]
/// </summary>
public static class FrameType
{
    // Client → Server
    public const byte Query   = 0x01;  // payload = UTF-8 SQL string
    public const byte Ping    = 0x02;  // no payload

    // Server → Client
    public const byte Ok      = 0x10;  // payload = ResultSet (see below)
    public const byte Error   = 0x11;  // payload = UTF-8 error message
    public const byte Pong    = 0x12;  // no payload

    // ResultSet wire format:
    //   [2B] column count
    //   For each column: [2B name length][name UTF-8]
    //   [4B] row count
    //   For each row:
    //     For each column:
    //       [1B] value type (0=null,1=int64,2=float64,3=text,4=bool)
    //       [payload per type]
}
