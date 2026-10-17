#nullable enable
using System;
using System.IO;
using System.Linq;
using Tavern.Core.Content;
using Tavern.Core.Game;
using Tavern.Core.Gm;
using Tavern.Unity.Dice;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The whole app: one canvas, three screens (title, party, table). Command-line flags for automated checks:
    ///   -demo            canned AI answers (no quota used)
    ///   -quickstart      skip the title and party screens with a preset party
    ///   -autoplay N      a bot plays N turns (suggestions outside fights, cards and attacks inside)
    ///   -continue        load the saved evening at start
    ///   -savefile PATH   use another save file (tests)
    /// plus DevShot's -screenshot / -shotat / -autoquit.
    /// </summary>
    public sealed class AppMain : MonoBehaviour
    {
        public static AppMain Instance { get; private set; } = null!;
        public ContentDb Db { get; private set; } = null!;
        public ScenarioDef Scenario { get; private set; } = null!;
        public StoryContent Story { get; private set; } = null!;
        public AiSetup.Backends Ai { get; private set; } = null!;
        public Transform Root { get; private set; } = null!;
        public DiceRoller Dice { get; private set; } = null!;
        public string SavePath { get; private set; } = "";
        TableScreen? _table;

        public static string? Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        public static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 60;
            Db = ContentDb.Load(Text("Content/classes"), Text("Content/cards"), Text("Content/enemies"));
            Scenario = ContentDb.LoadScenario(Text("Content/Scenarios/grey_shrine"));
            Story = StoryContent.Load(Text("Content/world"), Text("Content/art_catalog"), Text("Content/templates"));
            ScenarioPrompt.World = Story.World.Describe();
            Ai = AiSetup.Create(Flag("-demo"));
            SavePath = Arg("-savefile") ?? Path.Combine(Application.persistentDataPath, "evening.json");
            foreach (var g in LoadChronicle().Entries.GroupBy(e => e.Teller)) ScenarioPrompt.NightsByTeller[g.Key] = g.Count();
            AudioListener.volume = Flag("-autoquit") || Flag("-mute") ? 0f : 0.4f;
        }

        static string Text(string path) => Resources.Load<TextAsset>(path)?.text ?? throw new Exception("missing " + path);

        void Start()
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Everything is laid out in a fixed 1920x1080 box, letterboxed on other aspect ratios.
            var stage = UiKit.Rect("Stage", canvasGo.transform);
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(1920, 1080);
            canvasGo.AddComponent<StageFitter>().Stage = stage;
            Root = stage;
            Dice = DiceRoller.Create(transform);
            AudioDirector.Create(transform);

            if (Flag("-onlinehost") || Arg("-onlinejoinfile") != null) StartCoroutine(AutoOnline());
            else if (Flag("-continue") && File.Exists(SavePath)) ContinueEvening();
            else if (Flag("-quickstart")) StartEvening(PartyScreen.Preset(Db), Storyteller.Find(Arg("-teller")), Arg("-story") is string st ? new StoryChoice { Prepared = st == "prepared", TemplateId = st == "auto" || st == "prepared" ? null : st } : null);
            else if (Flag("-partyscreen")) ShowParty();
            else if (int.TryParse(Arg("-dicetest"), out var face))
            {
                UiKit.Box(Root, "Bg", Theme.Bg).rectTransform.Fill();
                UiKit.Picture(Root, Dice.Texture, 660, 240, 600, 600);
                Dice.Roll(face, 1.2f);
            }
            else ShowTitle();
        }

        void Update() { _table?.Update(); }

        public void ShowTitle()
        {
            _table = null;
            UiKit.Clear(Root);
            new TitleScreen(this).Build(Root);
        }

        public void ShowParty() => ShowParty(OnlineMode.None);

        public void ShowParty(OnlineMode mode)
        {
            _table = null;
            UiKit.Clear(Root);
            new PartyScreen(this, mode).Build(Root);
        }

        // ---------------------------------------------------------------- online

        public async void HostOnline(string playerName, Tavern.Core.Rules.Character character)
        {
            try
            {
                var code = await Tavern.Unity.Net.NetSession.HostAsync();
                if (Arg("-codefile") is string file) File.WriteAllText(file, code);
                UiKit.Clear(Root);
                new LobbyScreen(this, true, playerName, CharacterSave.From(character)).Build(Root);
            }
            catch (Exception e) { ShowError("部屋を作れなかった: " + e.Message); }
        }

        public async void JoinOnline(string code, string playerName, Tavern.Core.Rules.Character character)
        {
            try
            {
                await Tavern.Unity.Net.NetSession.JoinAsync(code);
                UiKit.Clear(Root);
                new LobbyScreen(this, false, playerName, CharacterSave.From(character)).Build(Root);
            }
            catch (Exception e) { ShowError("部屋に入れなかった: " + e.Message); }
        }

        public void EnterAsHost(TavernGame game, string me, System.Collections.Generic.Dictionary<string, ulong> players, bool opening = true)
        {
            UiKit.Clear(Root);
            _table = new TableScreen(this, game, NetRole.Host, me);
            _table.SetPlayers(players);
            _table.Build(Root, opening);
            if (!opening) _table.SendStateToAll();
        }

        /// <summary>Reopens a saved online evening as a new room (friends rejoin by name).</summary>
        public async void HostResume(TavernGame game)
        {
            try
            {
                var code = await Tavern.Unity.Net.NetSession.HostAsync();
                if (Arg("-codefile") is string file) File.WriteAllText(file, code);
                UiKit.Clear(Root);
                new ResumeLobbyScreen(this, game).Build(Root);
            }
            catch (Exception e) { ShowError("部屋を作れなかった: " + e.Message); }
        }

        public bool SaveIsOnline
        {
            get { try { return HasSave && SaveData.FromJson(File.ReadAllText(SavePath)).State.Owners.Count > 0; } catch { return false; } }
        }

        public void EnterAsFriend(TurnMsg first, string me)
        {
            UiKit.Clear(Root);
            _table = new TableScreen(this, Mirror.Build(first, Db, Scenario), NetRole.Client, me);
            _table.Build(Root, opening: false);
        }

        void ShowError(string message)
        {
            ShowTitle();
            var box = UiKit.Box(Root, "Error", new Color(0.24f, 0.04f, 0.03f, 0.9f), frame: true);
            box.rectTransform.Place(460, 900, 1000, 60);
            UiKit.Label(box.transform, message, 20, Theme.Hex("ffd9c9"), align: TextAnchor.MiddleCenter).rectTransform.Fill(6);
            Debug.LogWarning("[Online] " + message);
        }

        /// <summary>Test flags: -onlinehost / -onlinejoinfile with a preset character (-presetchar N, -playername S).</summary>
        System.Collections.IEnumerator AutoOnline()
        {
            var preset = PartyScreen.Preset(Db);
            var index = int.TryParse(Arg("-presetchar"), out var n) ? Mathf.Clamp(n, 0, preset.Count - 1) : 0;
            var name = Arg("-playername") ?? "旅人" + (index + 1);
            if (Flag("-onlinehost")) { HostOnline(name, preset[index]); yield break; }
            var file = Arg("-onlinejoinfile")!;
            while (!File.Exists(file) || new FileInfo(file).Length == 0) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(0.5f);
            JoinOnline(File.ReadAllText(file).Trim(), name, preset[index]);
        }

        public async void StartEvening(System.Collections.Generic.List<Tavern.Core.Rules.Character> party, Storyteller? teller = null, StoryChoice? story = null)
        {
            var scenario = await PrepareScenarioAsync(party, story);
            if (scenario == null) return;
            var game = TavernGame.New(Db, scenario, teller ?? Storyteller.Olwen, party, Ai.Everyday, Ai.Highlight, new Tavern.Core.Rules.SeededRandom((ulong)DateTime.Now.Ticks));
            ShowTable(game, opening: true);
        }

        /// <summary>
        /// The prepared scenario, or one the storyteller writes now (strong model, falls back to the everyday one).
        /// Shows a waiting screen meanwhile. Null if writing failed (the title shows why).
        /// </summary>
        public async System.Threading.Tasks.Task<ScenarioDef?> PrepareScenarioAsync(System.Collections.Generic.IReadOnlyList<Tavern.Core.Rules.Character> party, StoryChoice? story, bool fallBackToPrepared = false)
        {
            if (story == null || story.Prepared) return Scenario;
            UiKit.Clear(Root);
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(Root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            UiKit.Box(Root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.72f)).rectTransform.Fill();
            UiKit.Label(Root, "語り部が今宵の物語を考えている……", 44, Theme.Gold, 0, 470, 1920, 70, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(Root, "（長くても 1 分ほど）", 22, Theme.Dim, 0, 550, 1920, 40, align: TextAnchor.MiddleCenter);
            var rng = new Tavern.Core.Rules.SeededRandom((ulong)DateTime.Now.Ticks);
            var template = Story.Template(story.TemplateId) ?? Story.Templates[rng.Next(0, Story.Templates.Count)];
            try
            {
                // The strong model gets 40 s (FallbackLlm.Patience), then the everyday model writes the story.
                return await ScenarioGenerator.GenerateAsync(Ai.Highlight ?? Ai.Everyday, Story, Db, template, party, story.Request, rng);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Story] " + e);
                if (fallBackToPrepared) return Scenario;
                ShowError((AiNotices.Failed(e, Ai.LocalOnly(), DateTimeOffset.UtcNow, AiSetup.LocalOffset) ?? "物語を作れなかった（" + e.Message + "）。") + "用意された物語で遊ぶこともできます。");
                return null;
            }
        }

        public void ContinueEvening()
        {
            var save = SaveData.FromJson(File.ReadAllText(SavePath));
            var game = save.Load(Db, Scenarios.For(save.State, Scenario), Storyteller.Find(save.State.StorytellerId), Ai.Everyday, Ai.Highlight, new Tavern.Core.Rules.SeededRandom((ulong)DateTime.Now.Ticks));
            if (save.State.Owners.Count > 0) HostResume(game);
            else ShowTable(game, opening: false);
        }

        void ShowTable(TavernGame game, bool opening)
        {
            UiKit.Clear(Root);
            _table = new TableScreen(this, game);
            _table.Build(Root, opening);
        }

        public bool HasSave => File.Exists(SavePath);

        // The chronicle and the roster live next to the save (tests point them elsewhere with -savefile).
        string RecordPath(string name) => Path.Combine(Path.GetDirectoryName(SavePath) ?? Application.persistentDataPath, name);
        public Chronicle LoadChronicle() { try { var p = RecordPath("chronicle.json"); return Chronicle.Load(File.Exists(p) ? File.ReadAllText(p) : null); } catch { return new Chronicle(); } }
        public Roster LoadRoster() { try { var p = RecordPath("roster.json"); return Roster.Load(File.Exists(p) ? File.ReadAllText(p) : null); } catch { return new Roster(); } }
        public void SaveChronicle(Chronicle c) { try { File.WriteAllText(RecordPath("chronicle.json"), c.ToJson()); } catch (Exception e) { Debug.LogWarning("[Records] " + e.Message); } }
        public void SaveRoster(Roster r) { try { File.WriteAllText(RecordPath("roster.json"), r.ToJson()); } catch (Exception e) { Debug.LogWarning("[Records] " + e.Message); } }

        public void Save(TavernGame game)
        {
            try { File.WriteAllText(SavePath, SaveData.From(game).ToJson()); }
            catch (Exception e) { Debug.LogWarning("[Save] " + e.Message); }
        }
    }

}
