namespace sk.training;

public enum SeedSplit { Training, Validation, Confirmation }

/// <summary>Versioned SplitMix64 PRNG; independent of framework Random changes.</summary>
public sealed class StableRandom(ulong seed)
{
    private ulong state = seed;
    public static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }
    public ulong NextUInt64() => Mix(state = unchecked(state + 0x9e3779b97f4a7c15UL));
    public int Next(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        ulong size = (ulong)bound, threshold = unchecked(0UL - size) % size;
        ulong value;
        do value = NextUInt64(); while (value < threshold);
        return (int)(value % size);
    }
}

public static class SeedSchedule
{
    public const string Version = "splitmix64-partition-v1";
    public static ulong Deal(ulong master, SeedSplit split, ulong index)
    {
        if (!Enum.IsDefined(split)) throw new ArgumentOutOfRangeException(nameof(split));
        if (index > ulong.MaxValue >> 2) throw new ArgumentOutOfRangeException(nameof(index));
        // Low bits partition the input domain. Mix is bijective: no overlap for one master.
        return StableRandom.Mix(master ^ ((index << 2) | (ulong)split));
    }
    public static ulong Sampling(ulong master, ulong index, int dealer)
    {
        if (index > ulong.MaxValue >> 2) throw new ArgumentOutOfRangeException(nameof(index));
        if (dealer is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(dealer));
        return StableRandom.Mix(master ^ ((index << 2) | (uint)dealer) ^ 0xa3f0c1b2785964deUL);
    }
}
