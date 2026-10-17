#nullable enable
using System;

namespace Tavern.Core.Rules
{
    /// <summary>The four abilities (力・技・知・心). Modifiers run +0..+4.</summary>
    public enum Stat { Might, Skill, Wits, Spirit }

    /// <summary>The four check difficulties (DC 10 / 13 / 16 / 19).</summary>
    public enum Difficulty { Easy = 10, Normal = 13, Hard = 16, Heroic = 19 }

    public static class RuleNames
    {
        public static string Label(this Stat s) => s switch
        {
            Stat.Might => "力", Stat.Skill => "技", Stat.Wits => "知", Stat.Spirit => "心",
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        };

        public static string Label(this Difficulty d) => d switch
        {
            Difficulty.Easy => "易しい", Difficulty.Normal => "普通", Difficulty.Hard => "難しい", Difficulty.Heroic => "至難",
            _ => throw new ArgumentOutOfRangeException(nameof(d)),
        };

        /// <summary>Reads a stat from the game-master AI (Japanese label or English name).</summary>
        public static bool TryParseStat(string? text, out Stat stat)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "力": case "might": case "strength": stat = Stat.Might; return true;
                case "技": case "skill": case "dexterity": stat = Stat.Skill; return true;
                case "知": case "wits": case "intelligence": stat = Stat.Wits; return true;
                case "心": case "spirit": case "wisdom": case "charisma": stat = Stat.Spirit; return true;
                default: stat = Stat.Might; return false;
            }
        }

        /// <summary>Reads a difficulty from the AI (label, English name or the DC number).</summary>
        public static bool TryParseDifficulty(string? text, out Difficulty difficulty)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "易しい": case "easy": case "10": difficulty = Difficulty.Easy; return true;
                case "普通": case "normal": case "13": difficulty = Difficulty.Normal; return true;
                case "難しい": case "hard": case "16": difficulty = Difficulty.Hard; return true;
                case "至難": case "heroic": case "very hard": case "19": difficulty = Difficulty.Heroic; return true;
                default: difficulty = Difficulty.Normal; return false;
            }
        }
    }
}
