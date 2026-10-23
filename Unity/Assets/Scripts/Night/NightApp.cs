#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Core.Story;
using Tavern.Unity.App;
using Tavern.Unity.Ui;
using UnityEngine;

namespace Tavern.Unity.Night
{
    /// <summary>A spot on the region map (world.json regions).</summary>
    public sealed class Region
    {
        public string Id = "", Name = "";
        public float X, Y;
    }

    /// <summary>
    /// The new evening (Docs/09_Spec.md, stage 5): party → map → handouts → table → ending, on the rules in Tavern.Core.Play and the
    /// flow in Tavern.Core.Flow. Solo on one PC for now; friends join in stage 6.
    /// </summary>
    public sealed class NightApp
    {
        public AppMain App { get; }
        public ClassBook Classes { get; }
        public Bestiary Enemies { get; }
        public List<StoryDef> Stories { get; } = new List<StoryDef>();
        public List<Region> Regions { get; } = new List<Region>();
        public NightStore Store { get; }
        NightAi? _ai;
        public NightAi Ai => _ai ??= NightAi.Create(AppMain.Flag("-demo"), AppMain.Flag("-testkey"));
        public Difficulty Difficulty { get; set; } = Difficulty.Standard;

        public NightApp(AppMain app)
        {
            App = app;
            Classes = ClassBook.Load(Text("Content/Rules/classes"));
            Enemies = Bestiary.Load(Text("Content/Rules/enemies"));
            foreach (var t in Resources.LoadAll<TextAsset>("Content/Stories"))
            {
                try { Stories.Add(StoryLoader.Load(t.text)); }
                catch (Exception e) { Debug.LogWarning($"[Night] story {t.name}: {e.Message}"); }
            }
            foreach (var r in (JArray?)JObject.Parse(Text("Content/world"))["regions"] ?? new JArray())
                Regions.Add(new Region { Id = (string)r["id"]!, Name = (string)r["name"]!, X = (float)r["x"]!, Y = (float)r["y"]! });
            Store = new NightStore(Path.GetDirectoryName(app.SavePath) ?? Application.persistentDataPath);
        }

        static string Text(string path) => Resources.Load<TextAsset>(path)?.text ?? throw new Exception("missing " + path);

        Transform Root => App.Root;

        public void ShowParty()
        {
            App.Tick = null;
            UiKit.Clear(Root);
            new PartyBuilder(this).Build(Root);
        }

        public void ShowMap(List<Adventurer> party)
        {
            App.Tick = null;
            UiKit.Clear(Root);
            new MapView(this, party).Build(Root);
        }

        /// <summary>Starts an evening: the world from the sheet, the handouts (one call), then the table.</summary>
        public async void Begin(StoryDef story, List<Adventurer> party, Difficulty difficulty)
        {
            Difficulty = difficulty;
            var e = Evening.Begin(story, party, difficulty);
            var tavern = Store.LoadTavern();
            e.Rumors.AddRange(tavern.RecentRumors);
            e.FirstEvening = Store.LoadChronicle().Count == 0;
            var table = NewTable(e, new SeededRandom((ulong)DateTime.Now.Ticks));
            UiKit.Clear(Root);
            var wait = UiKit.Label(Root, $"{story.Title}\n\n{story.Intro}\n\n（語り部がハンドアウトを用意している…）", 26, Theme.Ink, 360, 300, 1200, 500, serif: true, align: TextAnchor.UpperCenter);
            try { await table.DealHandoutsAsync(Ai.Bookends); }
            catch (Exception ex) { Debug.LogWarning("[Night] handouts: " + ex.Message); }
            if (wait != null) UnityEngine.Object.Destroy(wait.gameObject);
            Store.SaveEvening(table);
            ShowTable(table, opening: true);
        }

        public Table NewTable(Evening e, IRandom rng)
        {
            var view = new TableView(this);
            var t = new Table(e, Ai.Classifier, Ai.Narrator, view, rng, Enemies, Ai.Highlight);
            view.Attach(t);
            t.Save = Store.SaveEvening;
            return t;
        }

        void ShowTable(Table t, bool opening)
        {
            var view = (TableView)t.Players;
            App.Tick = view.Update;
            UiKit.Clear(Root);
            view.Build(Root, opening);
        }

        /// <summary>Continues the saved evening (a turn decided but not told is told now).</summary>
        public void Continue()
        {
            var save = Store.LoadEvening();
            var story = save == null ? null : Stories.FirstOrDefault(s => s.Id == save.StoryId);
            if (save == null || story == null) { App.ShowTitle(); return; }
            var view = new TableView(this);
            var t = save.Load(story, Classes, Enemies, Ai.Classifier, Ai.Narrator, view, Ai.Highlight);
            view.Attach(t);
            t.Save = Store.SaveEvening;
            ShowTable(t, opening: false);
        }

        public void ShowEnding(Table t, Chronicle? chronicle)
        {
            App.Tick = null;
            UiKit.Clear(Root);
            new EndingView(this, t, chronicle).Build(Root);
        }

        public void BackToTitle()
        {
            App.Tick = null;
            App.ShowTitle();
        }

        /// <summary>A ready-made party for quick starts and screenshots.</summary>
        public List<Adventurer> PresetParty(int size)
        {
            var presets = new[]
            {
                ("gareth", "warrior", "ガレス", "pc_warrior_m", "没落した騎士の家の三男。家名を取り戻したい"),
                ("elis", "mage", "エリス", "pc_mage_f", "魔法学院を追い出された。理由は語りたがらない"),
                ("rocco", "thief", "ロッコ", "pc_thief_m", "港町の孤児。お調子者で、光る物に目がない"),
                ("mina", "priest", "ミーナ", "pc_priest_f", "太陽神の神殿で育った。困っている人を放っておけない"),
            };
            return presets.Take(Mathf.Clamp(size, 1, 4)).Select(p => new Adventurer(Classes[p.Item2], p.Item3, p.Item1) { Portrait = p.Item4, Background = p.Item5 }).ToList();
        }
    }
}
