#nullable enable
using System.Collections.Generic;

namespace Tavern.Core.Game
{
    public enum LogKind { Narration, Declaration, Roll, Event, Warning }

    /// <summary>One line of the evening's log. The UI shows these; rolls also drive the 3D dice.</summary>
    public sealed class LogEntry
    {
        public LogKind Kind;
        /// <summary>Speaker or actor (storyteller name, character name, "" for the game).</summary>
        public string Who = "";
        public string Text = "";
        // Rolls only: the d20 face, the total and the target, for the dice and the log.
        public int Natural, Total, Target;
        public string Outcome = "";

        public static LogEntry Line(LogKind kind, string who, string text) => new LogEntry { Kind = kind, Who = who, Text = text };
    }

    public sealed class EnemyInstance
    {
        /// <summary>Unique id within the fight, e.g. wolf1.</summary>
        public string Uid = "";
        public string DefId = "", Name = "", Art = "";
        public int Hp, MaxHp, Defense;
        public bool Boss, Undead;
        public int Stunned;
        public bool Alive => Hp > 0;
    }

    public sealed class CombatState
    {
        public List<EnemyInstance> Enemies = new List<EnemyInstance>();
        public int Round = 1;
        /// <summary>Uid of the boss whose big move lands next round (announced this round).</summary>
        public string? BigMovePending;
        /// <summary>The boss's weakness has been used in this fight.</summary>
        public bool WeaknessUsed;
        /// <summary>Where the party flees to.</summary>
        public string FleeTo = "";
    }

    /// <summary>Everything about an evening in progress except the characters and the AI history (see SaveData).</summary>
    public sealed class GameState
    {
        public string ScenarioId = "", StorytellerId = "";
        public string Location = "";
        public List<string> Visited = new List<string>();
        public List<string> Clues = new List<string>();
        public int Act = 1;
        public int Turn;
        /// <summary>Minutes played (the UI adds real time; used for pacing hints).</summary>
        public double PlayMinutes;
        /// <summary>Minutes at which the current act started.</summary>
        public double ActStartedAt;
        /// <summary>success / bitter / failure once the story has ended.</summary>
        public string? Ending;
        public CombatState? Combat;
        public List<LogEntry> Log = new List<LogEntry>();
        public List<string> Suggestions = new List<string>();
    }
}
