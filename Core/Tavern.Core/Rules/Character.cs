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
        readonly Dictionary<Stat, int> _stats = new Dictionary<Stat, int>();

        public int MaxHp { get; }
        public int Hp { get; private set; }
        public int MaxFocus { get; }
        public int Focus { get; private set; }
        public int FateCoins { get; private set; } = 1;
        public LifeState State { get; private set; } = LifeState.Active;
        public int DeathSaveFailures { get; private set; }

        public Character(string name, string classId, int might, int skill, int wits, int spirit, int maxHp, int maxFocus, string background = "")
        {
            Name = name; ClassId = classId; Background = background;
            _stats[Stat.Might] = might; _stats[Stat.Skill] = skill; _stats[Stat.Wits] = wits; _stats[Stat.Spirit] = spirit;
            MaxHp = Hp = maxHp; MaxFocus = Focus = maxFocus;
        }

        public int Modifier(Stat stat) => _stats[stat];

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

        public bool SpendFateCoin()
        {
            if (FateCoins <= 0) return false;
            FateCoins--;
            return true;
        }

        internal void AddDeathSaveFailure()
        {
            DeathSaveFailures++;
            if (DeathSaveFailures >= DeathSaves.FailuresToDie) State = LifeState.Dead;
        }

        internal void StandUp() { Heal(1); }
    }
}
