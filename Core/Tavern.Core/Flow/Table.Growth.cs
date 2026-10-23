#nullable enable
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;

namespace Tavern.Core.Flow
{
    /// <summary>One growth an adventurer may pick at the end of an act (09_Spec 3).</summary>
    public sealed class GrowthOption
    {
        public string Label { get; set; } = "";
        public Ability? Ability { get; set; }
        public bool Toughen { get; set; }
        public Specialty? Specialty { get; set; }
    }

    public sealed partial class Table
    {
        /// <summary>Adventurers who still have to pick a growth (end of acts 1-2, or 1-4 in a long story).</summary>
        public List<string> GrowthDue { get; } = new List<string>();

        /// <summary>The choices: an ability +1 (up to +5), max HP +3, or a new specialty from the class's growth list.</summary>
        public static List<GrowthOption> GrowthOptions(Adventurer a)
        {
            var list = new List<GrowthOption>();
            foreach (var ab in new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd })
                if (a.Modifier(ab) < AbilityNames.MaxModifier)
                    list.Add(new GrowthOption { Label = $"{ab.Code()} +1（{ab.Meaning()}、{a.Modifier(ab)} → {a.Modifier(ab) + 1}）", Ability = ab });
            list.Add(new GrowthOption { Label = $"最大 HP +3（{a.MaxHp} → {a.MaxHp + 3}）", Toughen = true });
            foreach (var s in a.GrowthChoices()) list.Add(new GrowthOption { Label = "新しい得意: " + s.Text, Specialty = s });
            return list;
        }

        /// <summary>Applies a picked growth. False when the adventurer has none due or the option does not fit.</summary>
        public bool Grow(string adventurerId, GrowthOption pick)
        {
            var a = E.Find(adventurerId);
            if (a == null || !GrowthDue.Contains(adventurerId)) return false;
            var ok = pick.Ability is Ability ab ? a.Improve(ab) : pick.Toughen ? Toughen(a) : pick.Specialty != null && a.Learn(pick.Specialty);
            if (ok) GrowthDue.Remove(adventurerId);
            return ok;
        }

        static bool Toughen(Adventurer a) { a.Toughen(); return true; }
    }
}
