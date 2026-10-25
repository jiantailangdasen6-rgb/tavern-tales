#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Core.Story;
using Tavern.Unity.App;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The table on the new flow (09_Spec 15): the act, place and clocks along the top; the party on the left; the scene with
    /// people, enemies and the dice in the middle; the narration on the right; declarations at the bottom. On one PC, the player
    /// writes every adventurer's declaration and sends them together. It is also the players' side of the table (IPlayers).
    /// </summary>
    public sealed partial class TableView
    {
        readonly NightApp _n;
        Table _t = null!;
        Transform _root = null!;
        Evening E => _t.E;

        // top
        Text _actText = null!, _clueButtonText = null!, _quotaText = null!;
        RectTransform _clockRow = null!;
        // party
        readonly List<PartyCard> _cards = new List<PartyCard>();
        // scene
        RawImage _sceneBg = null!;
        Text _placeText = null!, _busyText = null!;
        RectTransform _peopleRow = null!, _enemyRow = null!, _sceneBox = null!;
        GameObject _warn = null!;
        Text _warnText = null!;
        // log
        RectTransform _log = null!;
        ScrollRect _logScroll = null!;
        // declarations
        readonly List<Button> _tabs = new List<Button>();
        RectTransform _tabRow = null!;
        InputField _input = null!, _cover = null!;
        Button _declare = null!, _secretButton = null!;
        Text _declHeader = null!, _declNote = null!;
        int _selected;
        readonly Dictionary<string, string> _drafts = new Dictionary<string, string>();
        readonly HashSet<string> _secret = new HashSet<string>();
        readonly Dictionary<string, string> _covers = new Dictionary<string, string>();
        bool _busy;
        bool _ending;

        // narration typed out
        readonly Queue<(string who, string text, bool narration)> _pending = new Queue<(string, string, bool)>();
        Text? _typing;
        string _typingFull = "";
        float _typingPos;
        float _hold;

        // bots and screenshots
        int _autoTurns, _autoDone;
        /// <summary>The width of the writing on the storyteller's page (inside its light middle).</summary>
        const float LogW = 620;
        float _autoWait;
        readonly List<string> _walk = new List<string>();

        sealed class PartyCard
        {
            public string Id = "";
            public Image Frame = null!, Border = null!;
            public RawImage Portrait = null!;
            public Text Name = null!, Hp = null!, Focus = null!, Status = null!;
            public Image HpBar = null!, FocusBar = null!;
            public Button Write = null!, Leave = null!;
        }

        public TableView(NightApp n)
        {
            _n = n;
            _autoTurns = int.TryParse(AppMain.Arg("-autoplay"), out var a) ? a : 0;
            if (AppMain.Arg("-walk") is string w && File.Exists(w))
                _walk.AddRange(File.ReadAllLines(w).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")));
        }

        public void Attach(Table t) { _t = t; }

        bool Auto => _autoTurns > 0 || AppMain.Flag("-autoquit");
        readonly HashSet<string> _bossSeen = new HashSet<string>();
        List<Adventurer> Living => E.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn).ToList();
        Adventurer Selected { get { var mine = MyLiving; return mine.Count == 0 ? E.Party[0] : mine[Mathf.Clamp(_selected, 0, mine.Count - 1)]; } }

        // ================================================================ build

        public void Build(Transform root, bool opening)
        {
            _root = root;
            UiKit.Box(root, "Bg", Theme.Bg).rectTransform.Fill();
            BuildTop(root);
            BuildParty(root);
            BuildScene(root);
            BuildLog(root);
            BuildDeclare(root);
            foreach (var l in _t.Log.Skip(Math.Max(0, _t.Log.Count - 40))) AddLine(l.StartsWith("語り: ") ? l.Substring(4) : l, l.StartsWith("語り: ") ? Theme.Ink : Theme.Dim, l.StartsWith("語り: ") ? 20 : 15, serif: l.StartsWith("語り: "));
            Listen();
            if (opening)
            {
                Queue("語り部 オルウェン", Story.Intro, true);
                if (!Auto && !AppMain.Flag("-showdice")) ShowHandouts();
            }
            else if (_t.Untold.Count > 0 && !Guest) _ = RunAsync(() => _t.TellUntoldAsync());
            else if (_t.Phase == Phase.Break && !Guest) { if (_t.GrowthDue.Count > 0) ShowGrowth(); else ShowNextNight(); }
            if (AppMain.Flag("-showsheet")) ShowSheet(E.Party[0].Id);   // screenshots
            if (AppMain.Flag("-showclues")) ShowClues();
            if (AppMain.Flag("-showhandouts")) ShowHandouts();
            if (AppMain.Flag("-showrequests")) ShowRequests();
            if (AppMain.Flag("-showsettings")) ShowSettings();
            if (AppMain.Flag("-showending")) { PreviewEnding(); return; }
            if (AppMain.Flag("-showfight")) PreviewFight();
            if (AppMain.Flag("-showdice")) _ = PreviewDice(int.Parse(AppMain.Arg("-showdice") ?? "1"));
            RefreshAll();
            BroadcastState();
        }

        /// <summary>The host's save (a friend's PC keeps no copy of the host's evening).</summary>
        void Persist() { if (!Guest) _n.SaveEvening(_t); }

        StoryDef Story => E.Story;

        void BuildTop(Transform root)
        {
            // a leather strip with a gold edge (C8b)
            var top = UiKit.Rect("Top", root).Place(0, 0, 1920, 72);
            UiKit.Ground(top, "leather");
            UiKit.Box(top, "Shade", new Color(0, 0, 0, 0.35f)).rectTransform.Fill();
            UiKit.Box(top, "Edge", Theme.FrameGold).rectTransform.Place(0, 70, 1920, 2);
            UiKit.Title(top, "酒場の語り部", 30, Theme.FrameGold, 24, 15, 240, 42).horizontalOverflow = HorizontalWrapMode.Overflow;
            _actText = UiKit.Label(top, "", 19, Theme.Ink, 276, 24, 520, 30, serif: true);
            _actText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _clockRow = UiKit.Rect("Clocks", top).Place(806, 12, 400, 48);
            var x = 1214f;
            var clues = UiKit.IconButton(top, "ic_clues", "手がかり", ShowClues, x, 14, 146, 44, 16); _clueButtonText = clues.GetComponentInChildren<Text>(); x += 152;
            UiKit.IconButton(top, "ic_handout", "ハンドアウト", ShowHandouts, x, 14, 134, 44, 15); x += 140;
            UiKit.IconButton(top, "ic_settings", "設定", ShowSettings, x, 14, 84, 44, 15); x += 90;
            UiKit.IconButton(top, "ic_quill", "語り部への注文", ShowRequests, x, 14, 164, 44, 15, gold: true); x += 170;
            UiKit.IconButton(top, "ic_tavern", Guest ? "部屋を出る" : "酒場へ戻る", Quit, x, 14, 134, 44, 15);
        }

        /// <summary>The class's emblem (game-icons.net).</summary>
        /// <summary>A portrait as a card: dark ground, the picture cropped to fill, a thin gold frame.</summary>
        static RawImage PersonCard(Transform parent, Texture? tex, float x, float y, float w, float h, Color frame)
        {
            var ground = UiKit.Box(parent, "Card", Theme.Hex("0c0907"));
            ground.rectTransform.Place(x, y, w, h);
            var pic = UiKit.Picture(ground.transform, tex, 3, 3, w - 6, h - 6);
            if (tex != null) pic.uvRect = ArtLibrary.Cover(tex, w - 6, h - 6, 0.7f);
            UiKit.Frame(ground.transform, thin: true, frame, 7);
            return pic;
        }

        /// <summary>A name on a dark plate, centred under a card.</summary>
        static void NamePlate(Transform parent, string name, float x, float y, float w, Color color)
        {
            var plate = UiKit.Box(parent, "Plate", new Color(0.05f, 0.035f, 0.025f, 0.86f));
            // a long name (ロウフォードの薬屋) takes two lines rather than shrinking out of sight
            var two = name.Length * 13 > w - 18;
            plate.rectTransform.Place(x + 6, y, w - 12, two ? 40 : 24);
            var t = UiKit.Label(plate.transform, name, 13, color, 3, 3, w - 18, two ? 36 : 20, serif: true, align: TextAnchor.UpperCenter);
            t.lineSpacing = 0.95f;
        }

        static string ClassIcon(Adventurer a) => a.Class.Id switch { "warrior" => "ic_warrior", "mage" => "ic_mage", "thief" => "ic_thief", _ => "ic_priest" };

        void BuildParty(Transform root)
        {
            // illustration cards: the portrait over the whole card, name and gauges over a dark fade (C8b)
            for (int i = 0; i < E.Party.Count; i++)
            {
                var a = E.Party[i];
                var y = 84 + i * 246;
                var card = UiKit.Box(root, "Card", Color.black);
                card.rectTransform.Place(14, y, 260, 238);
                var c = new PartyCard { Id = a.Id, Frame = card };
                var t = card.transform;
                var tex = ArtLibrary.Get(a.Portrait);
                c.Portrait = UiKit.Picture(t, tex, 0, 0, 260, 238);
                c.Portrait.uvRect = ArtLibrary.Cover(tex, 260, 238, 0.64f);
                // the dark fade only under the writing, so more of the figure shows (owner 2026-10-25)
                UiKit.Fade(t).rectTransform.offsetMax = new Vector2(0, -88);
                c.Border = UiKit.Frame(t, thin: true, Theme.FrameDim, 8);
                UiKit.Icon(t, ClassIcon(a), 12, 12, 30);
                var id = a.Id;
                // the whole card picks this adventurer for the declaration; the scroll opens the sheet
                c.Write = card.gameObject.AddComponent<Button>();
                c.Write.transition = Selectable.Transition.None;
                c.Write.onClick.AddListener(() => { var i2 = MyLiving.FindIndex(x => x.Id == id); if (i2 >= 0) Select(i2); });
                var sheet = UiKit.Button(t, "", () => ShowSheet(id), 214, 10, 36, 36, 12);
                UiKit.Icon(sheet.transform, "ic_clues", 7, 7, 22);
                c.Status = UiKit.Label(t, "", 13, Theme.Ink, 12, 112, 236, 40);
                c.Status.gameObject.AddComponent<Shadow>().effectColor = Color.black;
                c.Name = UiKit.Title(t, "", 24, Theme.GoldLight, 12, 150, 236, 34);
                c.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
                c.Name.gameObject.AddComponent<Shadow>().effectColor = Color.black;
                UiKit.Icon(t, "ic_life", 12, 190, 16, Theme.Hex("d8473a"));
                c.HpBar = UiKit.Bar(t, 34, 190, 214, 16, Theme.Red);
                c.Hp = UiKit.Label(t, "", 12, Theme.Hex("fff3d6"), 34, 190, 208, 16, align: TextAnchor.MiddleRight);
                c.Hp.font = Theme.Number;
                UiKit.Icon(t, "ic_focus", 12, 212, 16, Theme.Hex("6e9eea"));
                c.FocusBar = UiKit.Bar(t, 34, 212, 214, 16, Theme.Blue);
                c.Focus = UiKit.Label(t, "", 12, Theme.Hex("fff3d6"), 34, 212, 208, 16, align: TextAnchor.MiddleRight);
                c.Focus.font = Theme.Number;
                c.Leave = UiKit.Button(t, "今夜は抜けた", () => Withdraw(id), 120, 56, 128, 34, 14);
                _cards.Add(c);
            }
        }

        void BuildScene(Transform root)
        {
            var panel = UiKit.Panel(root, "Scene", 284, 84, 840, 554, leather: false).transform;
            _sceneBox = UiKit.Rect("Box", panel).Fill(10);
            _sceneBox.SetAsFirstSibling();   // under the panel's frame
            _sceneBg = UiKit.Picture(_sceneBox, null, 0, 0, 820, 534);
            _sceneBg.rectTransform.Fill();
            var strip = UiKit.Box(_sceneBox, "PlaceStrip", new Color(0.03f, 0.02f, 0.01f, 0.72f));
            strip.rectTransform.Place(0, 0, 820, 46);
            _placeText = UiKit.Title(_sceneBox, "", 24, Theme.GoldLight, 16, 8, 600, 34);
            _placeText.gameObject.AddComponent<Shadow>();
            _peopleRow = UiKit.Rect("People", _sceneBox).Place(820 - 8 - 4 * 106, 58, 4 * 106, 170);
            _enemyRow = UiKit.Rect("Enemies", _sceneBox).Place(10, 330, 808, 210);
            _warn = UiKit.Box(_sceneBox, "Warn", new Color(0.35f, 0.08f, 0.05f, 0.9f), frame: true).gameObject;
            ((RectTransform)_warn.transform).Place(150, 260, 528, 56);
            _warnText = UiKit.Label(_warn.transform, "", 18, Theme.GoldLight, align: TextAnchor.MiddleCenter);
            _warnText.rectTransform.Fill(6);
            _busyText = UiKit.Label(_sceneBox, "", 18, Theme.GoldLight, 12, 516, 800, 28);
            _busyText.gameObject.AddComponent<Shadow>();
            BuildDice(_sceneBox);
        }

        void BuildLog(Transform root)
        {
            // the storyteller's book: a parchment page in the gold frame (C8b)
            var panel = UiKit.Panel(root, "Log", 1134, 84, 772, 984, leather: false).transform;
            var page = UiKit.Rect("Page", panel).Fill(12);
            page.SetAsFirstSibling();
            UiKit.Ground(page, "parchment", 90);
            // a pale wash over the writing area keeps the mottled paper from fighting the small text; all writing stays on its
            // flat light middle (from 60 px in), off the darker burnt edges (owner 2026-10-25)
            UiKit.Soft(page, new Color(0.95f, 0.89f, 0.75f, 0.55f), 26, fade: 34);
            PersonCard(page, ArtLibrary.Get("pt_olwen"), 64, 62, 48, 48, Theme.FrameDim);   // the storyteller Olwen, by the book
            UiKit.Title(page, "語り部の書", 26, Theme.InkRed, 124, 68, 240, 36).Inked();
            _quotaText = UiKit.Label(page, "", 14, Theme.InkBrown, 330, 78, 354, 24, align: TextAnchor.UpperRight).Inked();
            _log = UiKit.Scroll(page, 64, 122, LogW, 776, out _logScroll, spacing: 12);
            var click = panel.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(FinishTyping);
        }

        void BuildDeclare(Transform root)
        {
            var panel = UiKit.Panel(root, "Declare", 284, 648, 840, 420, leather: false).transform;
            var hide = UiKit.Rect("Leather", panel).Fill(10);
            hide.SetAsFirstSibling();
            UiKit.Ground(hide, "leather");
            UiKit.Box(hide, "Shade", new Color(0, 0, 0, 0.3f)).rectTransform.Fill();
            _tabRow = UiKit.Rect("Tabs", panel).Place(14, 10, 812, 44);
            _declHeader = UiKit.Label(panel, "", 18, Theme.GoldLight, 14, 60, 640, 28);
            _input = UiKit.Input(panel, "自由に行動を書く…（例: 祭壇の燭台を調べて、【柱蹴りの一閃】で骨の兵士の首を狙う）", 14, 92, 620, 168, 18, multiline: true);
            _input.characterLimit = 200;
            _input.onValueChanged.AddListener(v => { if (MyLiving.Count > 0) _drafts[Selected.Id] = v; RefreshDeclState(); });
            _secretButton = UiKit.Button(panel, "内緒にする", ToggleSecret, 14, 268, 140, 40, 16);
            _cover = UiKit.Input(panel, "表向き: （内緒のとき、ほかの人に見える行動）", 162, 268, 472, 40, 15);
            _cover.onValueChanged.AddListener(v => { if (MyLiving.Count > 0) _covers[Selected.Id] = v; });
            _declNote = UiKit.Label(panel, "", 14, Theme.Dim, 14, 316, 620, 90);
            _declare = UiKit.Button(panel, "行動を\n宣言する", Declare, 648, 92, 178, 216, 22, gold: true);
            UiKit.Icon(_declare.transform, "ic_seal", 51, 26, 76, Theme.GoldLight);
            var dt = _declare.GetComponentInChildren<Text>();
            dt.font = Theme.Display;
            dt.rectTransform.offsetMax = new Vector2(dt.rectTransform.offsetMax.x, -104);
        }

        // ================================================================ refresh

        void RefreshAll()
        {
            var act = Story.Acts.FirstOrDefault(a => a.N == E.World.Act);
            var here = E.World.PlacesOfParty.FirstOrDefault() ?? Story.Start;
            var place = Story.PlaceOf(here);
            _actText.text = $"第{E.World.Act}幕「{act?.Title}」 — {place?.Name}" + (E.Tension.On ? "　<color=#c4553f>緊迫</color>" : "");
            _clueButtonText.text = $"手がかり {E.World.SharedClues} / {Story.Clues.Count}";
            _quotaText.text = (Guest ? "" : _n.Ai.QuotaText()) + (Online ? $"　部屋 {Room!.Code}" : "") + (E.World.PlacesOfParty.Distinct().Count() > 1 ? "　一行は分かれている" : "");
            RefreshClocks();
            RefreshCards();
            RefreshScene(place);
            RefreshTabs();
            RefreshDeclState();
            PlayMusic(place);
        }

        void RefreshClocks()
        {
            UiKit.Clear(_clockRow);
            // each clock: a candle (danger) or an hourglass (good), its name and how full it is
            var clocks = E.World.Clocks.All.Where(c => c.Visible && c.RunsIn(E.World.Act)).ToList();
            var w = clocks.Count == 0 ? 0 : Mathf.Min(200, 400f / clocks.Count);
            for (int i = 0; i < clocks.Count; i++)
            {
                var c = clocks[i];
                var x = i * w;
                UiKit.Icon(_clockRow, c.Def.Good ? "ic_hourglass" : "ic_candle", x, 8, 30, c.Def.Good ? Theme.Hex("a9c98a") : Theme.FrameGold);
                var name = UiKit.Label(_clockRow, c.Def.Name, 14, Theme.Ink, x + 34, 2, w - 38, 22, serif: true);
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                var n = UiKit.Label(_clockRow, $"{c.Filled} / {c.Def.Segments}", 16, c.Def.Good ? Theme.Hex("a9c98a") : Theme.Hex("f08a7c"), x + 34, 24, w - 38, 22);
                n.font = Theme.Number;
            }
        }

        void RefreshCards()
        {
            foreach (var c in _cards)
            {
                var a = E.Find(c.Id)!;
                c.Name.text = $"{a.Name}<size=14>　{a.Class.Name}{(Online ? "・" + OwnerName(a.Id) : "")}</size>";
                c.Hp.text = $"{a.Hp}/{a.MaxHp}";
                c.HpBar.fillAmount = a.MaxHp == 0 ? 0 : (float)a.Hp / a.MaxHp;
                c.Focus.text = $"{a.Focus}/{a.MaxFocus}";
                c.FocusBar.fillAmount = a.MaxFocus == 0 ? 0 : (float)a.Focus / a.MaxFocus;
                var bits = new List<string>
                {
                    $"コイン {new string('●', a.FateCoins)}{(a.FateCoins == 0 ? "なし" : "")}　印 {new string('✦', a.MarksLeft)}{new string('✧', a.Marks.Count - a.MarksLeft)}",
                };
                if (a.Life == Life.Down) bits.Add($"<color=#c4553f>倒れている（死のサイコロ {a.DeathFailures}/3）</color>");
                else if (a.Life == Life.Dead) bits.Add("<color=#c4553f>死亡（聞くだけ）</color>");
                if (a.Statuses.List.Count > 0) bits.Add(string.Join("・", a.Statuses.List.Select(s => s.Kind.Label())));
                if (E.World.PlacesOfParty.Distinct().Count() > 1) bits.Add("@" + Story.PlaceOf(E.PlaceOf(a))?.Name);
                if (a.Withdrawn) bits.Add("今夜は抜けた");
                else if (!Online) bits.Add(_drafts.TryGetValue(a.Id, out var d) && d.Trim().Length > 0 ? "<color=#7a9a3a>宣言を書いた</color>" : "考え中");
                else if (_ready.Contains(a.Id)) bits.Add("<color=#7a9a3a>宣言済み</color>" + (DeclarationShown(a.Id) is string said ? $"「{(said.Length > 30 ? said.Substring(0, 30) + "…" : said)}」" : ""));
                else bits.Add(Away(a.Id) ? "<color=#c4553f>接続が切れている</color>" : "考え中");
                // the coin line first, then the state (both short: they sit over the portrait)
                c.Status.text = bits[0] + "\n" + string.Join("・", bits.Skip(1));
                c.Write.interactable = IsMine(a.Id) && !a.Withdrawn && a.Life != Life.Dead;
                c.Leave.gameObject.SetActive(HostOnline && Away(a.Id) && !a.Withdrawn && a.Life != Life.Dead);
                var chosen = MyLiving.Count > 0 && Selected.Id == a.Id;
                c.Border.color = a.Life == Life.Dead ? Theme.Hex("4a4038") : chosen ? Theme.Hex("f7d682") : Theme.FrameDim;
                c.Portrait.color = a.Life == Life.Dead ? new Color(0.35f, 0.35f, 0.35f) : chosen ? Color.white : new Color(0.86f, 0.84f, 0.8f);
            }
        }

        void RefreshScene(Place? place)
        {
            _sceneBg.texture = ArtLibrary.Get(place?.Art ?? "bg_tavern");
            _placeText.text = place?.Name ?? "";
            UiKit.Clear(_peopleRow);
            var here = place?.Id ?? Story.Start;
            // the people here as framed cards along the right edge, a name plate under each
            var x = 4 * 106f;
            foreach (var p in E.PeopleAt(here).Where(p => E.Story.PersonOf(p.Id)?.Enemy.Length == 0 || p.Fighting == null).Take(4))
            {
                x -= 106;
                var card = PersonCard(_peopleRow, ArtLibrary.Get(ArtLibrary.Has(p.Portrait) ? p.Portrait : "pt_youth"), x, 0, 100, 134, Theme.FrameDim);
                if (p.WithParty) card.color = new Color(1, 1, 1, 0.85f);
                NamePlate(_peopleRow, p.Name, x - 4, 138, 108, Theme.GoldLight);
            }
            UiKit.Clear(_enemyRow);
            var combat = E.Combat;
            if (combat != null)
            {
                // foes as framed cards along the bottom, each with a name plate and an HP gauge
                var foes = combat.Foes.Where(f => !f.Left && !f.Killed).ToList();
                // a boss stands in the middle, larger, under a red glow and a tag; its first sight gets an entrance (owner 2026-10-25)
                var big = foes.Any(f => f.Boss);
                if (big)
                {
                    var rest = foes.Where(f => !f.Boss).ToList();
                    foes = rest.Take(rest.Count / 2).Concat(foes.Where(f => f.Boss)).Concat(rest.Skip(rest.Count / 2)).ToList();
                }
                const float BossW = 196, BossH = 262;
                var bosses = big ? foes.Count(f => f.Boss) : 0;
                var w = Mathf.Min(150, (800f - bosses * BossW) / Mathf.Max(1, foes.Count - bosses));
                var h = Mathf.Min(190, w * 1.3f);   // tall enough that the art keeps its lower part
                var fx = big ? (808 - bosses * BossW - (foes.Count - bosses) * w) / 2 : 0;
                for (int i = 0; i < foes.Count; i++)
                {
                    var f = foes[i];
                    var art = Enemies(f.Source)?.Art ?? f.Art;
                    var boss = big && f.Boss;
                    var (cw, ch) = boss ? (BossW, BossH) : (w, h);
                    var y = 210 - ch - 48;
                    if (boss) UiKit.Soft(UiKit.Rect("Glow", _enemyRow).Place(fx - 18, y - 18, cw + 36, ch + 36), new Color(0.85f, 0.18f, 0.08f, 0.55f), 0, 26);
                    var card = PersonCard(_enemyRow, ArtLibrary.Get(art), fx + 4, y, cw - 8, ch, boss ? Theme.Hex("f2c45a") : f.Boss ? Theme.Hex("e0583f") : Theme.Hex("a8463a"));
                    if (f.Down) card.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
                    if (boss)
                    {
                        UiKit.Frame(card.transform.parent, thin: false, Theme.Hex("b8321e"), 12);
                        var tag = UiKit.Box(_enemyRow, "BossTag", new Color(0.5f, 0.07f, 0.04f, 0.96f), frame: true);
                        tag.rectTransform.Place(fx + cw / 2 - 46, y - 16, 92, 30);
                        UiKit.Title(tag.transform, "ボス", 18, Theme.GoldLight, 0, 2, 92, 26, TextAnchor.UpperCenter);
                    }
                    NamePlate(_enemyRow, f.Name, fx, 210 - 46, cw, boss ? Theme.GoldLight : Theme.Hex("ffc4b6"));
                    var bar = UiKit.Bar(_enemyRow, fx + 8, 210 - 20, cw - 16, boss ? 16 : 12, Theme.Red);
                    bar.fillAmount = f.MaxHp == 0 ? 0 : (float)f.Hp / f.MaxHp;
                    fx += cw;
                    if (boss && _bossSeen.Add(f.Id) && !AppMain.Flag("-nocutin")) ShowBossEntrance(f, art);
                }
                var due = combat.BigMoveDue.Select(id => combat.Find(id)?.Creature).FirstOrDefault(c => c?.BigMove != null);
                _warn.SetActive(due != null);
                if (due != null) _warnText.text = $"{due.BigMove!.Warning}\n次の手番に大技「{due.BigMove.Name}」";
            }
            else _warn.SetActive(false);
        }

        EnemyDef? Enemies(string id) => _n.Enemies.Has(id) ? _n.Enemies[id] : null;

        /// <summary>A boss's entrance: its picture large over the darkened scene, its name on a red band; a click or a few seconds closes it.</summary>
        async void ShowBossEntrance(Creature foe, string art)
        {
            var layer = UiKit.Box(_sceneBox, "BossEntrance", new Color(0.02f, 0.01f, 0.01f, 0.8f));
            layer.rectTransform.Fill();
            var band = UiKit.Box(layer.transform, "Band", new Color(0.42f, 0.06f, 0.04f, 0.92f));
            band.rectTransform.Place(0, 200, 820, 132);
            UiKit.Frame(band.transform, thin: true, Theme.Hex("f2c45a"), 6);
            UiKit.Soft(UiKit.Rect("Glow", layer.transform).Place(36, 22, 316, 490), new Color(0.9f, 0.2f, 0.08f, 0.6f), 0, 30);
            var card = PersonCard(layer.transform, ArtLibrary.Get(art), 60, 46, 268, 442, Theme.Hex("f2c45a"));
            UiKit.Frame(card.transform.parent, thin: false, Theme.Hex("b8321e"), 14);
            UiKit.Label(layer.transform, "― 立ちはだかる者 ―", 18, Theme.Hex("ffc4b6"), 360, 214, 440, 28, serif: true);
            var name = UiKit.Title(layer.transform, foe.Name, 48, Theme.GoldLight, 360, 246, 450, 64);
            name.gameObject.AddComponent<Shadow>();
            var go = layer.gameObject;
            var click = go.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => UnityEngine.Object.Destroy(go));
            await Task.Delay(4000);
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        void RefreshTabs()
        {
            UiKit.Clear(_tabRow);
            _tabs.Clear();
            var living = MyLiving;
            var w = Mathf.Min(200, 812f / Mathf.Max(1, living.Count));
            for (int i = 0; i < living.Count; i++)
            {
                var idx = i;
                var a = living[i];
                var b = UiKit.Button(_tabRow, a.Name + (a.Life == Life.Down ? "（倒）" : ""), () => Select(idx), i * w, 0, w - 6, 42, 17, gold: idx == _selected);
                _tabs.Add(b);
            }
        }

        void Select(int i)
        {
            _selected = i;
            var a = Selected;
            _input.SetTextWithoutNotify(_drafts.TryGetValue(a.Id, out var d) ? d : "");
            _cover.SetTextWithoutNotify(_covers.TryGetValue(a.Id, out var c) ? c : "");
            RefreshTabs();
            RefreshCards();
            RefreshDeclState();
        }

        void RefreshDeclState()
        {
            if (MyLiving.Count == 0) { RefreshWatching(); return; }
            var a = Selected;
            var phase = _t.Phase switch { Phase.Combat => "戦闘中（判定の要る行動は 1 つ）", Phase.Afterword => "その後の宣言（最後の 1 回）", Phase.Break => "中休み", Phase.Over => "物語は終わった", _ => "探索中（判定の要る行動は 2 つまで）" };
            _declHeader.text = $"{a.Name}の行動　<size=14><color=#a8977a>{phase}</color></size>";
            var secret = _secret.Contains(a.Id);
            _secretButton.SetText(secret ? "内緒: はい" : "内緒にする");
            _secretButton.GetComponent<Image>().color = secret ? Theme.Hex("4a2a3a") : Theme.Hex("2e241a");
            _cover.gameObject.SetActive(secret && E.Combat == null);
            _secretButton.interactable = E.Combat == null && !_busy;
            var techniques = a.Techniques.Count > 0 ? "名付けた技: " + string.Join("、", a.Techniques.Select(t => "【" + t.Name + "】")) + "\n" : "";
            _declNote.text = techniques + "思いついたことを文章で書けます。技に名前を付けるなら【】で囲む。台詞は「」。" +
                (a.Life == Life.Down ? "\n倒れているので、仲間に起こしてもらうまで動けません。" : "");
            _declare.interactable = !_busy && !_ending && _t.Phase != Phase.Over;
            _input.interactable = !_busy;
            if (!Online) { _declare.SetText(_busy ? "語り部が\n考えている…" : _t.Phase == Phase.Afterword ? "その後を\n宣言する" : "行動を\n宣言する"); return; }
            // in a room: who is still writing, the closing countdown, and (host) going on without the slow ones
            var waiting = Living.Where(x => x.IsUp && !Away(x.Id) && !_ready.Contains(x.Id)).Select(x => x.Name).ToList();
            var closing = _closeAt > 0 ? Mathf.CeilToInt(Mathf.Max(0, _closeAt - Time.unscaledTime)) : 0;
            _declare.SetText(_busy ? "語り部が\n考えている…"
                : !AllMineIn ? (_t.Phase == Phase.Afterword ? "その後を\n宣言する" : "宣言する")
                : closing > 0 ? $"{closing} 秒で\n締め切り"
                : HostOnline ? $"待たずに\n進める" : "書き直して\n送る");
            if (!_busy) _busyText.text = closing > 0 ? $"全員そろった。{closing} 秒で締め切る（それまでは書き直せる）"
                : waiting.Count > 0 && AllMineIn ? $"{string.Join("・", waiting)}の宣言を待っている" : "";
        }

        /// <summary>No adventurer of one's own can act (dead, or left tonight): this player watches.</summary>
        void RefreshWatching()
        {
            _declHeader.text = "見ているだけ（あなたの冒険者は動けない）";
            _declNote.text = Online ? "仲間の宣言と語りは、このまま見られます。" : "";
            _declare.interactable = HostOnline && !_busy && !_ending && _t.Phase != Phase.Over && Living.Any(x => x.IsUp);
            _declare.SetText(HostOnline ? "待たずに\n進める" : "見ている");
            _input.interactable = false;
            _secretButton.interactable = false;
            _cover.gameObject.SetActive(false);
        }

        void ToggleSecret()
        {
            if (MyLiving.Count == 0 || E.Combat != null) return;
            var id = Selected.Id;
            if (!_secret.Remove(id)) _secret.Add(id);
            RefreshDeclState();
        }

        string _music = "";
        void PlayMusic(Place? place)
        {
            var name = _t.Phase == Phase.Over && _t.Ending != null ? "ending_" + (_t.Ending.Card switch { Card.Success => "success", Card.Bitter => "bitter", _ => "failure" })
                : E.Combat != null ? (E.Combat.Foes.Any(f => f.Boss && f.InFight) ? "boss" : "battle")
                : place?.Id == Story.Start || place?.Art == "bg_tavern" ? "tavern"
                : E.Tension.On ? "tense"   // only the three turns of tension (09_Spec), not the whole stay at a tense place (owner 2026-10-25)
                : AudioDirector.Zone(place?.Art ?? "");
            if (name == _music) return;
            _music = name;
            AudioDirector.Music(name);
        }

        // ================================================================ a turn

        void Declare()
        {
            if (_busy || _ending || _t.Phase == Phase.Over) return;
            var decls = new List<Declaration>();
            foreach (var a in MyLiving.Where(a => a.IsUp))
            {
                var text = _drafts.TryGetValue(a.Id, out var d) ? d.Trim() : "";
                if (text.Length == 0) text = E.Combat != null ? "身を守る" : "様子を見る";   // nothing written: wait and see (in a fight, defend)
                decls.Add(new Declaration { Who = a.Id, Text = text, Secret = _secret.Contains(a.Id) && E.Combat == null, Cover = _covers.TryGetValue(a.Id, out var c) ? c.Trim() : "" });
            }
            if (Guest) { if (decls.Count > 0) GuestDeclare(decls); return; }
            if (HostOnline)
            {
                // the host's own declarations go in with the friends'; pressing again once in goes on without the slow ones
                var again = decls.Count == 0 || AllMineIn;
                foreach (var d in decls) { _decls[d.Who] = d; _ready.Add(d.Who); }
                CheckClose();
                if (again && _closeAt <= 0) _closeAt = Time.unscaledTime;
                BroadcastState();
                RefreshCards();
                RefreshDeclState();
                return;
            }
            if (decls.Count == 0) return;
            foreach (var d in decls) AddLine($"{E.Find(d.Who)?.Name}{(d.Secret ? "（内緒）" : "")}: {d.Text}", Theme.Hex("b9a989"), 15);
            _drafts.Clear(); _secret.Clear(); _covers.Clear();
            _input.SetTextWithoutNotify(""); _cover.SetTextWithoutNotify("");
            _ = RunAsync(() => _t.ExploreAsync(decls));
        }

        async Task RunAsync(Func<Task<TurnReport>> turn)
        {
            _busy = true;
            _busyText.text = "語り部が考えている…";
            RefreshDeclState();
            BroadcastState();
            TurnReport? r = null;
            try { r = await turn(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Night] turn: " + e);
                AddLine("（AI の呼び出しに失敗しました。もう一度宣言してください: " + e.Message + "）", Theme.Warn, 15);
                var daily = e is Tavern.Core.Gm.LlmRateLimitException rl && rl.Daily;
                Notify(daily ? "（ホストの AI の今日の枠が尽きた。ホストが今夜を区切るか、もう一度試すのを待っている）" : "（語り部の AI が答えなかった。もう一度宣言してください）");
                if (daily) AskToStopForTonight();
            }
            _busy = false;
            _busyText.text = "";
            if (r != null) Show(r);
            if (r != null) SendReport(r); else BroadcastState();
            if (_n.Ai.Meter is Tavern.Core.Ai.CallMeter meter)
            {
                _n.Store.SaveCalls(_n.Ai.KeyName, meter);
                if (meter.Low) _n.Ai.Narrator.AllowRetell = _n.Ai.Highlight.AllowRetell = false;
            }
            RefreshAll();
            if (r?.Ending != null) _ = FinishEveningAsync();
            else if (_t.GrowthDue.Count > 0) ShowGrowth();
            else if (_t.Phase == Phase.Break) ShowBreak();
        }

        void Show(TurnReport r)
        {
            foreach (var l in r.Lines) { AddLine(l, LineColor(l), 15); Sfx(l); }
            foreach (var kv in r.PrivateLines.Where(kv => IsMine(kv.Key)))
                foreach (var l in kv.Value) AddLine($"（{E.Find(kv.Key)?.Name}だけ）{l}", Theme.Hex("c9a0c0"), 15);
            if (r.Narration.Length > 0) Queue("語り部 オルウェン", r.Narration, true);
            foreach (var kv in r.PrivateNarration.Where(kv => IsMine(kv.Key))) Queue($"{E.Find(kv.Key)?.Name}だけに", kv.Value, true);
            foreach (var kv in r.Deferred.Where(kv => IsMine(kv.Key))) _drafts[kv.Key] = string.Join("。", kv.Value);
            if (MyLiving.Count > 0) Select(Mathf.Min(_selected, MyLiving.Count - 1));
        }

        static Color LineColor(string l) =>
            l.StartsWith("手がかり") || l.StartsWith("確信") ? Theme.GoldLight
            : l.Contains("大成功") || l.Contains(" 成功") ? Theme.Hex("c8d6a0")
            : l.Contains("失敗") || l.Contains("できない") || l.Contains("ダメージ") ? Theme.Hex("d8a090")
            : Theme.Dim;

        static void Sfx(string l)
        {
            if (l.StartsWith("戦闘開始")) AudioDirector.Play("enemy");
            else if (l == "戦闘に勝った") AudioDirector.Play("victory");
            else if (l.Contains("大成功")) AudioDirector.Play("crit");
            else if (l.Contains("大失敗")) AudioDirector.Play("fumble");
            else if (l.StartsWith("手がかり") || l.StartsWith("確信")) AudioDirector.Play("growth", 0.6f);
            else if (l.Contains("ダメージ")) AudioDirector.Play("hit");
            else if (l.Contains("かわ") || l.Contains("空を切った")) AudioDirector.Play("miss");
            else if (l.StartsWith("時計が満ちた") || l.Contains("次の手番に大技")) AudioDirector.Play("warn");
        }

        async Task FinishEveningAsync()
        {
            _ending = true;
            while (_typing != null || _pending.Count > 0) await Task.Yield();
            await Wait(Auto ? 0.2f : 1.5f);
            Chronicle? chronicle = null;
            try
            {
                AddLine("（語り部が記録帳を書いている…）", Theme.Dim, 15);
                chronicle = await _t.WrapUpAsync(_n.Ai.Bookends);
                _n.Store.Record(_t, chronicle, Online ? Room!.MineOf(Room.Me) : null);
            }
            catch (Exception e) { Debug.LogWarning("[Night] wrap up: " + e); }
            SendEnd(chronicle);
            _n.ShowEnding(_t, chronicle);
        }

        void AskToStopForTonight()
        {
            Persist();
            Dialog("今夜の AI の枠が尽きました", "保存して、今夜はここで区切りますか？（続きは枠が戻ってから「続きから」で）",
                ("保存して酒場へ", Quit), ("もう一度試す", null));
        }

        void Quit()
        {
            if (_busy && !Guest) return;
            Persist();
            if (HostOnline) NetSession.Send(NightMsg.Closed, "{}");   // friends go back to their title; the room reopens with "続きから"
            _n.BackToTitle();
        }

        // ================================================================ the log

        void AddLine(string text, Color color, int size, bool serif = false)
        {
            var t = UiKit.Label(_log, text, Reading.Font(size), OnPage(color), serif).Inked();
            t.lineSpacing = 1.15f;
            ScrollDown();
        }

        /// <summary>The log is a parchment page: the light colours meant for dark panels become inks.</summary>
        static Color OnPage(Color c)
        {
            if (c == Theme.Ink) return Theme.InkDark;
            if (c == Theme.Gold || c == Theme.GoldLight) return Theme.InkRed;
            if (c == Theme.Hex("c8d6a0")) return Theme.InkGreen;
            if (c == Theme.Warn || c == Theme.Hex("d8a090")) return Theme.Hex("8e2a1c");
            if (c == Theme.Hex("c9a0c0")) return Theme.Hex("5a2a5e");
            return Theme.InkBrown;   // Dim, the said lines and anything else
        }

        void Queue(string who, string text, bool narration) => _pending.Enqueue((who, text, narration));

        void FinishTyping()
        {
            if (_typing == null) return;
            _typingPos = _typingFull.Length;
        }

        void ScrollDown()
        {
            Canvas.ForceUpdateCanvases();
            _logScroll.verticalNormalizedPosition = 0;
        }

        /// <summary>Per frame: narration typed out, the bot's turns, the roll timeout.</summary>
        public void Update()
        {
            if (_typing == null && _pending.Count > 0 && Time.unscaledTime >= _hold)
            {
                var (who, text, narration) = _pending.Dequeue();
                UiKit.Label(_log, who, Reading.Font(14), Theme.InkRed, serif: true).Inked();
                _typing = UiKit.Label(_log, "", Reading.Font(21), Theme.InkDark, serif: true).Inked();
                _typing.lineSpacing = 1.25f;
                _typingFull = UiKit.Kinsoku(text, Theme.Serif, Reading.Font(21), FontStyle.Normal, LogW - 4);
                _typing.horizontalOverflow = HorizontalWrapMode.Overflow;
                _typingPos = 0;
                if (narration) AudioDirector.Play("page", 0.5f);
            }
            if (_typing != null)
            {
                // the reading speed of this PC (設定); "すぐ全部" shows it at once
                _typingPos = float.IsInfinity(Reading.CharsPerSecond) && !Auto ? _typingFull.Length : _typingPos + Time.unscaledDeltaTime * (Auto ? 400 : Reading.CharsPerSecond);
                var n = Mathf.Min(_typingFull.Length, Mathf.FloorToInt(_typingPos));
                _typing.text = _typingFull.Substring(0, n);
                ScrollDown();
                if (n >= _typingFull.Length) { _typing = null; _hold = Time.unscaledTime + (Auto ? 0.1f : 0.6f); }
            }
            UpdateRoll();
            if (_closeAt > 0)
            {
                if (HostOnline && Time.unscaledTime >= _closeAt && !_busy && !_ending) CloseTurn();
                else if (Time.frameCount % 15 == 0) RefreshDeclState();   // the countdown
            }
            var waitingForOthers = Online && MyLiving.Any(a => a.IsUp) && AllMineIn;
            if (_autoTurns > 0 && !_busy && !_ending && !waitingForOthers && _typing == null && _pending.Count == 0 && _dialog == null && _t.Phase != Phase.Over)
            {
                _autoWait += Time.unscaledDeltaTime;
                if (_autoWait > 0.8f)
                {
                    _autoWait = 0; _autoTurns--;
                    // -switchai N: after the first turn, switch to paid model N as the host would in the settings (tests; not saved)
                    if (_autoDone++ == 1 && int.TryParse(AppMain.Arg("-switchai"), out var m)) { _n.Ai.Switch(true, m); RefreshAll(); }
                    BotDeclare();
                }
            }
        }

        /// <summary>The bot writes everyone's declarations: the walkthrough line (-walk), or a simple fight / look around.</summary>
        void BotDeclare()
        {
            if (_t.GrowthDue.Count > 0) return;
            if (E.Combat != null && !(_walk.Count > 0 && _walk[0].StartsWith("戦闘中:")))   // "戦闘中: …" lines are played in a fight
            {
                var foe = E.Combat.Foes.Where(f => f.InFight).OrderBy(f => f.Hp).FirstOrDefault();
                foreach (var a in MyLiving.Where(a => a.IsUp)) _drafts[a.Id] = a.Class.Id == "mage" ? $"魔力の矢を{foe?.Name}に放つ" : $"{foe?.Name}に攻撃する";
            }
            else if (_walk.Count > 0)
            {
                var line = _walk[0];
                _walk.RemoveAt(0);
                if (line.StartsWith("その後:") || line.StartsWith("戦闘中:")) line = line.Substring(4);
                foreach (var part in line.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var i = part.IndexOf(':');
                    if (i < 0) continue;
                    var who = part.Substring(0, i).Trim();
                    var a = E.Party.FirstOrDefault(x => x.Name == who || x.Id == who) ?? MyLiving.FirstOrDefault(x => who.EndsWith(x.Name));
                    if (a != null) _drafts[a.Id] = part.Substring(i + 1).Trim();
                }
            }
            else foreach (var a in MyLiving.Where(a => a.IsUp)) _drafts[a.Id] = "辺りを調べる";
            Declare();
        }
    }
}
