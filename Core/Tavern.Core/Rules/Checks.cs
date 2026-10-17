#nullable enable

namespace Tavern.Core.Rules
{
    public enum Outcome { Fumble, Failure, Success, Critical }

    /// <summary>A check the AI asked for: who, with which ability, how hard, and why.</summary>
    public sealed class CheckRequest
    {
        public string CharacterName { get; }
        public Stat Stat { get; }
        public Difficulty Difficulty { get; }
        public string Reason { get; }
        /// <summary>Number of allies who spend their action to help (+2 each).</summary>
        public int Helpers { get; }

        public CheckRequest(string characterName, Stat stat, Difficulty difficulty, string reason, int helpers = 0)
        {
            CharacterName = characterName; Stat = stat; Difficulty = difficulty; Reason = reason; Helpers = helpers;
        }
    }

    public sealed class CheckResult
    {
        public CheckRequest Request { get; }
        public int Natural { get; }
        public int Modifier { get; }
        public int HelpBonus { get; }
        public int Total => Natural + Modifier + HelpBonus;
        public int Dc { get; }
        public Outcome Outcome { get; }
        public bool Rerolled { get; }

        public CheckResult(CheckRequest request, int natural, int modifier, int helpBonus, bool rerolled, int? dc = null)
        {
            Request = request; Natural = natural; Modifier = modifier; HelpBonus = helpBonus; Rerolled = rerolled;
            Dc = dc ?? (int)request.Difficulty;
            // A natural 20 always succeeds brilliantly and a natural 1 always fails badly.
            Outcome = natural == 20 ? Outcome.Critical
                : natural == 1 ? Outcome.Fumble
                : Total >= Dc ? Outcome.Success : Outcome.Failure;
        }

        public bool Succeeded => Outcome == Outcome.Success || Outcome == Outcome.Critical;

        /// <summary>Short Japanese line for the log, e.g. "ガレス 力の判定 17+3 = 20（難しさ 16）成功".</summary>
        public string Describe()
        {
            var bonus = Modifier + HelpBonus;
            var outcome = Outcome switch
            {
                Outcome.Critical => "大成功", Outcome.Success => "成功", Outcome.Failure => "失敗", _ => "大失敗",
            };
            return $"{Request.CharacterName} {Request.Stat.Label()}の判定 {Natural}{(bonus >= 0 ? "+" : "")}{bonus} = {Total}（目標 {Dc}）{outcome}{(Rerolled ? "（振り直し）" : "")}";
        }
    }

    public static class Checks
    {
        public const int HelpBonusEach = 2;

        /// <summary>Rolls a check. The character's pending bonus (blessing, card, help) is added and used up.</summary>
        public static CheckResult Roll(CheckRequest request, Character character, IRandom rng, bool rerolled = false, int extraBonus = 0)
            => new CheckResult(request, Dice.D20(rng), character.Modifier(request.Stat),
                request.Helpers * HelpBonusEach + character.ConsumeBonus() + extraBonus, rerolled);

        /// <summary>A roll against a target number that is not a character check (attacks against an enemy's defense).</summary>
        public static CheckResult RollAgainst(Character character, Stat stat, int dc, string reason, IRandom rng, int extraBonus = 0)
            => new CheckResult(new CheckRequest(character.Name, stat, Difficulty.Normal, reason), Dice.D20(rng),
                character.Modifier(stat), character.ConsumeBonus() + extraBonus, false, dc);

        /// <summary>Spends the character's fate coin to roll the same check again. Null when no coin is left.</summary>
        public static CheckResult? Reroll(CheckResult previous, Character character, IRandom rng)
            => character.SpendFateCoin()
                ? new CheckResult(previous.Request, Dice.D20(rng), previous.Modifier, previous.HelpBonus, true, previous.Dc)
                : null;
    }
}
