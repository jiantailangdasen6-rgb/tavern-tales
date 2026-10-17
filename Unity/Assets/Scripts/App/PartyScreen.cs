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
        };

        readonly OnlineMode _mode;
        Func<Tavern.Core.Gm.Storyteller>? _teller;
        Func<StoryChoice>? _story;
        InputField? _playerName, _code;

        public PartyScreen(AppMain app, OnlineMode mode = OnlineMode.None) { _app = app; _mode = mode; }

        /// <summary>The sample party for -quickstart.</summary>
        public static List<Character> Preset(ContentDb db) => Samples
            .Select(s => TavernGame.CreateCharacter(db, s.name, s.cls, s.bg, $"pc_{s.cls}_{(s.female ? "f" : "m")}")).ToList();

        public void Build(Transform root)
        {
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            var shade = UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.78f));
            shade.rectTransform.Fill();

            UiKit.Label(root, "今宵の冒険者たち", 54, Theme.Gold, 0, 40, 1920, 80, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(root, "職業と見た目を選び、名前と生い立ちを書いてください（1〜3 人）", 22, Theme.Dim, 0, 118, 1920, 40, align: TextAnchor.MiddleCenter);

            if (_mode == OnlineMode.None) for (int i = 0; i < 3; i++) _slots.Add(BuildSlot(root, i, 90 + i * 590));
            else BuildOnline(root);
            UiKit.Button(root, "おまかせで埋める", FillSamples, 90, 980, 300, 64, 22);
            if (_mode == OnlineMode.None) { _teller = StorytellerPicker.Build(root, 630, 966); _story = StoryChooser.Build(root, _app.Story, _app.Scenario.Title, 90, 876); }
            UiKit.Button(root, "もどる", _app.ShowTitle, 410, 980, 200, 64, 22);
            _start = UiKit.Button(root, _mode == OnlineMode.Host ? "部屋を作る" : _mode == OnlineMode.Join ? "部屋に入る" : "この顔ぶれで酒場へ", Begin, 1430, 980, 400, 70, 28, gold: true);
            SetEnabled(_slots[0], true);
            Refresh();
        }

        Slot BuildSlot(Transform root, int index, float x)
        {
            var s = new Slot();
            var panel = UiKit.Panel(root, "Slot" + index, x, 180, 560, 680);
            s.Body = UiKit.Rect("Body", panel.transform).Fill().gameObject;
            var body = s.Body.transform;

            s.Portrait = UiKit.Picture(body, null, 20, 20, 220, 330);
            UiKit.Box(s.Portrait.transform, "Edge", new Color(0, 0, 0, 0), frame: true).rectTransform.Fill();
            s.ClassText = UiKit.Label(body, "", 30, Theme.Gold, 260, 20, 280, 44, serif: true);
            s.Blurb = UiKit.Label(body, "", 18, Theme.Ink, 260, 70, 280, 110);
            var classes = new[] { "warrior", "mage", "thief", "priest" };
            for (int c = 0; c < classes.Length; c++)
            {
                var id = classes[c];
                var b = UiKit.Button(body, _app.Db.Classes[id].Name, () => { s.ClassId = id; Refresh(); }, 260 + (c % 2) * 140, 190 + (c / 2) * 56, 130, 48, 20);
                s.ClassButtons[id] = b.GetComponent<Image>();
            }
            UiKit.Button(body, "男 / 女", () => { s.Female = !s.Female; Refresh(); }, 260, 310, 270, 40, 18);

            UiKit.Label(body, "名前", 18, Theme.Dim, 20, 370, 200, 28);
            s.Name = UiKit.Input(body, "例: ガレス", 20, 398, 520, 48, 22);
            UiKit.Label(body, "生い立ち・性格（自由に）", 18, Theme.Dim, 20, 458, 400, 28);
            s.Background = UiKit.Input(body, "例: 没落した騎士の家の三男。家名を取り戻したい", 20, 486, 520, 120, 20, multiline: true);
            UiKit.Button(body, "この席を空ける", () => { SetEnabled(s, false); Refresh(); }, 340, 620, 200, 44, 18);
            UiKit.Button(body, "前の冒険者を呼ぶ", () => PickFromRoster(s), 20, 620, 300, 44, 18, gold: true);

            s.Empty = UiKit.Rect("Empty", panel.transform).Fill().gameObject;
            UiKit.Button(s.Empty.transform, "＋ 冒険者を加える", () => { SetEnabled(s, true); Refresh(); }, 130, 300, 300, 80, 24);
            s.ClassId = classes[index % 4];
            SetEnabled(s, false);
            return s;
        }

        /// <summary>Online: one adventurer per PC, plus the player's name (and the room code when joining).</summary>
        void BuildOnline(Transform root)
        {
            _slots.Add(BuildSlot(root, 0, 90));
            var panel = UiKit.Panel(root, "Online", 680, 180, 1150, 330).transform;
            UiKit.Label(panel, _mode == OnlineMode.Host ? "友人と遊ぶ: 部屋を作る" : "友人の部屋に入る", 32, Theme.Gold, 30, 24, 900, 44, serif: true);
            UiKit.Label(panel, "あなたの名前（友人に表示されます）", 18, Theme.Dim, 30, 90, 600, 28);
            _playerName = UiKit.Input(panel, "例: たろう", 30, 120, 500, 50, 22);
            if (AppMain.Arg("-playername") is string pn) _playerName.text = pn;
            if (_mode == OnlineMode.Join)
            {
                UiKit.Label(panel, "部屋のコード（ホストから聞いた 6 文字）", 18, Theme.Dim, 30, 190, 600, 28);
                _code = UiKit.Input(panel, "例: ABC123", 30, 220, 500, 60, 30);
            }
            UiKit.Label(panel, "一人一人が自分の冒険者を 1 人作って持ち寄ります。AI はホストの PC だけで動きます。", 17, Theme.Dim, 580, 120, 540, 120);
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

        void SetEnabled(Slot s, bool on) { s.Enabled = on; s.Body.SetActive(on); s.Empty.SetActive(!on); }

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
                s.Portrait.uvRect = ArtLibrary.Cover(tex, 220, 330, 0.7f);
                foreach (var kv in s.ClassButtons) kv.Value.color = kv.Key == s.ClassId ? Theme.Hex("6b4f24") : Theme.Hex("2e241a");
            }
            _start.interactable = _slots.Any(s => s.Enabled);
        }

        void Begin()
        {
            var party = new List<Character>();
            var used = new HashSet<string>();
            foreach (var s in _slots.Where(s => s.Enabled))
            {
                var name = s.Name.text.Trim();
                if (name.Length == 0) name = _app.Db.Classes[s.ClassId].Name;
                while (!used.Add(name)) name += "二";
                if (s.FromRoster != null && s.FromRoster.Name == name && s.FromRoster.ClassId == s.ClassId) party.Add(s.FromRoster.ToCharacter(_app.Db));
                else party.Add(TavernGame.CreateCharacter(_app.Db, name, s.ClassId, s.Background.text.Trim(), s.Art));
            }
            if (party.Count == 0) return;
            if (_mode == OnlineMode.None) { _app.StartEvening(party, _teller?.Invoke(), _story?.Invoke()); return; }
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
    public enum OnlineMode { None, Host, Join }
}
