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
    /// Tonight's adventurers (1-4, 09_Spec 3), picked from the roster (owner 2026-10-25: registered ahead, from the title or right here
    /// through AdventurerForm); their veteran marks and named techniques come along.
    /// </summary>
    public sealed class PartyBuilder
    {
        const float SeatW = 440, SeatH = 640;
        readonly NightApp _n;
        readonly List<Seat> _seats = new List<Seat>();
        Transform _root = null!;

        sealed class Seat
        {
            public bool On;
            public RosterEntry? From;
            public GameObject Body = null!, Empty = null!;
            public RawImage Portrait = null!;
            public Text ClassName = null!, Stats = null!, ClassText = null!, Name = null!, Record = null!, Background = null!;
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
                44, Theme.FrameGold, 0, 26, 1920, 60, serif: true, align: TextAnchor.UpperCenter).font = Theme.Display;
            UiKit.Rule(root, 560, 94, 800, Theme.FrameDim);
            UiKit.Label(root, Online ? "あなたが今夜使う冒険者を、名簿から 1 人選んでください。新しく登録することもできます。"
                : "今夜の冒険者を、名簿から 1〜4 人選んでください。新しく登録することもできます。", 20, Theme.Ink, 0, 104, 1920, 30, align: TextAnchor.UpperCenter);
            for (int i = 0; i < (Online ? 1 : 4); i++) _seats.Add(BuildSeat(root, i));
            // the roster first (owner 2026-10-25): the first free adventurer sits down, or the player is asked to register one
            var first = Free.FirstOrDefault();
            if (first != null) { _seats[0].From = first; Enable(_seats[0], true); }
            if (Online) BuildRoomBox(root);
            UiKit.Button(root, "もどる", _n.BackToTitle, 70, 960, 220, 64, 22);
            _done = UiKit.Button(root, _mode switch { RoomMode.Host => "部屋を作る", RoomMode.Join => "部屋に入る", _ => "この顔ぶれで地図へ" }, Done, 1460, 950, 400, 80, 28, gold: true);
            Refresh();
            if (first == null) AskToRegister();
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
            var s = new Seat();
            var panel = UiKit.Panel(root, "Seat" + i, 70 + i * (SeatW + 15), 140, SeatW, SeatH).transform;
            s.Body = UiKit.Rect("Body", panel).Fill().gameObject;
            var b = s.Body.transform;
            var card = UiKit.Box(b, "Card", Theme.Hex("0c0907"));
            card.rectTransform.Place(16, 16, 156, 231);
            s.Portrait = UiKit.Picture(card.transform, null, 3, 3, 150, 225);
            UiKit.Frame(card.transform, thin: true, Theme.FrameGold, 7);
            s.Name = UiKit.Title(b, "", 28, Theme.GoldLight, 186, 12, 240, 40);
            s.ClassName = UiKit.Label(b, "", 18, Theme.FrameGold, 186, 56, 240, 26, serif: true);
            s.Stats = UiKit.Label(b, "", 15, Theme.Ink, 182, 88, 246, 60);
            s.Record = UiKit.Label(b, "", 14, Theme.GoldLight, 182, 160, 246, 86);
            s.ClassText = UiKit.Label(b, "", 14, Theme.Dim, 16, 256, SeatW - 32, 150);
            UiKit.Label(b, "生い立ち・性格", 16, Theme.Dim, 16, 412, 300, 24);
            s.Background = UiKit.Label(b, "", 16, Theme.Ink, 16, 438, SeatW - 32, 146);
            UiKit.Button(b, "名簿から選び直す", () => PickFromRoster(s), 16, 594, 220, 36, 16, gold: true);
            if (!Online) UiKit.Button(b, "この席を空ける", () => { s.From = null; Enable(s, false); Refresh(); }, 246, 594, SeatW - 262, 36, 16);
            s.Empty = UiKit.Rect("Empty", panel).Fill().gameObject;
            UiKit.Button(s.Empty.transform, "＋ 冒険者を加える", () => PickFromRoster(s), (SeatW - 300) / 2, 280, 300, 80, 24);
            Enable(s, false);
            return s;
        }

        static void Enable(Seat s, bool on) { s.On = on; s.Body.SetActive(on); s.Empty.SetActive(!on); }

        void Refresh()
        {
            foreach (var s in _seats.Where(s => s.On && s.From != null))
            {
                var r = s.From!;
                var cls = _n.Classes[r.ClassId];
                s.Portrait.texture = ArtLibrary.Get(r.Portrait.Length > 0 ? r.Portrait : $"pc_{r.ClassId}_m");
                if (s.Portrait.texture != null) s.Portrait.uvRect = ArtLibrary.Cover(s.Portrait.texture, 150, 225, 0.7f);
                s.Name.text = r.Name;
                s.ClassName.text = cls.Name;
                s.Stats.text = string.Join("  ", new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd }.Select(a => $"{a.Code()}+{cls.Stats[a]}")) + $"\nHP {cls.Hp}　気力 {cls.Focus}";
                s.Record.text = r.Evenings == 0 ? "まだ冒険に出ていない" :
                    $"{r.Evenings} 晩　歴戦の印 {r.Marks.Count}" + (r.Techniques.Count > 0 ? "\n名付けた技: " + string.Join("、", r.Techniques.Take(3).Select(t => "【" + t.Name + "】")) : "");
                s.ClassText.text = new Adventurer(cls, "probe").ClassText();
                s.Background.text = r.Background.Length > 0 ? r.Background : "（書かれていない）";
            }
            _done.interactable = _seats.Any(s => s.On && s.From != null);
        }

        List<RosterEntry> Free => _n.Store.LoadRoster().Where(r => !r.Dead && _seats.All(x => x.From?.Id != r.Id)).ToList();

        /// <summary>Who sits in this seat: one of the roster (not already seated), or someone registered right here.</summary>
        void PickFromRoster(Seat s)
        {
            var layer = UiKit.Rect("Roster", _root).Fill();
            UiKit.Box(layer, "Dim", new Color(0, 0, 0, 0.7f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 460, 140, 1000, 800).transform;
            UiKit.Heading(panel, "冒険者の名簿から選ぶ", 30, 18, 940, "ic_tavern", size: 30);
            var list = UiKit.Scroll(panel, 30, 86, 940, 614, out _);
            var free = Free;
            if (free.Count == 0) UiKit.Label(list, "選べる冒険者がいません。下の「新しく登録する」で作ってください。", 20, Theme.Dim);
            foreach (var r in free)
            {
                var b = UiKit.Button(list, $"{r.Name}（{_n.Classes[r.ClassId].Name}）　{(r.Evenings == 0 ? "まだ冒険に出ていない" : $"{r.Evenings} 晩・歴戦の印 {r.Marks.Count}・技 {r.Techniques.Count}")}", () =>
                {
                    Object.Destroy(layer.gameObject);
                    SitDown(s, r);
                }, 20);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            }
            UiKit.Button(panel, "＋ 新しく登録する", () =>
            {
                Object.Destroy(layer.gameObject);
                AdventurerForm.Show(_n, _root, r => SitDown(s, r));
            }, 30, 720, 300, 56, 20);
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 780, 720, 190, 56, 20);
        }

        void SitDown(Seat s, RosterEntry r)
        {
            s.From = r;
            Enable(s, true);
            Refresh();
        }

        /// <summary>No one in the roster yet: register first (owner 2026-10-25), or go back to the title.</summary>
        void AskToRegister()
        {
            var (layer, panel) = UiKit.Modal(_root, "NoAdventurer", 560, 300, 800, 420);
            UiKit.Heading(panel, "冒険者がまだいません", 34, 22, 732, "ic_warrior", size: 30);
            UiKit.Label(panel, "遊ぶ前に、今夜使う冒険者を名簿に登録してください。\n登録した冒険者は、印や名付けた技を持ったまま、次の晩も呼べます。\n（タイトルの「冒険者の名簿」からも登録できます）",
                20, Theme.Ink, 40, 96, 720, 160);
            UiKit.Button(panel, "タイトルへもどる", _n.BackToTitle, 40, 320, 260, 62, 20);
            UiKit.Button(panel, "冒険者を登録する", () =>
            {
                Object.Destroy(layer.gameObject);
                AdventurerForm.Show(_n, _root, r => SitDown(_seats[0], r), AskToRegister);
            }, 480, 320, 280, 62, 22, gold: true);
        }

        void Done()
        {
            var party = new List<Adventurer>();
            var used = new HashSet<string>();
            foreach (var (s, i) in _seats.Select((s, i) => (s, i)).Where(x => x.s.On && x.s.From != null))
            {
                var r = s.From!;
                var name = r.Name;
                while (!used.Add(name)) name += "二";
                var a = new Adventurer(_n.Classes[r.ClassId], name, "pc" + (i + 1))
                {
                    Background = r.Background,
                    Portrait = r.Portrait.Length > 0 ? r.Portrait : $"pc_{r.ClassId}_m",
                    RosterId = r.Id,
                };
                foreach (var m in r.Marks) a.EarnMark(m.Name);
                a.Techniques.AddRange(r.Techniques);
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
