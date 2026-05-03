namespace LambdaSQL.Core.Storage;

public sealed class Column
{
    public string Name { get; }
    public DataType Type { get; }
    public bool NotNull { get; }
    public bool PrimaryKey { get; }
    public object? Default { get; }

    public Column(string name, DataType type, bool notNull = false, bool primaryKey = false, object? @default = null)
    {
        Name = name;
        Type = type;
        NotNull = notNull;
        PrimaryKey = primaryKey;
        Default = @default;
    }

    public override string ToString() =>
        $"{Name} {DataTypeHelper.TypeName(Type)}{(NotNull ? " not null" : "")}{(PrimaryKey ? " primary key" : "")}";
}
