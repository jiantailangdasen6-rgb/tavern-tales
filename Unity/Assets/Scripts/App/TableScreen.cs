#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Content;
using Tavern.Core.Game;
using Tavern.Core.Gm;
using Tavern.Core.Rules;
using Tavern.Unity.Dice;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The table (Docs/mock/layout_combat.png): party on the left, the scene with enemies and the dice in the middle,
    /// the storyteller's log on the right, and everyone's actions at the bottom. M1 is played alone: one person
    /// writes each character's action, then declares them all at once.
    /// </summary>
    public sealed partial class TableScreen
    {
        readonly AppMain _app;
        TavernGame _game;
        Transform _root = null!;

        // top
        Text _actText = null!, _timeText = null!;
        // party
        readonly List<PartyCard> _partyCards = new List<PartyCard>();
        // scene
        RawImage _sceneBg = null!;
        Text _placeText = null!, _diceCaption = null!, _busyText = null!;
        RectTransform _enemyRow = null!, _npcBox = null!;
        GameObject _warnBanner = null!;
        Text _warnText = null!;
        RawImage _diceImage = null!;
        // log
        RectTransform _logContent = null!;
        ScrollRect _logScroll = null!;
        // actions
        Text _actorHeader = null!, _draftSummary = null!, _coinText = null!;
        RectTransform _cardRow = null!, _basicCol = null!, _suggestBox = null!, _allyRow = null!;
        InputField _input = null!;
        Button _declare = null!;
        GameObject _overlay = null!;

        int _selected;
        readonly Dictionary<string, Declaration> _drafts = new Dictionary<string, Declaration>();
        bool _busy;
        int _autoTurns;
        float _autoWait;

        // playback of a finished turn: dice first, then the narration typed out
        readonly Queue<LogEntry> _pending = new Queue<LogEntry>();
        Text? _typing;
        string _typingFull = "";
        float _typingPos;
        float _holdUntil;
        bool _diceRolling;

        sealed class PartyCard
        {
            public Image Frame = null!;
            public RawImage Portrait = null!;
            public Text Name = null!, Hp = null!, Focus = null!, Status = null!;
            public Image HpBar = null!, FocusBar = null!;
        }

        public TableScreen(AppMain app, TavernGame game, NetRole role = NetRole.Solo, string me = "")
        {
            _app = app; _game = game; _role = role; _me = me;
            _autoTurns = int.TryParse(AppMain.Arg("-autoplay"), out var n) ? n : 0;
            _game.ChooseRerolls = role == NetRole.Host ? HostChooseRerollsAsync : (Func<IReadOnlyList<LogEntry>, Task<IReadOnlyList<LogEntry>>>)ChooseRerollsAsync;
            SetupNet();
        }

        // ================================================================ fate coins

        readonly HashSet<int> _shownSeq = new HashSet<int>();
        TaskCompletionSource<bool>? _drained;
        RectTransform? _rerollBox;

        /// <summary>Shows the dice of this turn, then lets the players spend fate coins on failed checks.</summary>
        async Task<IReadOnlyList<LogEntry>> ChooseRerollsAsync(IReadOnlyList<LogEntry> failed)
        {
            EnqueueNew(_game.State.Log);
            _drained = new TaskCompletionSource<bool>();
            await _drained.Task;

            var choice = new TaskCompletionSource<IReadOnlyList<LogEntry>>();
            var picked = new List<LogEntry>();
            _rerollBox = UiKit.Rect("Reroll", _sceneBg.transform.parent).Place(154, 470, 700, 70 + failed.Count * 52);
            UiKit.Box(_rerollBox, "Bg", new Color(0.08f, 0.05f, 0.03f, 0.92f), frame: true).rectTransform.Fill();
            UiKit.Label(_rerollBox, "失敗した判定を、運命のコイン（1 晩 1 枚）で振り直せます", 18, Theme.GoldLight, 16, 12, 668, 28);
            for (int i = 0; i < failed.Count; i++)
            {
                var e = failed[i];
                UiKit.Button(_rerollBox, $"● {e.Who} のコインで振り直す", () => { picked.Add(e); choice.TrySetResult(picked); }, 16, 48 + i * 52, 420, 44, 18, gold: true);
            }
            UiKit.Button(_rerollBox, "このまま進む", () => choice.TrySetResult(picked), 452, 48, 232, 44, 18);
            if (_autoTurns > 0) choice.TrySetResult(new[] { failed[0] });   // the bot always tries its luck once
            var result = await choice.Task;
            UnityEngine.Object.Destroy(_rerollBox.gameObject);
            _rerollBox = null;
            return result;
        }

        Character Actor => _game.Party[Mathf.Clamp(_selected, 0, _game.Party.Count - 1)];
        Declaration Draft(Character c) => _drafts.TryGetValue(c.Name, out var d) ? d : _drafts[c.Name] = new Declaration { Character = c.Name };

        // ================================================================ build

        public void Build(Transform root, bool opening)
        {
            _root = root;
            UiKit.Box(root, "Bg", Theme.Bg).rectTransform.Fill();
            BuildTop(root);
            BuildParty(root);
            BuildScene(root);
            BuildLog(root);
            BuildActions(root);
            _overlay = UiKit.Rect("Overlay", root).Fill().gameObject;
            if (AppMain.Flag("-showsettings")) ShowSettings();   // screenshots
            if (AppMain.Flag("-showsheet")) ShowSheet(_game.Party[0]);

            if (opening) _ = OpenAsync();
            else if (_role == NetRole.Client && _game.State.Log.Count <= 12) { EnqueueNew(_game.State.Log); RefreshAll(); }
            else
            {
                foreach (var e in _game.State.Log.Skip(Math.Max(0, _game.State.Log.Count - 30))) AppendLog(e, instant: true);
                foreach (var e in _game.State.Log) _shownSeq.Add(e.Seq);
                RefreshAll();
            }
        }

        void BuildTop(Transform root)
        {
            var top = UiKit.Panel(root, "Top", 14, 12, 1892, 54).transform;
            UiKit.Label(top, "酒場の語り部", 26, Theme.Gold, 18, 9, 240, 40, serif: true);
            _actText = UiKit.Label(top, "", 21, Theme.Ink, 250, 13, 680, 34, serif: true);
            _actText.resizeTextForBestFit = true; _actText.resizeTextMinSize = 13; _actText.resizeTextMaxSize = 21;
            _actText.verticalOverflow = VerticalWrapMode.Truncate;
            _timeText = UiKit.Label(top, "", 17, Theme.Dim, 940, 16, 160, 30, align: TextAnchor.UpperRight);
            UiKit.Button(top, "地図", ShowMap, 1236, 8, 110, 38, 18);
            UiKit.Button(top, "設定", ShowSettings, 1110, 8, 116, 38, 18);
            UiKit.Button(top, "語り部への注文", ShowOrder, 1356, 8, 230, 38, 18, gold: true);
            UiKit.Button(top, _role == NetRole.Client ? "部屋を出る" : "保存して酒場を出る", () => { if (_role != NetRole.Client) _app.Save(_game); LeaveRoom(); _app.ShowTitle(); }, 1596, 8, 284, 38, 18);
        }

        bool Compact => _game.Party.Count > 3;
        float CardH => Compact ? 152 : 176;
        float PortraitW => Compact ? 80 : 96;
        float PortraitH => Compact ? 108 : 132;

        void BuildParty(Transform root)
        {
            var panel = UiKit.Panel(root, "Party", 14, 76, 300, 672).transform;
            for (int i = 0; i < _game.Party.Count; i++)
            {
                var idx = i;
                var c = new PartyCard();
                c.Frame = UiKit.Box(panel, "Card" + i, Theme.Panel2, frame: true);
                c.Frame.rectTransform.Place(12, 12 + i * (CardH + 8), 276, CardH);
                var btn = c.Frame.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => Select(idx));
                var t = c.Frame.transform;
                // four adventurers get smaller cards so all fit the panel
                var lx = PortraitW + 20; var dy = Compact ? -6f : 0f;
                c.Portrait = UiKit.Picture(t, null, 10, 10, PortraitW, PortraitH);
                c.Name = UiKit.Label(t, "", Compact ? 20 : 22, Theme.Ink, lx, 8, 266 - lx, 30, serif: true);
                UiKit.Label(t, "HP", 13, Theme.Dim, lx, 44 + dy, 40, 18);
                c.Hp = UiKit.Label(t, "", 13, Theme.Dim, 180, 44 + dy, 90, 18, align: TextAnchor.UpperRight);
                c.HpBar = UiKit.Bar(t, lx, 64 + dy, 268 - lx, 10, Theme.Red);
                UiKit.Label(t, "気力", 13, Theme.Dim, lx, 80 + dy * 2, 60, 18);
                c.Focus = UiKit.Label(t, "", 13, Theme.Dim, 180, 80 + dy * 2, 90, 18, align: TextAnchor.UpperRight);
                c.FocusBar = UiKit.Bar(t, lx, 100 + dy * 2, 268 - lx, 8, Theme.Blue);
                c.Status = UiKit.Label(t, "", 14, Theme.Gold, lx, 116 + dy * 3, 266 - lx, CardH - 116 - dy * 3);
                UiKit.Button(t, "詳しく", () => ShowSheet(_game.Party[idx]), 10, PortraitH + 14, PortraitW, Compact ? 20 : 24, 13);   // abilities, items, cards
                _partyCards.Add(c);
            }
            if (!Compact) UiKit.Label(panel, "カードを押すと、その冒険者の行動を書けます。「詳しく」で能力値と持ち物", 14, Theme.Dim, 16, 610, 270, 54);
        }

        void BuildScene(Transform root)
        {
            var panel = UiKit.Panel(root, "Scene", 328, 76, 1008, 672);
            panel.gameObject.AddComponent<RectMask2D>();
            var t = panel.transform;
            _sceneBg = UiKit.Picture(t, null, 0, 0, 1008, 672);
            var grad = UiKit.Box(t, "Grad", new Color(0, 0, 0, 0.35f));
            grad.rectTransform.Place(0, 470, 1008, 202);
            grad.raycastTarget = false;
            _placeText = UiKit.Label(t, "", 18, Theme.Ink, 14, 12, 600, 30);
            _placeText.gameObject.AddComponent<Shadow>();

            _npcBox = UiKit.Rect("Npc", t).Place(700, 120, 280, 420);
            _enemyRow = UiKit.Rect("Enemies", t).Place(20, 120, 968, 520);

            var warn = UiKit.Box(t, "Warn", new Color(0.24f, 0.04f, 0.03f, 0.88f), frame: true);
            warn.rectTransform.Place(204, 56, 600, 46);
            _warnText = UiKit.Label(warn.transform, "", 20, Theme.Hex("ffd9c9"), serif: true, align: TextAnchor.MiddleCenter);
            _warnText.rectTransform.Fill(4);
            _warnBanner = warn.gameObject;
            _warnBanner.SetActive(false);

            _diceImage = UiKit.Picture(t, _app.Dice.Texture, 0, 52, 1008, 1008 / DiceRoller.Aspect);   // the tray spans the scene
            _diceImage.gameObject.SetActive(false);
            var cap = UiKit.Box(t, "DiceCaption", new Color(0, 0, 0, 0.7f));
            cap.rectTransform.Place(154, 460, 700, 44);
            _diceCaption = UiKit.Label(cap.transform, "", 20, Theme.Ink, align: TextAnchor.MiddleCenter);
            _diceCaption.rectTransform.Fill(4);
            cap.gameObject.SetActive(false);

            var badge = UiKit.Box(t, "Teller", new Color(0, 0, 0, 0.55f), frame: true);
            badge.rectTransform.Place(770, 606, 224, 54);
            var tellerPic = UiKit.Picture(badge.transform, ArtLibrary.Get(_game.Teller.Portrait), 6, 5, 44, 44);
            tellerPic.uvRect = new Rect(0.25f, 0.6f, 0.5f, 0.33f);
            UiKit.Label(badge.transform, "語り部 " + _game.Teller.Name, 17, Theme.Gold, 58, 6, 170, 24);
            _busyText = UiKit.Label(badge.transform, "", 13, Theme.Dim, 58, 28, 170, 22);
        }

        void BuildLog(Transform root)
        {
            var panel = UiKit.Panel(root, "Log", 1350, 76, 556, 990).transform;
            UiKit.Label(panel, "語　り", 18, Theme.Gold, 18, 10, 300, 28, serif: true);
            UiKit.Box(panel, "Rule", Theme.Edge).rectTransform.Place(14, 42, 528, 1);
            _logContent = UiKit.Scroll(panel, 10, 50, 540, 930, out _logScroll, 12);
            // Clicking the log shows the rest of the narration at once.
            _logScroll.gameObject.AddComponent<Button>().onClick.AddListener(() => { if (_typing != null) _typingPos = _typingFull.Length; });
        }

        void BuildActions(Transform root)
        {
            var t = UiKit.Panel(root, "Actions", 14, 762, 1322, 304).transform;
            _actorHeader = UiKit.Label(t, "", 22, Theme.Gold, 18, 10, 500, 32, serif: true);
            _coinText = UiKit.Label(t, "", 15, Theme.Dim, 520, 16, 260, 24);
            _draftSummary = UiKit.Label(t, "", 15, Theme.Dim, 780, 12, 530, 50);
            _cardRow = UiKit.Rect("Cards", t).Place(16, 50, 480, 244);
            _basicCol = UiKit.Rect("Basics", t).Place(508, 50, 160, 244);
            _suggestBox = UiKit.Rect("Suggest", t).Place(682, 62, 626, 92);
            _allyRow = UiKit.Rect("Allies", t).Place(682, 156, 626, 34);
            _input = UiKit.Input(t, "自由に行動を書く…（例: 天井の鎖を焼き切って、シャンデリアを落とす）", 682, 192, 440, 100, 18, multiline: true);
            _input.onValueChanged.AddListener(v => { Draft(Actor).Text = v; RefreshDraftSummary(); });
            _declare = UiKit.Button(t, "全員の行動を\n宣言する", () => _ = SubmitAsync(), 1134, 192, 174, 100, 21, gold: true);
        }

        // ================================================================ refresh

        void RefreshAll()
        {
            RefreshTop(); RefreshParty(); RefreshScene(); RefreshActions();
        }

        void RefreshTop()
        {
            var act = _game.Scenario.Acts.FirstOrDefault(a => a.N == _game.State.Act);
            _actText.text = $"第{_game.State.Act}幕「{act?.Title}」— {_game.Here.Name}{(_game.InCombat ? $"　戦闘 第{_game.State.Combat!.Round}手番" : "")}";
            var m = (int)_game.State.PlayMinutes;
            _timeText.text = $"経過 {m / 60}:{m % 60:00}";
        }

        readonly HashSet<string> _deathsRecorded = new HashSet<string>();

        /// <summary>One of this PC's adventurers died: they go to the memorial now, not only when the evening is finished.</summary>
        void RecordDeaths()
        {
            if (!_game.Party.Any(p => p.State == LifeState.Dead && IsMine(p) && !_deathsRecorded.Contains(p.Name))) return;
            foreach (var p in _game.Party.Where(p => p.State == LifeState.Dead)) _deathsRecorded.Add(p.Name);
            var roster = _app.LoadRoster();
            if (roster.RecordDeaths(_game, IsMine)) _app.SaveRoster(roster);
        }

        void RefreshParty()
        {
            RecordDeaths();
            for (int i = 0; i < _partyCards.Count; i++)
            {
                var c = _partyCards[i];
                var p = _game.Party[i];
                var tex = ArtLibrary.Get(p.Portrait);
                c.Portrait.texture = tex;
                c.Portrait.uvRect = ArtLibrary.Cover(tex, PortraitW, PortraitH, 0.72f);
                c.Portrait.color = p.State == LifeState.Active ? Color.white : new Color(0.45f, 0.45f, 0.45f);
                c.Name.text = $"{p.Name} <size=14><color=#a8977a>{_app.Db.Classes[p.ClassId].Name}</color></size>";
                c.Hp.text = $"{p.Hp} / {p.MaxHp}";
                c.HpBar.fillAmount = (float)p.Hp / p.MaxHp;
                c.Focus.text = $"{p.Focus} / {p.MaxFocus}";
                c.FocusBar.fillAmount = (float)p.Focus / p.MaxFocus;
                c.Status.text = p.State == LifeState.Dead ? "<color=#c4553f>死亡</color>"
                    : p.State == LifeState.Down ? $"<color=#d88>倒れている　死のサイコロ {new string('●', p.DeathSaveFailures)}{new string('○', 3 - p.DeathSaveFailures)}</color>"
                    : !IsMine(p) ? (_declared.Contains(p.Name) ? "<color=#9fd08a>宣言済み</color>" : $"考え中 <size=12>（{OwnerOf(p)}）</size>")
                    : _declared.Contains(p.Name) ? "<color=#9fd08a>宣言済み</color>"
                    : (_drafts.TryGetValue(p.Name, out var d) && HasAction(d) ? "<color=#9fd08a>行動を決めた</color>" : "考え中");
                c.Frame.GetComponent<Outline>().effectColor = i == _selected ? Theme.Gold : Theme.Edge;
                c.Frame.GetComponent<Outline>().effectDistance = i == _selected ? new Vector2(2, -2) : new Vector2(1, -1);
            }
        }

        void RefreshScene()
        {
            var tex = ArtLibrary.Get(_game.Here.Art);
            AudioDirector.Music(_game.Ended ? "ending_" + _game.State.Ending : _game.InCombat ? (_game.State.Combat!.Enemies.Any(e => e.Boss && e.Alive) ? "boss" : "battle") : _game.State.Location == "tavern" ? "tavern" : AudioDirector.Zone(_game.Here.Art));
            _sceneBg.texture = tex;
            _sceneBg.uvRect = ArtLibrary.Cover(tex, 1008, 672);
            _placeText.text = _game.Here.Name;

            UiKit.Clear(_enemyRow);
            UiKit.Clear(_npcBox);
            var combat = _game.State.Combat;
            if (combat != null)
            {
                var enemies = combat.Enemies;
                float w = Mathf.Min(260, 940f / enemies.Count - 16);
                float startX = (968 - (w + 16) * enemies.Count + 16) / 2;
                var target = Draft(Actor).Target;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    var x = startX + i * (w + 16);
                    var big = e.Boss ? 1.25f : 1f;
                    var h = w * 1.5f * big;
                    var art = ArtLibrary.Get(e.Art);
                    var pic = UiKit.Picture(_enemyRow, art, x, 400 - h, w, h);
                    pic.uvRect = ArtLibrary.Cover(art, w, h, 0.5f);
                    pic.color = e.Alive ? Color.white : new Color(0.25f, 0.25f, 0.25f, 0.7f);
                    pic.raycastTarget = true;
                    var uid = e.Uid;
                    var b = pic.gameObject.AddComponent<Button>();
                    b.onClick.AddListener(() => { Draft(Actor).Target = uid; RefreshScene(); RefreshDraftSummary(); });
                    var outline = pic.gameObject.AddComponent<Outline>();
                    outline.effectColor = e.Uid == target && e.Alive ? Theme.Gold : Theme.Edge;
                    outline.effectDistance = e.Uid == target ? new Vector2(3, -3) : new Vector2(1, -1);
                    var plate = UiKit.Box(_enemyRow, "Plate", new Color(0, 0, 0, 0.7f), frame: true);
                    plate.rectTransform.Place(x, 410, w, 58);
                    UiKit.Label(plate.transform, e.Name + (e.Boss ? " <size=13><color=#e98>ボス</color></size>" : "") + (e.Stunned > 0 ? " <size=13>(動けない)</size>" : ""),
                        17, Theme.Ink, 8, 4, w - 16, 26, serif: true, align: TextAnchor.UpperCenter);
                    var bar = UiKit.Bar(plate.transform, 10, 36, w - 20, 10, e.Boss ? Theme.Warn : Theme.Green);
                    bar.fillAmount = (float)e.Hp / e.MaxHp;
                }
            }
            else
            {
                var npc = _game.Scenario.Npcs.FirstOrDefault(n => n.Where == _game.State.Location && _game.HasMet(n));
                if (npc != null)
                {
                    var art = ArtLibrary.Get(npc.Art);
                    var pic = UiKit.Picture(_npcBox, art, 0, 0, 280, 420);
                    pic.uvRect = ArtLibrary.Cover(art, 280, 420, 0.6f);
                    var plate = UiKit.Box(_npcBox, "Plate", new Color(0, 0, 0, 0.7f));
                    plate.rectTransform.Place(0, 384, 280, 36);
                    UiKit.Label(plate.transform, npc.Name, 18, Theme.Ink, serif: true, align: TextAnchor.MiddleCenter).rectTransform.Fill();
                }
            }
            var pending = combat?.BigMovePending;
            _warnBanner.SetActive(pending != null);
            if (pending != null)
            {
                var boss = combat!.Enemies.First(e => e.Uid == pending);
                _warnText.text = (_app.Db.Enemies[boss.DefId].BigMove?.Warning ?? "") + "（次の手番に大技）";
            }
        }

        static bool HasAction(Declaration d) => d.CardId != null || d.Basic != null || d.Text.Trim().Length > 0;

        void RefreshActions()
        {
            var who = Actor;
            var draft = Draft(who);
            _actorHeader.text = $"{who.Name}の行動";
            _coinText.text = $"運命のコイン {new string('●', who.FateCoins)}{new string('○', 1 - Math.Min(1, who.FateCoins))}";
            // Never while the player is typing: a friend declaring refreshes this, and resetting the field mid-IME mixed old and new text.
            if (_input.text != draft.Text && !_input.isFocused) _input.SetTextWithoutNotify(draft.Text);

            UiKit.Clear(_cardRow);
            for (int i = 0; i < who.Cards.Count; i++)
            {
                if (!_app.Db.Cards.TryGetValue(who.Cards[i], out var card)) continue;
                var cw = who.Cards.Count <= 3 ? 150f : 472f / who.Cards.Count - 6;
                BuildCard(card, who, draft, 8 + i * (cw + 6), cw);
            }

            UiKit.Clear(_basicCol);
            var basics = _game.InCombat
                ? new[] { ("attack", $"{_app.Db.Classes[who.ClassId].Weapon}で攻撃"), ("defend", "身を守る"), ("help", "仲間を援護"), ("revive", "倒れた仲間を起こす"), ("flee", "逃げる") }
                : new[] { ("revive", "倒れた仲間を起こす") };
            for (int i = 0; i < basics.Length; i++)
            {
                var (id, label) = basics[i];
                var b = UiKit.Button(_basicCol, label, () => { draft.Basic = draft.Basic == id ? null : id; if (draft.Basic != null) draft.CardId = null; DefaultTarget(draft); RefreshActions(); RefreshParty(); },
                    0, i * 48, 160, 42, 16);
                b.GetComponent<Image>().color = draft.Basic == id ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                b.interactable = who.CanAct && (id != "revive" || _game.Party.Any(p => p.State == LifeState.Down));
            }

            UiKit.Clear(_suggestBox);
            var sug = _game.Ended ? new List<string>() : _game.State.Suggestions;
            for (int i = 0; i < sug.Count && i < 3; i++)
            {
                var text = sug[i];
                UiKit.Button(_suggestBox, text, () => { draft.Text = text; _input.SetTextWithoutNotify(text); RefreshDraftSummary(); RefreshParty(); },
                    (i % 2) * 316, (i / 2) * 46, 308, 42, 16);
            }

            UiKit.Clear(_allyRow);
            var needsAlly = draft.Basic == "help" || draft.Basic == "revive" ||
                (draft.CardId != null && _app.Db.Cards.TryGetValue(draft.CardId, out var cd) && cd.Target == "ally");
            if (needsAlly)
            {
                UiKit.Label(_allyRow, "相手:", 16, Theme.Dim, 0, 6, 50, 24);
                var allies = _game.Party.Where(p => p != who || draft.CardId != null).ToList();
                for (int i = 0; i < allies.Count; i++)
                {
                    var name = allies[i].Name;
                    var b = UiKit.Button(_allyRow, name, () => { draft.Target = name; RefreshActions(); }, 54 + i * 130, 0, 122, 32, 16);
                    b.GetComponent<Image>().color = draft.Target == name ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                }
            }
            else if (_game.InCombat && (draft.Basic == "attack" || draft.CardId != null))
                UiKit.Label(_allyRow, "狙う敵は、場面の敵の絵を押して選べます", 15, Theme.Dim, 0, 8, 600, 24);

            _input.interactable = who.CanAct;
            _declare.interactable = !_busy && !_game.Ended;
            RefreshDraftSummary();
        }

        void BuildCard(CardDef card, Character who, Declaration draft, float x, float w = 150)
        {
            var on = draft.CardId == card.Id;
            var frame = UiKit.Box(_cardRow, "Card", Theme.Hex("2a2017"), frame: true);
            frame.rectTransform.Place(x, 0, w, 236);
            frame.GetComponent<Outline>().effectColor = on ? Theme.GoldLight : Theme.Gold;
            frame.GetComponent<Outline>().effectDistance = on ? new Vector2(3, -3) : new Vector2(1, -1);
            var art = ArtLibrary.Get("cd_" + card.Id);
            var pic = UiKit.Picture(frame.transform, art, 6, 6, w - 12, 96);
            pic.uvRect = ArtLibrary.Cover(art, w - 12, 96, 0.55f);
            UiKit.Label(frame.transform, card.Name, w < 120 ? 15 : 18, Theme.GoldLight, 6, 106, w - 10, 26, serif: true);
            UiKit.Label(frame.transform, card.Text, w < 120 ? 11 : 13, Theme.Dim, 6, 134, w - 10, 74);
            UiKit.Label(frame.transform, $"気力 {card.Cost}", 13, Theme.Hex("8fb3e0"), 6, 210, w - 10, 20);
            var b = frame.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;   // the frame shows the state itself (gold outline, dark when unaffordable)
            b.interactable = who.CanAct && who.Focus >= card.Cost;
            b.onClick.AddListener(() => { draft.CardId = on ? null : card.Id; if (draft.CardId != null) draft.Basic = null; DefaultTarget(draft); RefreshActions(); RefreshParty(); RefreshScene(); });
            if (!b.interactable) frame.color = Theme.Hex("17120d");
        }

        void DefaultTarget(Declaration d)
        {
            if (!_game.InCombat) return;
            var alive = _game.State.Combat!.Enemies.Where(e => e.Alive).ToList();
            if ((d.Basic == "attack" || (d.CardId != null && _app.Db.Cards[d.CardId].Target == "enemy")) && !alive.Any(e => e.Uid == d.Target))
                d.Target = alive.FirstOrDefault()?.Uid;
            if (d.Basic == "revive") d.Target = _game.Party.FirstOrDefault(p => p.State == LifeState.Down)?.Name;
        }

        void RefreshDraftSummary()
        {
            var parts = _game.Party.Where(p => p.CanAct).Select(p =>
            {
                var d = Draft(p);
                var what = d.CardId != null ? _app.Db.Cards[d.CardId].Name
                    : d.Basic != null ? d.Basic switch { "attack" => "攻撃", "defend" => "身を守る", "help" => "援護", "revive" => "起こす", "flee" => "逃げる", _ => d.Basic }
                    : d.Text.Trim().Length > 0 ? Short(d.Text) : "—";
                return $"{p.Name}: {what}";
            });
            _draftSummary.text = string.Join("　", parts);
        }

        static string Short(string s) { s = s.Trim().Replace("\n", " "); return s.Length > 12 ? s.Substring(0, 12) + "…" : s; }

        void Select(int index)
        {
            if (index < 0 || index >= _game.Party.Count || !IsMine(_game.Party[index])) return;
            _selected = index;
            RefreshParty(); RefreshActions(); RefreshScene();
        }

        // ================================================================ turns

        async Task OpenAsync()
        {
            RefreshAll();
            await RunAsync(() => _game.OpenAsync());
        }

        async Task SubmitAsync()
        {
            if (_busy || _game.Ended) return;
            if (_role != NetRole.Solo) { DeclareOnline(); return; }
            var decls = _game.Party.Where(p => p.CanAct || (_drafts.TryGetValue(p.Name, out var d) && d.Basic == "revive"))
                .Select(Draft).Where(HasAction).ToList();
            if (decls.Count == 0) { Flash("誰かの行動を決めてください（カード、基本の行動、または文章）"); return; }
            var ok = await RunAsync(() => _game.SubmitAsync(decls));
            if (ok) { _drafts.Clear(); _input.SetTextWithoutNotify(""); }
        }

        async Task<bool> RunAsync(Func<Task<TurnReport>> step)
        {
            _busy = true;
            _busyText.text = "語り部が考えている…";
            _declare.interactable = false;
            if (_role == NetRole.Host) BroadcastStatus();
            try
            {
                var report = await step();
                EnqueueNew(report.Entries);
                _app.Save(_game);
                if (_game.Ended) { try { System.IO.File.Delete(_app.SavePath); } catch { } }
                AfterHostTurn();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Table] " + ex);
                var why = AiNotices.Failed(ex, _app.Ai.LocalOnly(), DateTimeOffset.UtcNow, AiSetup.LocalOffset)
                    ?? "語り部が言葉に詰まった（" + Short(ex.Message) + "）。もう一度宣言してください。";
                _pending.Enqueue(LogEntry.Line(LogKind.Warning, "", why));
                return false;
            }
            finally
            {
                _busy = false;
                _busyText.text = "";
                if (Playing) _viewStale = true; else { RefreshTop(); RefreshParty(); }
                RefreshActions();
                if (_role == NetRole.Host) BroadcastTurn();
            }
        }

        /// <summary>Queues log lines not shown yet (by sequence number; game-less warnings have 0 and always show).</summary>
        /// <summary>
        /// The scene, the party and the top bar wait for the lines being played: the state is already the end of the turn, and
        /// drawing it at once showed a fight won or a place left before the dice that decided it had rolled.
        /// </summary>
        bool _viewStale;
        bool Playing => _pending.Count > 0 || _diceRolling || _typing != null;

        /// <summary>
        /// Brings the board up to the game state. That state is already the end of the turn, so it waits for the telling of
        /// the turn (the rule lines play over the old board), and for the host never runs while the turn is still being worked
        /// out (the boss was gone and the ending music played before the dice).
        /// </summary>
        void RefreshView()
        {
            if (_busy && _role != NetRole.Client) { _viewStale = true; return; }
            RefreshTop(); RefreshParty(); RefreshScene();
        }

        void EnqueueNew(IEnumerable<LogEntry> entries)
        {
            foreach (var e in entries)
            {
                if (e.Seq != 0 && !_shownSeq.Add(e.Seq)) continue;
                _pending.Enqueue(e);
                _viewStale = true;
                if (_autoTurns > 0) Debug.Log($"[Log] {e.Seq} {e.Kind} {e.Who}: {(e.Text.Length > 30 ? e.Text.Substring(0, 30) : e.Text)}");
            }
        }

        // ================================================================ per-frame

        public void Update()
        {
            if (!_game.Ended) _game.State.PlayMinutes += Time.unscaledDeltaTime / 60f;
            // the host hears when a free AI quota runs out and another model takes over
            if (_role != NetRole.Client) while (_app.Ai.Notices.TryDequeue(out var notice)) _pending.Enqueue(LogEntry.Line(LogKind.Warning, "", notice));
            if (Time.frameCount % 30 == 0) { if (!Playing && !_busy) RefreshTop(); if (_role == NetRole.Host && _pending.Count == 0 && _typing == null) TrySubmitHost(); }
            Playback();
            Autoplay();
        }

        void Playback()
        {
            var speed = _autoTurns > 0 ? 3f : 1f;
            if (_typing != null)
            {
                _typingPos = float.IsPositiveInfinity(Reading.CharsPerSecond) ? _typingFull.Length : _typingPos + Time.deltaTime * Reading.CharsPerSecond * speed;
                var n = Mathf.Min(_typingFull.Length, (int)_typingPos);
                _typing.text = _typingFull.Substring(0, n);
                if (n >= _typingFull.Length) { _typing = null; _holdUntil = Time.time + 0.2f; }
                ScrollToEnd();
                return;
            }
            if (_diceRolling || Time.time < _holdUntil) return;
            if (_pending.Count == 0)
            {
                if (_viewStale) { _viewStale = false; RefreshView(); }
                if (_drained != null) { var d = _drained; _drained = null; d.TrySetResult(true); return; }
                if (_diceImage.gameObject.activeSelf && Time.time > _holdUntil && _rerollBox == null) HideDice();
                return;
            }
            var e = _pending.Dequeue();
            if (e.Kind == LogKind.Roll && e.Natural > 0)
            {
                _diceRolling = true;
                _diceImage.gameObject.SetActive(true);
                _diceCaption.transform.parent.gameObject.SetActive(true);
                _diceCaption.text = e.Who + "　" + (e.Text.Contains("死のサイコロ") ? "死のサイコロ" : e.Reason.Length > 0 ? e.Reason : "判定") + "…";
                AudioDirector.Play("dice_roll");
                _app.Dice.Roll(e.Natural, 2.4f / speed, () =>
                {
                    _diceRolling = false;
                    // (each bounce on the tray already knocked: AppMain hears Dice.Bounced)
                    if (e.Natural == 20) AudioDirector.Play("crit"); else if (e.Natural == 1) AudioDirector.Play("fumble");
                    _diceCaption.text = $"{e.Text}";
                    _holdUntil = Time.time + 0.9f / speed;
                    AppendLog(e, instant: true);
                });
                return;
            }
            HideDice();
            AppendLog(e, instant: false);
            if (e.Kind != LogKind.Narration) _holdUntil = Time.time + 0.15f / speed;
            // the board catches up with the telling of the turn (dice and rule lines play over the board as it was)
            if (e.Kind == LogKind.Narration) { _viewStale = false; RefreshView(); }
        }

        void HideDice()
        {
            _app.Dice.Hide();
            _diceImage.gameObject.SetActive(false);
            _diceCaption.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>The sound that goes with a new log line.</summary>
        static void SoundFor(LogEntry e)
        {
            var t = e.Text;
            if (e.Kind == LogKind.Narration) AudioDirector.Play("page", 0.5f);
            else if (e.Kind == LogKind.Warning && e.Seq != 0) AudioDirector.Play("warn");
            else if (e.Kind != LogKind.Event) return;
            else if (t.StartsWith("戦闘開始")) AudioDirector.Play("enemy");
            else if (t == "勝利") AudioDirector.Play("victory");
            else if (t.Contains("物語の結末: 失敗")) AudioDirector.Play("defeat");
            else if (t.Contains("成長した")) AudioDirector.Play("growth");
            else if (t.Contains("ダメージ")) AudioDirector.Play("hit");
            else if (t.Contains("当たらなかった") || t.Contains("かわされた") || t.Contains("かわした") || t.Contains("空を切った")) AudioDirector.Play("miss");
            else if (t.Contains("HP が")) AudioDirector.Play("heal");
            else if (t.Contains("動きが止まった") || t.Contains("眠りに落ちた") || t.Contains("煙に包まれた")) AudioDirector.Play("spell");
        }

        void AppendLog(LogEntry e, bool instant)
        {
            if (!instant) SoundFor(e);
            var row = UiKit.Rect("Entry", _logContent);
            row.gameObject.AddComponent<LayoutElement>();
            var fitter = row.gameObject.AddComponent<VerticalLayoutGroup>();
            fitter.childControlHeight = true; fitter.childControlWidth = true; fitter.childForceExpandHeight = false;
            switch (e.Kind)
            {
                case LogKind.Narration:
                {
                    fitter.spacing = 2;
                    UiKit.Label(row, e.Who, Reading.Font(14), Theme.Gold, serif: true);
                    var wrapped = UiKit.Kinsoku(e.Text, Theme.Serif, Reading.Font(21), FontStyle.Normal, 512);
                    var t = UiKit.Label(row, instant ? wrapped : "", Reading.Font(21), Theme.Hex("efe3c8"), serif: true);
                    t.lineSpacing = 1.15f;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    if (!instant) { _typing = t; _typingFull = wrapped; _typingPos = 0; }
                    break;
                }
                case LogKind.Declaration:
                {
                    var color = DeclColor(e.Who);
                    fitter.padding = new RectOffset(10, 4, 4, 4);
                    var bg = row.gameObject.AddComponent<Image>(); bg.color = new Color(1, 1, 1, 0.035f);
                    UiKit.Label(row, $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{e.Who}</color>：{e.Text}", Reading.Font(16), Theme.Ink);
                    break;
                }
                case LogKind.Roll:
                    fitter.padding = new RectOffset(12, 4, 0, 0);
                    UiKit.Label(row, $"🎲 {e.Text}".Replace("🎲 ", "◆ "), Reading.Font(15), e.Outcome.Contains("成功") ? Theme.Hex("c8d6a0") : Theme.Hex("c9a39a"));
                    break;
                case LogKind.Warning:
                {
                    // wrapped by hand like the narration (Unity would break only at the spaces in "Gemini 3.5 Flash-Lite")
                    var w = UiKit.Label(row, UiKit.Kinsoku("⚠ " + e.Text, Theme.Serif, Reading.Font(17), FontStyle.Normal, 496), Reading.Font(17), Theme.Hex("ffb8a0"), serif: true);
                    w.horizontalOverflow = HorizontalWrapMode.Overflow;
                    break;
                }
                default:
                    fitter.padding = new RectOffset(12, 4, 0, 0);
                    UiKit.Label(row, (e.Who.Length > 0 && !e.Text.StartsWith(e.Who) ? e.Who + "　" : "") + e.Text, Reading.Font(15), Theme.Gold);
                    break;
            }
            ScrollToEnd();
        }

        Color DeclColor(string who)
        {
            var i = _game.Party.FindIndex(p => p.Name == who);
            return i switch { 0 => Theme.Hex("d98a6a"), 1 => Theme.Hex("8aa8e0"), 2 => Theme.Hex("a6d08a"), _ => Theme.Hex("d8c27a") };
        }

        void ScrollToEnd()
        {
            Canvas.ForceUpdateCanvases();
            _logScroll.verticalNormalizedPosition = 0;
        }

        void Flash(string message) => _pending.Enqueue(LogEntry.Line(LogKind.Warning, "", message));

        // ================================================================ overlays

        void ShowMap()
        {
            UiKit.Clear(_overlay.transform);
            var shade = UiKit.Box(_overlay.transform, "Shade", new Color(0, 0, 0, 0.6f));
            shade.rectTransform.Fill();
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => UiKit.Clear(_overlay.transform));
            var panel = UiKit.Panel(_overlay.transform, "Map", 360, 140, 1200, 760).transform;
            UiKit.Label(panel, "地図", 30, Theme.Gold, 30, 20, 400, 44, serif: true);
            UiKit.Button(panel, "閉じる", () => UiKit.Clear(_overlay.transform), 1060, 20, 110, 40, 18);

            // Columns by distance from the start; only places the party has seen (visited, or next to a visited place).
            var s = _game.Scenario;
            var depth = new Dictionary<string, int> { [s.Start] = 0 };
            var queue = new Queue<string>(new[] { s.Start });
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                foreach (var x in s.Location(id).Exits) if (!depth.ContainsKey(x)) { depth[x] = depth[id] + 1; queue.Enqueue(x); }
            }
            var visited = _game.State.Visited.ToHashSet();
            var known = visited.Concat(visited.SelectMany(v => s.Location(v).Exits)).ToHashSet();
            var pos = new Dictionary<string, Vector2>();
            foreach (var col in depth.Where(kv => known.Contains(kv.Key)).GroupBy(kv => kv.Value))
            {
                var ids = col.Select(kv => kv.Key).ToList();
                for (int i = 0; i < ids.Count; i++)
                    pos[ids[i]] = new Vector2(80 + col.Key * 150, 380 + (i - (ids.Count - 1) / 2f) * 150);
            }
            foreach (var a in pos)
                foreach (var b in s.Location(a.Key).Exits.Where(pos.ContainsKey))
                    if (string.CompareOrdinal(a.Key, b) < 0) Line(panel, a.Value, pos[b]);
            foreach (var kv in pos)
            {
                var here = kv.Key == _game.State.Location;
                var seen = visited.Contains(kv.Key);
                var dot = UiKit.Box(panel, "Node", here ? Theme.Gold : seen ? Theme.Hex("6b5a40") : Theme.Hex("2a231b"), frame: true);
                dot.rectTransform.Place(kv.Value.x - 14, kv.Value.y - 14, 28, 28);
                UiKit.Label(panel, seen ? s.Location(kv.Key).Name : "？", 16, here ? Theme.GoldLight : seen ? Theme.Ink : Theme.Dim,
                    kv.Value.x - 70, kv.Value.y + 18, 140, 48, serif: true, align: TextAnchor.UpperCenter);
            }
            var clues = _game.State.Clues.Select(id => "・" + s.Clues.First(c => c.Id == id).Text);
            UiKit.Label(panel, "見つけた手がかり\n" + (clues.Any() ? string.Join("\n", clues) : "（まだない）"), 17, Theme.Ink, 30, 600, 1140, 150);
        }

        static void Line(Transform parent, Vector2 a, Vector2 b)
        {
            var img = UiKit.Box(parent, "Line", Theme.Edge);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(a.x, -a.y);
            var d = b - a;
            rt.sizeDelta = new Vector2(d.magnitude, 3);
            rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        void ShowOrder()
        {
            UiKit.Clear(_overlay.transform);
            var shade = UiKit.Box(_overlay.transform, "Shade", new Color(0, 0, 0, 0.6f));
            shade.rectTransform.Fill();
            var panel = UiKit.Panel(_overlay.transform, "Order", 560, 280, 800, 440).transform;
            UiKit.Label(panel, "語り部への注文", 28, Theme.Gold, 30, 20, 500, 40, serif: true);
            UiKit.Label(panel, "物語の外から語り部に頼めます。次の語りから反映されます。\n例:「もっと怖く」「さっきの猟師をまた出して」", 17, Theme.Dim, 30, 70, 740, 60);
            var field = UiKit.Input(panel, "注文を書く…", 30, 140, 740, 120, 20, multiline: true);
            field.text = _game.StorytellerRequest ?? "";

            UiKit.Label(panel, "語りの長さ", 18, Theme.Dim, 30, 282, 140, 30);
            var lengths = new[] { ("short", "短め"), ("normal", "普通"), ("long", "長め") };
            var lengthButtons = new List<Button>();
            for (int i = 0; i < lengths.Length; i++)
            {
                var (id, label) = lengths[i];
                var b = UiKit.Button(panel, label, () => { _game.State.NarrationLength = id; foreach (var x in lengthButtons) x.GetComponent<Image>().color = Theme.Hex("2e241a");
                    lengthButtons[Array.FindIndex(lengths, l => l.Item1 == id)].GetComponent<Image>().color = Theme.Hex("6b4f24"); }, 170 + i * 130, 276, 120, 40, 18);
                b.GetComponent<Image>().color = _game.State.NarrationLength == id ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
                lengthButtons.Add(b);
            }
            UiKit.Button(panel, "頼む", () => { SendOrder(field.text.Trim()); UiKit.Clear(_overlay.transform);
                if (field.text.Trim().Length > 0) Flash("語り部への注文を伝えた: " + field.text.Trim()); }, 560, 362, 210, 52, 20, gold: true);
            UiKit.Button(panel, "閉じる", () => UiKit.Clear(_overlay.transform), 330, 362, 210, 52, 20);
        }

        bool _recorded;

        /// <summary>The evening ended: write it into this PC's chronicle and keep this PC's characters for next time.</summary>
        void RecordEnding()
        {
            if (_recorded) return;
            _recorded = true;
            var chronicle = _app.LoadChronicle();
            if (chronicle.Add(Chronicle.From(_game, DateTime.Now))) _app.SaveChronicle(chronicle);
            var roster = _app.LoadRoster();
            roster.Record(_game, IsMine);
            _app.SaveRoster(roster);
        }

        void ShowEnding()
        {
            RecordEnding();
            UiKit.Clear(_overlay.transform);
            var shade = UiKit.Box(_overlay.transform, "Shade", new Color(0, 0, 0, 0.5f));
            shade.rectTransform.Place(328, 76, 1008, 672);
            var label = _game.State.Ending == "success" ? "成功" : _game.State.Ending == "bitter" ? "苦い成功" : "失敗";
            UiKit.Label(_overlay.transform, "物語の結末", 30, Theme.Dim, 328, 280, 1008, 50, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(_overlay.transform, label, 72, Theme.Gold, 328, 330, 1008, 100, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Button(_overlay.transform, "酒場（タイトル）へ戻る", _app.ShowTitle, 682, 470, 300, 60, 22, gold: true);
        }

        // ================================================================ growth between acts

        void ShowGrowthIfDue()
        {
            if (_busy || _pending.Count > 0 || _typing != null || _diceRolling || _overlay.transform.childCount > 0) return;
            AskRemoteGrowth();
            var name = MyPendingGrowth();
            if (name == null) return;
            var who = _game.Find(name);
            if (who == null) { _game.State.PendingGrowth.Remove(name); return; }
            if (_autoTurns > 0) { var learn = _game.GrowthCards(name).FirstOrDefault(); Grow(name, learn != null ? "card:" + learn.Id : _app.Db.Classes[who.ClassId].AttackStat); return; }

            var shade = UiKit.Box(_overlay.transform, "Shade", new Color(0, 0, 0, 0.55f));
            shade.rectTransform.Fill();
            var panel = UiKit.Panel(_overlay.transform, "Growth", 560, 230, 800, 600).transform;
            var pic = UiKit.Picture(panel, ArtLibrary.Get(who.Portrait), 30, 30, 160, 240);
            pic.uvRect = ArtLibrary.Cover(pic.texture, 160, 240, 0.7f);
            UiKit.Label(panel, $"{who.Name} は成長する", 32, Theme.Gold, 220, 30, 560, 46, serif: true);
            UiKit.Label(panel, $"第{_game.State.Act - 1}幕を越えた。伸ばしたいものを一つ選んでください。", 18, Theme.Dim, 220, 84, 560, 30);
            var stats = new[] { Stat.Might, Stat.Skill, Stat.Wits, Stat.Spirit };
            for (int i = 0; i < stats.Length; i++)
            {
                var st = stats[i];
                UiKit.Button(panel, $"{st.Label()} +{who.Modifier(st)} → +{Math.Min(5, who.Modifier(st) + 1)}", () => Grow(name, st.Label()),
                    220 + (i % 2) * 280, 130 + (i / 2) * 70, 268, 60, 22, gold: st.Label() == _app.Db.Classes[who.ClassId].AttackStat);
            }
            UiKit.Button(panel, $"最大 HP {who.MaxHp} → {who.MaxHp + TavernGame.ToughenAmount}", () => Grow(name, "hp"), 220, 270, 548, 60, 22);
            UiKit.Label(panel, "力=腕っぷし　技=器用さ・素早さ（かわす判定にも効く）　知=知識・魔法　心=意志・話術・信仰", 15, Theme.Dim, 30, 350, 740, 50);
            var cards = _game.GrowthCards(name);
            if (cards.Count > 0) UiKit.Label(panel, "または、新しい技を覚える", 18, Theme.GoldLight, 220, 400, 548, 28);
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var b = UiKit.Button(panel, $"「{card.Name}」（気力 {card.Cost}）\n<size=14>{card.Text}</size>", () => Grow(name, "card:" + card.Id), 30 + i * 380, 436, 360, 130, 19, gold: true);
                var art = ArtLibrary.Get("cd_" + card.Id);
                var cardPic = UiKit.Picture(b.transform, art, 6, 6, 80, 118);
                cardPic.uvRect = ArtLibrary.Cover(art, 80, 118, 0.55f);
                b.GetComponentInChildren<Text>().rectTransform.offsetMin = new Vector2(94, 6);
            }
        }

        void Grow(string name, string choice)
        {
            if (_role == NetRole.Client) { SendGrowth(name, choice); UiKit.Clear(_overlay.transform); return; }
            _game.Grow(name, choice);
            AudioDirector.Play("growth");
            if (_role == NetRole.Host) BroadcastTurn();
            UiKit.Clear(_overlay.transform);
            _app.Save(_game);
            var last = _game.State.Log.LastOrDefault();
            if (last != null) AppendLog(last, instant: true);
            RefreshParty();
        }

        // ================================================================ autoplay (checks without a person)

        void Autoplay()
        {
            ShowGrowthIfDue();
            if (_game.Ended)
            {
                // not while the storyteller is still writing the closing (the ending is known before it is told)
                if (!_busy && _pending.Count == 0 && _typing == null && !_diceRolling && _overlay.transform.childCount == 0) ShowEnding();
                return;
            }
            if (_autoTurns <= 0 || _busy || _pending.Count > 0 || _typing != null || _diceRolling || _game.State.PendingGrowth.Count > 0) return;
            // Online, the bot only plays this PC's characters, once per turn.
            if (_role != NetRole.Solo && _game.Party.Where(IsMine).Where(p => p.CanAct).All(p => _declared.Contains(p.Name))) return;
            if (_autoWait <= 0) { _autoWait = Time.time + 1.2f; return; }
            if (Time.time < _autoWait) return;
            _autoWait = 0;
            _autoTurns--;
            var sug = _game.State.Suggestions;
            for (int i = 0; i < _game.Party.Count; i++)
            {
                var p = _game.Party[i];
                if (!p.CanAct || !IsMine(p)) continue;
                var d = Draft(p);
                if (_game.InCombat)
                {
                    var downed = _game.Party.FirstOrDefault(x => x.State == LifeState.Down);
                    var card = p.Cards.Select(id => _app.Db.Cards[id]).FirstOrDefault(c => (c.Kind == "attack" || (c.Kind == "heal" && downed != null)) && p.Focus >= c.Cost);
                    if (downed != null && card?.Kind == "heal") { d.CardId = card.Id; d.Target = downed.Name; }
                    else if (card != null && i % 2 == 0) { d.CardId = card.Id; DefaultTarget(d); }
                    else { d.Basic = "attack"; DefaultTarget(d); }
                }
                else d.Text = sug.Count > 0 ? sug[i % sug.Count] : "辺りを調べる";
            }
            Select(_game.Party.FindIndex(IsMine));
            _ = SubmitAsync();
        }
    }
}
