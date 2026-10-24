#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Ai
{
    /// <summary>
    /// One evening in progress: the story sheet, the world built from it, the party, and what the AI is reminded of every call
    /// (facts, rumours). The turn flow (stage 4) runs on this; the AI calls read it through SceneText.
    /// </summary>
    public sealed class Evening
    {
        public StoryDef Story { get; }
        public World World { get; } = new World();
        public List<Adventurer> Party { get; } = new List<Adventurer>();
        public Dictionary<string, Mechanism> Mechanisms { get; } = new Dictionary<string, Mechanism>();
        /// <summary>事実メモ: facts settled tonight (fate dice, small additions); never contradicted.</summary>
        public List<string> Facts { get; } = new List<string>();
        /// <summary>The display names of the places' foes by id (set by the table from the bestiary; not saved).</summary>
        public Dictionary<string, string> FoeNames { get; } = new Dictionary<string, string>();
        /// <summary>Rumours of the last three evenings.</summary>
        public List<string> Rumors { get; } = new List<string>();
        public Difficulty Difficulty { get; set; } = Difficulty.Standard;
        public int Turn { get; set; }
        public bool ClimaxStarted { get; set; }
        public Tension Tension { get; } = new Tension();
        public Hints Hints { get; } = new Hints();
        /// <summary>The last sentence of each of the last three narrations (the narrator avoids ending the same way).</summary>
        public List<string> LastEndings { get; } = new List<string>();
        /// <summary>The first evening of these players: the narrator slips in how to play.</summary>
        public bool FirstEvening { get; set; }
        /// <summary>The fight in progress, if any.</summary>
        public Combat? Combat { get; set; }

        public Evening(StoryDef story) { Story = story; }

        /// <summary>Builds tonight's world from the sheet: everyone at the start, people and items in place, clocks set.</summary>
        public static Evening Begin(StoryDef story, IEnumerable<Adventurer> party, Difficulty difficulty)
        {
            var e = new Evening(story) { Difficulty = difficulty };
            foreach (var a in party)
            {
                a.StartEvening(difficulty, story.Advance);
                e.Party.Add(a);
                e.World.Move(a.Id, story.Start);
            }
            e.World.Events.Add("visited:" + story.Start);
            foreach (var p in story.Places) e.World.Links[p.Id] = p.Links.Select(l => l.To).ToList();
            foreach (var p in story.People)
                e.World.Cast.People.Add(new Npc
                {
                    Id = p.Id, Name = p.Name, Portrait = p.Portrait, Location = p.At, Attitude = p.Attitude, Build = p.Build,
                    Role = p.CombatRole, Joins = p.Joins, Escort = p.Escort,
                });
            foreach (var i in story.Items)
            {
                e.World.ItemKinds[i.Item.Id] = i.Item.Kind;
                // "party": the client's charge, in the first adventurer's pack from the start (the holy lamp to deliver)
                if (i.At == "party" && e.Party.FirstOrDefault(a => a.Gear.Add(Carried(i))) is Adventurer bearer)
                {
                    e.World.PutItem(i.Item.Id, World.Pc(bearer.Id));
                    continue;
                }
                e.World.PutItem(i.Item.Id, i.At.StartsWith("npc:") ? World.NpcHolder(i.At.Substring(4)) : i.At);
            }
            foreach (var c in story.Clocks) e.World.Clocks.All.Add(new Clock(c.Def) { Visible = c.Visible.IsEmpty });
            foreach (var m in story.Mechanisms) e.Mechanisms[m.Id] = new Mechanism(m);
            return e;
        }

        static Item Carried(StoryItem i)
        {
            var it = i.Item.Clone();
            it.StoryId = i.Item.Id;
            return it;
        }

        public Adventurer? Find(string id) => Party.FirstOrDefault(a => a.Id == id);
        public string PlaceOf(Adventurer a) => World.PartyAt.TryGetValue(a.Id, out var p) ? p : Story.Start;
        public IEnumerable<Adventurer> At(string place) => Party.Where(a => PlaceOf(a) == place && a.Life != Life.Dead && !a.Withdrawn);
        public IEnumerable<Npc> PeopleAt(string place) =>
            World.Cast.People.Where(p => p.State != PersonState.Dead && (p.Location == place || (p.WithParty && At(place).Any())));
        public IEnumerable<StoryItem> ItemsAt(string place) => Story.Items.Where(i => World.WhereIs(i.Item.Id) == place);
        public IEnumerable<MechanismDef> MechanismsAt(string place) => Story.Mechanisms.Where(m => m.Place == place && !Mechanisms[m.Id].Spent);

        /// <summary>Clues that can be found here now: in this place (or with a person here), not yet known to the party, conditions met.</summary>
        public IEnumerable<Clue> CluesHere(string place) =>
            Story.Clues.Where(c =>
                (c.Person.Length > 0 ? PeopleAt(place).Any(p => p.Id == c.Person) : c.Place == place)
                && !(World.Clues.TryGetValue(c.Id, out var who) && who.Contains(World.Shown))
                && (c.Acts.Count == 0 || c.Acts.Contains(World.Act))
                && c.Requires.Holds(World));

        /// <summary>The ways out of a place and whether each is open.</summary>
        public IEnumerable<(Link link, bool open)> Ways(string place) =>
            (Story.PlaceOf(place)?.Links ?? new List<Link>()).Select(l => (l, l.Open.Holds(World) && OpenInAct(l.To)));

        /// <summary>
        /// A long story's places open with the acts: a place first used in act N can be reached from act N-1 on
        /// (the next act's places are the frontier; without this a party could walk to the last place in act 2).
        /// </summary>
        public bool OpenInAct(string place) =>
            !Story.Long || !(Story.PlaceOf(place) is Place p) || p.Acts.Count == 0 || p.Acts.Min() <= World.Act + 1;

        /// <summary>
        /// The way from one place to another over open links (the places passed, ending with the target), or null when there is none.
        /// Only places the party knows (visited) or the target itself may be passed through.
        /// </summary>
        public List<string>? PathTo(string from, string to)
        {
            if (from == to) return new List<string>();
            var prev = new Dictionary<string, string> { [from] = "" };
            var q = new Queue<string>();
            q.Enqueue(from);
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                foreach (var (link, open) in Ways(p))
                {
                    if (!open || prev.ContainsKey(link.To)) continue;
                    if (link.To != to && !World.Visited.Contains(link.To)) continue;
                    prev[link.To] = p;
                    if (link.To == to)
                    {
                        var path = new List<string> { to };
                        for (var x = p; x != from; x = prev[x]) path.Insert(0, x);
                        return path;
                    }
                    q.Enqueue(link.To);
                }
            }
            return null;
        }

        /// <summary>Places reachable from here over open links but not next door: known ones, and the next new place beyond them (for the AI's list of destinations).</summary>
        public IEnumerable<string> KnownFurther(string from) =>
            Story.Places.Select(p => p.Id).Where(v => v != from && !Ways(from).Any(w => w.link.To == v) && PathTo(from, v) != null);

        /// <summary>Clocks that became visible since the last look (their condition now holds).</summary>
        public List<Clock> RevealClocks()
        {
            var shown = new List<Clock>();
            foreach (var sc in Story.Clocks)
            {
                var c = World.Clocks.Find(sc.Def.Id)!;
                if (!c.Visible && sc.Visible.Holds(World)) { c.Visible = true; shown.Add(c); }
            }
            return shown;
        }

        /// <summary>Remembers the last sentence of a narration (three kept).</summary>
        public void NoteEnding(string narration)
        {
            var sentences = narration.Split(new[] { '。', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            if (sentences.Count == 0) return;
            LastEndings.Add(sentences[sentences.Count - 1] + "。");
            while (LastEndings.Count > 3) LastEndings.RemoveAt(0);
        }
    }
}
