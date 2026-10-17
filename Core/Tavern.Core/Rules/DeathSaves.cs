#nullable enable
using System;

namespace Tavern.Core.Rules
{
    public enum DeathSaveOutcome { Holds, Fails, StandsUp, Died }

    /// <summary>
    /// A downed character rolls a d20 on each of their turns: 10+ holds on, below 10 is a failure,
    /// three failures and they die, a natural 20 and they stand up with 1 HP. Allies can still revive them.
    /// </summary>
    public static class DeathSaves
    {
        public const int FailuresToDie = 3;
        public const int HoldOn = 10;

        public static DeathSaveOutcome Roll(Character character, IRandom rng, out int natural)
        {
            if (character.State != LifeState.Down) throw new InvalidOperationException($"{character.Name} is not down");
            natural = Dice.D20(rng);
            return Apply(character, natural);
        }

        /// <summary>Applies an already rolled number (the 3D dice decide it in the game).</summary>
        public static DeathSaveOutcome Apply(Character character, int natural)
        {
            if (natural == 20) { character.StandUp(); return DeathSaveOutcome.StandsUp; }
            if (natural >= HoldOn) return DeathSaveOutcome.Holds;
            character.AddDeathSaveFailure();
            return character.State == LifeState.Dead ? DeathSaveOutcome.Died : DeathSaveOutcome.Fails;
        }
    }
}
