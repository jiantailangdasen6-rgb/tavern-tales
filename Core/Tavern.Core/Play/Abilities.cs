#nullable enable
using System;

namespace Tavern.Core.Play
{
    /// <summary>The five abilities (Docs/09_Spec.md 3). Modifiers run 0..+5.</summary>
    public enum Ability { Str, Dex, Agi, Int, Mnd }

    /// <summary>Check difficulties and their target numbers.</summary>
    public enum Tier { Easy = 10, Normal = 13, Hard = 16, Extreme = 19 }

    public static class AbilityNames
    {
        public const int MaxModifier = 5;

        public static string Code(this Ability a) => a switch
        {
            Ability.Str => "STR", Ability.Dex => "DEX", Ability.Agi => "AGI", Ability.Int => "INT", Ability.Mnd => "MND",
            _ => throw new ArgumentOutOfRangeException(nameof(a)),
        };

        /// <summary>The plain word the sheet shows under the code ("腕っぷし").</summary>
        public static string Meaning(this Ability a) => a switch
        {
            Ability.Str => "腕っぷし", Ability.Dex => "手先の器用さ", Ability.Agi => "身のこなし", Ability.Int => "知識・魔法", Ability.Mnd => "意志・話術",
            _ => throw new ArgumentOutOfRangeException(nameof(a)),
        };

        public static string Label(this Tier t) => t switch
        {
            Tier.Easy => "易しい", Tier.Normal => "普通", Tier.Hard => "難しい", Tier.Extreme => "至難",
            _ => throw new ArgumentOutOfRangeException(nameof(t)),
        };

        /// <summary>Moves a tier by steps (+1 = one step harder), staying within easy..extreme.</summary>
        public static Tier Shift(this Tier t, int steps)
        {
            int i = Math.Max(0, Math.Min(3, Index(t) + steps));
            return i switch { 0 => Tier.Easy, 1 => Tier.Normal, 2 => Tier.Hard, _ => Tier.Extreme };
        }

        static int Index(Tier t) => t switch { Tier.Easy => 0, Tier.Normal => 1, Tier.Hard => 2, _ => 3 };

        /// <summary>Reads an ability from the AI or a content file ("STR", "腕っぷし", "力").</summary>
        public static bool TryParse(string? text, out Ability ability)
        {
            switch ((text ?? "").Trim().ToUpperInvariant())
            {
                case "STR": case "腕っぷし": case "力": ability = Ability.Str; return true;
                case "DEX": case "手先の器用さ": case "器用さ": case "技": ability = Ability.Dex; return true;
                case "AGI": case "身のこなし": case "素早さ": ability = Ability.Agi; return true;
                case "INT": case "知識・魔法": case "知識": case "知": ability = Ability.Int; return true;
                case "MND": case "意志・話術": case "意志": case "心": ability = Ability.Mnd; return true;
                default: ability = Ability.Str; return false;
            }
        }

        public static bool TryParseTier(string? text, out Tier tier)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "易しい": case "easy": case "10": tier = Tier.Easy; return true;
                case "普通": case "normal": case "13": tier = Tier.Normal; return true;
                case "難しい": case "hard": case "16": tier = Tier.Hard; return true;
                case "至難": case "extreme": case "19": tier = Tier.Extreme; return true;
                default: tier = Tier.Normal; return false;
            }
        }
    }
}
