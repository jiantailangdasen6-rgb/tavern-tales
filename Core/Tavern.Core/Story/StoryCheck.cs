#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;

namespace Tavern.Core.Story
{
    public sealed class Problem
    {
        public bool Error { get; set; }
        public string Message { get; set; } = "";
        public override string ToString() => (Error ? "ERROR " : "warn  ") + Message;
    }

    /// <summary>
    /// The checker for hand-written story sheets (09_Spec 9, 08_Review F1/M2): every conclusion has three clues in different places or
    /// people, every act moves on with a place condition, ids are unique and every reference resolves, the events conditions wait for are
    /// raised by something, places are reachable, items fit the grade rules. Errors must be fixed; warnings are worth a look.
    /// </summary>
    public static class StoryCheck
    {
        /// <summary>Events the game raises by itself (besides "clue:{id}", "visited:{place}" and "cleared:{place}" once a fight there is won).</summary>
        public static readonly string[] GameEvents = { "climax", "boss_defeated", "climax_solved" };

        public static List<Problem> Run(StoryDef s, Bestiary? enemies = null, ISet<string>? art = null, ISet<string>? regions = null)
        {
            var p = new List<Problem>();
            void E(string m) => p.Add(new Problem { Error = true, Message = m });
            void W(string m) => p.Add(new Problem { Error = false, Message = m });

            // ---- basics
            if (s.Id.Length == 0) E("id がない");
            if (s.Title.Length == 0) E("題がない");
            if (s.Intro.Length == 0) E("導入がない");
            if (s.Truth.Length == 0) E("真相がない");
            if (s.Goal.IsEmpty) E("目的の条件がない");
            if (s.EndingSuccess.Length == 0 || s.EndingBitter.Length == 0 || s.EndingFailure.Length == 0) E("結末の骨組み（成功・苦い成功・失敗）がそろっていない");
            if (s.PlaceOf(s.Start) == null) E($"起点 '{s.Start}' が場所にない");
            if (regions != null && !regions.Contains(s.Region)) E($"地図の場所 '{s.Region}' が world.json の regions にない");
            if (s.Acts.Count != s.ActCount) E($"幕の数が {s.Acts.Count}（{(s.Long ? "中編は 5" : "短編は 3")}）");
            if (!s.Losses.Any(l => l.Heavy)) W("重い失いうるもの（★）がない（冒険者の死は自動で重い）");

            // ---- unique ids
            var ids = new Dictionary<string, string>();
            void Id(string kind, string id)
            {
                if (id.Length == 0) { E($"{kind} に id がない"); return; }
                if (ids.TryGetValue(id, out var other)) E($"id '{id}' が {other} と {kind} で重なっている");
                else ids[id] = kind;
            }
            foreach (var x in s.Places) Id("場所", x.Id);
            foreach (var x in s.People) Id("人物", x.Id);
            foreach (var x in s.Items) Id("物", x.Item.Id);
            foreach (var x in s.Clues) Id("手がかり", x.Id);
            foreach (var x in s.Conclusions) Id("結論", x.Id);
            foreach (var x in s.Clocks) Id("時計", x.Def.Id);
            foreach (var x in s.Mechanisms) Id("仕掛け", x.Id);
            foreach (var x in s.PersonalGoals) Id("個人の目的", x.Id);
            foreach (var x in s.Losses) Id("失いうるもの", x.Id);
            foreach (var x in s.Climax.Solutions) Id("解き方", x.Id);

            // ---- counts (09_Spec 9; long stories are bigger)
            Range("時計", s.Clocks.Count, 2, 4);
            Range("結論", s.Conclusions.Count, 3, 6);
            Range("ハンドアウト用の断片", s.Fragments.Count, 2, 6);
            if (s.PersonalGoals.Count < 2) W($"個人の目的の候補が {s.PersonalGoals.Count}（2 つ以上あると配りやすい）");
            Range("山場の解き方", s.Climax.Solutions.Count, 2, 4);   // the archive and the fort were written with four
            void Range(string what, int n, int min, int max) { if (n < min || n > max) E($"{what}の数が {n}（{min}〜{max}）"); }

            // ---- conclusions and clues
            foreach (var c in s.Conclusions)
            {
                if (c.Clues.Count != 3) E($"結論 {c.Id} の手がかりが {c.Clues.Count}（3 つ）");
                var holders = new List<string>();
                foreach (var cid in c.Clues)
                {
                    var clue = s.ClueOf(cid);
                    if (clue == null) { E($"結論 {c.Id} の手がかり '{cid}' がない"); continue; }
                    if (clue.Conclusion != c.Id) E($"手がかり {cid} の結論が '{clue.Conclusion}'（{c.Id} のはず）");
                    holders.Add(clue.Person.Length > 0 ? "人:" + clue.Person : "所:" + clue.Place);
                }
                if (holders.Distinct().Count() < holders.Count) W($"結論 {c.Id} の手がかりが同じ場所・人に重なっている（{string.Join(", ", holders)}）");
                if (c.RewardClock.Length > 0 && s.ClockOf(c.RewardClock) == null) E($"結論 {c.Id} のご褒美の時計 '{c.RewardClock}' がない");
                if (c.RewardClock.Length == 0) W($"結論 {c.Id} に確信のご褒美の時計がない");
            }
            foreach (var clue in s.Clues)
            {
                if (!s.Conclusions.Any(c => c.Clues.Contains(clue.Id))) E($"手がかり {clue.Id} がどの結論にも入っていない");
                if (s.PlaceOf(clue.Place) == null) E($"手がかり {clue.Id} の場所 '{clue.Place}' がない");
                if (clue.Person.Length > 0)
                {
                    var who = s.PersonOf(clue.Person);
                    if (who == null) E($"手がかり {clue.Id} の人物 '{clue.Person}' がない");
                    else if (!who.Clues.Contains(clue.Id)) W($"人物 {who.Id} の知っている手がかりに {clue.Id} がない");
                }
                if (clue.Access == ClueAccess.Request && clue.Person.Length == 0) E($"手がかり {clue.Id} は頼みで得るのに人物がない");
                if (clue.Text.Length == 0) E($"手がかり {clue.Id} に文章がない");
                foreach (var a in clue.Acts) if (a < 1 || a > s.ActCount) E($"手がかり {clue.Id} の幕 {a}");
            }
            foreach (var who in s.People)
                foreach (var cid in who.Clues)
                    if (s.ClueOf(cid) == null) E($"人物 {who.Id} の手がかり '{cid}' がない");

            // ---- places
            foreach (var pl in s.Places)
            {
                if (pl.Acts.Count == 0) W($"場所 {pl.Id} に幕がない");
                foreach (var a in pl.Acts) if (a < 1 || a > s.ActCount) E($"場所 {pl.Id} の幕 {a}");
                foreach (var l in pl.Links)
                {
                    if (s.PlaceOf(l.To) == null) E($"場所 {pl.Id} のつながる先 '{l.To}' がない");
                    else if (!s.PlaceOf(l.To)!.Links.Any(b => b.To == pl.Id)) W($"{pl.Id} → {l.To} は一方通行（戻れない）");
                }
                foreach (var r in pl.Rules) if (r.Clock.Length > 0 && s.ClockOf(r.Clock) == null) E($"場所 {pl.Id} の決まりの時計 '{r.Clock}' がない");
                if (enemies != null) foreach (var e in pl.Enemies) if (!enemies.Has(e)) E($"場所 {pl.Id} の敵 '{e}' が敵の表にない");
                if (art != null && pl.Art.Length > 0 && !art.Contains(pl.Art)) W($"場所 {pl.Id} の絵 '{pl.Art}' がまだない");
                if (pl.Art.Length == 0) W($"場所 {pl.Id} に絵の id がない");
            }
            var reach = Reachable(s);
            foreach (var pl in s.Places.Where(x => !reach.Contains(x.Id))) E($"場所 {pl.Id} に起点からたどり着けない");
            if (!s.Places.Any(x => x.Tense)) W("緊迫の場所がない");

            // ---- items, people, mechanisms
            foreach (var it in s.Items)
            {
                if (!it.Item.IsValid(out var why)) E($"物 {it.Item.Id}: {why}");
                CheckHolder($"物 {it.Item.Id}", it.At);
            }
            foreach (var who in s.People)
            {
                if (s.PlaceOf(who.At) == null) E($"人物 {who.Id} の居場所 '{who.At}' がない");
                if (who.Enemy.Length > 0 && enemies != null && !enemies.Has(who.Enemy)) E($"人物 {who.Id} の敵の型 '{who.Enemy}' が敵の表にない");
                if (who.Joins == JoinWillingness.Conditional && who.JoinWhen.IsEmpty) E($"人物 {who.Id} は条件付きで同行するのに条件がない");
                if (art != null && who.Portrait.Length > 0 && !art.Contains(who.Portrait)) W($"人物 {who.Id} の立ち絵 '{who.Portrait}' がまだない");
            }
            foreach (var m in s.Mechanisms)
            {
                if (s.PlaceOf(m.Place) == null) E($"仕掛け {m.Id} の場所 '{m.Place}' がない");
                if (m.Event.Length == 0 && m.CombatEffect.Length == 0) E($"仕掛け {m.Id} が出来事も戦闘の効果も持たない");
            }
            void CheckHolder(string what, string at)
            {
                if (at.StartsWith("npc:")) { if (s.PersonOf(at.Substring(4)) == null) E($"{what} の持ち主 '{at}' がない"); }
                else if (at.StartsWith("enemy:"))
                {
                    var e = at.Substring(6);
                    if (enemies != null && !enemies.Has(e)) E($"{what} を持つ敵 '{e}' が敵の表にない");
                    if (!s.Places.Any(pl => pl.Enemies.Contains(e)) && !s.People.Any(x => x.Enemy == e)) W($"{what} を持つ敵 '{e}' がどこにも出ない");
                }
                else if (s.PlaceOf(at) == null) E($"{what} の場所 '{at}' がない");
            }

            // ---- acts and climax
            foreach (var act in s.Acts)
            {
                if (act.N == s.ActCount) { if (!act.Advance.IsEmpty) W($"最後の幕 {act.N} に進む条件がある（山場で終わる）"); continue; }
                if (act.Advance.IsEmpty) E($"幕 {act.N} に進む条件がない");
                else if (!act.Advance.Groups.Any(g => g.Any(c => c.Type == ConditionType.Place && !c.Not)))
                    E($"幕 {act.N} の進む条件に場所の条件がない");
                // a long story's places open one act ahead (Evening.OpenInAct): a group needing a place further on never holds
                if (s.Long && act.Advance.Groups.Count > 0 && act.Advance.Groups.All(g => g.Any(c => c.Type == ConditionType.Place && !c.Not
                        && s.PlaceOf(c.Id) is Place p && p.Acts.Count > 0 && p.Acts.Min() > act.N + 1)))
                    E($"幕 {act.N} の進む条件の場所が、まだ開いていない幕の場所（中編の場所は 1 つ先の幕まで）");
            }
            if (s.Climax.Starts.IsEmpty) E("山場の始まる条件がない");

            // ---- every condition and action points at something real; events are raised somewhere
            var raised = new HashSet<string>(GameEvents);
            foreach (var c in s.Clues) raised.Add("clue:" + c.Id);
            foreach (var pl in s.Places) { raised.Add("visited:" + pl.Id); if (pl.Enemies.Count > 0) raised.Add("cleared:" + pl.Id); }   // cleared: a fight won there
            foreach (var m in s.Mechanisms) if (m.Event.Length > 0) raised.Add(m.Event);
            foreach (var a in AllActions(s)) if (a.Kind == "event") raised.Add(a.Id);

            foreach (var (where, when) in AllWhens(s))
                foreach (var c in when.All) CheckCondition(where, c);
            foreach (var a in AllActions(s)) CheckAction(a);

            void CheckCondition(string where, Condition c)
            {
                switch (c.Type)
                {
                    case ConditionType.Person: if (s.PersonOf(c.Id) == null) E($"{where}: 人物 '{c.Id}' がない"); if (c.Is == "at" && s.PlaceOf(c.Value) == null) E($"{where}: 場所 '{c.Value}' がない"); break;
                    case ConditionType.Item:
                        if (c.Kind == null && s.ItemOf(c.Id) == null) E($"{where}: 物 '{c.Id}' がない");
                        if (c.Is == "at" && s.PlaceOf(c.Value) == null) E($"{where}: 場所 '{c.Value}' がない");
                        if (c.Is == "held_by" && c.Value != "party" && c.Value != "self" && s.PersonOf(c.Value) == null) E($"{where}: 持ち主 '{c.Value}' がない");
                        break;
                    case ConditionType.Place: if (s.PlaceOf(c.Id) == null) E($"{where}: 場所 '{c.Id}' がない"); break;
                    case ConditionType.Event: if (!raised.Contains(c.Id)) E($"{where}: 出来事 '{c.Id}' を立てるものがない"); break;
                    case ConditionType.Clock: if (s.ClockOf(c.Id) == null) E($"{where}: 時計 '{c.Id}' がない"); break;
                    case ConditionType.Conviction: if (!s.Conclusions.Any(x => x.Id == c.Id)) E($"{where}: 結論 '{c.Id}' がない"); break;
                }
            }
            void CheckAction(StoryAction a)
            {
                switch (a.Kind)
                {
                    case "person_state": case "person_move": if (s.PersonOf(a.Id) == null) E($"出来事: 人物 '{a.Id}' がない"); if (a.Kind == "person_move" && a.Value != "party" && s.PlaceOf(a.Value) == null) E($"出来事: 場所 '{a.Value}' がない"); break;
                    case "item_move": if (s.ItemOf(a.Id) == null) E($"出来事: 物 '{a.Id}' がない"); if (s.PlaceOf(a.Value) == null && a.Value != "gone") E($"出来事: 場所 '{a.Value}' がない"); break;
                    case "clock_push": case "clock_reveal": if (s.ClockOf(a.Id) == null) E($"出来事: 時計 '{a.Id}' がない"); break;
                    case "enemies":
                        if (enemies != null) foreach (var e in a.Enemies) if (!enemies.Has(e)) E($"出来事: 敵 '{e}' が敵の表にない");
                        if (a.Value != "party" && s.PlaceOf(a.Value) == null) E($"出来事: 場所 '{a.Value}' がない");
                        break;
                }
            }

            // ---- clocks
            foreach (var c in s.Clocks)
            {
                if (c.Def.Speed == ClockSpeed.EveryTurns && c.Def.Every <= 0) E($"時計 {c.Def.Id} は N 手ごとなのに N がない");
                if (c.Def.Name.Length == 0) E($"時計 {c.Def.Id} に表の名前がない");
                if (c.OnFull.Count == 0 && c.Def.OnFull.Count == 0) E($"時計 {c.Def.Id} に満ちたらがない");
                foreach (var a in c.Def.Acts) if (a < 1 || a > s.ActCount) E($"時計 {c.Def.Id} の幕 {a}");
                foreach (var r in c.Def.Rewinds) if (!c.RewindWhen.TryGetValue(r.Id, out var w) || w.IsEmpty) W($"時計 {c.Def.Id} の戻し方 {r.Id} に条件がない");
            }
            foreach (var h in s.Hooks) if (s.PlaceOf(h.Place) == null) E($"因縁の差し込み口の場所 '{h.Place}' がない");
            return p;
        }

        static HashSet<string> Reachable(StoryDef s)
        {
            var seen = new HashSet<string>();
            if (s.PlaceOf(s.Start) == null) return seen;
            var q = new Queue<string>();
            q.Enqueue(s.Start); seen.Add(s.Start);
            while (q.Count > 0)
            {
                var pl = s.PlaceOf(q.Dequeue());
                if (pl == null) continue;
                foreach (var l in pl.Links) if (seen.Add(l.To)) q.Enqueue(l.To);
            }
            return seen;
        }

        static IEnumerable<(string, When)> AllWhens(StoryDef s)
        {
            yield return ("目的", s.Goal);
            foreach (var x in s.Losses) yield return ($"失いうるもの {x.Id}", x.When);
            foreach (var x in s.Clues) yield return ($"手がかり {x.Id}", x.Requires);
            foreach (var x in s.PersonalGoals) yield return ($"個人の目的 {x.Id}", x.When);
            foreach (var pl in s.Places) foreach (var l in pl.Links) yield return ($"道 {pl.Id}→{l.To}", l.Open);
            foreach (var x in s.People) { yield return ($"人物 {x.Id} の同行", x.JoinWhen); yield return ($"人物 {x.Id} の裏切り", x.Betrays); }
            foreach (var x in s.Acts) yield return ($"幕 {x.N}", x.Advance);
            yield return ("山場の始まり", s.Climax.Starts);
            foreach (var x in s.Climax.Solutions) yield return ($"解き方 {x.Id}", x.When);
            foreach (var c in s.Clocks)
            {
                yield return ($"時計 {c.Def.Id} の見える条件", c.Visible);
                foreach (var kv in c.RewindWhen) yield return ($"時計 {c.Def.Id} の戻し方 {kv.Key}", kv.Value);
            }
        }

        static IEnumerable<StoryAction> AllActions(StoryDef s) =>
            s.Acts.SelectMany(a => a.OnStart)
                .Concat(s.Climax.OnStart)
                .Concat(s.Climax.Solutions.SelectMany(x => x.Then))
                .Concat(s.Clocks.SelectMany(c => c.OnFull));
    }
}
