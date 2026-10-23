#nullable enable
using System.Linq;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Unity.App;
using Tavern.Unity.Ui;
using UnityEngine;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The ending (09_Spec 10, 11): the card from the client's point of view, what was lost, each personal goal, the reveal of
    /// tonight's secret actions, the record book's story, the rumours, and the veteran marks earned.
    /// </summary>
    public sealed class EndingView
    {
        readonly NightApp _n;
        readonly Table _t;
        readonly Chronicle? _c;

        public EndingView(NightApp n, Table t, Chronicle? c) { _n = n; _t = t; _c = c; }

        public void Build(Transform root)
        {
            var end = _t.Ending!;
            var e = _t.E;
            AudioDirector.Music("ending_" + (end.Card == Card.Success ? "success" : end.Card == Card.Bitter ? "bitter" : "failure"));
            UiKit.Picture(root, ArtLibrary.Get("bg_tavern"), 0, 0, 1920, 1080).color = new Color(0.3f, 0.26f, 0.22f);
            UiKit.Label(root, e.Story.Title, 30, Theme.Dim, 0, 40, 1920, 44, serif: true, align: TextAnchor.UpperCenter);
            var color = end.Card == Card.Success ? Theme.Gold : end.Card == Card.Bitter ? Theme.Hex("c9a0a0") : Theme.Hex("a08080");
            UiKit.Label(root, EndingResult.Label(end.Card), 72, color, 0, 90, 1920, 100, serif: true, align: TextAnchor.UpperCenter);

            var left = UiKit.Panel(root, "Facts", 80, 220, 840, 760).transform;
            var lines = new System.Collections.Generic.List<string>();
            if (end.SolvedBy.Length > 0) lines.Add("山場: " + e.Story.Climax.Solutions.First(s => s.Id == end.SolvedBy).Text);
            if (end.Lost.Count > 0) lines.Add("失ったもの: " + string.Join("、", end.Lost));
            if (end.Dead.Count > 0) lines.Add("<color=#c4553f>この晩に没した: " + string.Join("、", end.Dead) + "</color>");
            foreach (var kv in end.Personal) lines.Add($"{e.Find(kv.Key)?.Name}の目的「{kv.Value.goal}」: {(kv.Value.done ? "<color=#c9a24a>果たした</color>" : "果たせなかった")}");
            if (end.Convictions.Count > 0) lines.Add("確信したこと: " + string.Join(" / ", end.Convictions));
            if (_c != null)
            {
                foreach (var kv in _c.Marks) lines.Add($"歴戦の印: {e.Find(kv.Key)?.Name}「{kv.Value}」");
                foreach (var r in _c.Rumors) lines.Add("酒場の噂: " + r);
            }
            if (end.Reveal.Count > 0)
            {
                lines.Add("\n<color=#c9a24a>種明かし（今夜の内緒の行動）</color>");
                foreach (var s in end.Reveal) lines.Add($"・{e.Find(s.Who)?.Name}: {s.Text} → {s.Result}{(s.Found ? "（見つかった）" : "")}");
            }
            var facts = UiKit.Scroll(left, 20, 20, 800, 720, out _);
            UiKit.Label(facts, string.Join("\n", lines), 19, Theme.Ink);

            var right = UiKit.Panel(root, "Chronicle", 940, 220, 900, 760).transform;
            UiKit.Label(right, "語り部の記録帳", 26, Theme.Gold, 24, 18, 600, 40, serif: true);
            var story = UiKit.Scroll(right, 24, 70, 852, 600, out _);
            UiKit.Label(story, _c == null ? "（記録帳を書けなかった）" : $"「{_c.Title}」\n\n{_c.Story}", 20, Theme.Hex("efe3c8"), serif: true);
            UiKit.Button(right, "酒場へ戻る", _n.BackToTitle, 600, 690, 280, 56, 22, gold: true);
        }
    }
}
