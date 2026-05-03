namespace LambdaSQL.Server;

public sealed class ServerConfig
{
    public string Host     { get; init; } = "0.0.0.0";
    public int    Port     { get; init; } = 5464;
    public string DataDir  { get; init; } = "data";
    public int    MaxConns { get; init; } = 256;

    public static ServerConfig Default => new();

    public static ServerConfig FromArgs(string[] args)
    {
        var cfg = new ServerConfig();
        string host    = cfg.Host;
        int    port    = cfg.Port;
        string dataDir = cfg.DataDir;
        int    maxConn = cfg.MaxConns;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--host":    host    = args[++i]; break;
                case "--port":    port    = int.Parse(args[++i]); break;
                case "--data":    dataDir = args[++i]; break;
                case "--maxconn": maxConn = int.Parse(args[++i]); break;
            }
        }

        return new ServerConfig
        {
            Host     = host,
            Port     = port,
            DataDir  = dataDir,
            MaxConns = maxConn
        };
    }
}
