#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Tavern.Core.Ai;

namespace Tavern.Core.Flow
{
    // Online play on the new flow (09_Spec 14). The host owns the table and the AI; friends send what their own adventurer does and
    // draw the state the host sends them, cut to what that person may see. All messages are JSON (NetSession zips them).

    /// <summary>Message names (NetSession registers these).</summary>
    public static class NightMsg
    {
        public const string Hello = "n_hello", Room = "n_room", Pin = "n_pin", State = "n_state", Report = "n_report", Declare = "n_declare",
            Ask = "n_ask", Answer = "n_answer", Dice = "n_dice", Order = "n_order", Clue = "n_clue", End = "n_end", Closed = "n_closed";
        public static readonly string[] All = { Hello, Room, Pin, State, Report, Declare, Ask, Answer, Dice, Order, Clue, End, Closed };
    }

    /// <summary>A friend knocks: who they are and the adventurer they bring (ignored when they come back to their seat).</summary>
    public sealed class NightHello
    {
        public string PlayerName = "";
        /// <summary>Stable per PC and profile (the anonymous sign-in id): a friend who drops out gets the same seat back.</summary>
        public string Key = "";
        public string Version = "";
        public SavedAdventurer Adventurer = new SavedAdventurer();
    }

    public sealed class RoomPlayer
    {
        public ulong Id;
        public string Key = "", PlayerName = "", Version = "";
        public bool Host, Connected = true;
        public SavedAdventurer Adventurer = new SavedAdventurer();
    }

    /// <summary>The room before and during the evening: who is in, the pins on the map, what the host is doing.</summary>
    public sealed class RoomMsg
    {
        public string Code = "", Version = "";
        /// <summary>"map" choosing a story, "handouts" the storyteller prepares, "table" the evening runs, "resume" waiting for friends to come back.</summary>
        public string Phase = "map";
        public List<RoomPlayer> Players = new List<RoomPlayer>();
        /// <summary>Player name → story id.</summary>
        public Dictionary<string, string> Pins = new Dictionary<string, string>();
        public string Difficulty = "";
        /// <summary>A reason the room turned the reader away ("版が違う", "物語はもう始まっている").</summary>
        public string Refused = "";
    }

    public sealed class PinMsg { public string StoryId = ""; }

    /// <summary>The table as one person may see it, after every change.</summary>
    public sealed class StateMsg
    {
        /// <summary>SaveGame JSON cut for the reader (OnlineView.SaveFor).</summary>
        public string Save = "";
        /// <summary>The reader's adventurers.</summary>
        public List<string> Mine = new List<string>();
        /// <summary>Adventurer id → player name.</summary>
        public Dictionary<string, string> Owners = new Dictionary<string, string>();
        /// <summary>Adventurers whose declaration is in for the coming turn.</summary>
        public List<string> Declared = new List<string>();
        /// <summary>The others' declarations, only once the reader has declared (a secret one shows its cover).</summary>
        public Dictionary<string, string> Shown = new Dictionary<string, string>();
        /// <summary>Adventurers whose player has dropped out.</summary>
        public List<string> Away = new List<string>();
        public bool Busy;
        /// <summary>Seconds left before the declarations close (0 while not everyone is in).</summary>
        public float Closing;
        public string Quota = "";
    }

    /// <summary>A turn's results and telling for one person (their own secret lines only).</summary>
    public sealed class ReportMsg
    {
        public List<string> Lines = new List<string>();
        public Dictionary<string, List<string>> Private = new Dictionary<string, List<string>>();
        public string Narration = "";
        public Dictionary<string, string> PrivateNarration = new Dictionary<string, string>();
        public Dictionary<string, List<string>> Deferred = new Dictionary<string, List<string>>();
        /// <summary>The declarations of the turn as this reader sees them (others' secret ones by their cover).</summary>
        public List<string> Said = new List<string>();
        /// <summary>An announcement in the log ("ロッコは今夜は抜けた").</summary>
        public string Note = "";

        public static ReportMsg For(TurnReport r, ICollection<string> mine) => new ReportMsg
        {
            Lines = r.Lines.ToList(),
            Private = r.PrivateLines.Where(kv => mine.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
            Narration = r.Narration,
            PrivateNarration = r.PrivateNarration.Where(kv => mine.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Deferred = r.Deferred.Where(kv => mine.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
        };

        public TurnReport ToReport()
        {
            var r = new TurnReport { Narration = Narration };
            r.Lines.AddRange(Lines);
            foreach (var kv in Private) r.PrivateLines[kv.Key] = kv.Value.ToList();
            foreach (var kv in PrivateNarration) r.PrivateNarration[kv.Key] = kv.Value;
            foreach (var kv in Deferred) r.Deferred[kv.Key] = kv.Value.ToList();
            return r;
        }
    }

    /// <summary>A friend's declarations (rewritten freely until the turn closes).</summary>
    public sealed class NightDeclare
    {
        public List<Declaration> Declarations = new List<Declaration>();
        public bool Ready;
    }

    /// <summary>A die as the players see it before rolling.</summary>
    public sealed class RollView
    {
        public string Who = "", For = "", Heading = "";
        public List<string> Reasons = new List<string>();
        public RollKind Kind;
        public bool Private, MarkAllowed, CoinAllowed, Important;
        public int Bonus, Target;

        public static RollView From(RollRequest r) => new RollView
        {
            Who = r.Who, For = r.For, Heading = r.Heading, Reasons = r.Reasons.ToList(), Kind = r.Kind, Private = r.Private,
            MarkAllowed = r.Check != null && !r.Check.MarkUsed, CoinAllowed = r.CoinAllowed, Important = r.Important, Bonus = r.Bonus, Target = r.Target,
        };

        public RollRequest ToRequest()
        {
            var r = new RollRequest { Who = Who, For = For, Heading = Heading, Kind = Kind, Private = Private, Bonus = Bonus, Target = Target, CoinAllowed = CoinAllowed, Important = Important };
            r.Reasons.AddRange(Reasons);
            return r;
        }
    }

    /// <summary>The host asks one friend: "roll" (their dice), "coins" (failed rolls), "redeclare" (refused actions), "growth" (one adventurer).</summary>
    public sealed class AskMsg
    {
        public int Id;
        public string Kind = "";
        public List<RollView> Rolls = new List<RollView>();
        public List<string> Who = new List<string>();
        public List<string> Refusals = new List<string>();
        public float Seconds;
    }

    public sealed class AnswerMsg
    {
        public int Id;
        /// <summary>roll: indexes whose veteran mark is used / coins: indexes rolled again / growth: the option index.</summary>
        public List<int> Picks = new List<int>();
        public List<Declaration> Declarations = new List<Declaration>();
    }

    /// <summary>The dice everyone watches roll (a secret die goes to its roller only).</summary>
    public sealed class DiceMsg
    {
        public List<RollView> Rolls = new List<RollView>();
        public List<int> Naturals = new List<int>();
    }

    /// <summary>Asking the storyteller (only the telling changes) or for a hint.</summary>
    public sealed class NightOrder { public string Text = ""; public bool Hint; }

    public sealed class ClueMsg { public string ClueId = ""; }

    /// <summary>The evening is over: the ending, the chronicle, and the last state.</summary>
    public sealed class EndMsg
    {
        public string Save = "";
        public EndingResult Ending = new EndingResult();
        public Chronicle? Chronicle;
        public List<string> Mine = new List<string>();
    }

    public static class OnlineView
    {
        public const int LogLines = 60;

        /// <summary>
        /// The saved table as one person may see it: only their own handouts (or shown ones), secret actions, personal goals and
        /// secret clues; the log cut to the recent lines; no dice state.
        /// </summary>
        public static SaveGame SaveFor(Table t, ICollection<string> mine)
        {
            var s = SaveGame.From(t);
            s.RngState = 0;
            s.Handouts = s.Handouts.Where(h => mine.Contains(h.Who) || h.Shown).ToList();
            s.Secrets = s.Secrets.Where(x => mine.Contains(x.Who)).ToList();
            s.PersonalGoals = s.PersonalGoals.Where(kv => mine.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            s.Clues = s.Clues
                .Select(kv => (kv.Key, who: kv.Value.Where(w => w == Play.World.Shown || mine.Contains(w)).ToList()))
                .Where(x => x.who.Count > 0).ToDictionary(x => x.Key, x => x.who);
            if (s.Log.Count > LogLines) s.Log = s.Log.Skip(s.Log.Count - LogLines).ToList();
            return s;
        }

        public static string Json(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings)!;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };
    }
}
