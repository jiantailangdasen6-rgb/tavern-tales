#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    public enum Life { Up, Down, Dead }

    /// <summary>歴戦の印: earned for each evening survived; once per evening it makes one of the owner's checks a step easier.</summary>
    public sealed class VeteranMark
    {
        public string Name { get; set; } = "";
        public bool UsedTonight { get; set; }
    }

    /// <summary>名付けた技: a 【name】 from a declaration, kept with the effect it was first given.</summary>
    public sealed class NamedTechnique
    {
        public const int MaxName = 12;
        public string Name { get; set; } = "";
        public string Note { get; set; } = "";
        public EffectKind Kind { get; set; }
        public EffectSize Size { get; set; }
        public Ability Ability { get; set; }
    }

    public enum DeathSave { Holds, Fails, StandsUp, Died }

    /// <summary>
    /// A player's adventurer (09_Spec 3). The game owns every number here; the AI only reads them.
    /// What lasts between evenings: name, class, background, portrait, veteran marks, named techniques. Growth, gear and silver reset.
    /// </summary>
    public sealed class Adventurer
    {
        public const int MaxMarks = 3;
        public const int MaxTechniques = 8;
        public const int StartingSilver = 10;
        public const int DeathSaveTarget = 10;
        public const int DeathSaveFailures = 3;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ClassDef Class { get; }
        public string Background { get; set; } = "";
        public string Portrait { get; set; } = "";
        public string RosterId { get; set; } = "";

        /// <summary>Growth tonight (+1 steps per ability); resets each evening.</summary>
        public Dictionary<Ability, int> Growth { get; } = new Dictionary<Ability, int>();
        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public int MaxFocus { get; private set; }
        public int Focus { get; private set; }
        public int FateCoins { get; set; }
        public int Silver { get; set; }
        public Life Life { get; private set; } = Life.Up;
        public int DeathFailures { get; private set; }
        /// <summary>Specialties gained by growth tonight (the class ones are always there).</summary>
        public List<Specialty> Gained { get; } = new List<Specialty>();
        public List<VeteranMark> Marks { get; } = new List<VeteranMark>();
        public List<NamedTechnique> Techniques { get; } = new List<NamedTechnique>();
        public Gear Gear { get; private set; } = new Gear();
        public StatusSet Statuses { get; } = new StatusSet();

        /// <summary>加護 waiting for this adventurer's next check (the biggest one counts, they do not add up).</summary>
        public int PendingBoost { get; set; }
        /// <summary>守り: taken off the next damage (the biggest one counts).</summary>
        public int Ward { get; set; }
        /// <summary>Left the evening without dying (今夜は抜けた).</summary>
        public bool Withdrawn { get; set; }

        public Adventurer(ClassDef cls, string name, string id = "")
        {
            Class = cls;
            Name = name;
            Id = string.IsNullOrEmpty(id) ? name : id;
            StartEvening(Difficulty.Standard);
        }

        public IEnumerable<Specialty> Specialties => Class.Specialties.Concat(Gained);
        public bool HasSpecialty(EffectKind k) => Specialties.Any(s => s.Kind == k);
        public Specialty? SpecialtyFor(EffectKind k) => Specialties.FirstOrDefault(s => s.Kind == k);

        /// <summary>Ability modifier: class value plus tonight's growth, at most +5.</summary>
        public int Modifier(Ability a) => Math.Min(AbilityNames.MaxModifier, Class.Stats[a] + (Growth.TryGetValue(a, out var g) ? g : 0));

        public bool IsUp => Life == Life.Up && !Withdrawn;
        /// <summary>Up and not held by a status that takes the whole turn.</summary>
        public bool CanAct => IsUp && Statuses.CanAct;

        /// <summary>
        /// Back to the class's starting values for a new evening (growth, HP, focus, gear, silver, coins, statuses); marks become usable again.
        /// Marks and named techniques stay.
        /// </summary>
        public void StartEvening(Difficulty difficulty, int extraSilver = 0)
        {
            Growth.Clear();
            Gained.Clear();
            MaxHp = Hp = Class.Hp;
            MaxFocus = Focus = Class.Focus;
            FateCoins = difficulty.FateCoins();
            Silver = StartingSilver + extraSilver;
            Life = Life.Up;
            DeathFailures = 0;
            Withdrawn = false;
            PendingBoost = 0; Ward = 0;
            Statuses.List.Clear();
            foreach (var m in Marks) m.UsedTonight = false;
            Gear = new Gear();
            foreach (var g in Class.Gear)
            {
                var item = g.Item.Clone();
                Gear.Add(item);
                switch (g.Equip)
                {
                    case "main": Gear.Equip(item.Id, Modifier(Ability.Str)); break;
                    case "off": Gear.Equip(item.Id, Modifier(Ability.Str), offHand: true); break;
                    case "body": case "trinket": Gear.Equip(item.Id, Modifier(Ability.Str)); break;
                }
            }
        }

        // ---- class text ----

        /// <summary>The sheet's class text: one sentence per specialty, growth sentences added at the end.</summary>
        public string ClassText()
        {
            var parts = new List<string> { Class.Intro };
            parts.AddRange(Class.Specialties.Select(s => s.Text));
            parts.AddRange(Gained.Select(s => s.Text));
            if (Class.Outro.Length > 0) parts.Add(Class.Outro);
            return string.Concat(parts);
        }

        // ---- growth (end of acts) ----

        /// <summary>+1 to an ability. False at the +5 cap.</summary>
        public bool Improve(Ability a)
        {
            if (Modifier(a) >= AbilityNames.MaxModifier) return false;
            Growth[a] = (Growth.TryGetValue(a, out var g) ? g : 0) + 1;
            return true;
        }

        /// <summary>+3 max HP (current HP rises by the same).</summary>
        public void Toughen(int amount = 3) { MaxHp += amount; if (Life != Life.Dead) Hp += amount; }

        /// <summary>The class's growth candidates not taken yet.</summary>
        public List<Specialty> GrowthChoices() => Class.Growth.Where(g => !Gained.Any(x => x.Text == g.Text)).ToList();

        public bool Learn(Specialty s)
        {
            if (!Class.Growth.Contains(s) || Gained.Contains(s)) return false;
            Gained.Add(s);
            return true;
        }

        // ---- HP, focus, death ----

        public void TakeDamage(int amount)
        {
            if (Life == Life.Dead || amount <= 0) return;
            Hp = Math.Max(0, Hp - amount);
            if (Hp == 0 && Life == Life.Up) { Life = Life.Down; DeathFailures = 0; }
        }

        public void Heal(int amount)
        {
            if (Life == Life.Dead || amount <= 0) return;
            Hp = Math.Min(MaxHp, Hp + amount);
            if (Life == Life.Down && Hp > 0) { Life = Life.Up; DeathFailures = 0; }
        }

        /// <summary>An ally spends their action to get a downed adventurer back up (1 HP).</summary>
        public void Revive() { if (Life == Life.Down) Heal(1); }

        /// <summary>とどめ: a downed adventurer dies at once.</summary>
        public void Finish() { if (Life == Life.Down) { Hp = 0; Life = Life.Dead; } }

        public void Kill() { Hp = 0; Life = Life.Dead; }

        public bool SpendFocus(int amount)
        {
            if (amount < 0 || Focus < amount) return false;
            Focus -= amount;
            return true;
        }

        public void RestoreFocus() => Focus = MaxFocus;
        public void RestoreFocusBy(int amount) => Focus = Math.Min(MaxFocus, Focus + Math.Max(0, amount));

        /// <summary>Rest (once per act): half the max HP back, all focus back.</summary>
        public void Rest()
        {
            if (Life != Life.Up) return;
            Heal(MaxHp / 2);
            RestoreFocus();
        }

        /// <summary>A downed adventurer's roll on their turn: 10+ holds, 3 failures dies, a natural 20 stands up with 1 HP.</summary>
        public DeathSave ApplyDeathSave(int natural)
        {
            if (Life != Life.Down) throw new InvalidOperationException($"{Name} is not down");
            if (natural == 20) { Heal(1); return DeathSave.StandsUp; }
            if (natural >= DeathSaveTarget) return DeathSave.Holds;
            DeathFailures++;
            if (DeathFailures >= DeathSaveFailures) { Life = Life.Dead; return DeathSave.Died; }
            return DeathSave.Fails;
        }

        public bool SpendFateCoin()
        {
            if (FateCoins <= 0) return false;
            FateCoins--;
            return true;
        }

        /// <summary>Uses one unused veteran mark (one check one step easier). False when none is left tonight.</summary>
        public bool UseMark()
        {
            var m = Marks.FirstOrDefault(x => !x.UsedTonight);
            if (m == null) return false;
            m.UsedTonight = true;
            return true;
        }

        public int MarksLeft => Marks.Count(m => !m.UsedTonight);

        /// <summary>A mark for surviving the evening (two for a long story). At three, no more are earned.</summary>
        public void EarnMark(string name)
        {
            if (Marks.Count < MaxMarks) Marks.Add(new VeteranMark { Name = name });
        }

        /// <summary>Takes the pending boost for a check.</summary>
        public int TakeBoost() { var b = PendingBoost; PendingBoost = 0; return b; }

        /// <summary>The mage's magic missile: a spell with INT, 2 damage, no focus.</summary>
        public AttackProfile MagicMissile()
        {
            var p = new AttackProfile { Ability = Ability.Int, Damage = 2 };
            return p;
        }

        public AttackProfile Attack() => Gear.Attack(Modifier(Ability.Str));

        // ---- named techniques ----

        /// <summary>
        /// Keeps a 【name】 technique. A name already on the sheet keeps its first content (returns that one).
        /// Null when the name is empty or too long, or the sheet already holds eight.
        /// </summary>
        public NamedTechnique? AddTechnique(string name, string note, EffectKind kind, EffectSize size, Ability ability)
        {
            name = name.Trim();
            if (name.Length == 0 || name.Length > NamedTechnique.MaxName) return null;
            var old = FindTechnique(name);
            if (old != null) return old;
            if (Techniques.Count >= MaxTechniques) return null;
            var t = new NamedTechnique { Name = name, Note = note, Kind = kind, Size = size, Ability = ability };
            Techniques.Add(t);
            return t;
        }

        public NamedTechnique? FindTechnique(string name) => Techniques.FirstOrDefault(t => t.Name == name.Trim());
        public bool ForgetTechnique(string name) => Techniques.RemoveAll(t => t.Name == name.Trim()) > 0;

        /// <summary>Puts saved numbers back (load).</summary>
        public void Restore(int maxHp, int hp, int maxFocus, int focus, Life life, int deathFailures, Gear gear)
        {
            MaxHp = maxHp; Hp = Math.Max(0, Math.Min(maxHp, hp)); MaxFocus = maxFocus; Focus = Math.Max(0, Math.Min(maxFocus, focus));
            Life = life; DeathFailures = deathFailures; Gear = gear;
        }
    }

    /// <summary>Finds 【named techniques】 in a declaration (the game does this, not the AI).</summary>
    public static class TechniqueNames
    {
        public static List<string> Find(string declaration)
        {
            var names = new List<string>();
            int i = 0;
            while ((i = declaration.IndexOf('【', i)) >= 0)
            {
                int j = declaration.IndexOf('】', i + 1);
                if (j < 0) break;
                var name = declaration.Substring(i + 1, j - i - 1).Trim();
                if (name.Length > 0 && !names.Contains(name)) names.Add(name);
                i = j + 1;
            }
            return names;
        }
    }
}
