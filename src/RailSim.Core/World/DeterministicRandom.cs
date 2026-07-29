using System.Text;

namespace RailSim.Core.World;

public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public int NextInt32(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public long NextInt64(long inclusiveMinimum, long exclusiveMaximum)
    {
        if (exclusiveMaximum <= inclusiveMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        var range = checked((ulong)(exclusiveMaximum - inclusiveMinimum));
        return checked(inclusiveMinimum + (long)(NextUInt64() % range));
    }
}

public static class SeedDeriver
{
    public static ulong Derive(ulong rootSeed, string streamName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        var hash = 14695981039346656037UL ^ rootSeed;
        foreach (var value in Encoding.UTF8.GetBytes(streamName))
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
