#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>The four sorts the classifier AI gives each step of a declaration (09_Spec 4).</summary>
    public enum Sort
    {
        /// <summary>そのまま成功: no danger or loss on failure, or the result is not in doubt.</summary>
        Auto,
        /// <summary>判定</summary>
        Check,
        /// <summary>できない: only for the three reasons below.</summary>
        Impossible,
        /// <summary>運命のダイス: a question about a fact of the world, not an action.</summary>
        FateDie,
    }

    public enum ImpossibleReason
    {
        /// <summary>世界の決まり (one of the eight, named).</summary>
        WorldRule,
        /// <summary>持っていない・知らない</summary>
        Lacking,
        /// <summary>状況 (always with how it could be done).</summary>
        Situation,
    }

    /// <summary>Why a check moves a step. In total at most one step either way.</summary>
    public enum Shift
    {
        /// <summary>得意でない (effects outside the adventurer's specialties).</summary>
        NotSpecialty,
        /// <summary>職業の不得意</summary>
        ClassWeakness,
        /// <summary>状況が有利</summary>
        Favorable,
        /// <summary>状況が不利</summary>
        Unfavorable,
    }

    /// <summary>The price of failing an important check (decided by the classifier before rolling).</summary>
    public enum Cost
    {
        /// <summary>時計 +1</summary>
        Clock,
        /// <summary>傷（ダメージ 小 = 3）</summary>
        Wound,
        /// <summary>気付かれる（敵が待ち構える、その場の NPC の態度 -1）</summary>
        Noticed,
        /// <summary>物を失う・壊す</summary>
        LoseItem,
    }

    public enum Outcome { Fumble, Failure, Success, Critical }

    public static class CheckWords
    {
        public static int Steps(this Shift s) => s == Shift.Favorable ? -1 : 1;

        public static string Label(this Shift s) => s switch
        {
            Shift.NotSpecialty => "得意ではない", Shift.ClassWeakness => "職業の不得意", Shift.Favorable => "有利", Shift.Unfavorable => "不利",
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        };

        public static string Label(this Cost c) => c switch
        {
            Cost.Clock => "時が過ぎる", Cost.Wound => "傷を負う", Cost.Noticed => "気付かれる", Cost.LoseItem => "物を失う",
            _ => throw new ArgumentOutOfRangeException(nameof(c)),
        };

        public static string Label(this ImpossibleReason r) => r switch
        {
            ImpossibleReason.WorldRule => "世界の決まり", ImpossibleReason.Lacking => "持っていない・知らない", ImpossibleReason.Situation => "状況",
            _ => throw new ArgumentOutOfRangeException(nameof(r)),
        };

        public static string Label(this Outcome o) => o switch
        {
            Outcome.Critical => "大成功", Outcome.Success => "成功", Outcome.Failure => "失敗", _ => "大失敗",
        };

        public static bool Succeeded(this Outcome o) => o == Outcome.Success || o == Outcome.Critical;
    }

    /// <summary>One check as the classifier asked for it.</summary>
    public sealed class CheckSpec
    {
        public Ability Ability { get; set; }
        public Tier Tier { get; set; } = Tier.Normal;
        public List<Shift> Shifts { get; } = new List<Shift>();
        public string Reason { get; set; } = "";
        /// <summary>大事な判定: on failure it still gets through, with a cost (two on a fumble).</summary>
        public bool Important { get; set; }
        /// <summary>Costs picked beforehand, in order (the first one; the first two on a fumble).</summary>
        public List<Cost> Costs { get; } = new List<Cost>();
        /// <summary>Sneaking or hiding (the 忍び armour trait makes it a step easier).</summary>
        public bool Stealth { get; set; }
        /// <summary>A spell (a staff gives +1).</summary>
        public bool Spell { get; set; }
        /// <summary>Looking or attacking (目くらみ makes it a step harder).</summary>
        public bool Sight { get; set; }
    }

    /// <summary>
    /// A check being rolled: the target and bonuses are fixed first (shown as "DEX・普通 13" with the reasons), then a natural is applied.
    /// A fate coin rolls the natural again; a veteran mark makes it one step easier.
    /// </summary>
    public sealed class CheckRoll
    {
        public const int HelpBonus = 2;

        public string Who { get; }
        public CheckSpec Spec { get; }
        /// <summary>The tier after shifts (at most one step), statuses, armour and marks.</summary>
        public Tier Tier { get; private set; }
        public int Modifier { get; }
        /// <summary>Help (+2 however many help), boost, trinket, staff.</summary>
        public int Bonus { get; }
        public List<string> Reasons { get; } = new List<string>();
        public int Natural { get; private set; }
        public bool Rerolled { get; private set; }
        public bool MarkUsed { get; private set; }
        public int Target => (int)Tier;
        public int Total => Natural + Modifier + Bonus;

        public CheckRoll(Adventurer who, CheckSpec spec, int helpers = 0, bool takeBoost = true)
        {
            Who = who.Name;
            Spec = spec;
            int steps = Math.Max(-1, Math.Min(1, spec.Shifts.Sum(s => s.Steps())));
            foreach (var s in spec.Shifts.Distinct()) Reasons.Add($"{s.Label()}: 1 段{(s.Steps() < 0 ? "易しい" : "難しい")}");
            // statuses and armour come on top of the one-step cap
            if (who.Statuses.Has(StatusKind.Weak)) { steps++; Reasons.Add("弱り: 1 段難しい"); }
            if (spec.Sight && who.Statuses.Has(StatusKind.Dazzled)) { steps++; Reasons.Add("目くらみ: 1 段難しい"); }
            if (spec.Ability == Ability.Agi && who.Gear.BodyItem?.Trait == Trait.Heavy) { steps++; Reasons.Add("重装: 1 段難しい"); }
            if (spec.Stealth && who.Gear.BodyItem?.Trait == Trait.Stealthy) { steps--; Reasons.Add("忍びの装備: 1 段易しい"); }
            Tier = spec.Tier.Shift(steps);
            Modifier = who.Modifier(spec.Ability);
            int bonus = helpers > 0 ? HelpBonus : 0;
            if (takeBoost) bonus += who.TakeBoost();
            bonus += who.Gear.TrinketBonus(spec.Ability);
            if (spec.Spell && who.Gear.MainItem?.Weapon == WeaponType.Staff) bonus += 1;
            Bonus = bonus;
        }

        /// <summary>A veteran mark: one step easier (once per evening per mark). False when none is left.</summary>
        public bool UseMark(Adventurer who)
        {
            if (MarkUsed || !who.UseMark()) return false;
            MarkUsed = true;
            Tier = Tier.Shift(-1);
            Reasons.Add("歴戦の印: 1 段易しい");
            return true;
        }

        public void Apply(int natural) => Natural = natural;

        /// <summary>A fate coin: the natural is rolled again. False when no coin is left.</summary>
        public bool Reroll(Adventurer who, int natural)
        {
            if (Rerolled || !who.SpendFateCoin()) return false;
            Natural = natural;
            Rerolled = true;
            return true;
        }

        public Outcome Outcome => Natural == 20 ? Outcome.Critical : Natural == 1 ? Outcome.Fumble : Total >= Target ? Outcome.Success : Outcome.Failure;
        public bool Succeeded => Outcome.Succeeded();

        /// <summary>An important check gets through even when it fails.</summary>
        public bool GetsThrough => Succeeded || Spec.Important;

        /// <summary>The costs that apply: none on success or for an unimportant check; one on failure, two on a fumble.</summary>
        public List<Cost> CostsDue()
        {
            if (Succeeded || !Spec.Important) return new List<Cost>();
            var n = Outcome == Outcome.Fumble ? 2 : 1;
            var list = Spec.Costs.Take(n).ToList();
            while (list.Count < n) list.Add(list.Count == 0 ? Cost.Clock : Cost.Wound);   // the classifier forgot: time, then a wound
            return list;
        }

        /// <summary>"DEX・普通 13" (what the roll button shows).</summary>
        public string Heading => $"{Spec.Ability.Code()}・{Tier.Label()} {Target}";

        /// <summary>The result line before the narration, e.g. "ロッコ DEX・普通 13: 15+3 = 18 成功".</summary>
        public string Describe()
        {
            var sign = Modifier + Bonus >= 0 ? "+" : "";
            return $"{Who} {Heading}: {Natural}{sign}{Modifier + Bonus} = {Total} {Outcome.Label()}{(Rerolled ? "（振り直し）" : "")}";
        }
    }

    /// <summary>運命のダイス: five likelihoods (09_Spec 4).</summary>
    public enum Likelihood { Certain, Likely, Even, Unlikely, Remote }

    public enum FateAnswer { NoAndWorse, No, Yes, YesAndBetter }

    public static class FateDie
    {
        /// <summary>The natural needed for "yes": 4 / 8 / 11 / 14 / 18.</summary>
        public static int Needed(this Likelihood l) => l switch
        {
            Likelihood.Certain => 4, Likelihood.Likely => 8, Likelihood.Even => 11, Likelihood.Unlikely => 14, _ => 18,
        };

        public static string Label(this Likelihood l) => l switch
        {
            Likelihood.Certain => "ほぼ確実", Likelihood.Likely => "ありそう", Likelihood.Even => "五分", Likelihood.Unlikely => "なさそう", _ => "まずない",
        };

        /// <summary>20 is yes with something good, 1 is no with something bad.</summary>
        public static FateAnswer Answer(Likelihood l, int natural) =>
            natural == 20 ? FateAnswer.YesAndBetter : natural == 1 ? FateAnswer.NoAndWorse : natural >= l.Needed() ? FateAnswer.Yes : FateAnswer.No;

        public static bool IsYes(this FateAnswer a) => a == FateAnswer.Yes || a == FateAnswer.YesAndBetter;

        public static string Label(this FateAnswer a) => a switch
        {
            FateAnswer.YesAndBetter => "はい（しかも良いことが）", FateAnswer.Yes => "はい", FateAnswer.No => "いいえ", _ => "いいえ（しかも悪いことが）",
        };
    }

    /// <summary>One side of a contest between adventurers.</summary>
    public sealed class ContestSide
    {
        public string Id { get; set; } = "";
        public Ability Ability { get; set; }
        public int Modifier { get; set; }
        public int Natural { get; set; }
        /// <summary>The side keeping what it has (wins ties).</summary>
        public bool Defender { get; set; }
        public int Total => Natural + Modifier;
    }

    public sealed class ContestResult
    {
        public ContestSide Winner { get; set; } = null!;
        /// <summary>The winner rolled 20: one extra good thing.</summary>
        public bool WinnerBonus { get; set; }
        /// <summary>Losers who rolled 1: a small mishap each.</summary>
        public List<ContestSide> Mishaps { get; } = new List<ContestSide>();
    }

    /// <summary>
    /// 仲間同士の対抗判定 (09_Spec 4): everyone involved rolls, the highest wins. 20 always wins and 1 always loses;
    /// ties (and two 20s) go to the defender.
    /// </summary>
    public static class Contest
    {
        public static ContestResult Resolve(IReadOnlyList<ContestSide> sides)
        {
            if (sides.Count < 2) throw new ArgumentException("a contest needs two sides");
            static int Rank(ContestSide s) => s.Natural == 20 ? 2 : s.Natural == 1 ? 0 : 1;
            var winner = sides
                .OrderByDescending(Rank)
                .ThenByDescending(s => s.Total)
                .ThenByDescending(s => s.Defender)
                .First();
            var r = new ContestResult { Winner = winner, WinnerBonus = winner.Natural == 20 };
            r.Mishaps.AddRange(sides.Where(s => s != winner && s.Natural == 1));
            return r;
        }

        /// <summary>Against someone who does not resist: only the actor rolls, against 10 + the other's modifier.</summary>
        public static bool Unresisted(int natural, int modifier, int otherModifier) =>
            natural == 20 || (natural != 1 && natural + modifier >= 10 + otherModifier);
    }

    /// <summary>内緒の行動: whether the others notice (only the actor rolls and sees the die).</summary>
    public static class Secrecy
    {
        public const Ability RollAbility = Ability.Agi;   // 〔仮〕 the spec names only the target; sneaking uses AGI

        /// <summary>
        /// The target: 10 + the highest INT among the allies at the spot who can act, +2 with a wary or hostile NPC there.
        /// Null when no ally is there (no roll).
        /// </summary>
        public static int? Target(IEnumerable<Adventurer> alliesPresent, bool waryNpcPresent)
        {
            var watchers = alliesPresent.Where(a => a.IsUp && !a.Statuses.Has(StatusKind.Asleep)).ToList();
            if (watchers.Count == 0) return null;
            return 10 + watchers.Max(a => a.Modifier(Ability.Int)) + (waryNpcPresent ? 2 : 0);
        }

        /// <summary>True when the secret is found out (failed, or a fumble).</summary>
        public static bool Found(int natural, int modifier, int target) =>
            natural == 1 || (natural != 20 && natural + modifier < target);
    }
}
