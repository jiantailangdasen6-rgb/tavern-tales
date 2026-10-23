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
    public sealed partial class Table
    {
        // ================================================================ starting a fight

        /// <summary>
        /// Starts a fight with enemies from the table (and story people who fight, "person:{id}"). Players roll initiative;
        /// spear and bow strike first. The first round comes with the next declarations.
        /// </summary>
        async Task StartFightAsync(IEnumerable<string> enemyIds, bool surprise, TurnReport report, List<string> changes, CancellationToken ct)
        {
            var ids = enemyIds.Concat(PendingFoes).ToList();
            PendingFoes.Clear();
            if (ids.Count == 0 || E.Combat != null) return;
            var foes = new List<Creature>();
            foreach (var g in ids.Where(i => !i.StartsWith("person:")).Take(6).GroupBy(i => i))
            {
                if (!Enemies.Has(g.Key)) continue;
                var d = Enemies[g.Key];
                int n = 0, count = g.Count();
                foreach (var _ in g)
                {
                    n++;
                    var hp = Combat.ScaledHp(d.Hp, PartySize, E.Difficulty);
                    foes.Add(Creature.FromEnemy(d, $"{d.Id}_{n}", count > 1 ? $"{d.Name}{(char)('A' + n - 1)}" : d.Name, hp));
                }
            }
            foreach (var pid in ids.Where(i => i.StartsWith("person:")).Select(i => i.Substring(7)).Distinct())
            {
                var npc = W.Cast.Find(pid);
                var def = Story.PersonOf(pid);
                if (npc == null || def == null || npc.State != PersonState.Alive) continue;
                Creature c;
                if (def.Enemy.Length > 0 && Enemies.Has(def.Enemy))
                {
                    var d = Enemies[def.Enemy];
                    c = Creature.FromEnemy(d, npc.Id, npc.Name, Combat.ScaledHp(d.Hp, PartySize, E.Difficulty));
                }
                else c = Creature.FromBuild(npc.Build, npc.Id, npc.Name, Side.Foe);
                npc.Fighting = c;
                foes.Add(c);
            }
            if (foes.Count == 0) return;
            var companions = W.Cast.Companions.Select(p =>
            {
                var c = Creature.FromBuild(p.Build, p.Id, p.Name, Side.Party, p.Role, p.Portrait);
                p.Fighting = c;
                return c;
            }).ToList();

            // initiative: everyone rolls at once
            var up = Active.Where(a => a.Life == Life.Up).ToList();
            var reqs = up.Select(a => new RollRequest { Who = a.Id, Kind = RollKind.Initiative, Heading = $"行動順（20 面 + AGI {a.Modifier(Ability.Agi):+0;-0;+0}）", Bonus = a.Modifier(Ability.Agi), CoinAllowed = false }).ToList();
            var nat = reqs.Count > 0 ? await Players.RollAsync(reqs, ct).ConfigureAwait(false) : new List<int>();
            var init = up.Select((a, i) => (a.Id, n: nat[i])).ToDictionary(x => x.Id, x => x.n);
            var combat = Combat.Start(Active, companions, foes, init, surprise, E.Difficulty, Rng);
            E.Combat = combat;
            Phase = Phase.Combat;
            report.CombatStarted = true;
            var names = string.Join("・", foes.Select(f => f.Name));
            report.Lines.Add($"戦闘開始: {names}{(surprise ? "（不意打ち）" : "")}");
            report.Lines.Add("行動順: " + string.Join(" → ", combat.Order.Select(f => f.Name)));
            changes.Add($"戦闘が始まった: {names}{(surprise ? "。一行の不意打ち" : "")}");

            // spear and bow strike before the enemies
            foreach (var a in combat.FirstStrikers())
            {
                var target = combat.Foes.FirstOrDefault(f => f.InFight);
                if (target == null) break;
                var n = (await Players.RollAsync(new[] { new RollRequest { Who = a.Id, Kind = RollKind.Attack, Heading = $"先手の一撃（守り {target.Defense}）", Bonus = a.Modifier(a.Attack().Ability), Target = target.Defense } }, ct).ConfigureAwait(false))[0];
                var r = combat.Attack(a, target.Id, n);
                report.Lines.AddRange(r.Lines);
            }
        }

        // ================================================================ a round

        /// <summary>One combat round: everyone declares at once; stances take effect; then everyone acts in turn order.</summary>
        public async Task<TurnReport> CombatRoundAsync(IReadOnlyList<Declaration> declarations, CancellationToken ct = default)
        {
            var report = new TurnReport { Scene = SceneKind.Combat };
            var combat = E.Combat!;
            var changes = new List<string>();
            var decls = declarations.Where(d => E.Find(d.Who)?.IsUp == true && d.Text.Trim().Length > 0).Select(d => { d.Secret = false; return d; }).ToList();
            var cls = decls.Count > 0 ? await Classifier.ClassifyAsync(E, decls, true, ct).ConfigureAwait(false) : new Classification();
            report.Calls += cls.Calls;

            // refused actions may be declared again once
            var refused = cls.Declarations.Where(c => c.Steps.FirstOrDefault()?.Sort == Sort.Impossible).ToList();
            if (refused.Count > 0)
            {
                var again = (await Players.RedeclareAsync(refused.Select(c => (c.Who, Refusal(c.Steps[0]))).ToList(), ct).ConfigureAwait(false)).Where(d => d.Text.Trim().Length > 0).ToList();
                foreach (var c in refused) report.Lines.Add($"{E.Find(c.Who)?.Name}: できない（{Refusal(c.Steps[0])}）");
                if (again.Count > 0)
                {
                    var cls2 = await Classifier.ClassifyAsync(E, again, true, ct).ConfigureAwait(false);
                    report.Calls += cls2.Calls;
                    foreach (var c in cls2.Declarations) { cls.Declarations.RemoveAll(x => x.Who == c.Who); cls.Declarations.Add(c); }
                    decls = decls.Where(d => again.All(a => a.Who != d.Who)).Concat(again).ToList();
                }
            }
            var actions = new Dictionary<string, Step>();
            foreach (var c in cls.Declarations)
            {
                var step = c.Steps.FirstOrDefault(s => s.Sort != Sort.Impossible);
                if (step == null) continue;
                var a = E.Find(c.Who)!;
                // a known 【name】 keeps its first effect
                if (step.Technique.Length > 0)
                {
                    var known = a.FindTechnique(step.Technique);
                    if (known != null) { step.Effect = new Effect(known.Kind, known.Size); step.EffectAbility = known.Ability; }
                    else if (step.Effect is Effect fx) a.AddTechnique(step.Technique, step.TechniqueNote, fx.Kind, fx.Size, step.EffectAbility ?? Ability.Str);
                }
                actions[c.Who] = step;
                if (c.Deferred.Count > 0) report.Deferred[c.Who] = c.Deferred.ToList();
            }

            // the dice for the round's actions, all at once (dodges come one by one as the enemies strike)
            var pre = new Dictionary<string, int>();
            var reqs = new List<RollRequest>();
            foreach (var a in Active)
            {
                if (a.Life == Life.Down) { reqs.Add(new RollRequest { Who = a.Id, Kind = RollKind.DeathSave, Heading = "死のサイコロ（10 以上で持ちこたえる）", Target = 10 }); continue; }
                if (a.Statuses.Has(StatusKind.Fear)) reqs.Add(new RollRequest { Who = a.Id, Kind = RollKind.Fear, Heading = "恐怖に耐える（MND・普通 13）", Bonus = a.Modifier(Ability.Mnd), Target = 13 });
                if (actions.TryGetValue(a.Id, out var s) && NeedsRoll(a, s)) reqs.Add(new RollRequest { Who = a.Id, Kind = s.CombatAct == "flee" ? RollKind.Flee : RollKind.Attack, Heading = RollHeading(a, s), For = s.Text, Bonus = a.Modifier(s.EffectAbility ?? s.Check?.Ability ?? a.Attack().Ability) });
            }
            if (reqs.Count > 0)
            {
                var nat = await Players.RollAsync(reqs, ct).ConfigureAwait(false);
                for (int i = 0; i < reqs.Count; i++) pre[reqs[i].Who + ":" + reqs[i].Kind] = nat[i];
                // a fate coin for a death save that failed
                var failedSaves = reqs.Where(q => q.Kind == RollKind.DeathSave && pre[q.Who + ":" + q.Kind] < 10 && E.Find(q.Who)?.FateCoins > 0).ToList();
                if (failedSaves.Count > 0)
                {
                    var use = await Players.OfferCoinsAsync(failedSaves, ct).ConfigureAwait(false);
                    var again = failedSaves.Where((q, i) => i < use.Count && use[i]).ToList();
                    if (again.Count > 0)
                    {
                        var re = await Players.RollAsync(again, ct).ConfigureAwait(false);
                        for (int i = 0; i < again.Count; i++) if (E.Find(again[i].Who)!.SpendFateCoin()) pre[again[i].Who + ":" + RollKind.DeathSave] = re[i];
                    }
                }
            }
            int Pre(Adventurer a, RollKind k) => pre.TryGetValue(a.Id + ":" + k, out var n) ? n : Rng.D20();

            // stances work from the start of the round
            foreach (var (who, s) in actions)
            {
                var a = E.Find(who)!;
                if (a.Life != Life.Up || s.Effect is not Effect ef) continue;
                var target = s.Target.Length > 0 ? s.Target : a.Id;
                int? n = a.HasSpecialty(ef.Kind) ? (int?)null : Pre(a, RollKind.Attack);
                ActionResult? r = ef.Kind switch
                {
                    EffectKind.Cover => combat.Cover(a, target, ef.Size, n, s.EffectAbility ?? Ability.Str),
                    EffectKind.Ward => combat.Ward(a, target, ef.Size, n, s.EffectAbility ?? Ability.Mnd),
                    EffectKind.Boost => combat.Boost(a, target, ef.Size, n, s.EffectAbility ?? Ability.Mnd),
                    EffectKind.Smoke => combat.Smoke(a, target, ef.Size, n, s.EffectAbility ?? Ability.Dex),
                    _ => null,
                };
                if (r == null) continue;
                report.Lines.AddRange(r.Done ? r.Lines : new List<string> { $"{a.Name}: できない（{r.Refused}）" });
                s.CombatAct = "done";
            }

            // turns in order
            Fighter? f;
            while ((f = combat.NextTurn()) != null && combat.State() == CombatEnd.Ongoing)
            {
                var lines = new List<string>();
                if (f.Adventurer is Adventurer a)
                {
                    if (a.Life == Life.Down) { combat.DeathSaveTurn(a, Pre(a, RollKind.DeathSave), lines); report.Lines.AddRange(lines); continue; }
                    var canAct = combat.BeginTurn(f, lines);
                    if (canAct && a.Statuses.Has(StatusKind.Fear)) canAct = combat.ShakeFear(a, Pre(a, RollKind.Fear), lines) || (actions.TryGetValue(a.Id, out var fs) && fs.CombatAct is "flee" or "defend");
                    if (canAct && actions.TryGetValue(a.Id, out var s) && s.CombatAct != "done") lines.AddRange(Act(combat, a, s, Pre(a, RollKind.Attack), Pre(a, RollKind.Flee), report, changes));
                    else if (canAct && !actions.ContainsKey(a.Id)) { a.Ward = Math.Max(a.Ward, 2); lines.Add($"{a.Name} は身を守っている。"); }
                }
                else if (f.Creature is Creature c && combat.BeginTurn(f, lines))
                {
                    if (c.Side == Side.Foe) await EnemyTurn(combat, c, lines, ct).ConfigureAwait(false);
                    else
                    {
                        var order = actions.Values.FirstOrDefault(s => s.Person == c.Id && s.Target.Length > 0);
                        lines.AddRange((order != null ? combat.CompanionAttack(c, order.Target) : combat.CompanionTurn(c)).Lines);
                    }
                }
                combat.EndTurn(f);
                report.Lines.AddRange(lines);
            }
            combat.EndRound();
            foreach (var cl in W.Clocks.CombatRound(W.Act)) changes.Add($"「{cl.Def.Name}」が満ちた");
            FireClocks(report, changes);

            // mechanisms that keep working in a fight (damage every round)
            foreach (var m in E.Mechanisms.Values.Where(m => m.Used > 0 && m.Def.CombatEffect == "damage_each_round" && E.World.PlacesOfParty.Contains(m.Def.Place)))
                foreach (var foe in combat.Foes.Where(x => x.InFight)) { foe.TakeDamage(m.Def.CombatAmount); report.Lines.Add($"{m.Def.Name}が {foe.Name} を打つ（{m.Def.CombatAmount}）"); }

            SyncPeople(combat, changes);
            var end = combat.State();
            if (end != CombatEnd.Ongoing) EndFight(end, report, changes);
            var progress = end != CombatEnd.Ongoing;
            if (Phase != Phase.Over) AfterTurn(report, changes, ref progress, countTurn: false);

            Save?.Invoke(this);
            var declared = decls.Select(d => (E.Find(d.Who)!, d)).ToList();
            if (Phase == Phase.Over) await TellEndingAsync(report, ct).ConfigureAwait(false);
            else await TellAsync(report, declared, changes, ct).ConfigureAwait(false);
            return report;
        }

        static string Refusal(Step s) => (s.Why?.Label() ?? "できない") + (s.How.Length > 0 ? "。" + s.How : "");

        static bool NeedsRoll(Adventurer a, Step s)
        {
            if (s.CombatAct is "flee") return true;
            if (s.Effect is Effect ef) return !ef.Kind.IsSupport() || !a.HasSpecialty(ef.Kind);
            return s.CombatAct is "attack" || (s.CombatAct.Length == 0 && s.Sort == Sort.Check);
        }

        string RollHeading(Adventurer a, Step s)
        {
            if (s.CombatAct == "flee") return "逃げる（AGI・普通 13）";
            if (s.Effect is Effect ef) return $"{ef}（{(s.EffectAbility ?? Ability.Int).Code()}）";
            var t = E.Combat?.Find(s.Target)?.Creature;
            return $"攻撃（{a.Attack().Ability.Code()}{(t != null ? $"、{t.Name}の守り {t.Defense}" : "")}）";
        }

        /// <summary>An adventurer's action on their turn.</summary>
        List<string> Act(Combat combat, Adventurer a, Step s, int natural, int fleeNatural, TurnReport report, List<string> changes)
        {
            var target = s.Target;
            ActionResult r;
            switch (s.CombatAct)
            {
                case "flee": r = combat.Flee(a, fleeNatural); break;
                case "revive": r = combat.Revive(a, target.Length > 0 ? target : Active.FirstOrDefault(x => x.Life == Life.Down)?.Id ?? ""); break;
                case "finish": r = combat.Finish(a.Name, target.Length > 0 ? target : combat.Foes.FirstOrDefault(x => x.Down && !x.Killed)?.Id ?? ""); break;
                case "defend": a.Ward = Math.Max(a.Ward, 2); r = new ActionResult(); r.Lines.Add($"{a.Name} は身を守る構えをとった。"); break;
                case "item":
                    var item = a.Gear.Find(s.Item) ?? a.Gear.Items.FirstOrDefault(i => i.Kind == ItemKind.Consumable);
                    var line = item != null ? Consumables.Use(a, item.Id, E.Find(target) ?? a, out _) : null;
                    r = new ActionResult(); r.Lines.Add(line ?? $"{a.Name} は使える物を持っていない。");
                    break;
                case "talk": r = new ActionResult(); r.Lines.Add($"{a.Name} は敵に呼びかけた。"); break;
                default:
                    if (s.Mechanism.Length > 0 && E.Mechanisms.TryGetValue(s.Mechanism, out var m) && m.Trigger(W))
                    {
                        r = new ActionResult(); r.Lines.Add($"{a.Name} が{m.Def.Name}を動かした。");
                        if (m.Def.CombatEffect == "hold_one" && combat.Foes.FirstOrDefault(x => x.InFight && !x.Boss) is Creature held) { held.Statuses.Add(StatusKind.Stunned); r.Lines.Add($"{held.Name} は動きを止めた。"); }
                        changes.Add($"仕掛け「{m.Def.Name}」が動いた");
                    }
                    else if (s.Effect is Effect ef && ef.Kind == EffectKind.Heal)
                        r = combat.HealEffect(a, target.Length > 0 ? target : a.Id, ef.Size, a.HasSpecialty(EffectKind.Heal) ? (int?)null : natural, s.EffectAbility ?? Ability.Mnd);
                    else if (s.Effect is Effect off && !off.Kind.IsSupport() && off.Kind != EffectKind.Unlock)
                        r = combat.Offense(a, off, s.EffectAbility ?? (a.Class.Spells ? Ability.Int : a.Attack().Ability), target, natural);
                    else if (s.CombatAct == "attack" || s.Sort == Sort.Check)
                        r = combat.Attack(a, target, natural, magicMissile: a.Class.MagicMissile && (s.Text.Contains("魔力") || s.Text.Contains("矢") || s.Check?.Ability == Ability.Int));
                    else { r = new ActionResult(); r.Lines.Add($"{a.Name}: {s.Text}"); }
                    break;
            }
            return r.Done ? r.Lines : new List<string> { $"{a.Name}: できない（{r.Refused}）" };
        }

        /// <summary>An enemy's turn: its plan, calls for help, and the blows the targets dodge (players roll their dodges).</summary>
        async Task EnemyTurn(Combat combat, Creature e, List<string> lines, CancellationToken ct)
        {
            var plan = combat.PlanEnemy(e);
            lines.AddRange(plan.Lines.Where(l => !l.Contains("次の手番に大技")));
            if (e.BigMove != null && e.BigMove.Summon.Length > 0 && plan.Lines.Any(l => l.Contains(e.BigMove.Name + "」！")) && Enemies.Has(e.BigMove.Summon))
            {
                var added = combat.Summon(e, Enemies[e.BigMove.Summon], PartySize);
                lines.Add($"{string.Join("・", added.Select(x => x.Name))} が駆けつけた。");
            }
            foreach (var blow in plan.Blows)
            {
                int natural = 0;
                if (combat.Find(blow.TargetId)?.Adventurer is Adventurer t && !combat.Covers.ContainsKey(t.Id))
                {
                    var bonus = t.Modifier(Ability.Agi) + t.Gear.ShieldDodgeBonus;
                    var req = new RollRequest { Who = t.Id, Kind = RollKind.Dodge, Heading = $"かわす（AGI、{e.Name}の{blow.Attack} {blow.Dc}）", Bonus = bonus, Target = blow.Dc };
                    natural = (await Players.RollAsync(new[] { req }, ct).ConfigureAwait(false))[0];
                    if (natural != 20 && (natural == 1 || natural + bonus < blow.Dc) && t.FateCoins > 0)
                    {
                        var use = await Players.OfferCoinsAsync(new[] { req }, ct).ConfigureAwait(false);
                        if (use.Count > 0 && use[0] && t.SpendFateCoin()) natural = (await Players.RollAsync(new[] { req }, ct).ConfigureAwait(false))[0];
                    }
                }
                lines.AddRange(combat.Resolve(blow, natural).Lines);
            }
            lines.AddRange(plan.Lines.Where(l => l.Contains("次の手番に大技")));
        }

        /// <summary>Story people who fight carry their state back to the cast (a fallen boss is "down").</summary>
        void SyncPeople(Combat combat, List<string> changes)
        {
            foreach (var p in W.Cast.People.Where(p => p.Fighting != null))
            {
                var c = p.Fighting!;
                if (c.Killed && p.State != PersonState.Dead) { p.State = PersonState.Dead; changes.Add($"{p.Name}は死んだ"); }
                else if (c.Down && p.State == PersonState.Alive)
                {
                    p.State = PersonState.Down;
                    if (c.Boss) W.Events.Add("boss_defeated");
                    changes.Add($"{p.Name}は倒れた");
                }
            }
        }

        /// <summary>Ends the fight: drops, companions' fate, the whole fight counts as one turn on the clocks.</summary>
        void EndFight(CombatEnd end, TurnReport report, List<string> changes)
        {
            var combat = E.Combat;
            if (combat == null) return;
            SyncPeople(combat, changes);
            foreach (var foe in combat.Foes.Where(f => f.Down))
                foreach (var it in Story.Items.Where(i => W.WhereIs(i.Item.Id) == "enemy:" + foe.Source))
                {
                    var place = W.PlacesOfParty.First();
                    W.PutItem(it.Item.Id, place);
                    changes.Add($"倒れた{foe.Name}から{it.Item.Name}が落ちた");
                }
            foreach (var p in W.Cast.People.Where(p => p.Fighting != null && p.Fighting.Side == Side.Party))
            {
                if (p.Fighting!.Down)
                {
                    var n = Rng.D20();
                    var alive = FateDie.Answer(Likelihood.Likely, n).IsYes();
                    p.State = alive ? PersonState.Alive : PersonState.Dead;
                    report.Lines.Add($"{p.Name}は{(alive ? "一命をとりとめた" : "息絶えた")}（運命のダイス ありそう: {n}）");
                }
                p.Fighting = null;
            }
            foreach (var p in W.Cast.People.Where(p => p.Fighting != null)) p.Fighting = null;
            foreach (var a in Active) { a.Ward = 0; a.Statuses.Remove(StatusKind.Stunned); }
            E.Combat = null;
            report.CombatEnded = end;
            if (end == CombatEnd.Fled) FledFoes.AddRange(combat.Foes.Where(f => f.InFight).Select(f => f.Name));
            report.Lines.Add(end switch { CombatEnd.Won => "戦闘に勝った", CombatEnd.Fled => "一行は戦いの場から逃れた", _ => "一行は倒れた" });
            changes.Add(end switch { CombatEnd.Won => "戦闘が終わった（敵は退けられた）", CombatEnd.Fled => "一行は逃げ延びた", _ => "一行は全員倒れた" });
            W.Clocks.Turn(W.Act, PartySize);
            if (Phase == Phase.Combat) Phase = Phase.Explore;
            if (end == CombatEnd.Wiped) Finish(EndingTrigger.Wiped, report);
        }
    }
}
