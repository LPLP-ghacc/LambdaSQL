namespace LambdaSQL.Core.Storage;

public enum DataType
{
    Int,
    BigInt,
    Float,
    Text,
    Bool,
}

public static class DataTypeHelper
{
    public static DataType Parse(string name) => name.ToLowerInvariant() switch
    {
        "int"    => DataType.Int,
        "bigint" => DataType.BigInt,
        "float"  => DataType.Float,
        "text"   => DataType.Text,
        "bool"   => DataType.Bool,
        _ => throw new ArgumentException($"Unknown data type: {name}")
    };

    public static object? Coerce(object? value, DataType type)
    {
        if (value is null) return null;

        return type switch
        {
            DataType.Int    => Convert.ToInt32(value),
            DataType.BigInt => Convert.ToInt64(value),
            DataType.Float  => Convert.ToDouble(value),
            DataType.Text   => Convert.ToString(value),
            DataType.Bool   => Convert.ToBoolean(value),
            _ => value
        };
    }

    public static string TypeName(DataType t) => t switch
    {
        DataType.Int    => "int",
        DataType.BigInt => "bigint",
        DataType.Float  => "float",
        DataType.Text   => "text",
        DataType.Bool   => "bool",
        _ => "unknown"
    };
}
