#nullable enable
using System.Linq;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>The tavern between evenings (09_Spec 11, 15): the roster with the epitaphs, the record book, the wall and the notice board.</summary>
    public static class TavernBook
    {
        static (RectTransform layer, Transform panel) Open(Transform root, string title, string icon)
        {
            var (layer, panel) = UiKit.Modal(root, title, 260, 80, 1400, 920, paper: true);
            UiKit.Heading(panel, title, 56, 44, 1288, icon, paper: true, size: 34);
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 1170, 834, 180, 52, 20, gold: true);
            return (layer, panel);
        }

        static Text Ink(Transform list, string text, int size, Color color) => UiKit.Label(list, UiKit.OnPaper(text), size, color).Inked();

        public static void ShowRoster(NightApp n, Transform root)
        {
            var (layer, panel) = Open(root, "冒険者の名簿", "ic_warrior");
            void Reopen() { Object.Destroy(layer.gameObject); ShowRoster(n, root); }
            // adventurers are registered here ahead of any evening (owner 2026-10-25); one never taken out can still be struck off
            UiKit.Button(panel, "＋ 新しい冒険者を登録", () => AdventurerForm.Show(n, root, _ => Reopen()), 56, 834, 340, 52, 20, gold: true);
            var list = UiKit.Scroll(panel, 52, 126, 1296, 690, out _, spacing: 14);
            var roster = n.Store.LoadRoster();
            if (roster.Count == 0) Ink(list, "まだ誰もいない。下の「新しい冒険者を登録」から作れる。", 20, Theme.InkBrown);
            foreach (var r in roster.OrderBy(r => r.Dead))
            {
                var cls = n.Classes.Classes.TryGetValue(r.ClassId, out var c) ? c.Name : r.ClassId;
                var text = r.Dead
                    ? $"<color=#a08080>† {r.Name}（{cls}）— {r.DiedIn}にて没。{r.Evenings} 晩を生きた。</color>"
                    : $"<color=#c9a24a>{r.Name}</color>（{cls}）{(r.Evenings == 0 ? "まだ冒険に出ていない" : $"{r.Evenings} 晩")}　歴戦の印: {(r.Marks.Count == 0 ? "なし" : string.Join("・", r.Marks.Select(m => m.Name)))}" +
                      (r.Techniques.Count > 0 ? "\n　名付けた技: " + string.Join("、", r.Techniques.Select(t => "【" + t.Name + "】")) : "") +
                      (r.Background.Length > 0 ? $"\n　<size=16>{r.Background}</size>" : "");
                Ink(list, text, 19, Theme.InkDark);
                if (r.Dead || r.Evenings > 0) continue;
                var row = UiKit.Rect("Row", list);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                var id = r.Id;
                UiKit.Button(row, "名簿から外す", () =>
                {
                    var all = n.Store.LoadRoster();
                    all.RemoveAll(x => x.Id == id);
                    n.Store.SaveRoster(all);
                    Reopen();
                }, 20, 0, 180, 38, 15);
            }
        }

        public static void ShowChronicle(NightApp n, Transform root)
        {
            var (_, panel) = Open(root, "語り部の記録帳", "ic_quill");
            var list = UiKit.Scroll(panel, 52, 126, 1296, 690, out _, spacing: 22);
            var book = n.Store.LoadChronicle();
            if (book.Count == 0) Ink(list, "まだ白紙だ。", 20, Theme.InkBrown);
            foreach (var e in book.AsEnumerable().Reverse())
            {
                if (e.Untold) { Ink(list, $"{e.Date:yyyy/MM/dd}　「{e.Title}」— 語られなかった晩", 20, Theme.InkBrown); continue; }
                var text = $"<color=#c9a24a>{e.Date:yyyy/MM/dd}　「{e.Title}」</color>　{e.StoryTitle}・{e.Card}　{string.Join("・", e.Party)}\n{e.Text}";
                if (e.Dead.Count > 0) text += $"\n<color=#a08080>墓碑: {string.Join("、", e.Dead)}</color>";
                if (e.Personal.Count > 0) text += "\n" + string.Join("\n", e.Personal.Select(p => "・" + p));
                if (e.Reveal.Count > 0) text += "\n<color=#c9a0c0>種明かし: " + string.Join(" / ", e.Reveal) + "</color>";
                var t = Ink(list, text, 18, Theme.InkDark);
                t.lineSpacing = 1.15f;
            }
        }

        public static void ShowWall(NightApp n, Transform root)
        {
            var (_, panel) = Open(root, "酒場の壁と掲示板", "ic_tavern");
            var tavern = n.Store.LoadTavern();
            var left = UiKit.Scroll(panel, 52, 126, 800, 690, out _, spacing: 10);
            if (tavern.Wall.Count == 0) Ink(left, "壁にはまだ何も掛かっていない。", 20, Theme.InkBrown);
            foreach (var w in tavern.Wall.AsEnumerable().Reverse())
            {
                var kind = w.Kind switch { "trophy" => "記念品", "portrait" => "肖像", _ => "品" };
                Ink(left, $"［{kind}］{w.Text}　<size=15><color=#a8977a>{w.Who}　{w.Date:yyyy/MM/dd}</color></size>", 19, Theme.InkDark);
            }
            var right = UiKit.Panel(panel, "Board", 872, 126, 476, 690).transform;
            UiKit.Heading(right, "掲示板（最近の噂）", 22, 18, 432, size: 24);
            var rumors = tavern.RecentRumors.ToList();
            UiKit.Label(right, rumors.Count == 0 ? "まだ噂はない。" : string.Join("\n\n", rumors.Select(r => "・" + r)), 18, Theme.Ink, 22, 70, 432, 600);
        }
    }
}
