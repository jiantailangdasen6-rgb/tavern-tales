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
        int _autoTurns;
        float _autoWait;
        readonly List<string> _walk = new List<string>();

        sealed class PartyCard
        {
            public string Id = "";
            public Image Frame = null!;
            public RawImage Portrait = null!;
            public Text Name = null!, Hp = null!, Focus = null!, Status = null!;
            public Image HpBar = null!, FocusBar = null!;
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
        List<Adventurer> Living => E.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn).ToList();
        Adventurer Selected => Living.Count == 0 ? E.Party[0] : Living[Mathf.Clamp(_selected, 0, Living.Count - 1)];

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
            if (opening)
            {
                Queue("語り部 オルウェン", Story.Intro, true);
                if (!Auto) ShowHandouts();
            }
            else if (_t.Untold.Count > 0) _ = RunAsync(() => _t.TellUntoldAsync());
            if (AppMain.Flag("-showsheet")) ShowSheet(E.Party[0].Id);   // screenshots
            if (AppMain.Flag("-showclues")) ShowClues();
            RefreshAll();
        }

        StoryDef Story => E.Story;

        void BuildTop(Transform root)
        {
            var top = UiKit.Panel(root, "Top", 14, 12, 1892, 54).transform;
            UiKit.Label(top, "酒場の語り部", 22, Theme.Gold, 14, 12, 180, 30, serif: true);
            _actText = UiKit.Label(top, "", 19, Theme.Ink, 200, 14, 560, 30);
            _clockRow = UiKit.Rect("Clocks", top).Place(770, 6, 560, 42);
            var x = 1340f;
            var clues = UiKit.Button(top, "手がかり", ShowClues, x, 6, 150, 42, 17); _clueButtonText = clues.GetComponentInChildren<Text>(); x += 156;
            UiKit.Button(top, "ハンドアウト", ShowHandouts, x, 6, 120, 42, 16); x += 126;
            UiKit.Button(top, "語り部への注文", ShowRequests, x, 6, 150, 42, 16, gold: true); x += 156;
            UiKit.Button(top, "保存して酒場へ", Quit, x, 6, 104, 42, 14);
        }

        void BuildParty(Transform root)
        {
            var panel = UiKit.Panel(root, "Party", 14, 78, 260, 990).transform;
            for (int i = 0; i < E.Party.Count; i++)
            {
                var a = E.Party[i];
                var y = 10 + i * 238;
                var frame = UiKit.Box(panel, "Card", Theme.Panel2, frame: true);
                frame.rectTransform.Place(8, y, 244, 228);
                var c = new PartyCard { Id = a.Id, Frame = frame };
                var t = frame.transform;
                c.Portrait = UiKit.Picture(t, ArtLibrary.Get(a.Portrait), 8, 8, 84, 112);
                c.Name = UiKit.Label(t, "", 19, Theme.GoldLight, 100, 6, 140, 50, serif: true);
                c.Hp = UiKit.Label(t, "", 14, Theme.Ink, 100, 58, 140, 20);
                c.HpBar = UiKit.Bar(t, 100, 80, 136, 8, Theme.Red);
                c.Focus = UiKit.Label(t, "", 14, Theme.Ink, 100, 92, 140, 20);
                c.FocusBar = UiKit.Bar(t, 100, 114, 136, 8, Theme.Blue);
                c.Status = UiKit.Label(t, "", 13, Theme.Dim, 8, 126, 228, 64);
                var id = a.Id;
                UiKit.Button(t, "詳しく", () => ShowSheet(id), 8, 190, 110, 30, 14);
                UiKit.Button(t, "宣言を書く", () => { var i2 = Living.FindIndex(x => x.Id == id); if (i2 >= 0) Select(i2); }, 124, 190, 112, 30, 14);
                _cards.Add(c);
            }
        }

        void BuildScene(Transform root)
        {
            var panel = UiKit.Panel(root, "Scene", 284, 78, 840, 560).transform;
            _sceneBox = UiKit.Rect("Box", panel).Fill(6);
            _sceneBg = UiKit.Picture(_sceneBox, null, 0, 0, 828, 548);
            _sceneBg.rectTransform.Fill();
            _placeText = UiKit.Label(_sceneBox, "", 20, Theme.GoldLight, 12, 8, 600, 30, serif: true);
            _placeText.gameObject.AddComponent<Shadow>();
            _peopleRow = UiKit.Rect("People", _sceneBox).Place(828 - 8 - 4 * 96, 8, 4 * 96, 150);
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
            var panel = UiKit.Panel(root, "Log", 1134, 78, 772, 990).transform;
            UiKit.Label(panel, "語　り", 22, Theme.Gold, 18, 12, 200, 30, serif: true);
            _quotaText = UiKit.Label(panel, "", 14, Theme.Dim, 260, 18, 494, 24, align: TextAnchor.UpperRight);
            _log = UiKit.Scroll(panel, 14, 52, 744, 926, out _logScroll, spacing: 12);
            var click = panel.gameObject.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(FinishTyping);
        }

        void BuildDeclare(Transform root)
        {
            var panel = UiKit.Panel(root, "Declare", 284, 648, 840, 420).transform;
            _tabRow = UiKit.Rect("Tabs", panel).Place(14, 10, 812, 44);
            _declHeader = UiKit.Label(panel, "", 18, Theme.GoldLight, 14, 60, 640, 28);
            _input = UiKit.Input(panel, "自由に行動を書く…（例: 祭壇の燭台を調べて、【柱蹴りの一閃】で骨の兵士の首を狙う）", 14, 92, 620, 168, 18, multiline: true);
            _input.characterLimit = 200;
            _input.onValueChanged.AddListener(v => { if (Living.Count > 0) _drafts[Selected.Id] = v; RefreshDeclState(); });
            _secretButton = UiKit.Button(panel, "内緒にする", ToggleSecret, 14, 268, 140, 40, 16);
            _cover = UiKit.Input(panel, "表向き: （内緒のとき、ほかの人に見える行動）", 162, 268, 472, 40, 15);
            _cover.onValueChanged.AddListener(v => { if (Living.Count > 0) _covers[Selected.Id] = v; });
            _declNote = UiKit.Label(panel, "", 14, Theme.Dim, 14, 316, 620, 90);
            _declare = UiKit.Button(panel, "全員の行動を\n宣言する", Declare, 648, 92, 178, 216, 22, gold: true);
        }

        // ================================================================ refresh

        void RefreshAll()
        {
            var act = Story.Acts.FirstOrDefault(a => a.N == E.World.Act);
            var here = E.World.PlacesOfParty.FirstOrDefault() ?? Story.Start;
            var place = Story.PlaceOf(here);
            _actText.text = $"第{E.World.Act}幕「{act?.Title}」 — {place?.Name}" + (E.Tension.On ? "　<color=#c4553f>緊迫</color>" : "");
            _clueButtonText.text = $"手がかり {E.World.SharedClues} / {Story.Clues.Count}";
            _quotaText.text = _n.Ai.QuotaText() + (E.World.PlacesOfParty.Distinct().Count() > 1 ? "　一行は分かれている" : "");
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
            var x = 0f;
            foreach (var c in E.World.Clocks.All.Where(c => c.Visible && c.RunsIn(E.World.Act)))
            {
                UiKit.Label(_clockRow, c.Def.Name, 15, c.Def.Good ? Theme.Green : Theme.GoldLight, x, 2, 140, 20);
                for (int i = 0; i < c.Def.Segments; i++)
                {
                    var seg = UiKit.Box(_clockRow, "Seg", i < c.Filled ? (c.Def.Good ? Theme.Green : Theme.Warn) : Theme.Hex("3a2e22"), frame: true);
                    seg.rectTransform.Place(x + i * 16, 24, 13, 13);
                }
                x += Mathf.Max(150, c.Def.Segments * 16 + 20);
            }
        }

        void RefreshCards()
        {
            foreach (var c in _cards)
            {
                var a = E.Find(c.Id)!;
                c.Name.text = $"{a.Name}\n<size=13>{a.Class.Name}</size>";
                c.Hp.text = $"HP {a.Hp}/{a.MaxHp}";
                c.HpBar.fillAmount = a.MaxHp == 0 ? 0 : (float)a.Hp / a.MaxHp;
                c.Focus.text = $"気力 {a.Focus}/{a.MaxFocus}";
                c.FocusBar.fillAmount = a.MaxFocus == 0 ? 0 : (float)a.Focus / a.MaxFocus;
                var bits = new List<string>
                {
                    $"コイン {new string('●', a.FateCoins)}{(a.FateCoins == 0 ? "なし" : "")}　印 {new string('✦', a.MarksLeft)}{new string('✧', a.Marks.Count - a.MarksLeft)}",
                };
                if (a.Life == Life.Down) bits.Add($"<color=#c4553f>倒れている（死のサイコロ {a.DeathFailures}/3）</color>");
                else if (a.Life == Life.Dead) bits.Add("<color=#c4553f>死亡（聞くだけ）</color>");
                if (a.Statuses.List.Count > 0) bits.Add(string.Join("・", a.Statuses.List.Select(s => s.Kind.Label())));
                if (E.World.PlacesOfParty.Distinct().Count() > 1) bits.Add("@" + Story.PlaceOf(E.PlaceOf(a))?.Name);
                bits.Add(_drafts.TryGetValue(a.Id, out var d) && d.Trim().Length > 0 ? "<color=#7a9a3a>宣言を書いた</color>" : "考え中");
                c.Status.text = string.Join("\n", bits);
                c.Frame.color = a.Life == Life.Dead ? Theme.Hex("1a1512") : Living.Count > 0 && Selected.Id == a.Id ? Theme.Hex("3a2e1c") : Theme.Panel2;
                c.Portrait.color = a.Life == Life.Dead ? new Color(0.4f, 0.4f, 0.4f) : Color.white;
            }
        }

        void RefreshScene(Place? place)
        {
            _sceneBg.texture = ArtLibrary.Get(place?.Art ?? "bg_tavern");
            _placeText.text = place?.Name ?? "";
            UiKit.Clear(_peopleRow);
            var here = place?.Id ?? Story.Start;
            var x = 4 * 96f;
            foreach (var p in E.PeopleAt(here).Where(p => E.Story.PersonOf(p.Id)?.Enemy.Length == 0 || p.Fighting == null).Take(4))
            {
                x -= 96;
                var pic = UiKit.Picture(_peopleRow, ArtLibrary.Get(p.Portrait.Length > 0 ? p.Portrait : "pt_youth"), x, 0, 90, 120);
                UiKit.Label(_peopleRow, p.Name, 12, Theme.GoldLight, x - 4, 122, 98, 28, align: TextAnchor.UpperCenter).gameObject.AddComponent<Shadow>();
                if (p.WithParty) pic.color = new Color(1, 1, 1, 0.85f);
            }
            UiKit.Clear(_enemyRow);
            var combat = E.Combat;
            if (combat != null)
            {
                var foes = combat.Foes.Where(f => !f.Left && !f.Killed).ToList();
                var w = Mathf.Min(160, 808f / Mathf.Max(1, foes.Count));
                for (int i = 0; i < foes.Count; i++)
                {
                    var f = foes[i];
                    var art = Enemies(f.Source)?.Art ?? f.Art;
                    var pic = UiKit.Picture(_enemyRow, ArtLibrary.Get(art), i * w + 4, 0, w - 8, w - 8);
                    if (f.Down) pic.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
                    UiKit.Label(_enemyRow, f.Name, 13, Theme.GoldLight, i * w, w - 6, w, 20, align: TextAnchor.UpperCenter).gameObject.AddComponent<Shadow>();
                    var bar = UiKit.Bar(_enemyRow, i * w + 8, w + 14, w - 16, 7, Theme.Red);
                    bar.fillAmount = f.MaxHp == 0 ? 0 : (float)f.Hp / f.MaxHp;
                }
                var due = combat.BigMoveDue.Select(id => combat.Find(id)?.Creature).FirstOrDefault(c => c?.BigMove != null);
                _warn.SetActive(due != null);
                if (due != null) _warnText.text = $"{due.BigMove!.Warning}\n次の手番に大技「{due.BigMove.Name}」";
            }
            else _warn.SetActive(false);
        }

        EnemyDef? Enemies(string id) => _n.Enemies.Has(id) ? _n.Enemies[id] : null;

        void RefreshTabs()
        {
            UiKit.Clear(_tabRow);
            _tabs.Clear();
            var living = Living;
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
            if (Living.Count == 0) return;
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
            _declare.SetText(_busy ? "語り部が\n考えている…" : _t.Phase == Phase.Afterword ? "その後を\n宣言する" : "全員の行動を\n宣言する");
            _input.interactable = !_busy;
        }

        void ToggleSecret()
        {
            if (Living.Count == 0 || E.Combat != null) return;
            var id = Selected.Id;
            if (!_secret.Remove(id)) _secret.Add(id);
            RefreshDeclState();
        }

        string _music = "";
        void PlayMusic(Place? place)
        {
            var name = _t.Phase == Phase.Over && _t.Ending != null ? "ending_" + (_t.Ending.Card switch { Card.Success => "success", Card.Bitter => "bitter", _ => "failure" })
                : E.Combat != null ? (E.Combat.Foes.Any(f => f.Boss && f.InFight) ? "boss" : "battle")
                : place?.Id == Story.Start || place?.Art == "bg_tavern" ? "tavern" : AudioDirector.Zone(place?.Art ?? "");
            if (name == _music) return;
            _music = name;
            AudioDirector.Music(name);
        }

        // ================================================================ a turn

        void Declare()
        {
            if (_busy || _ending || _t.Phase == Phase.Over) return;
            var decls = new List<Declaration>();
            foreach (var a in Living.Where(a => a.IsUp))
            {
                var text = _drafts.TryGetValue(a.Id, out var d) ? d.Trim() : "";
                if (text.Length == 0) text = E.Combat != null ? "身を守る" : "様子を見る";   // nothing written: wait and see (in a fight, defend)
                decls.Add(new Declaration { Who = a.Id, Text = text, Secret = _secret.Contains(a.Id) && E.Combat == null, Cover = _covers.TryGetValue(a.Id, out var c) ? c.Trim() : "" });
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
            TurnReport? r = null;
            try { r = await turn(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Night] turn: " + e);
                AddLine("（AI の呼び出しに失敗しました。もう一度宣言してください: " + e.Message + "）", Theme.Warn, 15);
                if (e is Tavern.Core.Gm.LlmRateLimitException rl && rl.Daily) AskToStopForTonight();
            }
            _busy = false;
            _busyText.text = "";
            if (r != null) Show(r);
            if (_n.Ai.Meter?.Low == true) _n.Ai.Narrator.AllowRetell = _n.Ai.Highlight.AllowRetell = false;
            RefreshAll();
            if (r?.Ending != null) _ = FinishEveningAsync();
            else if (_t.GrowthDue.Count > 0) ShowGrowth();
            else if (_t.Phase == Phase.Break) ShowBreak();
        }

        void Show(TurnReport r)
        {
            foreach (var l in r.Lines) { AddLine(l, LineColor(l), 15); Sfx(l); }
            foreach (var kv in r.PrivateLines)
                foreach (var l in kv.Value) AddLine($"（{E.Find(kv.Key)?.Name}だけ）{l}", Theme.Hex("c9a0c0"), 15);
            if (r.Narration.Length > 0) Queue("語り部 オルウェン", r.Narration, true);
            foreach (var kv in r.PrivateNarration) Queue($"{E.Find(kv.Key)?.Name}だけに", kv.Value, true);
            foreach (var kv in r.Deferred) _drafts[kv.Key] = string.Join("。", kv.Value);
            if (Living.Count > 0) Select(Mathf.Min(_selected, Living.Count - 1));
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
            await Task.Delay(Auto ? 200 : 1500);
            Chronicle? chronicle = null;
            try
            {
                AddLine("（語り部が記録帳を書いている…）", Theme.Dim, 15);
                chronicle = await _t.WrapUpAsync(_n.Ai.Bookends);
                _n.Store.Record(_t, chronicle);
            }
            catch (Exception e) { Debug.LogWarning("[Night] wrap up: " + e); }
            _n.ShowEnding(_t, chronicle);
        }

        void AskToStopForTonight()
        {
            _n.Store.SaveEvening(_t);
            Dialog("今夜の AI の枠が尽きました", "保存して、今夜はここで区切りますか？（続きは枠が戻ってから「続きから」で）",
                ("保存して酒場へ", Quit), ("もう一度試す", null));
        }

        void Quit()
        {
            if (_busy) return;
            _n.Store.SaveEvening(_t);
            _n.BackToTitle();
        }

        // ================================================================ the log

        void AddLine(string text, Color color, int size, bool serif = false)
        {
            var t = UiKit.Label(_log, text, size, color, serif);
            t.lineSpacing = 1.15f;
            ScrollDown();
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
                UiKit.Label(_log, who, 14, Theme.Gold, serif: true);
                _typing = UiKit.Label(_log, "", 21, Theme.Hex("efe3c8"), serif: true);
                _typing.lineSpacing = 1.25f;
                _typingFull = UiKit.Kinsoku(text, Theme.Serif, 21, FontStyle.Normal, 720);
                _typing.horizontalOverflow = HorizontalWrapMode.Overflow;
                _typingPos = 0;
                if (narration) AudioDirector.Play("page", 0.5f);
            }
            if (_typing != null)
            {
                _typingPos += Time.unscaledDeltaTime * (Auto ? 400 : 28);
                var n = Mathf.Min(_typingFull.Length, Mathf.FloorToInt(_typingPos));
                _typing.text = _typingFull.Substring(0, n);
                ScrollDown();
                if (n >= _typingFull.Length) { _typing = null; _hold = Time.unscaledTime + (Auto ? 0.1f : 0.6f); }
            }
            UpdateRoll();
            if (_autoTurns > 0 && !_busy && !_ending && _typing == null && _pending.Count == 0 && _dialog == null && _t.Phase != Phase.Over)
            {
                _autoWait += Time.unscaledDeltaTime;
                if (_autoWait > 0.8f) { _autoWait = 0; _autoTurns--; BotDeclare(); }
            }
        }

        /// <summary>The bot writes everyone's declarations: the walkthrough line (-walk), or a simple fight / look around.</summary>
        void BotDeclare()
        {
            if (_t.GrowthDue.Count > 0) return;
            if (E.Combat != null)
            {
                var foe = E.Combat.Foes.Where(f => f.InFight).OrderBy(f => f.Hp).FirstOrDefault();
                foreach (var a in Living.Where(a => a.IsUp)) _drafts[a.Id] = a.Class.Id == "mage" ? $"魔力の矢を{foe?.Name}に放つ" : $"{foe?.Name}に攻撃する";
            }
            else if (_walk.Count > 0)
            {
                var line = _walk[0];
                _walk.RemoveAt(0);
                if (line.StartsWith("その後:")) line = line.Substring(4);
                foreach (var part in line.Split(new[] { " / " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var i = part.IndexOf(':');
                    if (i < 0) continue;
                    var who = part.Substring(0, i).Trim();
                    var a = E.Party.FirstOrDefault(x => x.Name == who || x.Id == who) ?? Living.FirstOrDefault(x => who.EndsWith(x.Name));
                    if (a != null) _drafts[a.Id] = part.Substring(i + 1).Trim();
                }
            }
            else foreach (var a in Living.Where(a => a.IsUp)) _drafts[a.Id] = "辺りを調べる";
            Declare();
        }
    }
}
