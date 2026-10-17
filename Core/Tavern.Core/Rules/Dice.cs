#nullable enable
using System;

namespace Tavern.Core.Rules
{
    /// <summary>Source of randomness. Passed in everywhere so tests and saved games stay deterministic.</summary>
    public interface IRandom
    {
        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        int Next(int minInclusive, int maxExclusive);
    }

    /// <summary>Small deterministic generator (xorshift64*). Same seed, same rolls on every platform.</summary>
    public sealed class SeededRandom : IRandom
    {
        ulong _state;

        public SeededRandom(ulong seed) { _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentException("empty range");
            _state ^= _state >> 12; _state ^= _state << 25; _state ^= _state >> 27;
            var r = _state * 0x2545F4914F6CDD1DUL;
            return minInclusive + (int)(r % (ulong)(maxExclusive - minInclusive));
        }
    }

    public static class Dice
    {
        public static int D20(IRandom rng) => rng.Next(1, 21);
    }
}
