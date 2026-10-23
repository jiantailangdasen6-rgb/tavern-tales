#nullable enable
using System;

namespace Tavern.Core.Play
{
    /// <summary>Source of randomness. Passed in everywhere so tests and saved games stay deterministic.</summary>
    public interface IRandom
    {
        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        int Next(int minInclusive, int maxExclusive);
    }

    /// <summary>Small deterministic generator (xorshift64*). Same seed, same rolls on every platform; the state can be saved.</summary>
    public sealed class SeededRandom : IRandom
    {
        public ulong State { get; private set; }

        public SeededRandom(ulong seed) { State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentException("empty range");
            var s = State;
            s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
            State = s;
            var r = s * 0x2545F4914F6CDD1DUL;
            return minInclusive + (int)(r % (ulong)(maxExclusive - minInclusive));
        }
    }

    /// <summary>Rolls fixed in advance (tests, and replaying a saved turn).</summary>
    public sealed class FixedRandom : IRandom
    {
        readonly int[] _values;
        int _i;
        public FixedRandom(params int[] values) { _values = values; }
        /// <summary>Returns the next listed value clamped into the range (cycling when the list runs out).</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            var v = _values.Length == 0 ? minInclusive : _values[_i++ % _values.Length];
            return Math.Max(minInclusive, Math.Min(maxExclusive - 1, v));
        }
    }

    public static class Dice
    {
        public static int D20(this IRandom rng) => rng.Next(1, 21);
    }
}
