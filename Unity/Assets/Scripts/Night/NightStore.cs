#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using UnityEngine;

namespace Tavern.Unity.Night
{
    /// <summary>An adventurer kept between evenings (09_Spec 3): who they are, their veteran marks and named techniques.</summary>
    public sealed class RosterEntry
    {
        public string Id = "", Name = "", ClassId = "", Background = "", Portrait = "";
        public List<VeteranMark> Marks = new List<VeteranMark>();
        public List<NamedTechnique> Techniques = new List<NamedTechnique>();
        public bool Dead;
        /// <summary>The epitaph (the evening they died in).</summary>
        public string DiedIn = "";
        public int Evenings;
    }

    public sealed class ChronicleEntry
    {
        public DateTime Date;
        public string StoryId = "", StoryTitle = "", Title = "", Card = "", Text = "";
        public List<string> Party = new List<string>(), Dead = new List<string>(), Personal = new List<string>(), Reveal = new List<string>(), Rumors = new List<string>();
        /// <summary>"語られなかった晩": only the title is kept (the witch took the memory).</summary>
        public bool Untold;
    }

    public sealed class WallItem
    {
        /// <summary>trophy (a boss's memento) / item (something brought home) / portrait (an adventurer).</summary>
        public string Kind = "", Text = "", Who = "";
        public DateTime Date;
    }

    /// <summary>The tavern between evenings: rumours of the last three evenings, bonds, the wall, the stories played.</summary>
    public sealed class TavernState
    {
        public int Version = SaveGame.CurrentVersion;
        public List<List<string>> Rumors = new List<List<string>>();
        public List<Bond> Bonds = new List<Bond>();
        public List<WallItem> Wall = new List<WallItem>();
        /// <summary>Story id → endings ("成功: ガレス・エリス").</summary>
        public Dictionary<string, List<string>> Played = new Dictionary<string, List<string>>();
        public IEnumerable<string> RecentRumors => Rumors.Skip(Math.Max(0, Rumors.Count - 3)).SelectMany(r => r);
    }

    sealed class Versioned<T>
    {
        public int Version = SaveGame.CurrentVersion;
        public List<T> Items = new List<T>();
    }

    /// <summary>
    /// The host's files for the new version (09_Spec 14). On the first start of this version the old files are moved, not deleted,
    /// into "old"; everything starts empty. Settings (volume) are not here and stay.
    /// </summary>
    public sealed class NightStore
    {
        public string Folder { get; }
        public NightStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
            var moved = SaveGame.MoveOldAway(folder, new[] { "evening.json", "roster.json", "chronicle.json" });
            if (moved > 0) Debug.Log($"[Night] moved {moved} old file(s) to {Path.Combine(folder, "old")}");
        }

        string P(string name) => Path.Combine(Folder, name);
        public string EveningPath => P("night_evening.json");
        public bool HasEvening => File.Exists(EveningPath);

        public void SaveEvening(Table t) => Write(EveningPath, SaveGame.From(t).ToJson());
        public SaveGame? LoadEvening()
        {
            try { return HasEvening ? SaveGame.FromJson(File.ReadAllText(EveningPath)) : null; }
            catch (Exception e) { Debug.LogWarning("[Night] cannot read the evening: " + e.Message); return null; }
        }
        public void ClearEvening() { try { if (HasEvening) File.Delete(EveningPath); } catch (Exception e) { Debug.LogWarning(e.Message); } }

        public List<RosterEntry> LoadRoster() => ReadList<RosterEntry>(P("night_roster.json"));
        public void SaveRoster(List<RosterEntry> r) => WriteList(P("night_roster.json"), r);
        public List<ChronicleEntry> LoadChronicle() => ReadList<ChronicleEntry>(P("night_chronicle.json"));
        public void SaveChronicle(List<ChronicleEntry> c) => WriteList(P("night_chronicle.json"), c);

        public TavernState LoadTavern()
        {
            try
            {
                var path = P("night_tavern.json");
                return File.Exists(path) ? JsonConvert.DeserializeObject<TavernState>(File.ReadAllText(path)) ?? new TavernState() : new TavernState();
            }
            catch (Exception e) { Debug.LogWarning("[Night] tavern: " + e.Message); return new TavernState(); }
        }
        public void SaveTavern(TavernState t) => Write(P("night_tavern.json"), JsonConvert.SerializeObject(t, Formatting.Indented));

        sealed class Calls { public int Used; public DateTimeOffset ResetsAt; }

        /// <summary>The AI calls used today on this key (the meter starts from here; a passed reset time means a fresh day).</summary>
        public int CallsToday(string key)
        {
            try
            {
                var path = P($"night_calls_{key}.json");
                if (!File.Exists(path)) return 0;
                var c = JsonConvert.DeserializeObject<Calls>(File.ReadAllText(path));
                return c == null || DateTimeOffset.UtcNow >= c.ResetsAt ? 0 : c.Used;
            }
            catch (Exception) { return 0; }
        }

        public void SaveCalls(string key, Tavern.Core.Ai.CallMeter m) =>
            Write(P($"night_calls_{key}.json"), JsonConvert.SerializeObject(new Calls { Used = m.Used, ResetsAt = m.ResetsAt }));

        List<T> ReadList<T>(string path)
        {
            try { return File.Exists(path) ? JsonConvert.DeserializeObject<Versioned<T>>(File.ReadAllText(path))?.Items ?? new List<T>() : new List<T>(); }
            catch (Exception e) { Debug.LogWarning($"[Night] {Path.GetFileName(path)}: {e.Message}"); return new List<T>(); }
        }
        void WriteList<T>(string path, List<T> items) => Write(path, JsonConvert.SerializeObject(new Versioned<T> { Items = items }, Formatting.Indented));

        static void Write(string path, string json)
        {
            try { File.WriteAllText(path, json); App.WebPage.Flush(); }
            catch (Exception e) { Debug.LogWarning("[Night] cannot write " + path + ": " + e.Message); }
        }

        /// <summary>
        /// After an evening: the roster keeps everyone (the dead get an epitaph), the chronicle gets the story, the tavern its rumours,
        /// bonds, wall and the played mark.
        /// </summary>
        public void Record(Table t, Chronicle c)
        {
            var e = t.E;
            var end = t.Ending!;
            var roster = LoadRoster();
            foreach (var a in e.Party)
            {
                var r = roster.FirstOrDefault(x => x.Id == a.RosterId) ?? roster.FirstOrDefault(x => x.Id == a.Id);
                if (r == null) { r = new RosterEntry { Id = a.RosterId.Length > 0 ? a.RosterId : Guid.NewGuid().ToString("N") }; roster.Add(r); a.RosterId = r.Id; }
                r.Name = a.Name; r.ClassId = a.Class.Id; r.Background = a.Background; r.Portrait = a.Portrait;
                r.Marks = a.Marks.Select(m => new VeteranMark { Name = m.Name }).ToList();
                r.Techniques = a.Techniques.ToList();
                r.Evenings++;
                if (a.Life == Life.Dead) { r.Dead = true; r.DiedIn = e.Story.Title; }
            }
            SaveRoster(roster);

            var chron = LoadChronicle();
            chron.Add(new ChronicleEntry
            {
                Date = DateTime.Now, StoryId = e.Story.Id, StoryTitle = e.Story.Title, Title = c.Title, Card = EndingResult.Label(end.Card), Text = c.Story,
                Party = e.Party.Select(a => a.Name).ToList(), Dead = end.Dead.ToList(),
                Personal = end.Personal.Select(kv => $"{e.Find(kv.Key)?.Name}: {kv.Value.goal}（{(kv.Value.done ? "果たした" : "果たせなかった")}）").ToList(),
                Reveal = end.Reveal.Select(s => $"{e.Find(s.Who)?.Name}: {s.Text}{(s.Found ? "（見つかった）" : "")}").ToList(),
                Rumors = c.Rumors.ToList(),
            });
            SaveChronicle(chron);

            var tavern = LoadTavern();
            tavern.Rumors.Add(c.Rumors.ToList());
            while (tavern.Rumors.Count > 3) tavern.Rumors.RemoveAt(0);
            Bookends.AgeBonds(tavern.Bonds);
            tavern.Bonds.AddRange(c.Bonds);
            foreach (var a in e.Party)
            {
                foreach (var it in a.Gear.Items.Where(i => i.StoryId.Length > 0 || i.Grade != Grade.Plain || i.Kind == ItemKind.Treasure))
                    tavern.Wall.Add(new WallItem { Kind = "item", Text = it.Name, Who = a.Name, Date = DateTime.Now });
                if (a.Life == Life.Dead) tavern.Wall.Add(new WallItem { Kind = "portrait", Text = $"{a.Name}（{e.Story.Title}で没）", Who = a.Name, Date = DateTime.Now });
            }
            if (e.World.Events.Contains("boss_defeated"))
                foreach (var boss in e.Story.People.Where(p => p.Enemy.Length > 0 && e.World.Cast.Find(p.Id)?.State is PersonState.Down or PersonState.Dead))
                    tavern.Wall.Add(new WallItem { Kind = "trophy", Text = $"{boss.Name}の記念品", Who = string.Join("・", e.Party.Select(a => a.Name)), Date = DateTime.Now });
            if (!tavern.Played.TryGetValue(e.Story.Id, out var played)) tavern.Played[e.Story.Id] = played = new List<string>();
            played.Add($"{EndingResult.Label(end.Card)}: {string.Join("・", e.Party.Select(a => a.Name))}");
            SaveTavern(tavern);
            ClearEvening();
        }
    }
}
