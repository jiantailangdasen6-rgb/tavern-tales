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
            Ai = AiSetup.Create(Flag("-demo"));
            SavePath = Arg("-savefile") ?? Path.Combine(Application.persistentDataPath, "evening.json");
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

            if (Flag("-continue") && File.Exists(SavePath)) ContinueEvening();
            else if (Flag("-quickstart")) StartEvening(PartyScreen.Preset(Db));
            else if (Flag("-partyscreen")) ShowParty();
            else ShowTitle();
        }

        void Update() { _table?.Update(); }

        public void ShowTitle()
        {
            _table = null;
            UiKit.Clear(Root);
            new TitleScreen(this).Build(Root);
        }

        public void ShowParty()
        {
            _table = null;
            UiKit.Clear(Root);
            new PartyScreen(this).Build(Root);
        }

        public void StartEvening(System.Collections.Generic.List<Tavern.Core.Rules.Character> party)
        {
            var game = TavernGame.New(Db, Scenario, Storyteller.Olwen, party, Ai.Everyday, Ai.Highlight, new Tavern.Core.Rules.SeededRandom((ulong)DateTime.Now.Ticks));
            ShowTable(game, opening: true);
        }

        public void ContinueEvening()
        {
            var save = SaveData.FromJson(File.ReadAllText(SavePath));
            var game = save.Load(Db, Scenario, Storyteller.Olwen, Ai.Everyday, Ai.Highlight, new Tavern.Core.Rules.SeededRandom((ulong)DateTime.Now.Ticks));
            ShowTable(game, opening: false);
        }

        void ShowTable(TavernGame game, bool opening)
        {
            UiKit.Clear(Root);
            _table = new TableScreen(this, game);
            _table.Build(Root, opening);
        }

        public bool HasSave => File.Exists(SavePath);

        public void Save(TavernGame game)
        {
            try { File.WriteAllText(SavePath, SaveData.From(game).ToJson()); }
            catch (Exception e) { Debug.LogWarning("[Save] " + e.Message); }
        }
    }

}
