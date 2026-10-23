#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tavern.Core.Ai;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Flow
{
    public sealed class SavedAdventurer
    {
        public string Id = "", Name = "", ClassId = "", Background = "", Portrait = "", RosterId = "";
        public Dictionary<Ability, int> Growth = new Dictionary<Ability, int>();
        /// <summary>Indexes into the class's growth list.</summary>
        public List<int> Gained = new List<int>();
        public int MaxHp, Hp, MaxFocus, Focus, FateCoins, Silver, DeathFailures, PendingBoost, Ward;
        public Life Life;
        public bool Withdrawn;
        public List<VeteranMark> Marks = new List<VeteranMark>();
        public List<NamedTechnique> Techniques = new List<NamedTechnique>();
        public List<Item> Items = new List<Item>();
        public string MainHand = "", OffHand = "", Body = "", Trinket = "";
        public List<Status> Statuses = new List<Status>();

        public static SavedAdventurer From(Adventurer a) => new SavedAdventurer
        {
            Id = a.Id, Name = a.Name, ClassId = a.Class.Id, Background = a.Background, Portrait = a.Portrait, RosterId = a.RosterId,
            Growth = new Dictionary<Ability, int>(a.Growth), Gained = a.Gained.Select(g => a.Class.Growth.IndexOf(g)).ToList(),
            MaxHp = a.MaxHp, Hp = a.Hp, MaxFocus = a.MaxFocus, Focus = a.Focus, FateCoins = a.FateCoins, Silver = a.Silver,
            DeathFailures = a.DeathFailures, PendingBoost = a.PendingBoost, Ward = a.Ward, Life = a.Life, Withdrawn = a.Withdrawn,
            Marks = a.Marks.ToList(), Techniques = a.Techniques.ToList(), Items = a.Gear.Items.Select(i => i.Clone()).ToList(),
            MainHand = a.Gear.MainHand, OffHand = a.Gear.OffHand, Body = a.Gear.Body, Trinket = a.Gear.Trinket,
            Statuses = a.Statuses.List.ToList(),
        };

        public Adventurer ToAdventurer(ClassBook classes)
        {
            var a = new Adventurer(classes[ClassId], Name, Id) { Background = Background, Portrait = Portrait, RosterId = RosterId };
            foreach (var kv in Growth) a.Growth[kv.Key] = kv.Value;
            foreach (var i in Gained.Where(i => i >= 0 && i < a.Class.Growth.Count)) a.Gained.Add(a.Class.Growth[i]);
            var gear = new Gear();
            foreach (var it in Items) gear.Add(it);
            gear.Restore(MainHand, OffHand, Body, Trinket);
            a.Restore(MaxHp, Hp, MaxFocus, Focus, Life, DeathFailures, gear);
            a.FateCoins = FateCoins; a.Silver = Silver; a.PendingBoost = PendingBoost; a.Ward = Ward; a.Withdrawn = Withdrawn;
            a.Marks.AddRange(Marks); a.Techniques.AddRange(Techniques);
            a.Statuses.List.AddRange(Statuses);
            return a;
        }
    }

    public sealed class SavedClock
    {
        public string Id = "";
        public int Filled;
        public double Counted;
        public bool Visible, Fired, Paused;
        public List<string> Rewinds = new List<string>();
    }

    public sealed class SavedFighter
    {
        public string Id = "";
        public int Initiative;
        public bool Fled;
        /// <summary>For creatures (enemies, companions); adventurers come from the party.</summary>
        public Creature? Creature;
    }

    public sealed class SavedCombat
    {
        public List<SavedFighter> Order = new List<SavedFighter>();
        public int Round, Summoned;
        public bool Surprise;
        public List<string> BigMoveDue = new List<string>();
        public Dictionary<string, string> TauntBy = new Dictionary<string, string>();
        public Dictionary<string, int> TauntRounds = new Dictionary<string, int>();
    }

    /// <summary>
    /// An evening in progress, saved on the host as the story goes (09_Spec 14). Results are saved before the telling, so a crash and
    /// reload give the same results. A new format version means old files are moved to "old" and the game starts empty.
    /// </summary>
    public sealed class SaveGame
    {
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public string StoryId = "";
        public Difficulty Difficulty;
        public Telling Length;
        public int Turn, Act, RestedAct;
        public bool ClimaxStarted, UnexpectedUsed, FirstEvening;
        public string SolvedBy = "";
        public Phase Phase;
        public ulong RngState;
        public int TensionLeft, TensionUsed, HintStalled, HintLevel;
        public List<string> LastEndings = new List<string>(), Facts = new List<string>(), Rumors = new List<string>(), Log = new List<string>();
        public List<SavedAdventurer> Party = new List<SavedAdventurer>();
        public Dictionary<string, string> PartyAt = new Dictionary<string, string>(), ItemAt = new Dictionary<string, string>();
        public List<string> Events = new List<string>(), Convictions = new List<string>(), Visited = new List<string>();
        public Dictionary<string, List<string>> Clues = new Dictionary<string, List<string>>();
        public List<SavedClock> Clocks = new List<SavedClock>();
        public List<Npc> People = new List<Npc>();
        public Dictionary<string, int> MechanismsUsed = new Dictionary<string, int>();
        public List<SecretRecord> Secrets = new List<SecretRecord>();
        public Dictionary<string, string> PersonalGoals = new Dictionary<string, string>();
        public List<Handout> Handouts = new List<Handout>();
        public List<string> PendingFoes = new List<string>();
        public SavedCombat? Combat;
        /// <summary>The last turn's result lines when its telling did not finish (told again on load).</summary>
        public List<string> Untold = new List<string>();

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },

            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);
        public static SaveGame FromJson(string json) => JsonConvert.DeserializeObject<SaveGame>(json, Settings)!;

        /// <summary>Whether a saved file is in the current format (anything else goes to "old").</summary>
        public static bool IsCurrent(string json)
        {
            try { return (int?)JObject.Parse(json)["Version"] == CurrentVersion; }
            catch (JsonException) { return false; }
        }

        public static SaveGame From(Table t)
        {
            var e = t.E;
            var w = e.World;
            var s = new SaveGame
            {
                StoryId = e.Story.Id, Difficulty = e.Difficulty, Length = t.Length, Turn = e.Turn, Act = w.Act, RestedAct = t.RestedAct,
                ClimaxStarted = e.ClimaxStarted, UnexpectedUsed = t.UnexpectedUsed, FirstEvening = e.FirstEvening, SolvedBy = t.SolvedBy, Phase = t.Phase,
                RngState = (t.Rng as SeededRandom)?.State ?? 0,
                TensionLeft = e.Tension.Left, TensionUsed = e.Tension.Used, HintStalled = e.Hints.Stalled, HintLevel = e.Hints.Level,
                LastEndings = e.LastEndings.ToList(), Facts = e.Facts.ToList(), Rumors = e.Rumors.ToList(), Log = t.Log.ToList(),
                Party = e.Party.Select(SavedAdventurer.From).ToList(),
                PartyAt = new Dictionary<string, string>(w.PartyAt), ItemAt = new Dictionary<string, string>(w.ItemAt),
                Events = w.Events.ToList(), Convictions = w.Convictions.ToList(), Visited = w.Visited.ToList(),
                Clues = w.Clues.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                Clocks = w.Clocks.All.Select(c => new SavedClock { Id = c.Id, Filled = c.Filled, Counted = c.Counted, Visible = c.Visible, Fired = c.Fired, Paused = c.Paused, Rewinds = c.RewindsUsed.ToList() }).ToList(),
                People = w.Cast.People.Select(p => { var n = (Npc)p; return n; }).ToList(),
                MechanismsUsed = e.Mechanisms.ToDictionary(kv => kv.Key, kv => kv.Value.Used),
                Secrets = t.Secrets.ToList(), PersonalGoals = new Dictionary<string, string>(t.PersonalGoals), Handouts = t.Handouts.ToList(),
                PendingFoes = t.PendingFoes.ToList(), Untold = t.Untold.ToList(),
            };
            if (e.Combat is Combat c)
            {
                s.Combat = new SavedCombat { Round = c.Round, Surprise = c.SurpriseRound, Summoned = c.Summoned, BigMoveDue = c.BigMoveDue.ToList() };
                foreach (var f in c.Order) s.Combat.Order.Add(new SavedFighter { Id = f.Id, Initiative = f.Initiative, Fled = f.Fled, Creature = f.Creature });
                foreach (var kv in c.Taunts) { s.Combat.TauntBy[kv.Key] = kv.Value.by; s.Combat.TauntRounds[kv.Key] = kv.Value.rounds; }
            }
            return s;
        }

        /// <summary>Rebuilds the evening and the table (the AI parts and the players come from the caller).</summary>
        public Table Load(StoryDef story, ClassBook classes, Bestiary enemies, Classifier classifier, Narrator narrator, IPlayers players, Narrator? highlight = null)
        {
            var e = new Evening(story) { Difficulty = Difficulty, Turn = Turn, ClimaxStarted = ClimaxStarted, FirstEvening = FirstEvening };
            var w = e.World;
            w.Act = Act;
            foreach (var a in Party) e.Party.Add(a.ToAdventurer(classes));
            foreach (var kv in PartyAt) w.PartyAt[kv.Key] = kv.Value;
            foreach (var kv in ItemAt) w.ItemAt[kv.Key] = kv.Value;
            foreach (var i in story.Items) w.ItemKinds[i.Item.Id] = i.Item.Kind;
            foreach (var x in Events) w.Events.Add(x);
            foreach (var x in Convictions) w.Convictions.Add(x);
            foreach (var x in Visited) w.Visited.Add(x);
            foreach (var kv in Clues) w.Clues[kv.Key] = new HashSet<string>(kv.Value);
            foreach (var p in story.Places) w.Links[p.Id] = p.Links.Select(l => l.To).ToList();
            foreach (var sc in story.Clocks)
            {
                var c = new Clock(sc.Def);
                if (Clocks.FirstOrDefault(x => x.Id == sc.Def.Id) is SavedClock saved)
                {
                    c.Restore(saved.Filled, saved.Counted);
                    c.Visible = saved.Visible; c.Fired = saved.Fired; c.Paused = saved.Paused;
                    foreach (var r in saved.Rewinds) c.RewindsUsed.Add(r);
                }
                w.Clocks.All.Add(c);
            }
            w.Cast.People.AddRange(People);
            foreach (var m in story.Mechanisms)
            {
                var mech = new Mechanism(m);
                mech.Restore(MechanismsUsed.TryGetValue(m.Id, out var u) ? u : 0);
                e.Mechanisms[m.Id] = mech;
            }
            e.Facts.AddRange(Facts); e.Rumors.AddRange(Rumors); e.LastEndings.AddRange(LastEndings);
            e.Tension.Restore(TensionLeft, TensionUsed);
            e.Hints.Restore(HintStalled, HintLevel);
            var rng = new SeededRandom(RngState == 0 ? (ulong)DateTime.Now.Ticks : RngState);
            if (Combat is SavedCombat sc2)
            {
                var order = new List<Fighter>();
                foreach (var f in sc2.Order)
                {
                    Fighter? fighter = f.Creature != null ? new Fighter(f.Creature) : e.Find(f.Id) is Adventurer a ? new Fighter(a) : null;
                    if (fighter == null) continue;
                    fighter.Initiative = f.Initiative; fighter.Fled = f.Fled;
                    order.Add(fighter);
                    if (f.Creature != null && w.Cast.Find(f.Id) is Npc person) person.Fighting = f.Creature;
                }
                e.Combat = Play.Combat.Restore(order, sc2.Round, sc2.Surprise, sc2.BigMoveDue,
                    sc2.TauntBy.ToDictionary(kv => kv.Key, kv => (kv.Value, sc2.TauntRounds.TryGetValue(kv.Key, out var r) ? r : 1)), sc2.Summoned, Difficulty, rng);
            }
            var t = new Table(e, classifier, narrator, players, rng, enemies, highlight)
            {
                Phase = Phase, Length = Length, RestedAct = RestedAct, UnexpectedUsed = UnexpectedUsed, SolvedBy = SolvedBy,
            };
            t.Secrets.AddRange(Secrets);
            foreach (var kv in PersonalGoals) t.PersonalGoals[kv.Key] = kv.Value;
            t.Handouts.AddRange(Handouts);
            t.PendingFoes.AddRange(PendingFoes);
            t.Log.AddRange(Log);
            t.Untold.AddRange(Untold);
            return t;
        }

        /// <summary>
        /// A new version starts everything empty (09_Spec 14): files in the folder that are not in the current format are moved,
        /// not deleted, into an "old" folder next to them. Settings files are left alone by the caller.
        /// </summary>
        public static int MoveOldAway(string folder, IEnumerable<string> fileNames)
        {
            var moved = 0;
            var old = Path.Combine(folder, "old");
            foreach (var name in fileNames)
            {
                var path = Path.Combine(folder, name);
                if (!File.Exists(path) || IsCurrent(File.ReadAllText(path))) continue;
                Directory.CreateDirectory(old);
                var target = Path.Combine(old, name);
                if (File.Exists(target)) target = Path.Combine(old, Path.GetFileNameWithoutExtension(name) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(name));
                File.Move(path, target);
                moved++;
            }
            return moved;
        }
    }
}
