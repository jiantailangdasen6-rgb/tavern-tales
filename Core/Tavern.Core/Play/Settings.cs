#nullable enable

namespace Tavern.Core.Play
{
    /// <summary>The host picks this at the start of the evening and may change it between acts (09_Spec 2) 〔仮〕.</summary>
    public enum Difficulty { Gentle, Standard, Harsh }

    public static class DifficultyRules
    {
        public static string Label(this Difficulty d) => d switch { Difficulty.Gentle => "易しい", Difficulty.Harsh => "厳しい", _ => "普通" };

        /// <summary>Enemy HP: 0.8x / 1x / 1.25x.</summary>
        public static int EnemyHp(this Difficulty d, int hp) => d switch
        {
            Difficulty.Gentle => System.Math.Max(1, (int)System.Math.Round(hp * 0.8, System.MidpointRounding.AwayFromZero)),
            Difficulty.Harsh => (int)System.Math.Round(hp * 1.25, System.MidpointRounding.AwayFromZero),
            _ => hp,
        };

        /// <summary>Added to enemy attacks (the dodge target): -1 / 0 / +1.</summary>
        public static int EnemyAttack(this Difficulty d) => d switch { Difficulty.Gentle => -1, Difficulty.Harsh => 1, _ => 0 };

        public static int FateCoins(this Difficulty d) => d == Difficulty.Gentle ? 2 : 1;

        /// <summary>Stalled turns before hint step 1, then more turns to steps 2 and 3 (0 = step 3 never comes).</summary>
        public static (int first, int second, int third) HintTurns(this Difficulty d) => d switch
        {
            Difficulty.Gentle => (3, 2, 2),
            Difficulty.Harsh => (6, 4, 0),
            _ => (4, 3, 3),
        };
    }
}
