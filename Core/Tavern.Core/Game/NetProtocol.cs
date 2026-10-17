#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Tavern.Core.Game
{
    // Messages of online play (Docs/04_Online.md). All are JSON; the host owns the game, friends mirror it.

    public sealed class HelloMsg
    {
        public string PlayerName = "";
        public CharacterSave Character = new CharacterSave();
    }

    public sealed class LobbyPlayer
    {
        public ulong Id;
        public string PlayerName = "", CharacterName = "", ClassId = "", Portrait = "";
    }

    public sealed class LobbyMsg
    {
        public List<LobbyPlayer> Players = new List<LobbyPlayer>();
        public string Code = "";
    }

    public sealed class DeclareMsg
    {
        public List<Declaration> Declarations = new List<Declaration>();
    }

    /// <summary>The state after a turn (or on joining): what friends need to draw the table.</summary>
    public sealed class TurnMsg
    {
        /// <summary>SaveData without the AI history, the log cut to the recent lines.</summary>
        public SaveData Snapshot = new SaveData();
        /// <summary>Character names whose action is already declared for the coming turn.</summary>
        public List<string> Declared = new List<string>();
        /// <summary>The host is waiting for the AI.</summary>
        public bool Busy;

        public const int LogLines = 60;

        public static TurnMsg From(TavernGame game, IEnumerable<string> declared, bool busy)
        {
            var snap = new SaveData
            {
                State = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(game.State))!,
                Party = game.Party.Select(CharacterSave.From).ToList(),
            };
            var log = snap.State.Log;
            if (log.Count > LogLines) snap.State.Log = log.Skip(log.Count - LogLines).ToList();
            return new TurnMsg { Snapshot = snap, Declared = declared.ToList(), Busy = busy };
        }
    }

    public sealed class AskRerollMsg
    {
        public int Id;
        /// <summary>Roll lines up to now (so the dice can be shown before the question) and the failed ones to choose from.</summary>
        public List<LogEntry> Rolls = new List<LogEntry>();
        public List<int> FailedSeq = new List<int>();
    }

    public sealed class RerollMsg
    {
        public int Id;
        public List<int> PickedSeq = new List<int>();
    }

    public sealed class GrowthMsg
    {
        public string Character = "", Choice = "";
    }

    public sealed class OrderMsg
    {
        public string Text = "", Length = "";
    }

    public static class NetJson
    {
        public static string Json(object o) => JsonConvert.SerializeObject(o);
        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json)!;
    }
}

namespace Tavern.Core.Game
{
    /// <summary>A friend's PC never calls the AI; its mirrored game gets this.</summary>
    public sealed class NoLlm : Tavern.Core.Gm.ILlmClient
    {
        public string Name => "none";
        public System.Threading.Tasks.Task<Tavern.Core.Gm.LlmReply> ChatAsync(IReadOnlyList<Tavern.Core.Gm.ChatMessage> messages,
            IReadOnlyList<Tavern.Core.Gm.ToolDef> tools, string? forceTool = null, System.Threading.CancellationToken ct = default)
            => throw new System.InvalidOperationException("only the host talks to the AI");
    }

    public static class Mirror
    {
        /// <summary>Builds a read-only copy of the host's game from a turn message (friends draw the table from it).</summary>
        public static TavernGame Build(TurnMsg msg, Content.ContentDb db, Content.ScenarioDef scenario)
            => msg.Snapshot.Load(db, Scenarios.For(msg.Snapshot.State, scenario), Tavern.Core.Gm.Storyteller.Find(msg.Snapshot.State.StorytellerId), new NoLlm(), null, new Rules.SeededRandom(1));
    }
}
