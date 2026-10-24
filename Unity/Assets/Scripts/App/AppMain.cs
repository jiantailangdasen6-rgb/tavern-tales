#nullable enable
using System;
using System.IO;
using System.Linq;
using Tavern.Unity.Dice;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The whole app: one canvas, the title and the evening (Night). Command-line flags for automated checks:
    ///   -demo            canned AI answers (no quota used)
    ///   -quickstart      skip the title and party screens with a preset party (-partysize, -story, -difficulty)
    ///   -autoplay N      a bot plays N turns (-walk FILE: the walkthrough's lines)
    ///   -continue        load the saved evening at start
    ///   -savefile PATH   keep the save and records next to another file (tests)
    ///   -onlinehost / -onlinejoinfile FILE   rooms with a preset adventurer (-presetchar N, -playername S, -codefile, -startwhen N)
    /// plus DevShot's -screenshot / -shotat / -autoquit. -night is accepted (it was the new flow's switch) and changes nothing.
    /// </summary>
    public sealed class AppMain : MonoBehaviour
    {
        public static AppMain Instance { get; private set; } = null!;
        public Transform Root { get; private set; } = null!;
        public DiceRoller Dice { get; private set; } = null!;
        public string SavePath { get; private set; } = "";
        Tavern.Unity.Night.NightApp? _night;
        /// <summary>The evening: party, map, table and ending.</summary>
        public Tavern.Unity.Night.NightApp Night => _night ??= new Tavern.Unity.Night.NightApp(this);

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
            SavePath = Arg("-savefile") ?? Path.Combine(Application.persistentDataPath, "evening.json");
            // automated runs are silent: -autoquit / -mute, and the browser build opened with ?mute (tests in the browser pane)
            AudioListener.volume = Flag("-autoquit") || Flag("-mute") || WebPage.MutedByUrl ? 0f : 1f;   // loudness is set by AudioDirector.Level
        }

        void Start()
        {
            // Windows: the classic input module, through which uGUI input fields take Japanese IME text (with the Input System module
            // the text being converted and the converted text could both end up in the field). The browser gets no input through
            // the classic module at all, and types in HTML fields anyway (WebPage), so it keeps the Input System module.
            var es = new GameObject("EventSystem", typeof(EventSystem));
            if (WebPage.IsBrowser) es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            else es.AddComponent<StandaloneInputModule>();
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
            Dice.Bounced += strength => AudioDirector.Play("dice_land", 0.25f + 0.75f * strength);   // a knock each time the die hits the tray
            AudioDirector.Create(transform);
            FontRefresh.Create(transform);

            InviteLink.Register();
            WebPage.Setup();
            InviteCode = InviteLink.CodeFromArgs() ?? WebPage.CodeFromUrl();
            if (Flag("-onlinehost") || Arg("-onlinejoinfile") != null) StartCoroutine(AutoOnline());
            else if (Flag("-continue") && Night.Store.HasEvening) Night.Continue();
            else if (Flag("-quickstart")) QuickStart();
            else if (Flag("-map")) Night.ShowMap(Night.PresetParty(PartySize));
            else if (InviteCode != null) Night.ShowParty(Tavern.Unity.Night.RoomMode.Join);   // opened from an invite link
            else if (Flag("-partyscreen")) Night.ShowParty();
            else if (int.TryParse(Arg("-dicetest"), out var face))
            {
                UiKit.Box(Root, "Bg", Theme.Bg).rectTransform.Fill();
                UiKit.Picture(Root, Dice.Texture, 160, 90, 1600, 900);
                Dice.Roll(face, 1.2f);
            }
            else ShowTitle();
        }

        /// <summary>Per-frame work of the current screen (the table, the room); it clears itself when its screen is gone.</summary>
        public Action? Tick;

        /// <summary>The room code of the invite link the game was opened from (the join screen fills it in).</summary>
        public string? InviteCode { get; private set; }

        void Update() => Tick?.Invoke();

        void OnApplicationQuit() => Tavern.Unity.Net.NetSession.Leave();   // the room hears at once that this player left

        static int PartySize => int.TryParse(Arg("-partysize"), out var ps) ? ps : 3;

        /// <summary>-quickstart: a preset party on -story (default the shrine) at -difficulty.</summary>
        void QuickStart()
        {
            var story = Night.Stories.FirstOrDefault(s => s.Id == (Arg("-story") ?? "grey_shrine")) ?? Night.Stories[0];
            var d = Arg("-difficulty") switch { "gentle" => Tavern.Core.Play.Difficulty.Gentle, "harsh" => Tavern.Core.Play.Difficulty.Harsh, _ => Tavern.Core.Play.Difficulty.Standard };
            Night.Begin(story, Night.PresetParty(PartySize), d);
        }

        public void ShowTitle()
        {
            Tick = null;
            UiKit.Clear(Root);
            new TitleScreen(this).Build(Root);
        }

        public void ShowError(string message)
        {
            ShowTitle();
            var box = UiKit.Box(Root, "Error", new Color(0.24f, 0.04f, 0.03f, 0.9f), frame: true);
            box.rectTransform.Place(460, 900, 1000, 60);
            UiKit.Label(box.transform, message, 20, Theme.Hex("ffd9c9"), align: TextAnchor.MiddleCenter).rectTransform.Fill(6);
            Debug.LogWarning("[Online] " + message);
        }

        /// <summary>
        /// Tests of rooms (Tools/OnlineTest.ps1): -onlinehost (-codefile, -startwhen N) or -onlinejoinfile,
        /// each with a preset adventurer (-presetchar N) and a player name (-playername S).
        /// </summary>
        System.Collections.IEnumerator AutoOnline()
        {
            var preset = Night.PresetParty(4);
            var index = int.TryParse(Arg("-presetchar"), out var n) ? Mathf.Clamp(n, 0, preset.Count - 1) : 0;
            var name = Arg("-playername") ?? "旅人" + (index + 1);
            string? code = null;
            if (!Flag("-onlinehost"))
            {
                var file = Arg("-onlinejoinfile")!;
                while (!File.Exists(file) || new FileInfo(file).Length == 0) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(0.5f);
                code = File.ReadAllText(file).Trim();
            }
            OpenRoom(code, name, preset[index]);
        }

        async void OpenRoom(string? code, string name, Tavern.Core.Play.Adventurer mine)
        {
            try
            {
                Night.Room = code == null ? await Tavern.Unity.Night.NightRoom.HostAsync(Night, name, mine) : await Tavern.Unity.Night.NightRoom.JoinAsync(Night, code, name, mine);
                if (code == null && Arg("-codefile") is string file) File.WriteAllText(file, Night.Room!.Code);
                Night.ShowRoom();
            }
            catch (Exception e) { ShowError((code == null ? "部屋を作れなかった: " : "部屋に入れなかった: ") + e.Message); }
        }
    }
}
