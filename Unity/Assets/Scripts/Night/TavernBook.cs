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
        static (RectTransform layer, Transform panel) Open(Transform root, string title)
        {
            var layer = UiKit.Rect(title, root).Fill();
            UiKit.Box(layer, "Dim", new Color(0, 0, 0, 0.75f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 260, 90, 1400, 900).transform;
            UiKit.Label(panel, title, 32, Theme.Gold, 30, 20, 1000, 48, serif: true);
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 1200, 830, 180, 52, 20, gold: true);
            return (layer, panel);
        }

        public static void ShowRoster(NightApp n, Transform root)
        {
            var (_, panel) = Open(root, "冒険者の名簿");
            var list = UiKit.Scroll(panel, 30, 90, 1340, 720, out _, spacing: 14);
            var roster = n.Store.LoadRoster();
            if (roster.Count == 0) UiKit.Label(list, "まだ誰もいない。今夜の冒険を終えると、名前がここに残る。", 20, Theme.Dim);
            foreach (var r in roster.OrderBy(r => r.Dead))
            {
                var cls = n.Classes.Classes.TryGetValue(r.ClassId, out var c) ? c.Name : r.ClassId;
                var text = r.Dead
                    ? $"<color=#a08080>† {r.Name}（{cls}）— {r.DiedIn}にて没。{r.Evenings} 晩を生きた。</color>"
                    : $"<color=#c9a24a>{r.Name}</color>（{cls}）{r.Evenings} 晩　歴戦の印: {(r.Marks.Count == 0 ? "なし" : string.Join("・", r.Marks.Select(m => m.Name)))}" +
                      (r.Techniques.Count > 0 ? "\n　名付けた技: " + string.Join("、", r.Techniques.Select(t => "【" + t.Name + "】")) : "");
                UiKit.Label(list, text, 19, Theme.Ink);
            }
        }

        public static void ShowChronicle(NightApp n, Transform root)
        {
            var (_, panel) = Open(root, "語り部の記録帳");
            var list = UiKit.Scroll(panel, 30, 90, 1340, 720, out _, spacing: 22);
            var book = n.Store.LoadChronicle();
            if (book.Count == 0) UiKit.Label(list, "まだ白紙だ。", 20, Theme.Dim);
            foreach (var e in book.AsEnumerable().Reverse())
            {
                if (e.Untold) { UiKit.Label(list, $"{e.Date:yyyy/MM/dd}　「{e.Title}」— 語られなかった晩", 20, Theme.Dim); continue; }
                var text = $"<color=#c9a24a>{e.Date:yyyy/MM/dd}　「{e.Title}」</color>　{e.StoryTitle}・{e.Card}　{string.Join("・", e.Party)}\n{e.Text}";
                if (e.Dead.Count > 0) text += $"\n<color=#a08080>墓碑: {string.Join("、", e.Dead)}</color>";
                if (e.Personal.Count > 0) text += "\n" + string.Join("\n", e.Personal.Select(p => "・" + p));
                if (e.Reveal.Count > 0) text += "\n<color=#c9a0c0>種明かし: " + string.Join(" / ", e.Reveal) + "</color>";
                var t = UiKit.Label(list, text, 18, Theme.Ink, serif: false);
                t.lineSpacing = 1.15f;
            }
        }

        public static void ShowWall(NightApp n, Transform root)
        {
            var (_, panel) = Open(root, "酒場の壁と掲示板");
            var tavern = n.Store.LoadTavern();
            var left = UiKit.Scroll(panel, 30, 90, 820, 720, out _, spacing: 10);
            if (tavern.Wall.Count == 0) UiKit.Label(left, "壁にはまだ何も掛かっていない。", 20, Theme.Dim);
            foreach (var w in tavern.Wall.AsEnumerable().Reverse())
            {
                var kind = w.Kind switch { "trophy" => "記念品", "portrait" => "肖像", _ => "品" };
                UiKit.Label(left, $"［{kind}］{w.Text}　<size=15><color=#a8977a>{w.Who}　{w.Date:yyyy/MM/dd}</color></size>", 19, Theme.Ink);
            }
            var right = UiKit.Panel(panel, "Board", 880, 90, 490, 720).transform;
            UiKit.Label(right, "掲示板（最近の噂）", 22, Theme.Gold, 18, 14, 450, 32, serif: true);
            var rumors = tavern.RecentRumors.ToList();
            UiKit.Label(right, rumors.Count == 0 ? "まだ噂はない。" : string.Join("\n\n", rumors.Select(r => "・" + r)), 18, Theme.Ink, 18, 60, 454, 640);
        }
    }
}
