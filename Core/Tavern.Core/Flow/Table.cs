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
    public enum Phase { Explore, Combat, Afterword, Break, Over }

    /// <summary>What one turn (or combat round) produced, for the screens.</summary>
    public sealed class TurnReport
    {
        /// <summary>The result lines everyone sees, before the narration.</summary>
        public List<string> Lines { get; } = new List<string>();
        /// <summary>Lines only one adventurer sees (secret results), by id.</summary>
        public Dictionary<string, List<string>> PrivateLines { get; } = new Dictionary<string, List<string>>();
        public string Narration { get; set; } = "";
        public Dictionary<string, string> PrivateNarration { get; } = new Dictionary<string, string>();
        /// <summary>Steps left for the next turn, by adventurer id (kept in their declaration box).</summary>
        public Dictionary<string, List<string>> Deferred { get; } = new Dictionary<string, List<string>>();
        public SceneKind Scene { get; set; }
        public bool ActChanged { get; set; }
        /// <summary>Adventurers who choose a growth now (Table.GrowthDue).</summary>
        public bool GrowthDue { get; set; }
        public bool ClimaxStarted { get; set; }
        public bool CombatStarted { get; set; }
        public CombatEnd? CombatEnded { get; set; }
        public EndingResult? Ending { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public int Calls { get; set; }

        public void Private(string who, string line)
        {
            if (!PrivateLines.TryGetValue(who, out var l)) PrivateLines[who] = l = new List<string>();
            l.Add(line);
        }
    }

    /// <summary>A secret action, kept for the reveal after the ending (種明かし).</summary>
    public sealed class SecretRecord
    {
        public string Who { get; set; } = "";
        public string Text { get; set; } = "";
        public string Result { get; set; } = "";
        public bool Found { get; set; }
        public int Turn { get; set; }
    }

    /// <summary>
    /// The evening's game master on the host (09_Spec 4, 5, 10, 11): runs turns and fights, decides every result, and asks the AI only to
    /// sort declarations and to tell what happened. The players roll their dice through IPlayers.
    /// The table resumes on the caller's context after every await (no ConfigureAwait(false)), so the screens may be touched from IPlayers.
    /// </summary>
    public sealed partial class Table
    {
        /// <summary>The event set once a fight was won at a place: its own enemy table does not come back.</summary>
        public static string Cleared(string place) => "cleared:" + place;

        public Evening E { get; }
        public Classifier Classifier { get; }
        public Narrator Narrator { get; }
        /// <summary>The stronger narrator for the boss's entrance and the ending (falls back to the everyday one).</summary>
        public Narrator Highlight { get; }
        public IPlayers Players { get; }
        public IRandom Rng { get; }
        public Bestiary Enemies { get; }
        public Phase Phase { get; set; } = Phase.Explore;
        public Telling Length { get; set; } = Telling.Standard;
        /// <summary>Players' requests on the telling for the next turn.</summary>
        public List<string> Requests { get; } = new List<string>();
        /// <summary>The act in which the party last rested (once per act).</summary>
        public int RestedAct { get; set; }
        public bool UnexpectedUsed { get; set; }
        /// <summary>The climax solution that solved it (empty until then).</summary>
        public string SolvedBy { get; set; } = "";
        public List<SecretRecord> Secrets { get; } = new List<SecretRecord>();
        /// <summary>Called with the table after the results are decided and before the narration (save here).</summary>
        public Action<Table>? Save { get; set; }
        /// <summary>Each adventurer's handout personal goal id.</summary>
        public Dictionary<string, string> PersonalGoals { get; } = new Dictionary<string, string>();
        /// <summary>Decided by Finish (a friend's mirror of the table gets it from the host).</summary>
        public EndingResult? Ending { get; set; }
        /// <summary>The evening so far: result lines and narrations (for the chronicle call).</summary>
        public List<string> Log { get; } = new List<string>();
        /// <summary>The last turn's lines while its telling has not finished (a reload tells them again).</summary>
        public List<string> Untold { get; } = new List<string>();
        public List<Handout> Handouts { get; } = new List<Handout>();
        /// <summary>The last answer of the classifier (for logs and debugging).</summary>
        public Classification? LastClassification { get; set; }

        public Table(Evening e, Classifier classifier, Narrator narrator, IPlayers players, IRandom rng, Bestiary enemies, Narrator? highlight = null)
        {
            E = e; Classifier = classifier; Narrator = narrator; Highlight = highlight ?? narrator; Players = players; Rng = rng; Enemies = enemies;
            // the names of the places' foes, so the AI can match "大熊に斬りかかる" to [dire_bear]
            foreach (var id in e.Story.Places.SelectMany(p => p.Enemies).Distinct().Where(enemies.Has)) e.FoeNames[id] = enemies[id].Name;
        }

        public StoryDef Story => E.Story;
        public World W => E.World;
        public IEnumerable<Adventurer> Active => E.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn);
        int PartySize => Math.Max(1, Active.Count());

        // ================================================================ exploration

        sealed class StepRun
        {
            public Adventurer A = null!;
            public Declaration D = null!;
            public Classified C = null!;
            public Step S = null!;
            public int Index;
            public string Refused = "";
            /// <summary>For a move: the places passed, ending with the destination.</summary>
            public List<string> Path = new List<string>();
            public CheckRoll? Check;
            public RollRequest? Request;
            /// <summary>Decided without the step's own check (contest won, request free, item handed over...).</summary>
            public bool? Forced;
            public int FateNatural;
            /// <summary>Traps at a place reached for the first time: the entrant rolls to spot them (natural per trap).</summary>
            public List<(Trap trap, RollRequest req)> Traps = new List<(Trap, RollRequest)>();
            public List<int> TrapNaturals = new List<int>();
            public bool Stopped;
            public bool Secret => D.Secret;
            public bool Succeeded => Forced ?? (S.Sort == Sort.Auto || (Check != null && Check.GetsThrough));
        }

        /// <summary>One exploration turn: everyone's declarations at once.</summary>
        public async Task<TurnReport> ExploreAsync(IReadOnlyList<Declaration> declarations, CancellationToken ct = default)
        {
            if (Phase == Phase.Combat) return await CombatRoundAsync(declarations, ct);
            if (Phase == Phase.Afterword) return await AfterwordAsync(declarations, ct);
            if (Phase == Phase.Break || Phase == Phase.Over) return new TurnReport();
            return await ExploreTurnAsync(declarations, afterword: false, ct);
        }

        async Task<TurnReport> ExploreTurnAsync(IReadOnlyList<Declaration> declarations, bool afterword, CancellationToken ct)
        {
            var report = new TurnReport();
            E.Turn++;
            FirstVisitThisTurn.Clear();
            var decls = declarations.Where(d => E.Find(d.Who)?.IsUp == true && d.Text.Trim().Length > 0).ToList();

            // 1. sort (AI call 1), with one chance to declare again for refused actions
            var cls = await Classifier.ClassifyAsync(E, decls, false, ct);
            LastClassification = cls;
            report.Calls += cls.Calls;
            var runs = Plan(decls, cls, false);
            var refused = runs.Where(r => r.Index == 0 && r.Refused.Length > 0).ToList();
            if (refused.Count > 0)
            {
                var again = (await Players.RedeclareAsync(refused.Select(r => (r.A.Id, r.Refused)).ToList(), ct))
                    .Where(d => d.Text.Trim().Length > 0).ToList();
                foreach (var r in refused) report.Lines.Add($"{r.A.Name}: できない（{r.Refused}）");
                if (again.Count > 0)
                {
                    foreach (var d in again) { var old = decls.First(x => x.Who == d.Who); d.Secret = old.Secret; }
                    var cls2 = await Classifier.ClassifyAsync(E, again, false, ct);
                    report.Calls += cls2.Calls;
                    decls = decls.Where(d => again.All(a => a.Who != d.Who)).Concat(again).ToList();
                    foreach (var c in cls2.Declarations) { cls.Declarations.RemoveAll(x => x.Who == c.Who); cls.Declarations.Add(c); }
                    runs = Plan(decls, cls, false);
                }
            }
            foreach (var c in cls.Declarations.Where(c => c.Deferred.Count > 0)) report.Deferred[c.Who] = c.Deferred.ToList();
            foreach (var r in runs.Where(r => r.Refused.Length > 0 && !(r.Index == 0 && refused.Any(x => x.A == r.A))))
                Line(report, r, $"{r.A.Name}: できない（{r.Refused}）");

            var progress = false;
            var changes = new List<string>();

            // 2. contests between adventurers, secret detection, and the rolls (everyone at once)
            var contests = FindContests(runs);
            var secretRolls = SecretRolls(runs);
            await RollAll(runs, contests, secretRolls, report, ct);
            foreach (var c in contests) ResolveContest(c, report, changes);

            // 3. results, in declaration order; a failed step stops the rest of its declaration
            foreach (var group in runs.GroupBy(r => r.A))
            {
                foreach (var r in group.OrderBy(r => r.Index))
                {
                    if (r.Refused.Length > 0) { r.Stopped = true; continue; }
                    if (group.Any(x => x.Index < r.Index && x.Stopped)) { r.Stopped = true; continue; }
                    progress |= Apply(r, report, changes);
                    if (!r.Succeeded && r.S.Sort == Sort.Check) r.Stopped = true;
                }
            }
            foreach (var s in secretRolls) FinishSecret(s, runs, report);

            // 4. the world moves: clocks, acts, the climax, hints, tension
            AfterTurn(report, changes, ref progress, countTurn: !afterword);
            if (afterword && Phase != Phase.Over) Finish(EndingTrigger.Solved, report);
            // everyone still standing gives up the request: they leave the map
            if (Phase != Phase.Over && !afterword && Active.Where(a => a.IsUp).All(a => runs.Any(r => r.A == a && r.S.Abandon && !r.Stopped)))
                Finish(EndingTrigger.LeftMap, report);
            if (Phase == Phase.Over)
            {
                Save?.Invoke(this);
                await TellEndingAsync(report, ct);
                return report;
            }

            // a fight the classifier started (or the climax) begins now; its first round comes with the next declarations
            if ((cls.Fight != null && !runs.All(r => r.Secret)) || PendingFoes.Count > 0)
                if (Phase == Phase.Explore || Phase == Phase.Combat && E.Combat == null)
                    await StartFightAsync(cls.Fight?.Enemies ?? new List<string>(), cls.Fight?.Surprise ?? false, report, changes, ct);

            // 5. save, then tell
            report.Scene = SceneFor(runs, report);
            Save?.Invoke(this);
            await TellAsync(report, runs.Select(r => (r.A, r.D)).Distinct().ToList(), changes, ct);
            return report;
        }

        static void Line(TurnReport report, StepRun r, string line)
        {
            if (r.Secret) report.Private(r.A.Id, line); else report.Lines.Add(line);
        }

        // ---------------------------------------------------------------- planning (before any die)

        List<StepRun> Plan(IReadOnlyList<Declaration> decls, Classification cls, bool inCombat)
        {
            var runs = new List<StepRun>();
            foreach (var d in decls)
            {
                var a = E.Find(d.Who)!;
                var c = cls.For(d.Who);
                var pos = E.PlaceOf(a);
                if (c == null) continue;
                foreach (var (s, i) in c.Steps.Select((s, i) => (s, i)))
                {
                    var r = new StepRun { A = a, D = d, C = c, S = s, Index = i };
                    Validate(r, pos);
                    if (r.Refused.Length == 0 && r.Path.Count > 0) pos = r.Path[r.Path.Count - 1];
                    runs.Add(r);
                }
                // help: +2 on the first check of the one helped
            }
            foreach (var helper in runs.Where(r => r.S.Helps.Length > 0 && r.Refused.Length == 0).ToList())
            {
                var target = runs.FirstOrDefault(r => r.A.Id == helper.S.Helps && r.Check != null);
                if (target != null) target.Check = new CheckRoll(target.A, target.Check.Spec, helpers: 1, takeBoost: false);
                helper.Forced = true;
            }
            return runs;
        }

        /// <summary>
        /// The game's own checks before rolling (09_Spec 4): ids must be there, items held, people present, ways open, focus and silver enough.
        /// Also fixes the check from the sheet (clue, mechanism) and from the request table (attitude).
        /// </summary>
        void Validate(StepRun r, string here)
        {
            var s = r.S;
            var a = r.A;

            if (s.Sort == Sort.Impossible)
            {
                r.Refused = (s.Why?.Label() ?? "できない") + (s.WorldRule > 0 ? $" {s.WorldRule}" : "") + (s.How.Length > 0 ? $"。{s.How}" : "");
                return;
            }
            // the game knows who cannot move: bound, asleep, stunned (speech still works for the bound)
            if (!a.Statuses.CanAct && !(a.Statuses.Has(StatusKind.Bound) && s.Sort == Sort.Auto && s.Person.Length > 0))
            {
                var st = a.Statuses.List.First(x => StatusInfo.Disables(x.Kind)).Kind;
                r.Refused = $"状況。{st.Label()}で動けない" + (st == StatusKind.Bound ? "（縄を解けば動ける。仲間が解くか、STR か DEX の難しい判定）" : "");
                return;
            }
            // targets
            if (s.Item.Length > 0)
            {
                var own = a.Gear.Has(s.Item) || W.Holds(a.Id, s.Item);
                if (s.Take && !own)
                {
                    var where = W.WhereIs(s.Item);
                    var fromAlly = where != null && where.StartsWith("pc:") && s.Against.Length > 0;
                    if (where == null || where == "gone") { r.Refused = "ない"; return; }
                    if (!fromAlly && where != here)
                    {
                        r.Refused = where.StartsWith("npc:") ? "誰かが持っている" : where.StartsWith("pc:") ? "仲間が持っている" : where.StartsWith("enemy:") ? "敵が持っている" : "ここにはない";
                        return;
                    }
                }
                else if ((s.GiveTo.Length > 0 || s.PlaceAt.Length > 0) && !own) { r.Refused = "持っていない"; return; }
                if (s.GiveTo.Length > 0)
                {
                    var there = E.Find(s.GiveTo) is Adventurer to ? E.PlaceOf(to) : W.Cast.Find(s.GiveTo) is Npc p ? (p.WithParty ? here : p.Location) : null;
                    if (there == null) { r.Refused = "相手がいない"; return; }
                    if (there != here) { r.Refused = "相手がその場にいない"; return; }
                }
                if (s.PlaceAt.Length > 0 && s.PlaceAt != here) { r.Refused = "その場所にいない"; return; }
            }
            if (s.MoveTo.Length > 0 && s.MoveTo != here)
            {
                // along open ways, through places the party knows (09_Spec 12: never off the map's lines)
                var path = E.PathTo(here, s.MoveTo);
                if (path == null)
                {
                    var way = E.Ways(here).FirstOrDefault(w => w.link.To == s.MoveTo);
                    r.Refused = !E.OpenInAct(s.MoveTo) ? $"{Story.PlaceOf(s.MoveTo)?.Name}へは、物語がもっと進んでから"
                        : way.link != null && !way.open ? $"まだ通れない{(way.link.Text.Length > 0 ? "（" + way.link.Text + "）" : "")}"
                        : $"{Story.PlaceOf(s.MoveTo)?.Name}への道を、まだ知らない";
                    return;
                }
                r.Path = path;
            }
            if (s.Person.Length > 0 && !E.PeopleAt(here).Any(p => p.Id == s.Person)) { r.Refused = $"{W.Cast.Find(s.Person)?.Name ?? "その人"}はここにいない"; return; }
            if (s.Mechanism.Length > 0 && !E.MechanismsAt(here).Any(m => m.Id == s.Mechanism)) s.Mechanism = "";
            // telling the others a clue only this adventurer knows: anywhere, no roll (Apply makes it the party's)
            var sharing = !r.Secret && s.Clue.Length > 0 && W.Knows(a.Id, s.Clue) && !W.Knows("", s.Clue);
            if (s.Clue.Length > 0 && !sharing && !E.CluesHere(here).Any(c => c.Id == s.Clue)) s.Clue = "";
            if (sharing) { s.Sort = Sort.Auto; s.Check = null; }
            if (s.Pay > 0 && a.Silver < s.Pay) { r.Refused = "銀貨が足りない"; return; }
            if (s.Buy.Length > 0 && !(Story.PlaceOf(here)?.Shop ?? false)) { r.Refused = "ここには店がない"; return; }
            if (s.Rest && RestedAct == W.Act) { r.Refused = "この幕ではもう休んだ（休めるのは 1 幕に 1 回）"; return; }

            // named techniques: a known name keeps its first content; a new one is written on the sheet
            if (s.Technique.Length > 0)
            {
                var known = a.FindTechnique(s.Technique);
                if (known != null) { s.Effect = new Effect(known.Kind, known.Size); s.EffectAbility = known.Ability; }
                else if (s.Effect is Effect fx) a.AddTechnique(s.Technique, s.TechniqueNote, fx.Kind, fx.Size, s.EffectAbility ?? s.Check?.Ability ?? Ability.Str);
            }
            if (s.Effect is Effect ef)
            {
                if (a.Focus < ef.FocusCost) { r.Refused = "気力が足りない"; return; }
                if (!a.HasSpecialty(ef.Kind) && s.Check == null && !ef.Kind.IsSupport()) s.Sort = Sort.Check;
            }

            // the check: the sheet's numbers win over the AI's for clues and mechanisms
            if (!sharing && s.Clue.Length > 0 && Story.ClueOf(s.Clue) is Clue clue && s.Person.Length == 0)
            {
                if (clue.Access == ClueAccess.Free) { s.Sort = Sort.Auto; s.Check = null; }
                else if (clue.Access == ClueAccess.Check)
                {
                    s.Sort = Sort.Check;
                    s.Check ??= new CheckSpec();
                    s.Check.Ability = clue.Ability!.Value; s.Check.Tier = clue.Tier!.Value; s.Check.Important = clue.Important;
                    if (s.Check.Reason.Length == 0) s.Check.Reason = clue.How;
                }
            }
            if (s.Mechanism.Length > 0 && Story.Mechanisms.First(m => m.Id == s.Mechanism) is MechanismDef md)
            {
                if (md.NoCheck) { s.Sort = Sort.Auto; s.Check = null; }
                else
                {
                    s.Sort = Sort.Check;
                    s.Check ??= new CheckSpec();
                    s.Check.Ability = md.Ability; s.Check.Tier = md.Tier; s.Check.Important = true;
                    if (s.Check.Reason.Length == 0) s.Check.Reason = md.Name;
                    if (s.Effect?.Kind == EffectKind.Unlock && a.HasSpecialty(EffectKind.Unlock))
                    {
                        if (s.Effect.Value.Size >= EffectSize.Medium && md.Tier <= Tier.Normal) { s.Sort = Sort.Auto; s.Check = null; }
                        else s.Check.Tier = md.Tier.Shift(-1);
                    }
                }
            }
            // rescued people come along without a roll, once they can
            if (s.Recruit && s.Person.Length > 0 && W.Cast.Find(s.Person) is Npc esc && esc.Escort)
            {
                if (Story.PersonOf(esc.Id)?.JoinWhen.Holds(W) == false) { r.Refused = $"{esc.Name}はまだ連れ出せない"; return; }
                s.Sort = Sort.Auto; s.Check = null; s.Request = null; r.Forced = true;
            }
            // requests: the attitude table decides
            if (s.Person.Length > 0 && s.Request != null && W.Cast.Find(s.Person) is Npc npc)
            {
                var how = s.Approach ?? Approach.Ask;
                var sweet = s.Pay >= Npcs.Price(s.Request.Value);
                var need = Npcs.Need(npc.Attitude, s.Request.Value, sweet);
                if (!need.Possible) { r.Refused = $"{npc.Name}は敵意を向けていて、大きな頼みは聞かない"; return; }
                if (!need.NeedsRoll) { s.Sort = Sort.Auto; s.Check = null; r.Forced = true; }
                else
                {
                    s.Sort = Sort.Check;
                    s.Check ??= new CheckSpec();
                    s.Check.Ability = Npcs.AbilityFor(how);
                    s.Check.Tier = need.Tier!.Value;
                    s.Check.Shifts.Clear();
                    if (s.Clue.Length > 0) s.Check.Important = Story.ClueOf(s.Clue)?.Important ?? false;
                    if (s.Check.Reason.Length == 0) s.Check.Reason = $"{npc.Name}への{(s.Request == RequestSize.Small ? "小さな" : s.Request == RequestSize.Normal ? "" : "大きな")}頼み";
                }
            }
            if (s.Sort == Sort.Check && s.Check != null) r.Check = new CheckRoll(a, s.Check, takeBoost: false);
        }

        // ---------------------------------------------------------------- contests and secrets

        sealed class ContestRun
        {
            public List<(StepRun run, ContestSide side)> Sides { get; } = new List<(StepRun, ContestSide)>();
            public bool Noisy;
            public ContestResult? Result;
        }

        /// <summary>
        /// Contests the game finds by ids (09_Spec 4): two adventurers taking the same item, or one acting against another who acts back.
        /// Against someone who does nothing about it, only the actor rolls (10 + the other's modifier).
        /// </summary>
        List<ContestRun> FindContests(List<StepRun> runs)
        {
            var list = new List<ContestRun>();
            var live = runs.Where(r => r.Refused.Length == 0).ToList();
            foreach (var g in live.Where(r => r.S.Item.Length > 0 && (r.S.Take || r.S.Against.Length > 0)).GroupBy(r => r.S.Item))
            {
                var involved = live.Where(r => r.S.Item == g.Key && (r.S.Take || r.S.Against.Length > 0 || r.S.Sort == Sort.Check)).GroupBy(r => r.A).Select(x => x.First()).ToList();
                if (involved.Count < 2) continue;
                var holder = W.WhereIs(g.Key);
                var c = new ContestRun { Noisy = true };
                foreach (var r in involved)
                {
                    var defender = holder == World.Pc(r.A.Id) || (!r.S.Take && involved.Any(x => x != r && x.S.Take));
                    var ability = r.Check?.Spec.Ability ?? (defender ? Ability.Agi : Ability.Dex);
                    c.Sides.Add((r, new ContestSide { Id = r.A.Id, Ability = ability, Modifier = r.A.Modifier(ability), Defender = defender }));
                }
                list.Add(c);
            }
            // one adventurer against another who answers with a step of their own toward the first
            foreach (var r in live.Where(r => r.S.Against.Length > 0 && r.S.Item.Length == 0))
            {
                var back = live.FirstOrDefault(x => x.A.Id == r.S.Against && x.S.Against == r.A.Id);
                if (back == null || list.Any(c => c.Sides.Any(s => s.run == r || s.run == back))) continue;
                var c = new ContestRun { Noisy = r.S.Noisy || back.S.Noisy };
                foreach (var (x, def) in new[] { (r, false), (back, true) })
                {
                    var ability = x.Check?.Spec.Ability ?? Ability.Str;
                    c.Sides.Add((x, new ContestSide { Id = x.A.Id, Ability = ability, Modifier = x.A.Modifier(ability), Defender = def }));
                }
                list.Add(c);
            }
            // a contest takes over these steps' own checks
            foreach (var c in list) foreach (var (run, _) in c.Sides) run.Check = null;
            return list;
        }

        sealed class SecretRun
        {
            public StepRun Run = null!;
            public int? Target;
            public RollRequest? Request;
            public int Natural;
            public bool Found;
        }

        List<SecretRun> SecretRolls(List<StepRun> runs)
        {
            var list = new List<SecretRun>();
            foreach (var r in runs.Where(r => r.Secret && r.Index == 0 && r.Refused.Length == 0))
            {
                var here = E.PlaceOf(r.A);
                var watchers = E.At(here).Where(x => x != r.A).ToList();
                var wary = E.PeopleAt(here).Any(p => p.Attitude <= Attitude.Wary && !p.WithParty);
                var s = new SecretRun { Run = r, Target = Secrecy.Target(watchers, wary) };
                if (s.Target != null)
                    s.Request = new RollRequest
                    {
                        Who = r.A.Id, Kind = RollKind.Secret, Private = true, For = "見つからずにやり遂げる",
                        Heading = $"AGI・目標 {s.Target}", Bonus = r.A.Modifier(Secrecy.RollAbility), Target = s.Target.Value,
                    };
                list.Add(s);
            }
            return list;
        }

        // ---------------------------------------------------------------- rolling

        async Task RollAll(List<StepRun> runs, List<ContestRun> contests, List<SecretRun> secrets, TurnReport report, CancellationToken ct)
        {
            var reqs = new List<RollRequest>();
            foreach (var r in runs.Where(r => r.Check != null && r.Refused.Length == 0))
            {
                r.Request = new RollRequest
                {
                    Who = r.A.Id, Kind = RollKind.Check, Heading = r.Check!.Heading, For = r.S.Text, Check = r.Check, Private = r.Secret,
                    Bonus = r.Check.Modifier + r.Check.Bonus, Target = r.Check.Target, Important = r.Check.Spec.Important,
                };
                r.Request.Reasons.AddRange(r.Check.Reasons);
                reqs.Add(r.Request);
            }
            foreach (var r in runs.Where(r => r.S.Sort == Sort.FateDie && r.Refused.Length == 0))
            {
                r.Request = new RollRequest { Who = r.A.Id, Kind = RollKind.Fate, Heading = $"運命のダイス（{r.S.Likelihood.Label()}: {r.S.Likelihood.Needed()} 以上で「はい」）", For = r.S.Question, CoinAllowed = false, Target = r.S.Likelihood.Needed() };
                reqs.Add(r.Request);
            }
            var contestReqs = new List<(ContestSide side, RollRequest req)>();
            foreach (var c in contests)
                foreach (var (run, side) in c.Sides)
                {
                    var req = new RollRequest
                    {
                        Who = run.A.Id, Kind = RollKind.Contest, Heading = $"対抗: {string.Join(" ／ ", c.Sides.Select(x => $"{x.run.A.Name} {x.side.Ability.Code()}"))}",
                        For = run.S.Text, Bonus = side.Modifier, CoinAllowed = true,
                    };
                    contestReqs.Add((side, req));
                    reqs.Add(req);
                }
            foreach (var s in secrets.Where(s => s.Request != null)) reqs.Add(s.Request!);
            foreach (var r in runs.Where(r => r.Refused.Length == 0 && r.Path.Count > 0))
                foreach (var trap in r.Path.Where(W.FirstVisit).SelectMany(p => Story.PlaceOf(p)?.Traps ?? new List<Trap>()))

                {
                    if (runs.Any(o => o != r && o.Traps.Any(x => x.trap == trap))) continue;   // the first one in meets it
                    var req = new RollRequest { Who = r.A.Id, Kind = RollKind.Trap, Heading = $"罠に気付く（{trap.Ability.Code()}・{trap.Tier.Label()} {(int)trap.Tier}）", For = trap.Text, Bonus = r.A.Modifier(trap.Ability), Target = (int)trap.Tier };
                    r.Traps.Add((trap, req));
                    reqs.Add(req);
                }
            if (reqs.Count == 0) return;

            var naturals = await Players.RollAsync(reqs, ct);
            for (int i = 0; i < reqs.Count; i++) Deliver(reqs[i], naturals[i], runs, contestReqs, secrets);

            // fate coins for failed checks, dodges and contest losses (not the fate die, not secrets)
            var failed = reqs.Where(q => q.CoinAllowed && q.Kind == RollKind.Check && q.Check != null && !q.Check.Succeeded && !q.Check.Rerolled
                && E.Find(q.Who)?.FateCoins > 0).ToList();
            if (failed.Count == 0) return;
            var use = await Players.OfferCoinsAsync(failed, ct);
            var again = failed.Where((q, i) => i < use.Count && use[i]).ToList();
            if (again.Count == 0) return;
            var re = await Players.RollAsync(again, ct);
            for (int i = 0; i < again.Count; i++) again[i].Check!.Reroll(E.Find(again[i].Who)!, re[i]);
        }

        static void Deliver(RollRequest q, int natural, List<StepRun> runs, List<(ContestSide side, RollRequest req)> contests, List<SecretRun> secrets)
        {
            switch (q.Kind)
            {
                case RollKind.Check: q.Check!.Apply(natural); break;
                case RollKind.Fate: runs.First(r => r.Request == q).FateNatural = natural; break;
                case RollKind.Trap: runs.First(r => r.Traps.Any(x => x.req == q)).TrapNaturals.Add(natural); break;
                case RollKind.Contest: contests.First(x => x.req == q).side.Natural = natural; break;
                case RollKind.Secret: var s = secrets.First(x => x.Request == q); s.Natural = natural; s.Found = Secrecy.Found(natural, q.Bonus, q.Target); break;
            }
        }

        void ResolveContest(ContestRun c, TurnReport report, List<string> changes)
        {
            c.Result = Contest.Resolve(c.Sides.Select(x => x.side).ToList());
            foreach (var (run, side) in c.Sides) run.Forced = side == c.Result.Winner;
            var winner = c.Sides.First(x => x.side == c.Result.Winner).run;
            report.Lines.Add($"対抗: {string.Join(" ／ ", c.Sides.Select(x => $"{x.run.A.Name} {x.side.Natural}+{x.side.Modifier} = {x.side.Total}"))} → {winner.A.Name} の勝ち" +
                (c.Result.WinnerBonus ? "（会心）" : "") + string.Concat(c.Result.Mishaps.Select(m => $"。{E.Find(m.Id)?.Name} に小さな災難")));
            if (c.Noisy) foreach (var cl in W.Clocks.PushAll(W.Act, 1)) changes.Add($"騒ぎで「{cl.Def.Name}」が進んだ");
            // a secret step caught in a contest is out in the open
            foreach (var (run, _) in c.Sides.Where(x => x.run.Secret)) run.D.Secret = false;
        }

        void FinishSecret(SecretRun s, List<StepRun> runs, TurnReport report)
        {
            var r = s.Run;
            var result = string.Join(" / ", runs.Where(x => x.A == r.A && !x.Stopped).Select(x => x.S.Text + (x.Succeeded ? "" : "（うまくいかなかった）")));
            Secrets.Add(new SecretRecord { Who = r.A.Id, Text = r.D.Text, Result = result, Found = s.Found, Turn = E.Turn });
            if (s.Request != null) report.Private(r.A.Id, $"見つかる判定: {s.Natural}+{s.Request.Bonus} = {s.Natural + s.Request.Bonus}（目標 {s.Target}）{(s.Found ? " 見つかった" : " 気付かれていない")}");
            if (s.Found)
            {
                report.Lines.Add($"{r.A.Name} の内緒の行動が見つかった: {r.D.Text}");
                foreach (var x in runs.Where(x => x.A == r.A)) x.D.Secret = false;
                foreach (var i in r.A.Gear.Items.Where(i => i.Concealed)) i.Concealed = false;
            }
        }

        // ---------------------------------------------------------------- applying a step

        /// <summary>Applies one step's result to the world. Returns whether it was progress (new clue, place, conviction...).</summary>
        bool Apply(StepRun r, TurnReport report, List<string> changes)
        {
            var s = r.S;
            var a = r.A;
            var progress = false;
            if (r.Check != null) Line(report, r, r.Check.Describe() + (s.Check!.Reason.Length > 0 ? $"（{s.Check.Reason}）" : ""));
            if (s.Sort == Sort.FateDie)
            {
                var ans = FateDie.Answer(s.Likelihood, r.FateNatural);
                var fact = $"{s.Question} → {ans.Label()}";
                E.Facts.Add(fact);
                Line(report, r, $"運命のダイス（{s.Likelihood.Label()}）: {r.FateNatural} → {ans.Label()}");
                changes.Add("事実メモ: " + fact);
                return false;
            }
            var ok = r.Succeeded;
            if (!ok && r.Check != null) return false;
            var failedButThrough = r.Check != null && !r.Check.Succeeded && r.Check.Spec.Important;
            if (failedButThrough) foreach (var cost in r.Check!.CostsDue()) PayCost(a, cost, report, changes, r);

            // silver and the shop
            if (s.Pay > 0) { Shop.Pay(a, s.Pay); Line(report, r, $"{a.Name} は銀貨 {s.Pay} 枚を渡した（残り {a.Silver}）"); }
            if (s.Buy.Length > 0)
            {
                var why = Shop.Buy(a, s.Buy);
                Line(report, r, why == null ? $"{a.Name} は{Shop.Wares.First(w => w.Key == s.Buy).Name}を買った（残り {a.Silver}）" : $"{a.Name}: 買えない（{why}）");
            }
            // clue
            if (s.Clue.Length > 0 && Story.ClueOf(s.Clue) is Clue clue)
            {
                var onlyMine = W.Knows(a.Id, clue.Id) && !W.Knows("", clue.Id);
                var isNew = W.Learn(clue.Id, a.Id, r.Secret);
                // a clue found in secret, told to everyone (the clue book's 見せる, as a declaration)
                if (onlyMine && !r.Secret)
                {
                    progress = true;
                    report.Lines.Add($"手がかりを皆に明かした（{a.Name}）: {clue.Text}");
                    changes.Add($"{a.Name}が、自分だけが知っていた手がかりを皆に明かした: {clue.Text}");
                }
                W.Events.Add("clue:" + clue.Id);
                if (isNew)
                {
                    progress = true;
                    if (r.Secret) report.Private(a.Id, $"手がかり（自分だけが知っている）: {clue.Text}");
                    else report.Lines.Add($"手がかり: {clue.Text}");
                    changes.Add((r.Secret ? $"（{a.Name}だけが知る）" : "") + "手がかりを得た: " + clue.Text);
                }
            }
            // mechanism
            if (s.Mechanism.Length > 0 && E.Mechanisms.TryGetValue(s.Mechanism, out var mech) && mech.Trigger(W))
            {
                Line(report, r, $"{mech.Def.Name}が動いた");
                changes.Add($"仕掛け「{mech.Def.Name}」が動いた（出来事: {mech.Def.Event}）");
                progress = true;
            }
            // items
            if (s.Item.Length > 0) MoveItem(r, report, changes);
            // a person
            if (s.Person.Length > 0 && W.Cast.Find(s.Person) is Npc npc)
            {
                var how = s.Approach ?? Approach.Ask;
                if (r.Check != null)
                {
                    var before = npc.Attitude;
                    npc.Attitude = Npcs.AfterRequest(npc.Attitude, how, r.Check.Outcome);
                    if (npc.Attitude != before) changes.Add($"{npc.Name}の態度: {before.Label()} → {npc.Attitude.Label()}");
                }
                else if (how == Approach.Threaten) { var before = npc.Attitude; npc.Change(AttitudeReason.Threatened); changes.Add($"{npc.Name}の態度: {before.Label()} → {npc.Attitude.Label()}"); }
                if ((s.Recruit || s.Request == RequestSize.Large) && Story.PersonOf(npc.Id) is Person def && def.Joins != JoinWillingness.Never && !npc.WithParty)
                {
                    if (W.Cast.Join(npc.Id, PartySize, def.JoinWhen.Holds(W)))
                    {
                        report.Lines.Add($"{npc.Name}が一行に加わった");
                        changes.Add($"{npc.Name}が一行と一緒に来る（{def.JoinNote}）");
                    }
                    else changes.Add($"{npc.Name}は一緒には来ない（{(def.Joins == JoinWillingness.Conditional ? "条件がそろっていない" : "仲間がいっぱい")}）");
                }
            }
            // moving
            if (s.MoveTo.Length > 0 && s.MoveTo != E.PlaceOf(a))
            {
                var first = W.FirstVisit(s.MoveTo);
                foreach (var passed in r.Path.Take(Math.Max(0, r.Path.Count - 1))) { W.Visited.Add(passed); W.Events.Add("visited:" + passed); }
                W.Move(a.Id, s.MoveTo);
                W.Events.Add("visited:" + s.MoveTo);
                // escorted people and companions come along with the one who leads
                foreach (var p in W.Cast.People.Where(p => p.WithParty)) p.Location = s.MoveTo;
                if (first) { progress = true; FirstVisitThisTurn.Add(s.MoveTo); changes.Add($"初めて「{Story.PlaceOf(s.MoveTo)?.Name}」に着いた"); }
                if (first && Story.PlaceOf(s.MoveTo) is Place pl && pl.Tense) E.Tension.Trigger();
                for (int i = 0; i < r.Traps.Count && i < r.TrapNaturals.Count; i++)
                {
                    var (trap, req) = r.Traps[i];
                    var n = r.TrapNaturals[i];
                    var spotted = n == 20 || (n != 1 && n + req.Bonus >= req.Target);
                    if (spotted) { Line(report, r, $"{a.Name} は{trap.Text}に気付いて外した（{n}+{req.Bonus}）"); changes.Add($"{a.Name}が罠（{trap.Text}）を見抜いた"); continue; }
                    a.TakeDamage(trap.Damage);
                    if (trap.Status is StatusKind st) a.Statuses.Add(st, "trap", a.Gear.Wears(Trait.Blessed));
                    Line(report, r, $"{a.Name} が{trap.Text}にかかった（{n}+{req.Bonus}）: {trap.Damage} ダメージ{(trap.Status != null ? "、" + trap.Status.Value.Label() : "")}（HP {a.Hp}/{a.MaxHp}）");
                    changes.Add($"{a.Name}が罠（{trap.Text}）にかかって傷を負った");
                }
            }
            // effects outside fights
            if (s.Effect is Effect ef && E.Combat == null)
            {
                a.SpendFocus(ef.FocusCost);
                var target = E.Find(s.Target) ?? a;
                switch (ef.Kind)
                {
                    case EffectKind.Heal: target.Heal(Effects.HealAmount(ef.Size)); target.Statuses.Remove(StatusKind.Poison); Line(report, r, $"{target.Name} の傷がふさがる（HP {target.Hp}/{target.MaxHp}）"); break;
                    case EffectKind.Boost:
                        foreach (var t in Effects.BoostAll(ef.Size) ? Active.ToList() : new List<Adventurer> { target }) t.PendingBoost = Math.Max(t.PendingBoost, Effects.BoostBonus(ef.Size));
                        break;
                    case EffectKind.Ward: target.Ward = Math.Max(target.Ward, Effects.WardReduction(ef.Size)); break;
                }
            }
            // deduction
            if (s.Conclusion.Length > 0 && s.Correct && !W.Convictions.Contains(s.Conclusion) && Story.Conclusions.FirstOrDefault(c => c.Id == s.Conclusion) is Conclusion con)
            {
                var known = con.Clues.Count(cid => W.Knows(a.Id, cid));
                if (known >= 2)
                {
                    W.Convictions.Add(con.Id);
                    report.Lines.Add($"確信: {con.Text}");
                    changes.Add($"一行は確信に至った: {con.Text}（語りに 1〜2 文、確信の手応えを）");
                    if (con.RewardClock.Length > 0 && W.Clocks.Find(con.RewardClock) is Clock rc) rc.Better();
                    progress = true;
                }
            }
            // an unexpected way through the climax (once per story)
            if (s.Unexpected && E.ClimaxStarted && !UnexpectedUsed && r.Check?.Succeeded == true && SolvedBy.Length == 0)
            {
                UnexpectedUsed = true;
                var sol = Story.Climax.Solutions.FirstOrDefault(x => x.Then.Any(t => t.Kind == "person_state")) ?? Story.Climax.Solutions.First();
                RunActions(sol.Then, report, changes);
                SolveClimax(sol.Id, report, changes);
                changes.Add("想定外のやり方で、山場が解けた");
            }
            if (s.PlaceRule) foreach (var rule in Story.PlaceOf(E.PlaceOf(a))?.Rules ?? new List<PlaceRule>())
                    if (W.Clocks.Find(rule.Clock) is Clock pc && pc.Push(rule.Amount) | true) changes.Add($"場所の決まりに触れた: {rule.Text}");
            if (s.Slow || s.Noisy) foreach (var cl in W.Clocks.PushAll(W.Act, 1)) changes.Add($"「{cl.Def.Name}」が満ちた");
            if (s.Rest && RestedAct != W.Act)
            {
                RestedAct = W.Act;
                foreach (var x in E.At(E.PlaceOf(a))) x.Rest();
                W.Clocks.PushAll(W.Act, Clocks.Amount(ClockPush.Rest));
                report.Lines.Add("一行は休息をとった（HP が半分、気力は全部戻る）");
                changes.Add("休息");
            }
            return progress;
        }

        void PayCost(Adventurer a, Cost cost, TurnReport report, List<string> changes, StepRun r)
        {
            switch (cost)
            {
                case Cost.Clock: W.Clocks.PushAll(W.Act, 1); break;
                case Cost.Wound: a.TakeDamage(3); break;
                case Cost.Noticed:
                    foreach (var p in E.PeopleAt(E.PlaceOf(a)).Where(p => !p.WithParty)) p.Attitude = p.Attitude.Move(-1);
                    W.Events.Add("noticed:" + E.PlaceOf(a));
                    break;
                case Cost.LoseItem:
                    var lose = a.Gear.Items.LastOrDefault(i => i.Kind == ItemKind.Tool || i.Kind == ItemKind.Consumable);
                    if (lose != null) a.Gear.Remove(lose.Id);
                    break;
            }
            Line(report, r, $"代償: {cost.Label()}{(cost == Cost.Wound ? $"（{a.Name} HP {a.Hp}/{a.MaxHp}）" : "")}");
            changes.Add($"失敗の代償: {a.Name} {cost.Label()}");
        }

        void MoveItem(StepRun r, TurnReport report, List<string> changes)
        {
            var s = r.S;
            var a = r.A;
            var story = Story.ItemOf(s.Item);
            var name = story?.Item.Name ?? a.Gear.Find(s.Item)?.Name ?? s.Item;
            if (s.Take)
            {
                var from = W.WhereIs(s.Item);
                if (from != null && from.StartsWith("pc:") && E.Find(from.Substring(3)) is Adventurer other && other != a)
                {
                    var it = other.Gear.Remove(s.Item);
                    if (it != null) { it.Concealed = r.Secret; a.Gear.Add(it); }
                }
                else if (story != null && !a.Gear.Has(s.Item))
                {
                    var it = story.Item.Clone();
                    it.StoryId = story.Item.Id;
                    it.Concealed = r.Secret;
                    if (!a.Gear.Add(it)) { Line(report, r, $"{a.Name}: 持ち物がいっぱいで{name}を持てない"); return; }
                }
                if (story != null) W.PutItem(s.Item, World.Pc(a.Id));
                Line(report, r, $"{a.Name} は{name}を手に入れた{(r.Secret ? "（隠し持ち）" : "")}");
                changes.Add($"{(r.Secret ? "（内緒）" : "")}{a.Name}が{name}を手に入れた");
            }
            else if (s.GiveTo.Length > 0)
            {
                var it = a.Gear.Remove(s.Item);
                if (E.Find(s.GiveTo) is Adventurer to)
                {
                    if (it != null) { it.Concealed = false; to.Gear.Add(it); }
                    if (story != null) W.PutItem(s.Item, World.Pc(to.Id));
                    changes.Add($"{a.Name}が{name}を{to.Name}に渡した");
                }
                else
                {
                    if (story != null) W.PutItem(s.Item, World.NpcHolder(s.GiveTo));
                    changes.Add($"{a.Name}が{name}を{W.Cast.Find(s.GiveTo)?.Name}に渡した");
                }
                Line(report, r, $"{a.Name} は{name}を渡した");
            }
            else if (s.PlaceAt.Length > 0)
            {
                a.Gear.Remove(s.Item);
                if (story != null) W.PutItem(s.Item, s.PlaceAt);
                Line(report, r, $"{a.Name} は{name}を置いた");
                changes.Add($"{a.Name}が{name}を{Story.PlaceOf(s.PlaceAt)?.Name}に置いた");
            }
            else if (a.Gear.Find(s.Item) is Item own && own.Kind == ItemKind.Consumable)
            {
                var target = E.Find(s.Target) ?? a;
                var line = Consumables.Use(a, own.Id, target, out _);
                if (line != null) { Line(report, r, line); changes.Add(line); }
            }
            else if (a.Gear.Find(s.Item) is Item seen && seen.Concealed && !r.Secret)
            {
                seen.Concealed = false;   // used in front of others: not hidden any more
            }
        }

        // ---------------------------------------------------------------- after the steps

        /// <summary>
        /// The world's own moves after a turn: clocks (by turns, then fills), visible clocks, act changes, the climax and its solutions,
        /// hints, tension, the ending triggers.
        /// </summary>
        void AfterTurn(TurnReport report, List<string> changes, ref bool progress, bool countTurn)
        {
            if (countTurn) W.Clocks.Turn(W.Act, PartySize);
            // rewinds the sheet allows, once each, as soon as their conditions hold
            foreach (var sc in Story.Clocks)
                foreach (var kv in sc.RewindWhen.Where(kv => !kv.Value.IsEmpty && kv.Value.Holds(W)))
                    if (W.Clocks.Find(sc.Def.Id) is Clock rc && !rc.RewindsUsed.Contains(kv.Key) && rc.Rewind(kv.Key))
                    {
                        var text = sc.Def.Rewinds.First(r => r.Id == kv.Key).Text;
                        if (rc.Visible) report.Lines.Add($"「{sc.Def.Name}」が戻った（{text}）");
                        changes.Add($"「{sc.Def.Name}」が戻った（{text}）");
                    }
            FireClocks(report, changes);
            foreach (var c in E.RevealClocks()) { report.Lines.Add($"時計が見えるようになった: {c.Def.Name}"); changes.Add($"一行は「{c.Def.Name}」の危険を知った"); }
            foreach (var c in W.Clocks.All.Where(c => !c.Visible && c.PastHalf && !c.Def.Good && c.RunsIn(W.Act)))
                changes.Add($"（まだ一行は知らないが）「{c.Def.SecretName}」の兆しを語りにそっと混ぜる");

            // acts move by the sheet
            var act = Story.Acts.FirstOrDefault(a => a.N == W.Act);
            while (act != null && act.N < Story.ActCount && !act.Advance.IsEmpty && act.Advance.Holds(W))
            {
                W.Act++;
                report.ActChanged = true;
                progress = true;
                foreach (var a in Active) a.RestoreFocus();
                // growth at the end of an act (short: acts 1-2, long: acts 1-4): each standing adventurer picks one
                foreach (var a in Active.Where(x => x.Life != Life.Dead)) if (!GrowthDue.Contains(a.Id)) GrowthDue.Add(a.Id);
                report.GrowthDue = true;
                act = Story.Acts.FirstOrDefault(a => a.N == W.Act);
                if (Story.Long && W.Act == 4 && Phase != Phase.Break)
                {
                    // a long story pauses after act 3: saved here, the next evening starts from here (09_Spec 10)
                    Phase = Phase.Break;
                    foreach (var c in W.Clocks.All) c.Paused = true;
                    report.Lines.Add("中休み: 物語はここで一晩明かす（続きは次の晩）");
                    changes.Add("中休み。一行は野営か宿で一晩明かす。穏やかに、次の晩への余韻を残して語る。");
                    break;
                }
                report.Lines.Add($"物語は次の幕へ: {act?.Title}");
                changes.Add($"新しい幕が始まる（{act?.Title}。目標: {act?.Goal}）。幕の始めは山場の語り。");
                if (act != null) RunActions(act.OnStart, report, changes);
            }
            // the climax
            if (!E.ClimaxStarted && SolvedBy.Length == 0 && Story.Climax.Starts.Holds(W))
            {
                E.ClimaxStarted = true;
                W.Events.Add("climax");
                report.ClimaxStarted = true;
                progress = true;
                changes.Add("山場が始まる: " + Story.Climax.Text);
                RunActions(Story.Climax.OnStart, report, changes);
            }
            if (E.ClimaxStarted && SolvedBy.Length == 0)
                foreach (var sol in Story.Climax.Solutions)
                    if (sol.When.Holds(W)) { RunActions(sol.Then, report, changes); SolveClimax(sol.Id, report, changes); break; }

            // whoever came to the tavern in the opening goes home once the party has left it (owner 2026-10-25: the client was told of
            // but not drawn, being placed at home from the start)
            if (!E.At(Story.Start).Any())
                foreach (var p in Story.People.Where(p => p.Opening && p.At != Story.Start))
                    if (W.Cast.Find(p.Id) is Npc n && n.Location == Story.Start && !n.WithParty) n.Location = p.At;

            // hints and tension
            E.Hints.Turn(progress, E.Difficulty);
            if (E.Tension.On) E.Tension.Turn();
            if (W.Clocks.AnyAtLastSegment(W.Act)) E.Tension.Trigger();
            // a fallen ally: outside a fight at once; in a fight only once it is over (the fight's own music and pace come first)
            if (E.Combat != null) { if (Active.Any(a => a.Life == Life.Down)) E.Tension.AfterFight = true; }
            else if (E.Tension.AfterFight || Active.Any(a => a.Life == Life.Down)) { E.Tension.AfterFight = false; E.Tension.Trigger(); }

            // endings the world decides
            if (!Active.Any(a => a.Life == Life.Up)) { Finish(EndingTrigger.Wiped, report); return; }
        }

        void FireClocks(TurnReport report, List<string> changes)
        {
            foreach (var c in W.Clocks.TakeFired())
            {
                report.Lines.Add($"時計が満ちた: {c.Def.Name}");
                changes.Add($"「{c.Def.Name}」が満ちた: {c.Def.FullText}");
                if (Story.ClockOf(c.Id) is StoryClock sc) RunActions(sc.OnFull, report, changes);
                if (c.Def.OnFull.Contains(ClockFull.Ending)) { Finish(EndingTrigger.ClockFull, report); return; }
            }
        }

        void SolveClimax(string id, TurnReport report, List<string> changes)
        {
            SolvedBy = id;
            W.Events.Add("climax_solved");
            var sol = Story.Climax.Solutions.First(s => s.Id == id);
            report.Lines.Add($"山場が解けた: {sol.Text}");
            changes.Add($"山場が解けた（{sol.Text}）。最後に「その後」の宣言を聞く。");
            if (E.Combat != null) EndFight(CombatEnd.Won, report, changes);
            Phase = Phase.Afterword;
        }

        /// <summary>Runs the sheet's actions (act starts, clocks filling, solutions).</summary>
        void RunActions(IEnumerable<StoryAction> actions, TurnReport report, List<string> changes)
        {
            foreach (var x in actions)
            {
                if (x.Text.Length > 0) changes.Add(x.Text);
                switch (x.Kind)
                {
                    case "event": W.Events.Add(x.Id); break;
                    case "person_state":
                        if (W.Cast.Find(x.Id) is Npc p)
                        {
                            p.State = Enum.Parse<PersonState>(x.Value);
                            if (p.Fighting != null && p.State != PersonState.Alive) p.Fighting.Left = true;
                        }
                        break;
                    case "person_move":
                        if (W.Cast.Find(x.Id) is Npc pm)
                        {
                            pm.Location = x.Value == "party" ? W.PlacesOfParty.First() : x.Value;
                            if (x.Value == "party" && Story.PersonOf(pm.Id)?.Enemy.Length > 0) PendingFoes.Add("person:" + pm.Id);
                        }
                        break;
                    case "item_move": W.PutItem(x.Id, x.Value); break;
                    case "clock_push": if (W.Clocks.Find(x.Id) is Clock c && c.Push(x.Amount)) FireClocks(report, changes); break;
                    case "clock_reveal": if (W.Clocks.Find(x.Id) is Clock cr) cr.Visible = true; break;
                    case "enemies": PendingFoes.AddRange(x.Enemies); break;
                    case "climax":
                        if (!E.ClimaxStarted)
                        {
                            E.ClimaxStarted = true;
                            W.Events.Add("climax");
                            report.ClimaxStarted = true;
                            RunActions(Story.Climax.OnStart, report, changes);
                        }
                        break;
                }
            }
        }

        /// <summary>Enemies (and story people who fight) the sheet called in; the fight starts after the turn's results.</summary>
        public List<string> PendingFoes { get; } = new List<string>();

        SceneKind SceneFor(List<StepRun> runs, TurnReport report)
        {
            if (report.ClimaxStarted || report.ActChanged || report.Ending != null) return SceneKind.Climax;
            if (E.Combat != null || report.CombatStarted) return SceneKind.Combat;
            if (E.Tension.On) return SceneKind.Tense;
            if (runs.Any(r => r.S.Rest && r.Succeeded && !r.Stopped)) return SceneKind.Rest;
            if (FirstVisitThisTurn.Count > 0) return SceneKind.NewPlace;
            if (runs.Any(r => r.S.Person.Length > 0 && !r.Stopped)) return SceneKind.Talk;
            return SceneKind.Explore;
        }

        /// <summary>Places first reached this turn (set while applying moves).</summary>
        HashSet<string> FirstVisitThisTurn { get; } = new HashSet<string>();

        // ---------------------------------------------------------------- telling

        async Task TellAsync(TurnReport report, List<(Adventurer a, Declaration d)> who, List<string> changes, CancellationToken ct)
        {
            Untold.Clear();
            Untold.AddRange(report.Lines.Concat(changes));
            try { await TellCoreAsync(report, who, changes, ct); }
            finally
            {
                Log.AddRange(report.Lines);
                if (report.Narration.Length > 0) Log.Add("語り: " + report.Narration);
            }
            Untold.Clear();
        }

        /// <summary>After a reload in the middle of a turn: tells what was decided but not told yet.</summary>
        public async Task<TurnReport> TellUntoldAsync(CancellationToken ct = default)
        {
            var report = new TurnReport { Scene = E.Combat != null ? SceneKind.Combat : SceneKind.Explore };
            if (Untold.Count == 0) return report;
            report.Lines.AddRange(Untold);
            await TellAsync(report, new List<(Adventurer, Declaration)>(), new List<string>(), ct);
            return report;
        }

        async Task TellCoreAsync(TurnReport report, List<(Adventurer a, Declaration d)> who, List<string> changes, CancellationToken ct)
        {
            var t = new TurnTelling { Scene = report.Scene, Length = Length };
            foreach (var (a, d) in who)
            {
                if (!t.Names.Contains(a.Name)) t.Names.Add(a.Name);
                if (d.Secret)
                {
                    t.Public[a.Id] = d.Cover.Length > 0 ? d.Cover : "辺りの様子をうかがっている";
                    t.Secret[a.Id] = d.Text + " → " + string.Join(" / ", report.PrivateLines.TryGetValue(a.Id, out var pl) ? pl : new List<string>());
                    foreach (var i in a.Gear.Items.Where(i => i.Concealed)) t.SecretWords.Add(i.Name);
                }
                else t.Public[a.Id] = d.Text;
            }
            t.Results.AddRange(report.Lines);
            t.Changes.AddRange(changes);
            t.Requests.AddRange(Requests);
            Requests.Clear();
            if (E.Hints.Due > 0) HintFor(t);
            var narrator = report.ClimaxStarted || report.Ending != null ? Highlight : Narrator;
            if (report.ClimaxStarted) t.Extra = "山場の始まり。ボスがいれば、その登場を迫力をもって。";
            var told = await narrator.TellAsync(E, t, ct);
            report.Calls += told.Calls;
            report.Narration = told.Text;
            foreach (var kv in told.Private) report.PrivateNarration[kv.Key] = kv.Value;
            foreach (var p in told.Problems) report.Notes.Add("見張り: " + p);
            ApplyAdditions(told);
        }

        /// <summary>The hint: the nearest unfound clue of this act, by where or who (never the clue itself).</summary>
        void HintFor(TurnTelling t)
        {
            var here = W.PlacesOfParty.FirstOrDefault() ?? Story.Start;
            var unfound = Story.Clues.Where(c => !W.Clues.ContainsKey(c.Id) && (c.Acts.Count == 0 || c.Acts.Contains(W.Act))
                && (Story.PlaceOf(c.Place)?.Acts.Contains(W.Act) ?? true)).ToList();
            if (unfound.Count == 0) unfound = Story.Clues.Where(c => !W.Clues.ContainsKey(c.Id)).ToList();
            var c = unfound.OrderBy(x => W.Distance(here, x.Place)).FirstOrDefault();
            if (c == null) return;
            t.HintLevel = E.Hints.Due;
            t.HintWhere = c.Person.Length > 0 ? $"{W.Cast.Find(c.Person)?.Name}（{Story.PlaceOf(c.Place)?.Name}にいる）" : $"{Story.PlaceOf(c.Place)?.Name}";
        }

        /// <summary>The narrator's small additions: facts, extras, small items (never masterworks), attitude reasons.</summary>
        void ApplyAdditions(TellingResult told)
        {
            foreach (var f in told.Facts) E.Facts.Add(f);
            foreach (var (name, portrait, place) in told.Extras)
                W.Cast.AddExtra(name, Story.PlaceOf(place) != null ? place : W.PlacesOfParty.FirstOrDefault() ?? Story.Start, portrait);
            foreach (var (name, kind, to) in told.Items)
                if (E.Find(to) is Adventurer a)
                {
                    var n = 1; while (a.Gear.Has($"found_{n}")) n++;
                    a.Gear.Add(new Item { Id = $"found_{n}", Name = name, Kind = kind });
                }
            foreach (var (person, reason) in told.Attitudes) W.Cast.Find(person)?.Change(reason);
        }
    }
}
