#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Flow
{
    /// <summary>What ends a story (09_Spec 10). The AI never decides it.</summary>
    public enum EndingTrigger { Solved, Wiped, LeftMap, ClockFull }

    /// <summary>The card, from the client's point of view.</summary>
    public enum Card { Success, Bitter, Failure }

    public sealed class EndingResult
    {
        public EndingTrigger Trigger { get; set; }
        public Card Card { get; set; }
        public string SolvedBy { get; set; } = "";
        /// <summary>The losses that happened (heavy ones marked ★).</summary>
        public List<string> Lost { get; } = new List<string>();
        public List<string> Convictions { get; } = new List<string>();
        /// <summary>Adventurer id → (goal text, achieved).</summary>
        public Dictionary<string, (string goal, bool done)> Personal { get; } = new Dictionary<string, (string, bool)>();
        public List<string> Dead { get; } = new List<string>();
        public List<SecretRecord> Reveal { get; } = new List<SecretRecord>();

        public static string Label(Card c) => c switch { Card.Success => "成功", Card.Bitter => "苦い成功", _ => "失敗" };
    }

    public sealed partial class Table
    {
        /// <summary>
        /// The afterword (その後): after the climax is solved, everyone declares once more; then the ending is decided and told.
        /// </summary>
        public async Task<TurnReport> AfterwordAsync(IReadOnlyList<Declaration> declarations, CancellationToken ct = default)
        {
            Phase = Phase.Explore;
            var saved = SolvedBy;
            // the afterword runs like an exploration turn (the solved climax does not start again)
            var report = await ExploreTurnAsync(declarations, afterword: true, ct).ConfigureAwait(false);
            SolvedBy = saved;
            if (Phase != Phase.Over) Finish(EndingTrigger.Solved, report);
            return report;
        }

        /// <summary>
        /// The next evening of a long story (09_Spec 10): the clocks run again, every survivor gets a fate coin back and a veteran mark
        /// for the first half, the fallen may be replaced by new adventurers (they join where the party stands).
        /// </summary>
        public void ResumeAfterBreak(IEnumerable<Adventurer> newcomers, IEnumerable<string>? leaving = null)
        {
            if (Phase != Phase.Break) return;
            foreach (var id in leaving ?? Enumerable.Empty<string>()) if (E.Find(id) is Adventurer gone) gone.Withdrawn = true;
            foreach (var a in E.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn))
            {
                a.FateCoins++;
                a.EarnMark(Story.Title + "の前半を越えた");
            }
            var here = W.PlacesOfParty.FirstOrDefault() ?? Story.Start;
            foreach (var n in newcomers)
            {
                n.StartEvening(E.Difficulty, Story.Advance);
                E.Party.Add(n);
                W.Move(n.Id, here);
            }
            foreach (var c in W.Clocks.All) c.Paused = false;
            E.Tension.NewNight();
            Phase = Phase.Explore;
            var act = Story.Acts.FirstOrDefault(a => a.N == W.Act);
            if (act != null) RunActions(act.OnStart, new TurnReport(), new List<string>());
        }

        /// <summary>The host gives up the request (地図の外へ出た).</summary>
        public TurnReport Abandon()
        {
            var report = new TurnReport { Scene = SceneKind.Climax };
            Finish(EndingTrigger.LeftMap, report);
            return report;
        }

        /// <summary>
        /// Decides the card (09_Spec 10): the goal met and nothing heavy lost = success; the goal met with a heavy loss = bitter;
        /// the goal not met = failure. A filled "ending worse" clock moves it one step down. Personal goals are judged one by one.
        /// </summary>
        void Finish(EndingTrigger trigger, TurnReport report)
        {
            if (Ending != null) { report.Ending = Ending; return; }
            var r = new EndingResult { Trigger = trigger, SolvedBy = SolvedBy };
            var goal = trigger != EndingTrigger.Wiped && trigger != EndingTrigger.LeftMap && Story.Goal.Holds(W);
            foreach (var l in Story.Losses.Where(l => l.When.Holds(W))) r.Lost.Add((l.Heavy ? "★" : "") + l.Text);
            foreach (var a in E.Party.Where(a => a.Life == Life.Dead)) { r.Dead.Add(a.Name); r.Lost.Add($"★{a.Name}の命"); }
            var heavy = r.Lost.Any(x => x.StartsWith("★"));
            r.Card = !goal ? Card.Failure : heavy ? Card.Bitter : Card.Success;
            if (W.Clocks.All.Any(c => c.Full && c.Def.OnFull.Contains(ClockFull.EndingWorse)) && r.Card != Card.Failure) r.Card++;
            foreach (var c in Story.Conclusions.Where(c => W.Convictions.Contains(c.Id))) r.Convictions.Add(c.Text);
            foreach (var a in E.Party)
                if (PersonalGoals.TryGetValue(a.Id, out var gid) && Story.PersonalGoals.FirstOrDefault(g => g.Id == gid) is PersonalGoal pg)
                    r.Personal[a.Id] = (pg.Text, a.Life != Life.Dead && pg.When.Holds(W, a.Id));
            r.Reveal.AddRange(Secrets);
            Ending = r;
            report.Ending = r;
            Phase = Phase.Over;
            report.Lines.Add($"物語の結末: {EndingResult.Label(r.Card)}");
            foreach (var kv in r.Personal) report.Lines.Add($"{E.Find(kv.Key)?.Name}の目的「{kv.Value.goal}」: {(kv.Value.done ? "果たした" : "果たせなかった")}");
        }

        /// <summary>Enemies the party fled from tonight (bond candidates: 逃がした敵).</summary>
        public List<string> FledFoes { get; } = new List<string>();

        /// <summary>The handouts, dealt in one call when the story is chosen; each adventurer's personal goal is kept for the ending.</summary>
        public async Task<List<Handout>> DealHandoutsAsync(Bookends bookends, CancellationToken ct = default)
        {
            var list = await bookends.HandoutsAsync(E, ct).ConfigureAwait(false);
            Handouts.Clear();
            Handouts.AddRange(list);
            foreach (var h in list.Where(h => h.GoalId.Length > 0)) PersonalGoals[h.Who] = h.GoalId;
            return list;
        }

        /// <summary>
        /// The end of the evening (09_Spec 11): the chronicle call (record book, rumours, bonds, mark names), and the veteran marks for the
        /// survivors (a long story gave the first at the break). Bond candidates come from what happened, never from the story's secrets.
        /// </summary>
        public async Task<Chronicle> WrapUpAsync(Bookends bookends, CancellationToken ct = default)
        {
            var r = Ending ?? throw new InvalidOperationException("the story has not ended");
            var candidates = new List<string>();
            foreach (var f in FledFoes.Distinct()) candidates.Add($"逃がした敵: {f}");
            foreach (var p in W.Cast.People.Where(p => !p.Extra && p.State != PersonState.Dead))
            {
                if (p.Attitude == Attitude.Hostile && Story.PersonOf(p.Id)?.Enemy.Length == 0) candidates.Add($"恨みを買った: {p.Name}");
                if (p.Attitude >= Attitude.Friendly && Story.PersonOf(p.Id)?.Attitude < p.Attitude) candidates.Add($"恩を売った: {p.Name}");
            }
            if (Story.Conclusions.Any(c => !W.Convictions.Contains(c.Id))) candidates.Add("解けずに残った謎");
            var summary = $"結末: {EndingResult.Label(r.Card)}。" + (r.Lost.Count > 0 ? $"失ったもの: {string.Join("、", r.Lost)}。" : "") +
                (r.Dead.Count > 0 ? $"亡くなった冒険者: {string.Join("、", r.Dead)}。" : "") +
                string.Concat(r.Personal.Select(kv => $"{E.Find(kv.Key)?.Name}の目的「{kv.Value.goal}」は{(kv.Value.done ? "果たした" : "果たせなかった")}。")) +
                (r.Reveal.Count > 0 ? "内緒の行動: " + string.Join(" / ", r.Reveal.Select(s => $"{E.Find(s.Who)?.Name}: {s.Text}")) : "");
            var c = await bookends.ChronicleAsync(E, summary, Log, candidates, ct).ConfigureAwait(false);
            foreach (var a in E.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn))
            {
                var name = c.Marks.TryGetValue(a.Id, out var n) ? n : Story.Title;
                a.EarnMark(name);

            }
            return c;
        }

        /// <summary>The ending told by the strong narrator: the card and the facts, then the reveal of the secrets.</summary>
        public async Task TellEndingAsync(TurnReport report, CancellationToken ct = default)
        {
            var r = Ending!;
            var t = new TurnTelling { Scene = SceneKind.Climax, Length = Length };
            t.Names.AddRange(E.Party.Select(a => a.Name));
            t.Results.Add($"結末の札: {EndingResult.Label(r.Card)}（{(r.Trigger switch { EndingTrigger.Solved => "山場が解けた", EndingTrigger.Wiped => "一行が全員倒れた", EndingTrigger.LeftMap => "依頼を捨てて去った", _ => "時が尽きた" })}）");
            if (r.SolvedBy.Length > 0) t.Results.Add("山場の解け方: " + Story.Climax.Solutions.First(s => s.Id == r.SolvedBy).Text);
            if (r.Lost.Count > 0) t.Results.Add("失ったもの: " + string.Join("、", r.Lost));
            if (r.Convictions.Count > 0) t.Results.Add("確信したこと: " + string.Join("、", r.Convictions));
            foreach (var kv in r.Personal) t.Results.Add($"{E.Find(kv.Key)?.Name}の個人の目的「{kv.Value.goal}」: {(kv.Value.done ? "果たした" : "果たせなかった")}");
            var seed = r.Card switch { Card.Success => Story.EndingSuccess, Card.Bitter => Story.EndingBitter, _ => Story.EndingFailure };
            t.Extra = $"物語の結末を語る（語りの種: {seed}）。冒険者一人ひとりの行いに触れて、余韻を残して締めくくる。結果は変えない。";
            var told = await Highlight.TellAsync(E, t, ct).ConfigureAwait(false);
            report.Calls += told.Calls;
            report.Narration = told.Text;
        }
    }
}
