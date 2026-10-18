#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Content;
using Tavern.Core.Game;
using Tavern.Core.Rules;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Character creation: up to three adventurers (M1 is played alone, so one person makes them all).
    /// Pick a class, a look, a name, and write a background freely.
    /// </summary>
    public sealed class PartyScreen
    {
        readonly AppMain _app;
        readonly List<Slot> _slots = new List<Slot>();
        Button _start = null!;

        sealed class Slot
        {
            public bool Enabled;
            public string ClassId = "warrior";
            public bool Female;
            public InputField Name = null!, Background = null!;
            public RawImage Portrait = null!;
            public Text ClassText = null!, Blurb = null!;
            public GameObject Body = null!, Empty = null!;
            public readonly Dictionary<string, Image> ClassButtons = new Dictionary<string, Image>();
            public string Art => $"pc_{ClassId}_{(Female ? "f" : "m")}";
            /// <summary>A character brought back from an earlier evening (keeps titles and keepsakes).</summary>
            public Tavern.Core.Game.RosterEntry? FromRoster;
        }

        static readonly (string name, string cls, bool female, string bg)[] Samples =
        {
            ("ガレス", "warrior", false, "没落した騎士の家の三男。家名を取り戻したい。曲がったことが嫌い"),
            ("エリス", "mage", true, "魔法学院を追い出された。理由は語りたがらない。甘い物に目がない"),
            ("ロッコ", "thief", false, "港町の孤児。お調子者で、光る物を見るとつい手が伸びる"),
            ("セラ", "priest", true, "辺境の小さな教会で育った見習い司祭。怖がりだが、仲間のためなら前に出る"),
        };

        /// <summary>Up to four adventurers an evening (NetSession.MaxPlayers online).</summary>
        public const int MaxSeats = 4;
        const float SeatWidth = 420;

        readonly OnlineMode _mode;
        Func<Tavern.Core.Gm.Storyteller>? _teller;
        Func<StoryChoice>? _story;
        InputField? _playerName, _code;

        public PartyScreen(AppMain app, OnlineMode mode = OnlineMode.None) { _app = app; _mode = mode; }

        /// <summary>The sample party for -quickstart.</summary>
        /// <summary>The sample party for tests and quick starts (three by default, up to four).</summary>
        public static List<Character> Preset(ContentDb db, int count = 3) => Samples.Take(Math.Max(1, Math.Min(MaxSeats, count)))
            .Select(s => TavernGame.CreateCharacter(db, s.name, s.cls, s.bg, $"pc_{s.cls}_{(s.female ? "f" : "m")}")).ToList();

        public void Build(Transform root)
        {
            AudioDirector.Music("tavern");
            _app.ResetPrefetch();
            _app.Tick = Tick;
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            var shade = UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.78f));
            shade.rectTransform.Fill();

            var roster = _mode == OnlineMode.Roster;
            UiKit.Label(root, roster ? "新しい冒険者を作る" : "今宵の冒険者たち", 54, Theme.Gold, 0, 40, 1920, 80, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(root, roster ? "作った冒険者は名簿に入り、次の晩に「前の冒険者を呼ぶ」で連れて行けます（1〜4 人）" : "職業と見た目を選び、名前と生い立ちを書いてください（1〜4 人）",
                22, Theme.Dim, 0, 118, 1920, 40, align: TextAnchor.MiddleCenter);

            if (_mode == OnlineMode.None || roster) for (int i = 0; i < MaxSeats; i++) _slots.Add(BuildSlot(root, i, 90 + i * (SeatWidth + 20)));
            else BuildOnline(root);
            UiKit.Button(root, "おまかせで埋める", FillSamples, 90, 980, 300, 64, 22);
            if (_mode == OnlineMode.None) { _teller = StorytellerPicker.Build(root, 630, 966); _story = StoryChooser.Build(root, _app.Story, _app.Prepared, 90, 876); }
            UiKit.Button(root, "もどる", roster ? _app.ShowRoster : (UnityEngine.Events.UnityAction)_app.ShowTitle, 410, 980, 200, 64, 22);
            _start = UiKit.Button(root, _mode == OnlineMode.Host ? "部屋を作る" : _mode == OnlineMode.Join ? "部屋に入る" : roster ? "名簿に加える" : "この顔ぶれで酒場へ", Begin, 1430, 980, 400, 70, 28, gold: true);
            SetEnabled(_slots[0], true);
            Refresh();
            if (AppMain.Flag("-fillparty")) FillSamples();   // tests: fill the seats, then -beginat <s> presses start
            _beginAt = float.TryParse(AppMain.Arg("-beginat"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var at) ? Time.unscaledTime + at : float.MaxValue;
        }

        Slot BuildSlot(Transform root, int index, float x)
        {
            var s = new Slot();
            var panel = UiKit.Panel(root, "Slot" + index, x, 180, SeatWidth, 680);
            s.Body = UiKit.Rect("Body", panel.transform).Fill().gameObject;
            var body = s.Body.transform;

            s.Portrait = UiKit.Picture(body, null, 20, 20, 150, 225);
            UiKit.Box(s.Portrait.transform, "Edge", new Color(0, 0, 0, 0), frame: true).rectTransform.Fill();
            s.ClassText = UiKit.Label(body, "", 28, Theme.Gold, 186, 18, 220, 40, serif: true);
            s.Blurb = UiKit.Label(body, "", 15, Theme.Ink, 186, 62, 220, 92);
            var classes = new[] { "warrior", "mage", "thief", "priest" };
            for (int c = 0; c < classes.Length; c++)
            {
                var id = classes[c];
                var b = UiKit.Button(body, _app.Db.Classes[id].Name, () => { s.ClassId = id; Refresh(); }, 186 + (c % 2) * 112, 160 + (c / 2) * 50, 104, 44, 18);
                s.ClassButtons[id] = b.GetComponent<Image>();
            }
            UiKit.Button(body, "男 / 女", () => { s.Female = !s.Female; Refresh(); }, 186, 262, 216, 36, 16);

            UiKit.Label(body, "名前", 17, Theme.Dim, 20, 300, 200, 26);
            s.Name = UiKit.Input(body, "例: ガレス", 20, 326, SeatWidth - 40, 44, 20);
            UiKit.Label(body, "生い立ち・性格（自由に）", 17, Theme.Dim, 20, 382, 360, 26);
            s.Background = UiKit.Input(body, "例: 没落した騎士の家の三男。家名を取り戻したい", 20, 408, SeatWidth - 40, 176, 18, multiline: true);
            UiKit.Button(body, "前の冒険者を呼ぶ", () => PickFromRoster(s), 20, 610, 200, 46, 17, gold: true);
            UiKit.Button(body, "この席を空ける", () => { SetEnabled(s, false); Refresh(); }, 230, 610, SeatWidth - 250, 46, 17);

            s.Empty = UiKit.Rect("Empty", panel.transform).Fill().gameObject;
            UiKit.Button(s.Empty.transform, "＋ 冒険者を加える", () => { SetEnabled(s, true); Refresh(); }, (SeatWidth - 300) / 2, 300, 300, 80, 24);
            s.ClassId = classes[index % 4];
            SetEnabled(s, false);
            return s;
        }

        /// <summary>Online: one adventurer per PC, plus the player's name (and the room code when joining).</summary>
        void BuildOnline(Transform root)
        {
            _slots.Add(BuildSlot(root, 0, 90));
            var panel = UiKit.Panel(root, "Online", 540, 180, 1290, 330).transform;
            UiKit.Label(panel, _mode == OnlineMode.Host ? "友人と遊ぶ: 部屋を作る" : "友人の部屋に入る", 32, Theme.Gold, 30, 24, 900, 44, serif: true);
            UiKit.Label(panel, "あなたの名前（友人に表示されます）", 18, Theme.Dim, 30, 90, 600, 28);
            _playerName = UiKit.Input(panel, "例: たろう", 30, 120, 500, 50, 22);
            if (AppMain.Arg("-playername") is string pn) _playerName.text = pn;
            if (_mode == OnlineMode.Join)
            {
                UiKit.Label(panel, "部屋のコード（ホストから聞いた 6 文字）", 18, Theme.Dim, 30, 190, 600, 28);
                _code = UiKit.Input(panel, "例: ABC123", 30, 220, 500, 60, 30);
                if (_app.InviteCode != null) _code.text = _app.InviteCode;
            }
            UiKit.Label(panel, "一人一人が自分の冒険者を 1 人作って持ち寄ります（4 人まで）。AI はホストの PC だけで動きます。", 17, Theme.Dim, 580, 120, 680, 120);
        }

        /// <summary>Lists the adventurers who survived earlier evenings on this PC; choosing one fills the seat.</summary>
        void PickFromRoster(Slot s)
        {
            var roster = _app.LoadRoster().Living.ToList();
            var root = s.Body.transform.parent.parent;
            var layer = UiKit.Rect("Roster", root).Fill();
            UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.75f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 460, 140, 1000, 800).transform;
            UiKit.Label(panel, "前の冒険者を呼ぶ", 32, Theme.Gold, 30, 20, 600, 44, serif: true);
            UiKit.Label(panel, "能力は職業の初めの値に戻ります。称号と記念の品（3 つまで）は持ち越します。", 17, Theme.Dim, 30, 70, 940, 28);
            UiKit.Button(panel, "やめる", () => UnityEngine.Object.Destroy(layer.gameObject), 800, 20, 170, 44, 18);
            if (roster.Count == 0) UiKit.Label(panel, "まだいません。一晩の冒険を生き延びた冒険者が、ここに並びます。", 20, Theme.Ink, 30, 130, 940, 60);
            var list = UiKit.Scroll(panel, 20, 110, 960, 670, out _, 8);
            foreach (var e in roster)
            {
                var entry = e;
                var cls = _app.Db.Classes.TryGetValue(e.ClassId, out var c) ? c.Name : e.ClassId;
                var b = UiKit.Button(list, $"{e.Name}（{cls}、{e.Nights} 晩）{(e.Titles.Count > 0 ? "　「" + string.Join("・", e.Titles) + "」" : "")}\n<size=15>{e.Background}{(e.Keepsakes.Count > 0 ? "　記念の品: " + string.Join("・", e.Keepsakes) : "")}</size>",
                    () =>
                    {
                        s.FromRoster = entry;
                        s.ClassId = entry.ClassId;
                        s.Female = entry.Portrait.EndsWith("_f");
                        s.Name.text = entry.Name;
                        s.Background.text = entry.Background;
                        UnityEngine.Object.Destroy(layer.gameObject);
                        Refresh();
                    }, 18);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
        }

        void SetEnabled(Slot s, bool on) { s.Enabled = on; s.Body.SetActive(on); s.Empty.SetActive(!on); if (!on) s.FromRoster = null; }

        void FillSamples()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var s = _slots[i];
                SetEnabled(s, true);
                s.ClassId = Samples[i].cls; s.Female = Samples[i].female;
                s.Name.text = Samples[i].name; s.Background.text = Samples[i].bg;
            }
            Refresh();
        }

        void Refresh()
        {
            foreach (var s in _slots)
            {
                var def = _app.Db.Classes[s.ClassId];
                s.ClassText.text = def.Name;
                s.Blurb.text = def.Blurb + $"\n力+{def.Might} 技+{def.Skill} 知+{def.Wits} 心+{def.Spirit}　HP {def.Hp}";
                var tex = ArtLibrary.Get(s.Art);
                s.Portrait.texture = tex;
                s.Portrait.uvRect = ArtLibrary.Cover(tex, 150, 225, 0.7f);
                foreach (var kv in s.ClassButtons) kv.Value.color = kv.Key == s.ClassId ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
            }
            _start.interactable = _slots.Any(s => s.Enabled);
        }

        List<Character> CurrentParty()
        {
            var party = new List<Character>();
            var used = new HashSet<string>();
            foreach (var s in _slots.Where(s => s.Enabled))
            {
                var name = s.Name.text.Trim();
                if (name.Length == 0) name = _app.Db.Classes[s.ClassId].Name;
                while (!used.Add(name)) name += "二";
                // The called adventurer only while the seat still shows them; changing the name, class or story makes a new one.
                if (s.FromRoster != null && s.FromRoster.Name == name && s.FromRoster.ClassId == s.ClassId && s.FromRoster.Background.Trim() == s.Background.text.Trim())
                    party.Add(s.FromRoster.ToCharacter(_app.Db));
                else party.Add(TavernGame.CreateCharacter(_app.Db, name, s.ClassId, s.Background.text.Trim(), s.Art));
            }
            return party;
        }

        // Writing the story ahead: once every adventurer has a name and nothing changed for a few seconds (solo evenings).
        string _lastKey = "";
        float _stableSince;

        float _beginAt;

        void Tick()
        {
            if (_start == null) { _app.Tick = null; return; }   // the screen is gone
            if (Time.unscaledTime >= _beginAt) { _beginAt = float.MaxValue; Begin(); return; }
            if (_mode != OnlineMode.None || _story == null || Time.frameCount % 15 != 0) return;
            var enabled = _slots.Where(s => s.Enabled).ToList();
            if (enabled.Count == 0 || enabled.Any(s => s.Name.text.Trim().Length == 0)) { _lastKey = ""; return; }
            var story = _story();
            var key = string.Join("|", enabled.Select(s => s.Name.text + "/" + s.ClassId + "/" + s.Background.text)) + story.Prepared + story.TemplateId + story.Request;
            if (key != _lastKey) { _lastKey = key; _stableSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _stableSince > 4f) _app.PrefetchStory(CurrentParty(), story);
        }

        /// <summary>Roster mode: the new adventurers join this PC's roster (a name already on it is kept as it is).</summary>
        void AddToRoster(List<Character> party)
        {
            var roster = _app.LoadRoster();
            foreach (var c in party)
                if (!roster.Living.Any(e => e.Name == c.Name))
                    roster.Characters.Add(new Tavern.Core.Game.RosterEntry { Id = Tavern.Core.Game.Roster.NewId(), Name = c.Name, ClassId = c.ClassId, Background = c.Background, Portrait = c.Portrait });
            _app.SaveRoster(roster);
            _app.ShowRoster();
        }

        void Begin()
        {
            var party = CurrentParty();
            if (party.Count == 0) return;
            if (_mode == OnlineMode.None) { _app.StartEvening(party, _teller?.Invoke(), _story?.Invoke()); return; }
            if (_mode == OnlineMode.Roster) { AddToRoster(party); return; }
            var player = (_playerName?.text ?? "").Trim();
            if (player.Length == 0) player = party[0].Name + "の中の人";
            _start.interactable = false;
            if (_mode == OnlineMode.Host) _app.HostOnline(player, party[0]);
            else _app.JoinOnline((_code?.text ?? "").Trim(), player, party[0]);
        }
    }
}

namespace Tavern.Unity.App
{
    /// <summary>None: a solo evening; Host / Join: online; Roster: making adventurers for the roster (no evening).</summary>
    public enum OnlineMode { None, Host, Join, Roster }
}
