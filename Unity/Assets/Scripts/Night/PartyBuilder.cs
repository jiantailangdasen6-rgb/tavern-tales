#nullable enable
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;
using Tavern.Unity.App;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// Tonight's adventurers (1-4, 09_Spec 3): class, look, name, background, or someone from the roster (their veteran marks and
    /// named techniques come along). The class text is shown, not a list of specialties.
    /// </summary>
    public sealed class PartyBuilder
    {
        const float SeatW = 440, SeatH = 640;
        readonly NightApp _n;
        readonly List<Seat> _seats = new List<Seat>();
        Transform _root = null!;
        static readonly string[] ClassIds = { "warrior", "mage", "thief", "priest" };

        sealed class Seat
        {
            public bool On;
            public string ClassId = "warrior";
            public bool Female;
            public RosterEntry? From;
            public GameObject Body = null!, Empty = null!;
            public RawImage Portrait = null!;
            public Text ClassName = null!, Stats = null!, ClassText = null!, FromText = null!;
            public InputField Name = null!, Background = null!;
            public Dictionary<string, Image> ClassButtons = new Dictionary<string, Image>();
        }

        readonly RoomMode _mode;
        InputField? _player, _code;
        Text? _note;
        Button _done = null!;

        public PartyBuilder(NightApp n, RoomMode mode = RoomMode.Alone) { _n = n; _mode = mode; }

        bool Online => _mode != RoomMode.Alone;

        public void Build(Transform root)
        {
            _root = root;
            UiKit.Picture(root, ArtLibrary.Get("bg_tavern"), 0, 0, 1920, 1080).color = new Color(0.35f, 0.3f, 0.26f);
            UiKit.Label(root, _mode switch { RoomMode.Host => "部屋を作る: あなたの冒険者", RoomMode.Join => "友人の部屋に入る: あなたの冒険者", _ => "今宵の冒険者たち" },
                44, Theme.Gold, 0, 30, 1920, 60, serif: true, align: TextAnchor.UpperCenter);
            UiKit.Label(root, Online ? "あなたが今夜使う冒険者を 1 人。職業と見た目を選び、名前と生い立ちを書いてください。名簿から前の冒険者も呼べます。"
                : "職業と見た目を選び、名前と生い立ちを書いてください（1〜4 人）。名簿から前の冒険者も呼べます。", 20, Theme.Ink, 0, 92, 1920, 30, align: TextAnchor.UpperCenter);
            for (int i = 0; i < (Online ? 1 : 4); i++) _seats.Add(BuildSeat(root, i));
            Enable(_seats[0], true);
            if (Online) BuildRoomBox(root);
            UiKit.Button(root, "もどる", _n.BackToTitle, 70, 960, 220, 64, 22);
            _done = UiKit.Button(root, _mode switch { RoomMode.Host => "部屋を作る", RoomMode.Join => "部屋に入る", _ => "この顔ぶれで地図へ" }, Done, 1460, 950, 400, 80, 28, gold: true);
            Refresh();
        }

        /// <summary>Online: the player's name in the room, and the room code to join (from an invite link if there was one).</summary>
        void BuildRoomBox(Transform root)
        {
            var box = UiKit.Panel(root, "Room", 70 + SeatW + 40, 140, 900, 400).transform;
            UiKit.Label(box, "あなたの名前（部屋でほかの人に見える）", 18, Theme.Dim, 30, 24, 800, 26);
            _player = UiKit.Input(box, "例: たろう", 30, 54, 500, 46, 20);
            _player.characterLimit = 12;
            _player.text = PlayerPrefs.GetString("night_player", "");
            if (_mode == RoomMode.Join)
            {
                UiKit.Label(box, "部屋のコード（ホストから聞く）", 18, Theme.Dim, 30, 124, 800, 26);
                _code = UiKit.Input(box, "例: ABC123", 30, 154, 500, 46, 22);
                _code.text = _n.App.InviteCode ?? "";
            }
            _note = UiKit.Label(box, _mode == RoomMode.Host
                ? "部屋を作ると、コードと招待リンクが出ます。友人にはそれを送ってください。語り部の AI はこの PC で動きます（4 人まで）。"
                : "途中で切れても、同じコードで入り直せば自分の冒険者に戻れます。", 17, Theme.Ink, 30, 230, 840, 140);
        }

        Seat BuildSeat(Transform root, int i)
        {
            var s = new Seat { ClassId = ClassIds[i % 4], Female = i % 2 == 1 };
            var panel = UiKit.Panel(root, "Seat" + i, 70 + i * (SeatW + 15), 140, SeatW, SeatH).transform;
            s.Body = UiKit.Rect("Body", panel).Fill().gameObject;
            var b = s.Body.transform;
            s.Portrait = UiKit.Picture(b, null, 16, 16, 150, 225);
            s.ClassName = UiKit.Label(b, "", 28, Theme.Gold, 182, 14, 240, 40, serif: true);
            s.Stats = UiKit.Label(b, "", 15, Theme.Ink, 182, 56, 246, 60);
            for (int c = 0; c < 4; c++)
            {
                var id = ClassIds[c];
                var btn = UiKit.Button(b, _n.Classes[id].Name, () => { if (s.From == null) { s.ClassId = id; Refresh(); } }, 182 + (c % 2) * 124, 118 + (c / 2) * 48, 116, 42, 18);
                s.ClassButtons[id] = btn.GetComponent<Image>();
            }
            UiKit.Button(b, "男 / 女", () => { s.Female = !s.Female; Refresh(); }, 182, 216, 240, 30, 15);
            s.ClassText = UiKit.Label(b, "", 14, Theme.Dim, 16, 250, SeatW - 32, 120);
            UiKit.Label(b, "名前", 16, Theme.Dim, 16, 372, 200, 24);
            s.Name = UiKit.Input(b, "例: ガレス", 16, 396, SeatW - 32, 42, 20);
            s.Name.characterLimit = 12;
            UiKit.Label(b, "生い立ち・性格（自由に）", 16, Theme.Dim, 16, 446, 300, 24);
            s.Background = UiKit.Input(b, "例: 没落した騎士の家の三男。家名を取り戻したい", 16, 470, SeatW - 32, 100, 17, multiline: true);
            s.Background.characterLimit = 300;
            s.FromText = UiKit.Label(b, "", 14, Theme.GoldLight, 16, 574, SeatW - 32, 20);
            UiKit.Button(b, "名簿から呼ぶ", () => PickFromRoster(s), 16, 594, 200, 36, 16, gold: true);
            if (!Online) UiKit.Button(b, "この席を空ける", () => { Enable(s, false); Refresh(); }, 226, 594, SeatW - 242, 36, 16);
            s.Empty = UiKit.Rect("Empty", panel).Fill().gameObject;
            UiKit.Button(s.Empty.transform, "＋ 冒険者を加える", () => { Enable(s, true); Refresh(); }, (SeatW - 300) / 2, 280, 300, 80, 24);
            Enable(s, false);
            return s;
        }

        static void Enable(Seat s, bool on) { s.On = on; s.Body.SetActive(on); s.Empty.SetActive(!on); }

        void Refresh()
        {
            foreach (var s in _seats.Where(s => s.On))
            {
                var cls = _n.Classes[s.ClassId];
                s.Portrait.texture = ArtLibrary.Get(s.From?.Portrait is string p && p.Length > 0 ? p : $"pc_{s.ClassId}_{(s.Female ? "f" : "m")}");
                s.ClassName.text = cls.Name;
                s.Stats.text = string.Join("  ", new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd }.Select(a => $"{a.Code()}+{cls.Stats[a]}")) + $"\nHP {cls.Hp}　気力 {cls.Focus}";
                var probe = new Adventurer(cls, "probe");
                s.ClassText.text = probe.ClassText();
                foreach (var kv in s.ClassButtons) kv.Value.color = kv.Key == s.ClassId ? Theme.Hex("6a5228") : Theme.Hex("2e241a");
                s.FromText.text = s.From == null ? "" : $"名簿から: 歴戦の印 {s.From.Marks.Count}・名付けた技 {s.From.Techniques.Count}";
            }
        }

        void PickFromRoster(Seat s)
        {
            var layer = UiKit.Rect("Roster", _root).Fill();
            UiKit.Box(layer, "Dim", new Color(0, 0, 0, 0.7f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 460, 140, 1000, 800).transform;
            UiKit.Label(panel, "冒険者の名簿", 30, Theme.Gold, 30, 20, 600, 44, serif: true);
            var list = UiKit.Scroll(panel, 30, 80, 940, 620, out _);
            var living = _n.Store.LoadRoster().Where(r => !r.Dead && _seats.All(x => x.From?.Id != r.Id)).ToList();
            if (living.Count == 0) UiKit.Label(list, "まだ誰もいません。今夜の冒険を終えると、ここに名前が残ります。", 20, Theme.Dim);
            foreach (var r in living)
            {
                var b = UiKit.Button(list, $"{r.Name}（{_n.Classes[r.ClassId].Name}）　歴戦の印 {r.Marks.Count}　技 {r.Techniques.Count}　{r.Evenings} 晩", () =>
                {
                    s.From = r; s.ClassId = r.ClassId; s.Name.text = r.Name; s.Background.text = r.Background;
                    Object.Destroy(layer.gameObject);
                    Refresh();
                }, 20);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            }
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 780, 720, 190, 56, 20);
        }

        void Done()
        {
            var party = new List<Adventurer>();
            var used = new HashSet<string>();
            foreach (var (s, i) in _seats.Select((s, i) => (s, i)).Where(x => x.s.On))
            {
                var name = s.Name.text.Trim();
                if (name.Length == 0) name = new[] { "ガレス", "エリス", "ロッコ", "ミーナ" }[i];
                while (!used.Add(name)) name += "二";
                var a = new Adventurer(_n.Classes[s.ClassId], name, "pc" + (i + 1))
                {
                    Background = s.Background.text.Trim(),
                    Portrait = s.From?.Portrait is string p && p.Length > 0 ? p : $"pc_{s.ClassId}_{(s.Female ? "f" : "m")}",
                };
                if (s.From != null)
                {
                    a.RosterId = s.From.Id;
                    foreach (var m in s.From.Marks) a.EarnMark(m.Name);
                    a.Techniques.AddRange(s.From.Techniques);
                }
                party.Add(a);
            }
            if (party.Count == 0) return;
            if (Online) Connect(party[0]);
            else _n.ShowMap(party);
        }

        async void Connect(Adventurer mine)
        {
            var name = _player?.text.Trim() ?? "";
            if (name.Length == 0) name = "旅人";
            PlayerPrefs.SetString("night_player", name);
            var code = _code?.text.Trim() ?? "";
            if (_mode == RoomMode.Join && code.Length == 0) { _note!.text = "<color=#c4553f>部屋のコードを入れてください</color>"; return; }
            _done.interactable = false;
            _note!.text = _mode == RoomMode.Host ? "部屋を作っています…" : "部屋に入っています…（少しかかることがあります）";
            try
            {
                _n.Room = _mode == RoomMode.Host ? await NightRoom.HostAsync(_n, name, mine) : await NightRoom.JoinAsync(_n, code, name, mine);
                _n.ShowRoom();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Online] " + e);
                _note.text = $"<color=#c4553f>{(_mode == RoomMode.Host ? "部屋を作れませんでした" : "部屋に入れませんでした")}: {e.Message}</color>";
                _done.interactable = true;
            }
        }
    }
}
