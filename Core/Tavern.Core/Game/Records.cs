#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Tavern.Core.Content;
using Tavern.Core.Rules;

namespace Tavern.Core.Game
{
    /// <summary>One finished evening in the storyteller's chronicle (kept on every player's PC).</summary>
    public sealed class ChronicleEntry
    {
        public DateTime Date;
        public string Scenario = "", Teller = "", Ending = "";
        public List<string> Party = new List<string>();
        public List<string> Fallen = new List<string>();
        /// <summary>The storyteller's closing words of the evening.</summary>
        public string Story = "";

        public string EndingLabel => Ending == "success" ? "成功" : Ending == "bitter" ? "苦い成功" : "失敗";
    }

    public sealed class Chronicle
    {
        public List<ChronicleEntry> Entries = new List<ChronicleEntry>();

        public static Chronicle Load(string? json) => string.IsNullOrWhiteSpace(json) ? new Chronicle() : JsonConvert.DeserializeObject<Chronicle>(json!) ?? new Chronicle();
        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>The entry for an ended evening (the last narration is the storyteller's closing).</summary>
        public static ChronicleEntry From(TavernGame game, DateTime now) => new ChronicleEntry
        {
            Date = now,
            Scenario = game.Scenario.Title,
            Teller = game.Teller.Name,
            Ending = game.State.Ending ?? "",
            Party = game.Party.Select(p => $"{p.Name}（{Gm.Prompts.ClassLabel(p.ClassId)}）").ToList(),
            Fallen = game.Party.Where(p => p.State == LifeState.Dead).Select(p => p.Name).ToList(),
            Story = game.State.Log.LastOrDefault(e => e.Kind == LogKind.Narration)?.Text ?? "",
        };

        /// <summary>Adds the evening once (a resumed or replayed ending is not written twice).</summary>
        public bool Add(ChronicleEntry e)
        {
            if (Entries.Any(x => x.Scenario == e.Scenario && x.Story == e.Story)) return false;
            Entries.Add(e);
            return true;
        }
    }

    /// <summary>A character kept between evenings: the class basics plus titles and keepsakes ("carry over a little").</summary>
    public sealed class RosterEntry
    {
        /// <summary>Who this is, whatever the name (two adventurers may share one; a new one never takes over an old one's entry).</summary>
        public string Id = "";
        public string Name = "", ClassId = "", Background = "", Portrait = "";
        public List<string> Titles = new List<string>();
        public List<string> Keepsakes = new List<string>();
        public int Nights;
        public bool Dead;
        public string LastScenario = "";

        public const int MaxKeepsakes = 3;

        /// <summary>A fresh character for a new evening: stats back to the class basics, titles and keepsakes kept.</summary>
        public Character ToCharacter(ContentDb db)
        {
            var bg = Background;
            if (Titles.Count > 0) bg += $"（称号: {string.Join("・", Titles)}）";
            if (Nights > 0) bg += $"（これまでに {Nights} 晩の冒険を生き延びた）";
            var c = TavernGame.CreateCharacter(db, Name, ClassId, bg, Portrait);
            c.RosterId = Id;
            c.Items.AddRange(Keepsakes);
            return c;
        }
    }

    public sealed class Roster
    {
        public List<RosterEntry> Characters = new List<RosterEntry>();

        public static Roster Load(string? json)
        {
            var r = string.IsNullOrWhiteSpace(json) ? new Roster() : JsonConvert.DeserializeObject<Roster>(json!) ?? new Roster();
            foreach (var e in r.Characters.Where(e => e.Id.Length == 0)) { e.Id = NewId(); r.GaveIds = true; }   // entries from before ids
            return r;
        }
        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>Load gave ids to old entries: save it back so the ids stay the same.</summary>
        [JsonIgnore] public bool GaveIds;

        public IEnumerable<RosterEntry> Living => Characters.Where(c => !c.Dead);
        public IEnumerable<RosterEntry> Fallen => Characters.Where(c => c.Dead);

        /// <summary>
        /// This PC's entry for an adventurer of tonight: the one it was called from (by id), else a new entry. Never by name:
        /// a new adventurer who happens to share an old one's name must not inherit (or overwrite) the old one's keepsakes.
        /// </summary>
        RosterEntry EntryFor(Character c)
        {
            var e = c.RosterId.Length > 0 ? Characters.FirstOrDefault(x => x.Id == c.RosterId) : null;
            if (e != null) return e;
            e = new RosterEntry { Id = c.RosterId.Length > 0 ? c.RosterId : NewId(), Name = c.Name, ClassId = c.ClassId, Portrait = c.Portrait,
                Background = StripNotes(c.Background) };
            c.RosterId = e.Id;   // the same evening recorded again finds this entry
            Characters.Add(e);
            return e;
        }

        /// <summary>Death is final at once: an adventurer who died tonight is on the memorial even if the evening is never finished.</summary>
        public bool RecordDeaths(TavernGame game, Func<Character, bool> mine)
        {
            var changed = false;
            foreach (var c in game.Party.Where(mine).Where(c => c.State == LifeState.Dead))
            {
                var e = EntryFor(c);
                if (!e.Dead) { e.Dead = true; changed = true; }
            }
            return changed;
        }

        /// <summary>
        /// After an evening ends: this PC's characters are kept (or remembered as fallen). The background is stored without
        /// the title/night notes added for the evening, items become keepsakes (up to three), an ending gives a title.
        /// </summary>
        public void Record(TavernGame game, Func<Character, bool> mine)
        {
            foreach (var c in game.Party.Where(mine))
            {
                var stamp = game.Scenario.Id + game.State.Turn;
                var e = EntryFor(c);
                if (e.LastScenario == stamp) continue;   // already recorded this ending
                e.Name = c.Name;
                e.Background = StripNotes(c.Background);
                e.Nights++;
                e.LastScenario = stamp;
                e.Keepsakes = c.Items.Concat(e.Keepsakes).Distinct().Take(RosterEntry.MaxKeepsakes).ToList();
                var title = game.State.Ending switch
                {
                    "success" => $"{game.Scenario.Title}の英雄",
                    "bitter" => $"{game.Scenario.Title}の生還者",
                    _ => null,
                };
                if (title != null && c.State != LifeState.Dead && !e.Titles.Contains(title)) e.Titles.Add(title);
                if (c.State == LifeState.Dead) e.Dead = true;
            }
        }

        static string StripNotes(string bg)
        {
            var i = bg.IndexOf("（称号:", StringComparison.Ordinal);
            if (i < 0) i = bg.IndexOf("（これまでに", StringComparison.Ordinal);
            return i >= 0 ? bg.Substring(0, i) : bg;
        }
    }
}
