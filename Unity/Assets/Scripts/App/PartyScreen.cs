#nullable enable
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
        }

        static readonly (string name, string cls, bool female, string bg)[] Samples =
        {
            ("ガレス", "warrior", false, "没落した騎士の家の三男。家名を取り戻したい。曲がったことが嫌い"),
            ("エリス", "mage", true, "魔法学院を追い出された。理由は語りたがらない。甘い物に目がない"),
            ("ロッコ", "thief", false, "港町の孤児。お調子者で、光る物を見るとつい手が伸びる"),
        };

        public PartyScreen(AppMain app) { _app = app; }

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

            for (int i = 0; i < 3; i++) _slots.Add(BuildSlot(root, i, 90 + i * 590));
            UiKit.Button(root, "おまかせで埋める", FillSamples, 90, 980, 300, 64, 22);
            UiKit.Button(root, "もどる", _app.ShowTitle, 410, 980, 200, 64, 22);
            _start = UiKit.Button(root, "この顔ぶれで酒場へ", Begin, 1430, 980, 400, 70, 28, gold: true);
            SetEnabled(_slots[0], true);
            Refresh();
        }

        Slot BuildSlot(Transform root, int index, float x)
        {
            var s = new Slot();
            var panel = UiKit.Panel(root, "Slot" + index, x, 180, 560, 770);
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
            s.Background = UiKit.Input(body, "例: 没落した騎士の家の三男。家名を取り戻したい", 20, 486, 520, 180, 20, multiline: true);
            UiKit.Button(body, "この席を空ける", () => { SetEnabled(s, false); Refresh(); }, 340, 690, 200, 44, 18);

            s.Empty = UiKit.Rect("Empty", panel.transform).Fill().gameObject;
            UiKit.Button(s.Empty.transform, "＋ 冒険者を加える", () => { SetEnabled(s, true); Refresh(); }, 130, 340, 300, 80, 24);
            s.ClassId = classes[index % 4];
            return s;
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
                party.Add(TavernGame.CreateCharacter(_app.Db, name, s.ClassId, s.Background.text.Trim(), s.Art));
            }
            if (party.Count > 0) _app.StartEvening(party);
        }
    }
}
