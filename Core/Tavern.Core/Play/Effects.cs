#nullable enable
using System;

namespace Tavern.Core.Play
{
    /// <summary>
    /// What a technique, spell or free action in combat does (09_Spec 3, the effect table). The AI picks the kind and size;
    /// the numbers and the focus cost come from here.
    /// </summary>
    public enum EffectKind
    {
        /// <summary>1 体攻撃</summary>
        Strike,
        /// <summary>全体攻撃</summary>
        Sweep,
        /// <summary>動きを止める</summary>
        Hold,
        /// <summary>かばう</summary>
        Cover,
        /// <summary>加護</summary>
        Boost,
        /// <summary>守り</summary>
        Ward,
        /// <summary>回復</summary>
        Heal,
        /// <summary>挑発</summary>
        Taunt,
        /// <summary>煙</summary>
        Smoke,
        /// <summary>調べる・開ける</summary>
        Unlock,
    }

    public enum EffectSize { Small = 1, Medium = 2, Large = 3 }

    public static class Effects
    {
        public static string Label(this EffectKind k) => k switch
        {
            EffectKind.Strike => "1 体攻撃", EffectKind.Sweep => "全体攻撃", EffectKind.Hold => "動きを止める", EffectKind.Cover => "かばう",
            EffectKind.Boost => "加護", EffectKind.Ward => "守り", EffectKind.Heal => "回復", EffectKind.Taunt => "挑発",
            EffectKind.Smoke => "煙", EffectKind.Unlock => "調べる・開ける",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        public static string Label(this EffectSize s) => s switch { EffectSize.Small => "小", EffectSize.Medium => "中", _ => "大" };

        /// <summary>Focus cost: small 1, medium 2, large 3 (plain attacks and plain actions cost nothing).</summary>
        public static int FocusCost(EffectSize s) => (int)s;

        /// <summary>Effects aimed at oneself or allies; with the matching specialty they need no roll.</summary>
        public static bool IsSupport(this EffectKind k) =>
            k == EffectKind.Heal || k == EffectKind.Boost || k == EffectKind.Ward || k == EffectKind.Cover || k == EffectKind.Smoke;

        /// <summary>Unlock has no large size.</summary>
        public static bool Exists(EffectKind k, EffectSize s) => !(k == EffectKind.Unlock && s == EffectSize.Large);

        /// <summary>The size one step up (undead-bane effects against the undead), capped at large.</summary>
        public static EffectSize Up(this EffectSize s) => s == EffectSize.Large ? s : s + 1;

        public static int StrikeDamage(EffectSize s) => s switch { EffectSize.Small => 3, EffectSize.Medium => 5, _ => 7 };
        public static int SweepDamage(EffectSize s) => s switch { EffectSize.Small => 2, EffectSize.Medium => 3, _ => 4 };
        /// <summary>Damage taken off a covered ally's hit; -1 means all of it.</summary>
        public static int CoverReduction(EffectSize s) => s switch { EffectSize.Small => 1, EffectSize.Medium => 2, _ => -1 };
        public static int BoostBonus(EffectSize s) => s == EffectSize.Large ? 3 : 2;
        /// <summary>Small boost is one ally; medium and large are everyone.</summary>
        public static bool BoostAll(EffectSize s) => s != EffectSize.Small;
        public static int WardReduction(EffectSize s) => s switch { EffectSize.Small => 2, EffectSize.Medium => 4, _ => 6 };
        public static int HealAmount(EffectSize s) => s switch { EffectSize.Small => 3, EffectSize.Medium => 5, _ => 8 };
        /// <summary>Taunt / smoke: how many enemy turns it lasts (small taunts one enemy for its next turn).</summary>
        public static int TauntRounds(EffectSize s) => s == EffectSize.Large ? 2 : 1;
        /// <summary>Smoke: how many steps easier fleeing and hiding become (small hides one ally instead).</summary>
        public static int SmokeSteps(EffectSize s) => s switch { EffectSize.Small => 0, EffectSize.Medium => 1, _ => 2 };
    }

    /// <summary>A kind and a size together.</summary>
    public readonly struct Effect
    {
        public EffectKind Kind { get; }
        public EffectSize Size { get; }
        public Effect(EffectKind kind, EffectSize size) { Kind = kind; Size = size; }
        public int FocusCost => Effects.FocusCost(Size);
        public override string ToString() => $"{Kind.Label()}（{Size.Label()}）";
    }
}
