#nullable enable
using System;
using System.Collections.Generic;

namespace Tavern.Core.Rules
{
    public enum LifeState { Active, Down, Dead }

    /// <summary>A player character. The game owns these numbers; the AI only reads them.</summary>
    public sealed class Character
    {
        public string Name { get; }
        /// <summary>Class id: warrior / mage / thief / priest.</summary>
        public string ClassId { get; }
        /// <summary>Free-text background and personality written by the player.</summary>
        public string Background { get; set; }
        /// <summary>Portrait art id (e.g. pc_warrior_f).</summary>
        public string Portrait { get; set; } = "";
        /// <summary>The roster entry this adventurer came from (empty for one made tonight); the roster matches by this, never by name.</summary>
        public string RosterId { get; set; } = "";
        readonly Dictionary<Stat, int> _stats = new Dictionary<Stat, int>();

        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public int MaxFocus { get; }
        public int Focus { get; private set; }
        public int FateCoins { get; private set; } = 1;
        public LifeState State { get; private set; } = LifeState.Active;
        public int DeathSaveFailures { get; private set; }
        /// <summary>Added to this character's next check, then cleared (blessings, utility cards, help).</summary>
        public int NextCheckBonus { get; set; }
        public List<string> Cards { get; } = new List<string>();
        public List<string> Items { get; } = new List<string>();

        public Character(string name, string classId, int might, int skill, int wits, int spirit, int maxHp, int maxFocus, string background = "")
        {
            Name = name; ClassId = classId; Background = background;
            _stats[Stat.Might] = might; _stats[Stat.Skill] = skill; _stats[Stat.Wits] = wits; _stats[Stat.Spirit] = spirit;
            MaxHp = Hp = maxHp; MaxFocus = Focus = maxFocus;
        }

        public int Modifier(Stat stat) => _stats[stat];
        public bool CanAct => State == LifeState.Active;

        public void TakeDamage(int amount)
        {
            if (State == LifeState.Dead || amount <= 0) return;
            Hp = Math.Max(0, Hp - amount);
            if (Hp == 0 && State == LifeState.Active) { State = LifeState.Down; DeathSaveFailures = 0; }
        }

        public void Heal(int amount)
        {
            if (State == LifeState.Dead || amount <= 0) return;
            Hp = Math.Min(MaxHp, Hp + amount);
            if (State == LifeState.Down && Hp > 0) { State = LifeState.Active; DeathSaveFailures = 0; }
        }

        public bool SpendFocus(int amount)
        {
            if (amount < 0 || Focus < amount) return false;
            Focus -= amount;
            return true;
        }

        public void RestoreFocus(int amount) { Focus = Math.Min(MaxFocus, Focus + Math.Max(0, amount)); }

        public bool SpendFateCoin()
        {
            if (FateCoins <= 0) return false;
            FateCoins--;
            return true;
        }

        /// <summary>Growth between acts: one ability +1 (up to +5).</summary>
        public void Improve(Stat stat) { _stats[stat] = Math.Min(5, _stats[stat] + 1); }

        /// <summary>Growth between acts: more max HP (current HP rises by the same amount).</summary>
        public void Toughen(int amount) { MaxHp += amount; if (State != LifeState.Dead) Hp += amount; }

        /// <summary>Takes and clears the pending bonus.</summary>
        public int ConsumeBonus() { var b = NextCheckBonus; NextCheckBonus = 0; return b; }

        internal void AddDeathSaveFailure()
        {
            DeathSaveFailures++;
            if (DeathSaveFailures >= DeathSaves.FailuresToDie) State = LifeState.Dead;
        }

        internal void StandUp() { Heal(1); }

        /// <summary>Puts saved numbers back (load game).</summary>
        public void Restore(int hp, int focus, int fateCoins, LifeState state, int deathSaveFailures, int nextBonus)
        {
            Hp = Math.Max(0, Math.Min(MaxHp, hp)); Focus = Math.Max(0, Math.Min(MaxFocus, focus));
            FateCoins = fateCoins; State = state; DeathSaveFailures = deathSaveFailures; NextCheckBonus = nextBonus;
        }
    }
}
