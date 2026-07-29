namespace RailSim.Core.World;

public readonly record struct StableId : IComparable<StableId>
{
    public StableId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public int CompareTo(StableId other)
    {
        return string.Compare(Value, other.Value, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return Value;
    }
}
