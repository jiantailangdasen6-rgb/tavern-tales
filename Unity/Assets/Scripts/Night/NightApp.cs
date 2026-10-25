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
using UnityEngine.UI;

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
    /// flow in Tavern.Core.Flow. Alone on one PC, or with friends in a room (NightRoom): the host runs the table, friends mirror it.
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
        public NightAi Ai => _ai ??= NightAi.Create(AppMain.Flag("-demo"), AppMain.Flag("-testkey"), Store.CallsToday, AiChoice.Paid || AppMain.Flag("-paidkey"), AiChoice.Model);

        /// <summary>The host changed the AI in the tavern's settings: the next evening makes it anew.</summary>
        public void ResetAi() => _ai = null;

        /// <summary>The host changed the AI at the table: tonight's calls so far are saved, then the next one goes to the new AI.</summary>
        public void SwitchAi()
        {
            if (_ai == null) return;
            if (_ai.Meter != null) Store.SaveCalls(_ai.KeyName, _ai.Meter);
            _ai.Switch(AiChoice.Paid, AiChoice.Model);
        }
        public Difficulty Difficulty { get; set; } = Difficulty.Standard;

        NightRoom? _room;
        /// <summary>The friends' room (null when playing alone on this PC).</summary>
        public NightRoom? Room
        {
            get => _room;
            set { _room = value; if (value != null) value.Changed += () => { if (_room == value && _screen == "map") ShowRoom(); }; }
        }
        string _screen = "";
        /// <summary>A friend's mirror of the host's table.</summary>
        TableView? _guest;

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

        void Screen(string name)
        {
            App.Tick = null;
            _screen = name;
            UiKit.Clear(Root);
        }

        public void ShowParty() => ShowParty(RoomMode.Alone);

        /// <summary>Tonight's adventurers: up to four on this PC, or one's own before opening or joining a room.</summary>
        public void ShowParty(RoomMode mode)
        {
            Screen("party");
            new PartyBuilder(this, mode).Build(Root);
        }

        public void ShowMap(List<Adventurer> party)
        {
            Screen("map");
            new MapView(this, party).Build(Root);
        }

        /// <summary>The room's map: everyone's adventurer and pins; the host starts (09_Spec 11).</summary>
        public void ShowRoom()
        {
            var room = Room;
            if (room == null) { BackToTitle(); return; }
            Screen("map");
            new MapView(this, room.Players.Select(p => p.Adventurer.ToAdventurer(Classes)).ToList()).Build(Root);
            // tests: -startwhen N starts on the first story once N players are in
            if (room.Host && room.Phase == "map" && int.TryParse(AppMain.Arg("-startwhen"), out var n) && room.Players.Count >= n)
            {
                if (AppMain.Arg("-difficulty") is string d) room.Difficulty = d switch { "gentle" => Difficulty.Gentle, "harsh" => Difficulty.Harsh, _ => Difficulty.Standard };
                Begin(Stories.FirstOrDefault(s => s.Id == (AppMain.Arg("-story") ?? "grey_shrine")) ?? Stories[0], room.Party(), room.Difficulty);
            }
        }

        public void ShowWaiting(string text)
        {
            Screen("waiting");
            UiKit.Picture(Root, ArtLibrary.Get("bg_tavern"), 0, 0, 1920, 1080).color = new Color(0.3f, 0.26f, 0.22f);
            UiKit.Label(Root, text, 30, Theme.Ink, 260, 440, 1400, 200, serif: true, align: TextAnchor.UpperCenter).gameObject.AddComponent<Shadow>();
            UiKit.Rule(Root, 660, 420, 600, Theme.FrameDim);
        }

        public void ShowError(string message)
        {
            _guest = null;
            _screen = "title";
            App.Tick = null;
            App.ShowError(message);
        }

        /// <summary>Starts an evening: the world from the sheet, the handouts (one call), then the table.</summary>
        public async void Begin(StoryDef story, List<Adventurer> party, Difficulty difficulty)
        {
            Difficulty = difficulty;
            Room?.SetPhase("handouts");
            var e = Evening.Begin(story, party, difficulty);
            var tavern = Store.LoadTavern();
            e.Rumors.AddRange(tavern.RecentRumors);
            e.FirstEvening = Store.LoadChronicle().Count == 0;
            var table = NewTable(e, new SeededRandom((ulong)DateTime.Now.Ticks));
            Screen("handouts");
            // the story's opening on a parchment page while the storyteller writes the handouts
            UiKit.Picture(Root, ArtLibrary.Get("bg_tavern"), 0, 0, 1920, 1080).color = new Color(0.28f, 0.24f, 0.2f);
            var page = UiKit.Panel(Root, "Opening", 460, 170, 1000, 740, leather: false).transform;
            UiKit.Paper(page);
            UiKit.Heading(page, story.Title, 60, 50, 880, "ic_quill", paper: true, size: 36);
            UiKit.Label(page, story.Intro, 22, Theme.InkDark, 64, 136, 872, 460).Inked().Wrapped(868).lineSpacing = 1.25f;
            UiKit.Label(page, "（語り部がハンドアウトを用意している…）", 19, Theme.InkBrown, 64, 640, 872, 40, align: TextAnchor.UpperCenter).Inked();
            var wait = page.gameObject;
            if (AppMain.Flag("-holdopening")) await System.Threading.Tasks.Task.Delay(8000);   // screenshots of this page
            try { await table.DealHandoutsAsync(Ai.Bookends); }
            catch (Exception ex) { Debug.LogWarning("[Night] handouts: " + ex.Message); }
            if (wait != null) UnityEngine.Object.Destroy(wait);
            Room?.SetPhase("table");
            SaveEvening(table);
            ShowTable(table, opening: true);
        }

        public Table NewTable(Evening e, IRandom rng)
        {
            var view = new TableView(this);
            var t = new Table(e, Ai.Classifier, Ai.Narrator, view, rng, Enemies, Ai.Highlight);
            RecordAi(t);
            view.Attach(t);
            t.Save = SaveEvening;
            return t;
        }

        /// <summary>Every AI call of this evening goes to its play record (night_logs/..._ai.txt): what was asked, the answer, how long.</summary>
        void RecordAi(Table t)
        {
            var path = Store.AiRecordPath(t);
            NightAi.Recorder = (model, tool, messages, reply, error, took) => NightStore.AppendAi(path, AiEntry(model, tool, messages, reply, error, took));
        }

        static string AiEntry(string model, string? tool, IReadOnlyList<Tavern.Core.Gm.ChatMessage> messages, Tavern.Core.Gm.LlmReply? reply, string? error, TimeSpan took)
        {
            var b = new System.Text.StringBuilder();
            b.AppendLine($"=== {DateTime.Now:HH:mm:ss} {model} {tool} {took.TotalSeconds:0.0} 秒");
            if (messages.LastOrDefault(m => m.Role == Tavern.Core.Gm.Role.User) is Tavern.Core.Gm.ChatMessage asked) b.AppendLine("[入力]").AppendLine(asked.Content);
            if (error != null) b.AppendLine("[失敗] " + error);
            if (reply != null)
            {
                if (reply.Content.Length > 0) b.AppendLine("[返事]").AppendLine(reply.Content);
                foreach (var c in reply.ToolCalls) b.AppendLine($"[{c.Name}]").AppendLine(c.Arguments.ToString(Newtonsoft.Json.Formatting.None));
            }
            return b.AppendLine().ToString();
        }

        /// <summary>The host's save: the table, and in a room who plays whom (so the room can be reopened).</summary>
        public void SaveEvening(Table t)
        {
            Store.SaveEvening(t);
            if (Room is NightRoom room) { if (room.Host) Store.SaveSeats(room.Seats()); }
            else Store.ClearSeats();   // alone now: an old room's seats would reopen a room on "続きから"
        }

        void ShowTable(Table t, bool opening)
        {
            if (t.E.Started.Length == 0) t.E.Started = DateTime.Now.ToString("yyyyMMdd_HHmm");   // an evening saved before records had names
            RecordAi(t);
            var view = (TableView)t.Players;
            Screen("table");
            App.Tick = view.Update;
            view.Build(Root, opening);
        }

        /// <summary>Continues the saved evening (a turn decided but not told is told now). A room's evening opens the room again.</summary>
        public async void Continue()
        {
            var save = Store.LoadEvening();
            var story = save == null ? null : Stories.FirstOrDefault(s => s.Id == save.StoryId);
            if (save == null || story == null) { App.ShowTitle(); return; }
            var seats = Store.LoadSeats();
            if (seats != null && seats.HasFriends && Room == null)
            {
                ShowWaiting("部屋を開き直しています…");
                try { Room = await NightRoom.ResumeAsync(this, seats); }
                catch (Exception e) { ShowError("部屋を開けませんでした（ひとりで続けるなら、もう一度「続きから」）: " + e.Message); Store.ClearSeats(); return; }
            }
            var view = new TableView(this);
            var t = save.Load(story, Classes, Enemies, Ai.Classifier, Ai.Narrator, view, Ai.Highlight);
            view.Attach(t);
            t.Save = SaveEvening;
            ShowTable(t, opening: false);
        }

        // ---------------------------------------------------------------- a friend's side

        /// <summary>A friend: the table as the host sent it (the first one opens the table, later ones update it).</summary>
        public void OnGuestState(StateMsg m)
        {
            if (_guest != null && _screen == "table") { _guest.OnState(m); return; }
            var view = new TableView(this);
            var t = Mirror(m.Save, view);
            if (t == null) return;
            view.Attach(t);
            _guest = view;
            view.OnState(m, redraw: false);
            ShowTable(t, opening: t.E.Turn == 0 && t.Log.Count == 0);
        }

        /// <summary>A friend: the evening is over; their own adventurer goes to their roster, the story to their chronicle.</summary>
        public async void OnGuestEnd(EndMsg m)
        {
            var reading = _guest;   // the table still on screen: the friend finishes the last narration first
            var t = Mirror(m.Save, new TableView(this));
            if (t == null) return;
            t.Ending = m.Ending;
            if (m.Chronicle != null) Store.Record(t, m.Chronicle, m.Mine, guest: true);
            Room?.Close();   // the evening is over: the host closing the room later is no surprise
            Room = null;
            _guest = null;
            if (reading != null && _screen == "table") await reading.ReadyForEndingAsync();
            ShowEnding(t, m.Chronicle);
        }

        /// <summary>A read-only copy of the host's table (it never calls the AI).</summary>
        public Table? Mirror(string saveJson, TableView view)
        {
            var save = SaveGame.FromJson(saveJson);
            var story = Stories.FirstOrDefault(s => s.Id == save.StoryId);
            if (story == null) { Room?.Close(); ShowError($"ホストの物語（{save.StoryId}）がこの版にありません。最新の版にしてください"); return null; }
            var none = new NoLlm();
            return save.Load(story, Classes, Enemies, new Classifier(none), new Narrator(none), view);
        }

        public void ShowEnding(Table t, Chronicle? chronicle)
        {
            Screen("ending");
            new EndingView(this, t, chronicle).Build(Root);
        }

        public void BackToTitle()
        {
            Room?.Close();
            Room = null;
            _guest = null;
            _screen = "title";
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

    /// <summary>How the party screen is used: tonight's adventurers on this PC, or one's own before opening or joining a room.</summary>
    public enum RoomMode { Alone, Host, Join }
}
