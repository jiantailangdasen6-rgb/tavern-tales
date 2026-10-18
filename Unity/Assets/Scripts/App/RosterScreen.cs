#nullable enable
using System.Linq;
using Tavern.Core.Game;
using Tavern.Core.Rules;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The title's "冒険者の名簿": the adventurers kept on this PC (roster.json), each one's details, making new ones ahead of
    /// an evening (PartyScreen in roster mode), and taking one off the list. Anyone here can be brought along with
    /// "前の冒険者を呼ぶ" when the party is made, in a solo evening or when joining a friend.
    /// </summary>
    public sealed class RosterScreen
    {
        readonly AppMain _app;
        Transform _detail = null!;
        Transform _list = null!;
        RosterEntry? _shown;

        public RosterScreen(AppMain app) { _app = app; }

        public void Build(Transform root)
        {
            AudioDirector.Music("tavern");
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.8f)).rectTransform.Fill();
            UiKit.Label(root, "冒険者の名簿", 54, Theme.Gold, 0, 40, 1920, 80, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(root, "この PC の冒険者たち。キャラ作りの「前の冒険者を呼ぶ」で、次の晩に連れて行けます", 22, Theme.Dim, 0, 118, 1920, 40, align: TextAnchor.MiddleCenter);

            var left = UiKit.Panel(root, "List", 90, 180, 760, 760).transform;
            _list = UiKit.Scroll(left, 10, 10, 740, 740, out _, 8);
            _detail = UiKit.Panel(root, "Detail", 880, 180, 950, 760).transform;

            UiKit.Button(root, "もどる", _app.ShowTitle, 90, 980, 240, 64, 22);
            UiKit.Button(root, "新しい冒険者を作る", () => _app.ShowParty(OnlineMode.Roster), 1430, 980, 400, 70, 26, gold: true);
            Refresh();
        }

        void Refresh()
        {
            UiKit.Clear(_list);
            var roster = _app.LoadRoster();
            var living = roster.Living.ToList();
            if (living.Count == 0)
            {
                var none = UiKit.Label(_list, "まだ誰もいません。「新しい冒険者を作る」で作るか、一晩の冒険を生き延びると、ここに並びます。", 20, Theme.Ink);
                none.gameObject.AddComponent<LayoutElement>().preferredHeight = 120;
            }
            foreach (var e in living)
            {
                var entry = e;
                var row = UiKit.Box(_list, "Row", entry == _shown || entry.Name == _shown?.Name ? Theme.Hex("3a2c1c") : Theme.Panel2, frame: true);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 128;
                row.gameObject.AddComponent<Button>().onClick.AddListener(() => { _shown = entry; Refresh(); });
                var tex = ArtLibrary.Get(entry.Portrait);
                var p = UiKit.Picture(row.transform, tex, 8, 8, 82, 112);
                p.uvRect = ArtLibrary.Cover(tex, 82, 112, 0.72f);
                UiKit.Label(row.transform, entry.Name, 28, Theme.Gold, 106, 10, 400, 40, serif: true);
                var cls = _app.Db.Classes.TryGetValue(entry.ClassId, out var c) ? c.Name : entry.ClassId;
                var nights = entry.Nights == 0 ? "まだ冒険に出ていない" : $"これまで {entry.Nights} 晩";
                UiKit.Label(row.transform, $"{cls}　{nights}", 18, Theme.Ink, 106, 54, 600, 28);
                if (entry.Titles.Count > 0) UiKit.Label(row.transform, "称号: " + string.Join("・", entry.Titles), 15, Theme.Dim, 106, 86, 620, 26);
            }
            ShowDetail(living.FirstOrDefault(x => x.Name == _shown?.Name) ?? living.FirstOrDefault());
        }

        void ShowDetail(RosterEntry? e)
        {
            UiKit.Clear(_detail);
            _shown = e;
            if (e == null) return;
            var c = e.ToCharacter(_app.Db);
            var cls = _app.Db.Classes[e.ClassId];
            var tex = ArtLibrary.Get(e.Portrait);
            var pic = UiKit.Picture(_detail, tex, 30, 30, 240, 340);
            pic.uvRect = ArtLibrary.Cover(tex, 240, 340, 0.72f);
            UiKit.Label(_detail, e.Name, 40, Theme.Gold, 300, 26, 620, 56, serif: true);
            UiKit.Label(_detail, $"{cls.Name}　武器: {cls.Weapon}　HP {c.MaxHp}　気力 {c.MaxFocus}", 20, Theme.Ink, 300, 90, 620, 30);
            var stats = new[] { Stat.Might, Stat.Skill, Stat.Wits, Stat.Spirit };
            UiKit.Label(_detail, string.Join("　", stats.Select(s => $"{s.Label()} {(c.Modifier(s) >= 0 ? "+" : "")}{c.Modifier(s)}")), 24, Theme.GoldLight, 300, 130, 620, 36, serif: true);
            UiKit.Label(_detail, "記念の品: " + (e.Keepsakes.Count == 0 ? "なし" : string.Join("・", e.Keepsakes)), 18, Theme.Ink, 300, 184, 620, 60);
            UiKit.Label(_detail, "称号: " + (e.Titles.Count == 0 ? "なし" : string.Join("・", e.Titles)), 18, Theme.Ink, 300, 248, 620, 60);
            UiKit.Label(_detail, "技のカード: " + string.Join("・", c.Cards.Select(id => _app.Db.Cards.TryGetValue(id, out var cd) ? cd.Name : id)), 18, Theme.Ink, 300, 312, 620, 60);
            UiKit.Label(_detail, "生い立ち・性格", 22, Theme.Gold, 30, 400, 300, 32, serif: true);
            UiKit.Label(_detail, string.IsNullOrWhiteSpace(e.Background) ? "（書かれていない）" : e.Background, 18, Theme.Ink, 30, 440, 890, 200);
            UiKit.Label(_detail, "冒険のたびに能力は職業の初めの値に戻り、称号と記念の品（3 つまで）が引き継がれます。", 15, Theme.Dim, 30, 640, 890, 30);

            // taking someone off the list needs a second press
            Button? remove = null;
            var armed = false;
            remove = UiKit.Button(_detail, "名簿から外す", () =>
            {
                if (!armed) { armed = true; remove!.SetText("本当に外す（もう一度押す）"); return; }
                var roster = _app.LoadRoster();
                roster.Characters.RemoveAll(x => x.Name == e.Name && !x.Dead);
                _app.SaveRoster(roster);
                _shown = null;
                Refresh();
            }, 600, 690, 320, 50, 18);
        }
    }
}
