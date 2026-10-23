#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>One place in the turn order: an adventurer or a creature (enemy or companion).</summary>
    public sealed class Fighter
    {
        public Adventurer? Adventurer { get; }
        public Creature? Creature { get; }
        public int Initiative { get; set; }
        public Fighter(Adventurer a) { Adventurer = a; }
        public Fighter(Creature c) { Creature = c; }

        public string Id => Adventurer?.Id ?? Creature!.Id;
        public string Name => Adventurer?.Name ?? Creature!.Name;
        public Side Side => Adventurer != null ? Side.Party : Creature!.Side;
        public bool InFight => Adventurer != null ? Adventurer.Life != Life.Dead && !Adventurer.Withdrawn && !Fled : Creature!.InFight;
        public bool Fled { get; set; }
        public StatusSet Statuses => Adventurer?.Statuses ?? Creature!.Statuses;
        public int Hp => Adventurer?.Hp ?? Creature!.Hp;
        public int MaxHp => Adventurer?.MaxHp ?? Creature!.MaxHp;
        /// <summary>Standing and able to be targeted.</summary>
        public bool Standing => Adventurer != null ? Adventurer.IsUp && !Fled : Creature!.InFight;
    }

    public enum CombatEnd { Ongoing, Won, Wiped, Fled }

    /// <summary>An enemy's planned blow; the target dodges it (adventurers roll; creatures are rolled for).</summary>
    public sealed class Blow
    {
        public Creature Attacker { get; set; } = null!;
        public string TargetId { get; set; } = "";
        public string Attack { get; set; } = "";
        public int Dc { get; set; }
        public int Damage { get; set; }
        public bool Poison { get; set; }
        public bool BigMove { get; set; }
    }

    /// <summary>What an enemy does on its turn: blows to dodge, enemies called in, or nothing.</summary>
    public sealed class EnemyPlan
    {
        public Creature Enemy { get; set; } = null!;
        public List<Blow> Blows { get; } = new List<Blow>();
        public List<Creature> Summoned { get; } = new List<Creature>();
        public bool Skipped { get; set; }
        public List<string> Lines { get; } = new List<string>();
    }

    /// <summary>The result of one action, with the lines for the result summary.</summary>
    public sealed class ActionResult
    {
        public bool Done { get; set; } = true;
        /// <summary>Why it could not be done (no focus, no target...). Nothing was spent.</summary>
        public string Refused { get; set; } = "";
        public Outcome? Outcome { get; set; }
        public List<string> Lines { get; } = new List<string>();
        public static ActionResult Refuse(string why) => new ActionResult { Done = false, Refused = why };
    }

    /// <summary>
    /// A fight (09_Spec 5). The game runs it; the AI only narrates. Players' dice come in as naturals (they roll them);
    /// everything the game rolls by itself (enemy targets, creatures' dodges and attacks) uses the random source.
    /// Round: every adventurer declares at once → stances (cover, ward, boost) take effect → turns in initiative order.
    /// </summary>
    public sealed class Combat
    {
        readonly IRandom _rng;
        public Difficulty Difficulty { get; }
        public List<Fighter> Order { get; } = new List<Fighter>();
        public int Round { get; private set; } = 1;
        int _index = -1;
        public bool SurpriseRound { get; private set; }
        /// <summary>Covered adventurer id → (coverer id, reduction; -1 = all of it). Lasts the round.</summary>
        public Dictionary<string, (string by, int reduction)> Covers { get; } = new Dictionary<string, (string, int)>();
        /// <summary>Enemy id (or "*" for all) → (taunter id, rounds left).</summary>
        public Dictionary<string, (string by, int rounds)> Taunts { get; } = new Dictionary<string, (string, int)>();
        /// <summary>Adventurers hidden by small smoke this round.</summary>
        public HashSet<string> Hidden { get; } = new HashSet<string>();
        /// <summary>Steps easier for fleeing this round (medium / large smoke).</summary>
        public int SmokeSteps { get; private set; }
        /// <summary>Enemy id whose big move comes on its next turn.</summary>
        public HashSet<string> BigMoveDue { get; } = new HashSet<string>();
        int _summoned;

        Combat(Difficulty difficulty, IRandom rng) { Difficulty = difficulty; _rng = rng; }

        public IEnumerable<Adventurer> Party => Order.Where(f => f.Adventurer != null).Select(f => f.Adventurer!);
        public IEnumerable<Creature> Foes => Order.Where(f => f.Creature?.Side == Side.Foe).Select(f => f.Creature!);
        public IEnumerable<Creature> Companions => Order.Where(f => f.Creature?.Side == Side.Party).Select(f => f.Creature!);
        public Fighter? Find(string id) => Order.FirstOrDefault(f => f.Id == id);
        public Fighter? Current => _index >= 0 && _index < Order.Count ? Order[_index] : null;

        /// <summary>Enemy HP for the party size (written for three; fewer face less, never under 0.45) and the difficulty.</summary>
        public static int ScaledHp(int hp, int partySize, Difficulty d) =>
            d.EnemyHp(Math.Max(2, (int)Math.Ceiling(hp * Math.Max(0.45, partySize / 3.0))));

        /// <summary>
        /// Starts a fight. initiative: each adventurer's natural for d20 + AGI. Enemies and companions use their speed.
        /// Ties go to the party. With surprise, the party acts first in the first round.
        /// </summary>
        public static Combat Start(IEnumerable<Adventurer> party, IEnumerable<Creature> companions, IEnumerable<Creature> foes,
            IReadOnlyDictionary<string, int> initiative, bool surprise, Difficulty difficulty, IRandom rng)
        {
            var c = new Combat(difficulty, rng) { SurpriseRound = surprise };
            foreach (var a in party.Where(a => a.Life != Life.Dead && !a.Withdrawn))
                c.Order.Add(new Fighter(a) { Initiative = (initiative.TryGetValue(a.Id, out var n) ? n : 10) + a.Modifier(Ability.Agi) });
            foreach (var m in companions) { m.Side = Side.Party; c.Order.Add(new Fighter(m) { Initiative = m.Speed }); }
            foreach (var f in foes) { f.Side = Side.Foe; c.Order.Add(new Fighter(f) { Initiative = f.Speed }); }
            c.Sort();
            return c;
        }

        void Sort()
        {
            var sorted = Order
                .OrderBy(f => SurpriseRound && f.Side == Side.Foe ? 1 : 0)
                .ThenByDescending(f => f.Initiative)
                .ThenBy(f => f.Side == Side.Party ? 0 : 1)
                .ToList();
            Order.Clear();
            Order.AddRange(sorted);
        }

        /// <summary>Spear and bow holders strike once before anyone else at the start of a fight.</summary>
        public List<Adventurer> FirstStrikers() => Party.Where(a => a.IsUp && a.Attack().FirstStrike).ToList();

        /// <summary>The next fighter whose turn it is (null at the end of the round: call EndRound).</summary>
        public Fighter? NextTurn()
        {
            while (++_index < Order.Count)
                if (Order[_index].InFight) return Order[_index];
            return null;
        }

        /// <summary>Clears the round's stances and counts down taunts; the first-round surprise order ends.</summary>
        public void EndRound()
        {
            Covers.Clear();
            Hidden.Clear();
            SmokeSteps = 0;
            foreach (var k in Taunts.Keys.ToList())
            {
                var (by, rounds) = Taunts[k];
                if (rounds <= 1) Taunts.Remove(k); else Taunts[k] = (by, rounds - 1);
            }
            if (SurpriseRound) { SurpriseRound = false; Sort(); }
            Round++;
            _index = -1;
        }

        public CombatEnd State()
        {
            if (!Foes.Any(f => f.InFight)) return CombatEnd.Won;
            var party = Order.Where(f => f.Adventurer != null).ToList();
            if (party.Any(f => f.Standing)) return CombatEnd.Ongoing;
            // nobody left standing here: away if someone got out on their feet, otherwise everyone fell
            return party.Any(f => f.Fled && f.Adventurer!.IsUp) ? CombatEnd.Fled : CombatEnd.Wiped;
        }

        // ---------------------------------------------------------------- start of a turn

        /// <summary>
        /// Start-of-turn upkeep for anyone: poison and fire hurt, statuses that take the turn are reported.
        /// Returns false when the fighter cannot act this turn (call EndTurn anyway).
        /// </summary>
        public bool BeginTurn(Fighter f, List<string> lines)
        {
            if (f.Creature is Creature c)
            {
                if (c.Burning > 0) { c.TakeDamage(c.Burning); lines.Add($"{c.Name} は炎に焼かれて {c.Burning} ダメージ（{Hp(c)}）。"); c.Burning = 0; }
                if (c.Statuses.PoisonDamage > 0 && c.InFight) { c.TakeDamage(1); lines.Add($"{c.Name} に毒が回る（{Hp(c)}）。"); }
                if (!c.InFight) return false;
                if (!c.Statuses.CanAct) { lines.Add($"{c.Name} は動けない。"); return false; }
                return true;
            }
            var a = f.Adventurer!;
            if (a.Life != Life.Up) return false;   // a downed adventurer rolls a death save instead (DeathSaveTurn)
            if (a.Statuses.PoisonDamage > 0) { a.TakeDamage(1); lines.Add($"{a.Name} に毒が回る（HP {a.Hp}/{a.MaxHp}{(a.Life == Life.Down ? "、倒れた" : "")}）。"); }
            if (a.Life != Life.Up) return false;
            if (!a.Statuses.CanAct) { lines.Add($"{a.Name} は動けない（{string.Join("・", a.Statuses.List.Where(s => StatusInfo.Disables(s.Kind)).Select(s => s.Kind.Label()))}）。"); return false; }
            return true;
        }

        /// <summary>Fear: at the start of the turn a MND normal check shakes it off.</summary>
        public bool ShakeFear(Adventurer a, int natural, List<string> lines)
        {
            if (!a.Statuses.Has(StatusKind.Fear)) return true;
            if (natural == 20 || (natural != 1 && natural + a.Modifier(Ability.Mnd) >= (int)Tier.Normal))
            {
                a.Statuses.Remove(StatusKind.Fear);
                lines.Add($"{a.Name} は恐怖を振り払った。");
                return true;
            }
            lines.Add($"{a.Name} はまだ恐怖に足がすくんでいる。");
            return false;
        }

        public void EndTurn(Fighter f) => f.Statuses.EndTurn();

        /// <summary>A downed adventurer's turn: the death save.</summary>
        public DeathSave DeathSaveTurn(Adventurer a, int natural, List<string> lines)
        {
            var r = a.ApplyDeathSave(natural);
            lines.Add(r switch
            {
                DeathSave.StandsUp => $"{a.Name} は自力で起き上がった（HP 1）。",
                DeathSave.Holds => $"{a.Name} はかろうじて持ちこたえている。",
                DeathSave.Died => $"{a.Name} は息を引き取った。",
                _ => $"{a.Name} の命の灯が揺らぐ（{a.DeathFailures}/{Adventurer.DeathSaveFailures}）。",
            });
            return r;
        }

        // ---------------------------------------------------------------- stances (from the start of the round)

        /// <summary>かばう: the coverer takes blows meant for the ally this round, minus the reduction (large or a tower shield: all).</summary>
        public ActionResult Cover(Adventurer coverer, string allyId, EffectSize size, int? natural = null, Ability ability = Ability.Str)
        {
            var spec = coverer.SpecialtyFor(EffectKind.Cover);
            var pay = Pay(coverer, size, spec != null, natural, ability, out var refused);
            if (pay == null) return ActionResult.Refuse(refused);
            if (!pay.Value) return Missed(coverer, "かばう");
            var ally = Find(allyId);
            if (ally?.Adventurer == null || allyId == coverer.Id) return ActionResult.Refuse("かばう相手がいない");
            var all = size == EffectSize.Large || coverer.Gear.Shield?.Trait == Trait.Tower;
            Covers[allyId] = (coverer.Id, all ? -1 : Effects.CoverReduction(size));
            var r = new ActionResult();
            r.Lines.Add($"{coverer.Name} は {ally.Name} の前に立った。");
            return r;
        }

        /// <summary>守り on oneself or an ally: the next damage is reduced.</summary>
        public ActionResult Ward(Adventurer caster, string targetId, EffectSize size, int? natural = null, Ability ability = Ability.Mnd)
        {
            var pay = Pay(caster, size, caster.HasSpecialty(EffectKind.Ward), natural, ability, out var refused);
            if (pay == null) return ActionResult.Refuse(refused);
            if (!pay.Value) return Missed(caster, "守り");
            var t = Find(targetId)?.Adventurer ?? caster;
            t.Ward = Math.Max(t.Ward, Effects.WardReduction(size));
            var r = new ActionResult();
            r.Lines.Add($"{caster.Name} の守りが {t.Name} を包む（次の傷 -{Effects.WardReduction(size)}）。");
            return r;
        }

        /// <summary>加護: small = one ally's next check +2; medium = everyone +2; large = everyone +3.</summary>
        public ActionResult Boost(Adventurer caster, string targetId, EffectSize size, int? natural = null, Ability ability = Ability.Mnd)
        {
            var pay = Pay(caster, size, caster.HasSpecialty(EffectKind.Boost), natural, ability, out var refused);
            if (pay == null) return ActionResult.Refuse(refused);
            if (!pay.Value) return Missed(caster, "加護");
            var bonus = Effects.BoostBonus(size);
            var targets = Effects.BoostAll(size) ? Party.Where(a => a.IsUp).ToList() : new List<Adventurer> { Find(targetId)?.Adventurer ?? caster };
            foreach (var t in targets) t.PendingBoost = Math.Max(t.PendingBoost, bonus);
            var r = new ActionResult();
            r.Lines.Add($"{caster.Name} の加護: {string.Join("・", targets.Select(t => t.Name))} の次の判定 +{bonus}。");
            return r;
        }

        /// <summary>煙: small hides one ally from the enemies this round; medium / large make fleeing 1 / 2 steps easier.</summary>
        public ActionResult Smoke(Adventurer caster, string targetId, EffectSize size, int? natural = null, Ability ability = Ability.Dex)
        {
            var pay = Pay(caster, size, caster.HasSpecialty(EffectKind.Smoke), natural, ability, out var refused);
            if (pay == null) return ActionResult.Refuse(refused);
            if (!pay.Value) return Missed(caster, "煙");
            var r = new ActionResult();
            if (size == EffectSize.Small)
            {
                var t = Find(targetId)?.Adventurer ?? caster;
                Hidden.Add(t.Id);
                r.Lines.Add($"煙と物陰が {t.Name} の姿を隠した。");
            }
            else
            {
                SmokeSteps = Math.Max(SmokeSteps, Effects.SmokeSteps(size));
                r.Lines.Add($"{caster.Name} の煙が辺りを包んだ（逃げる・隠れるが {SmokeSteps} 段易しい）。");
            }
            return r;
        }

        // ---------------------------------------------------------------- adventurers' actions

        /// <summary>A plain attack with what is in hand (or the mage's magic missile). natural: the to-hit die.</summary>
        public ActionResult Attack(Adventurer a, string targetId, int natural, bool magicMissile = false)
        {
            var t = TargetFoe(targetId);
            if (t == null) return ActionResult.Refuse("狙える敵がいない");
            var p = magicMissile && a.Class.MagicMissile ? a.MagicMissile() : a.Attack();
            int bonus = p.HitBonusAgainst(t.Defense) + a.TakeBoost() + a.Gear.TrinketBonus(p.Ability);
            if (magicMissile && a.Gear.MainItem?.Weapon == WeaponType.Staff) bonus++;
            int target = t.Defense + (a.Statuses.Has(StatusKind.Dazzled) ? 3 : 0);
            var r = new ActionResult();
            var name = magicMissile ? "魔力の矢" : a.Gear.MainItem?.Name ?? "素手";
            bool crit = natural == 20 || (natural == 19 && p.Has(Trait.Keen)) || t.Statuses.Helpless;
            bool hit = crit || (natural != 1 && natural + a.Modifier(p.Ability) + bonus >= target);
            r.Outcome = natural == 1 ? Outcome.Fumble : crit ? Outcome.Critical : hit ? Outcome.Success : Outcome.Failure;
            if (!hit) { r.Lines.Add($"{a.Name} の{name}は {t.Name} に{(natural == 1 ? "大きく空を切った" : "かわされた")}。"); return r; }
            int dmg = p.Damage;
            if (crit) dmg *= 2;
            if (p.Has(Trait.Bane) && t.Undead) dmg *= 2;
            if (SurpriseRound && p.Type == WeaponType.Dagger) dmg *= 2;
            t.TakeDamage(dmg);
            r.Lines.Add($"{a.Name} の{name}が {t.Name} に {dmg} ダメージ{(crit ? "（大成功）" : "")}（{Hp(t)}）。");
            if (p.Has(Trait.Cleave) && Foes.Where(f => f.InFight && f != t).ToList() is var others && others.Count > 0)
            {
                var second = others[_rng.Next(0, others.Count)];
                second.TakeDamage(dmg);
                r.Lines.Add($"返す刃が {second.Name} にも {dmg} ダメージ（{Hp(second)}）。");
            }
            if (p.Has(Trait.Flame) && t.InFight) t.Burning = 1;
            if (p.Has(Trait.Drain)) a.Heal(1);
            return r;
        }

        /// <summary>
        /// A technique, spell or free action aimed at enemies (strike, sweep, hold, taunt). The AI picked kind and size;
        /// natural is the roll (needed for all of these). Without the specialty it is a step harder.
        /// </summary>
        public ActionResult Offense(Adventurer a, Effect e, Ability ability, string targetId, int natural)
        {
            var spec = a.SpecialtyFor(e.Kind);
            if (!Effects.Exists(e.Kind, e.Size)) return ActionResult.Refuse("その大きさはない");
            if (!a.SpendFocus(e.FocusCost)) return ActionResult.Refuse("気力が足りない");
            int harder = spec == null ? 3 : 0;
            int roll = natural + a.Modifier(ability) + a.TakeBoost() + a.Gear.TrinketBonus(ability);
            var r = new ActionResult();
            bool Hits(int target) => natural == 20 || (natural != 1 && roll >= target + harder);
            bool crit = natural == 20;
            switch (e.Kind)
            {
                case EffectKind.Strike:
                {
                    var t = TargetFoe(targetId);
                    if (t == null) return Refund(a, e, "狙える敵がいない");
                    var size = spec?.UndeadBane == true && t.Undead ? e.Size.Up() : e.Size;
                    bool hit = Hits(t.Defense) || t.Statuses.Helpless;
                    r.Outcome = hit ? (crit || t.Statuses.Helpless ? Outcome.Critical : Outcome.Success) : natural == 1 ? Outcome.Fumble : Outcome.Failure;
                    if (!hit) { r.Lines.Add($"{a.Name} の一撃は {t.Name} に届かなかった。"); return r; }
                    var dmg = Effects.StrikeDamage(size) * (r.Outcome == Outcome.Critical ? 2 : 1);
                    t.TakeDamage(dmg);
                    r.Lines.Add($"{a.Name} の一撃が {t.Name} に {dmg} ダメージ（{Hp(t)}）。");
                    return r;
                }
                case EffectKind.Sweep:
                {
                    var hits = new List<string>();
                    foreach (var t in Foes.Where(f => f.InFight).ToList())
                    {
                        if (!Hits(t.Defense) && !t.Statuses.Helpless) continue;
                        var size = spec?.UndeadBane == true && t.Undead ? e.Size.Up() : e.Size;
                        var dmg = Effects.SweepDamage(size) * (crit ? 2 : 1);
                        t.TakeDamage(dmg);
                        hits.Add($"{t.Name} に {dmg}（{Hp(t)}）");
                    }
                    r.Outcome = hits.Count > 0 ? (crit ? Outcome.Critical : Outcome.Success) : natural == 1 ? Outcome.Fumble : Outcome.Failure;
                    r.Lines.Add(hits.Count > 0 ? $"{a.Name} の技が敵をまとめて打つ: {string.Join("、", hits)}。" : $"{a.Name} の技は敵に届かなかった。");
                    return r;
                }
                case EffectKind.Hold:
                {
                    var t = TargetFoe(targetId);
                    if (t == null) return Refund(a, e, "狙える敵がいない");
                    bool fits = e.Size switch
                    {
                        EffectSize.Small => t.Weak,
                        EffectSize.Medium => !t.Boss,
                        _ => !t.Boss || !t.HeldOnce,
                    };
                    if (!fits) return Refund(a, e, t.Boss ? "この相手は止められない（大きな術でも 1 戦闘 1 回）" : "この相手には小さすぎる");
                    bool ok = Hits((int)Tier.Normal);
                    r.Outcome = ok ? (crit ? Outcome.Critical : Outcome.Success) : natural == 1 ? Outcome.Fumble : Outcome.Failure;
                    if (!ok) { r.Lines.Add($"{t.Name} は {a.Name} の術を振り払った。"); return r; }
                    if (t.Boss) t.HeldOnce = true;
                    t.Statuses.Add(StatusKind.Stunned);
                    r.Lines.Add($"{t.Name} は動きを止めた（1 手番）。");
                    return r;
                }
                case EffectKind.Taunt:
                {
                    bool ok = Hits((int)Tier.Normal);
                    r.Outcome = ok ? (crit ? Outcome.Critical : Outcome.Success) : natural == 1 ? Outcome.Fumble : Outcome.Failure;
                    if (!ok) { r.Lines.Add($"{a.Name} の挑発に、敵は乗らなかった。"); return r; }
                    if (e.Size == EffectSize.Small)
                    {
                        var t = TargetFoe(targetId);
                        if (t == null) return Refund(a, e, "狙える敵がいない");
                        Taunts[t.Id] = (a.Id, 1);
                        r.Lines.Add($"{t.Name} の目が {a.Name} に向いた。");
                    }
                    else
                    {
                        Taunts["*"] = (a.Id, Effects.TauntRounds(e.Size));
                        r.Lines.Add($"敵の刃がそろって {a.Name} に向いた（{Effects.TauntRounds(e.Size)} 手番）。");
                    }
                    return r;
                }
                default:
                    return Refund(a, e, "敵に向ける効果ではない");
            }
        }

        /// <summary>回復: heals one (raises a downed adventurer). The warrior's second wind is only for themself.</summary>
        public ActionResult HealEffect(Adventurer caster, string targetId, EffectSize size, int? natural = null, Ability ability = Ability.Mnd)
        {
            var spec = caster.SpecialtyFor(EffectKind.Heal);
            if (spec?.SelfOnly == true && targetId != caster.Id) spec = null;   // on others it is not the specialty
            var pay = Pay(caster, size, spec != null, natural, ability, out var refused);
            if (pay == null) return ActionResult.Refuse(refused);
            if (!pay.Value) return Missed(caster, "回復");
            var r = new ActionResult();
            var t = Find(targetId);
            if (t?.Adventurer is Adventurer ta) { ta.Heal(Effects.HealAmount(size)); ta.Statuses.Remove(StatusKind.Poison); r.Lines.Add($"{caster.Name} の祈りで {ta.Name} の傷がふさがる（HP {ta.Hp}/{ta.MaxHp}）。"); }
            else if (t?.Creature is Creature tc) { tc.Heal(Effects.HealAmount(size)); r.Lines.Add($"{tc.Name} の傷がふさがる（{Hp(tc)}）。"); }
            else { caster.Heal(Effects.HealAmount(size)); r.Lines.Add($"{caster.Name} の傷がふさがる（HP {caster.Hp}/{caster.MaxHp}）。"); }
            return r;
        }

        /// <summary>Gets a downed ally back up (1 HP). Uses the turn, no roll.</summary>
        public ActionResult Revive(Adventurer by, string allyId)
        {
            var t = Find(allyId)?.Adventurer;
            if (t == null || t.Life != Life.Down) return ActionResult.Refuse("倒れた仲間がいない");
            t.Revive();
            var r = new ActionResult();
            r.Lines.Add($"{by.Name} が {t.Name} を助け起こした（HP 1）。");
            return r;
        }

        /// <summary>とどめ: a fallen enemy (or adventurer) dies at once, no roll.</summary>
        public ActionResult Finish(string byName, string targetId)
        {
            var t = Find(targetId);
            var r = new ActionResult();
            if (t?.Creature is Creature c && c.Down && !c.Killed) { c.Killed = true; r.Lines.Add($"{byName} が {c.Name} にとどめを刺した。"); return r; }
            if (t?.Adventurer is Adventurer a && a.Life == Life.Down) { a.Finish(); r.Lines.Add($"{byName} が {a.Name} にとどめを刺した。{a.Name} は死んだ。"); return r; }
            return ActionResult.Refuse("とどめを刺せる相手がいない");
        }

        /// <summary>Flee: AGI normal (smoke makes it easier). Success takes the adventurer out of the fight.</summary>
        public ActionResult Flee(Adventurer a, int natural)
        {
            var tier = Tier.Normal.Shift(-SmokeSteps + (a.Gear.BodyItem?.Trait == Trait.Heavy ? 1 : 0));
            var ok = natural == 20 || (natural != 1 && natural + a.Modifier(Ability.Agi) + a.TakeBoost() >= (int)tier);
            var r = new ActionResult { Outcome = ok ? Outcome.Success : Outcome.Failure };
            if (ok) { Find(a.Id)!.Fled = true; r.Lines.Add($"{a.Name} は戦いの場から逃れた。"); }
            else r.Lines.Add($"{a.Name} は逃げ道をふさがれた。");
            return r;
        }

        // ---------------------------------------------------------------- enemies

        /// <summary>
        /// What an enemy does: its due big move, or one of its attacks on a target by its pattern (taunts first, hidden adventurers never).
        /// After acting, a boss whose big move comes next turn shows its warning.
        /// </summary>
        public EnemyPlan PlanEnemy(Creature e)
        {
            var plan = new EnemyPlan { Enemy = e };
            var big = e.BigMove;
            if (big != null && BigMoveDue.Remove(e.Id))
            {
                plan.Lines.Add($"{e.Name} の「{big.Name}」！");
                if (big.Summon.Length > 0) return plan;   // the turn flow adds the summoned (SummonFor) since it knows the enemy table
                var targets = Targets().ToList();
                if (big.Single && targets.Count > 0) targets = new List<Fighter> { Pick(targets, Targeting.Weakest) };
                foreach (var t in targets)
                    plan.Blows.Add(new Blow { Attacker = e, TargetId = t.Id, Attack = big.Name, Dc = big.Dc + Difficulty.EnemyAttack(), Damage = big.Damage, BigMove = true });
            }
            else if (e.Attacks.Count > 0)
            {
                var atk = e.Attacks.Count == 1 ? e.Attacks[0] : e.Attacks[_rng.Next(0, e.Attacks.Count)];
                var target = Taunted(e) ?? (Targets().ToList() is var ts && ts.Count > 0 ? Pick(ts, e.Pattern) : null);
                if (target != null)
                    plan.Blows.Add(new Blow { Attacker = e, TargetId = target.Id, Attack = atk.Name, Dc = atk.Dc + Difficulty.EnemyAttack(), Damage = atk.Damage, Poison = atk.Poison });
            }
            if (big != null && big.Every > 0 && (Round + 1) % big.Every == 0)
            {
                BigMoveDue.Add(e.Id);
                plan.Lines.Add($"{big.Warning}（次の手番に大技「{big.Name}」）");
            }
            return plan;
        }

        /// <summary>Adds the enemies a summoning big move calls in (they act from the next round, at their speed).</summary>
        public List<Creature> Summon(Creature by, EnemyDef def, int partySize)
        {
            var list = new List<Creature>();
            var count = Math.Max(1, by.BigMove?.Count ?? 1);
            for (int i = 0; i < count; i++)
            {
                _summoned++;
                var hp = ScaledHp(def.Hp, partySize, Difficulty);
                var c = Creature.FromEnemy(def, $"{def.Id}_s{_summoned}", $"{def.Name}（加勢{_summoned}）", hp);
                Order.Add(new Fighter(c) { Initiative = c.Speed });
                list.Add(c);
            }
            return list;
        }

        Fighter? Taunted(Creature e)
        {
            if (!Taunts.TryGetValue(e.Id, out var t) && !Taunts.TryGetValue("*", out t)) return null;
            var f = Find(t.by);
            return f != null && f.Standing ? f : null;
        }

        IEnumerable<Fighter> Targets() =>
            Order.Where(f => f.Side == Side.Party && f.Standing && !Hidden.Contains(f.Id));

        Fighter Pick(List<Fighter> list, Targeting how) => how switch
        {
            Targeting.Weakest => list.OrderBy(f => f.Hp).ThenBy(_ => _rng.Next(0, 1000)).First(),
            Targeting.Strongest => list.OrderByDescending(f => f.Hp).ThenBy(_ => _rng.Next(0, 1000)).First(),
            _ => list[_rng.Next(0, list.Count)],
        };

        /// <summary>
        /// Resolves a blow. An adventurer dodges with AGI (shield, warding against big moves); natural is their die.
        /// A companion's dodge is rolled here (natural ignored). A covered adventurer's blow goes to the coverer, who does not dodge.
        /// </summary>
        public ActionResult Resolve(Blow b, int natural)
        {
            var r = new ActionResult();
            var target = Find(b.TargetId);
            if (target == null || !target.Standing) { r.Lines.Add($"{b.Attacker.Name} の{b.Attack}は空を切った。"); return r; }
            if (target.Adventurer is Adventurer a && Covers.TryGetValue(a.Id, out var cover) && Find(cover.by)?.Adventurer is Adventurer coverer && coverer.IsUp)
            {
                var taken = cover.reduction < 0 ? 0 : Math.Max(0, b.Damage - cover.reduction - coverer.Gear.ArmorReduction - TakeWard(coverer));
                coverer.TakeDamage(taken);
                r.Lines.Add($"{b.Attacker.Name} の{b.Attack}を {coverer.Name} が {a.Name} の代わりに受けた（{taken} ダメージ、HP {coverer.Hp}/{coverer.MaxHp}{Fell(coverer)}）。");
                return r;
            }
            if (target.Adventurer is Adventurer t)
            {
                int bonus = t.Gear.ShieldDodgeBonus + t.TakeBoost() + (b.BigMove && t.Gear.Shield?.Trait == Trait.Warding ? 2 : 0) + t.Gear.TrinketBonus(Ability.Agi);
                int dc = b.Dc + (t.Gear.BodyItem?.Trait == Trait.Heavy ? 3 : 0);
                bool dodged = natural == 20 || (natural != 1 && natural + t.Modifier(Ability.Agi) + bonus >= dc);
                r.Outcome = dodged ? Outcome.Success : Outcome.Failure;
                if (dodged)
                {
                    r.Lines.Add($"{t.Name} は {b.Attacker.Name} の{b.Attack}をかわした。");
                    if (t.Gear.Shield?.Trait == Trait.Riposte) { b.Attacker.TakeDamage(1); r.Lines.Add($"盾で弾き返し、{b.Attacker.Name} に 1 ダメージ（{Hp(b.Attacker)}）。"); }
                    return r;
                }
                var dmg = Math.Max(1, b.Damage - t.Gear.ArmorReduction - TakeWard(t));
                t.TakeDamage(dmg);
                if (b.Poison) t.Statuses.Add(StatusKind.Poison, b.Attacker.Id, t.Gear.Wears(Trait.Blessed));
                r.Lines.Add($"{b.Attacker.Name} の{b.Attack}が {t.Name} に {dmg} ダメージ（HP {t.Hp}/{t.MaxHp}{Fell(t)}）{(b.Poison && t.Statuses.Has(StatusKind.Poison) ? "。毒を受けた" : "")}。");
                return r;
            }
            var c = target.Creature!;
            var n = _rng.Next(1, 21);
            bool cDodged = n == 20 || (n != 1 && n + c.DodgeModifier >= b.Dc);
            if (cDodged) { r.Lines.Add($"{c.Name} は {b.Attacker.Name} の{b.Attack}をかわした。"); return r; }
            c.TakeDamage(b.Damage);
            r.Lines.Add($"{b.Attacker.Name} の{b.Attack}が {c.Name} に {b.Damage} ダメージ（{Hp(c)}）。");
            return r;
        }

        static int TakeWard(Adventurer a) { var w = a.Ward; a.Ward = 0; return w; }

        // ---------------------------------------------------------------- companions

        /// <summary>
        /// A companion NPC's turn by its role (attack the weakest enemy / cover the most hurt adventurer / heal one under half, else attack).
        /// The game rolls for it.
        /// </summary>
        public ActionResult CompanionTurn(Creature m)
        {
            var r = new ActionResult();
            if (m.Role == CompanionRole.Heal)
            {
                var hurt = Party.Where(a => a.Life != Life.Dead && a.Hp * 2 < a.MaxHp).OrderBy(a => a.Hp).FirstOrDefault();
                if (hurt != null)
                {
                    hurt.Heal(Effects.HealAmount(EffectSize.Small));
                    r.Lines.Add($"{m.Name} が {hurt.Name} の手当てをした（HP {hurt.Hp}/{hurt.MaxHp}）。");
                    return r;
                }
            }
            if (m.Role == CompanionRole.Defend)
            {
                var weak = Party.Where(a => a.IsUp && !Covers.ContainsKey(a.Id)).OrderBy(a => a.Hp).FirstOrDefault();
                if (weak != null)
                {
                    // A companion's cover: it takes the blow itself with 1 less damage.
                    Covers[weak.Id] = (m.Id, 1);
                    r.Lines.Add($"{m.Name} は {weak.Name} の前に立った。");
                    return r;
                }
            }
            return CompanionAttack(m, null);
        }

        /// <summary>A companion attacks (an adventurer may name the target; then the adventurer's roll is used).</summary>
        public ActionResult CompanionAttack(Creature m, string? targetId, int? natural = null)
        {
            var r = new ActionResult();
            var foes = Foes.Where(f => f.InFight).ToList();
            var t = targetId != null ? TargetFoe(targetId) : foes.OrderBy(f => f.Hp).FirstOrDefault();
            if (t == null) { r.Lines.Add($"{m.Name} は身構えている。"); return r; }
            var atk = m.Attacks.FirstOrDefault() ?? new EnemyAttack { Damage = 1, Name = "攻撃" };
            var n = natural ?? _rng.Next(1, 21);
            bool hit = n == 20 || t.Statuses.Helpless || (n != 1 && n + atk.Damage >= t.Defense);
            if (!hit) { r.Lines.Add($"{m.Name} の攻撃は {t.Name} にかわされた。"); return r; }
            var dmg = atk.Damage * (n == 20 || t.Statuses.Helpless ? 2 : 1);
            t.TakeDamage(dmg);
            r.Lines.Add($"{m.Name} の攻撃が {t.Name} に {dmg} ダメージ（{Hp(t)}）。");
            return r;
        }

        // ---------------------------------------------------------------- helpers

        Creature? TargetFoe(string id)
        {
            var f = Find(id)?.Creature;
            if (f != null && f.Side == Side.Foe && f.InFight) return f;
            return Foes.FirstOrDefault(x => x.InFight);   // the named target fell before this turn: the next one
        }

        /// <summary>
        /// Focus and the roll for a support effect: null = refused (no focus); false = rolled and missed; true = it works.
        /// With the specialty there is no roll; without it, a hard (16) roll.
        /// </summary>
        bool? Pay(Adventurer a, EffectSize size, bool specialty, int? natural, Ability ability, out string refused)
        {
            refused = "";
            if (a.Focus < Effects.FocusCost(size)) { refused = "気力が足りない"; return null; }
            if (!specialty && natural == null) { refused = "得意ではないので判定が要る"; return null; }
            a.SpendFocus(Effects.FocusCost(size));
            if (specialty) return true;
            var n = natural!.Value;
            return n == 20 || (n != 1 && n + a.Modifier(ability) + a.TakeBoost() + a.Gear.TrinketBonus(ability) >= (int)Tier.Hard);
        }

        static ActionResult Missed(Adventurer a, string what)
        {
            var r = new ActionResult { Outcome = Outcome.Failure };
            r.Lines.Add($"{a.Name} の{what}はうまくいかなかった。");
            return r;
        }

        static ActionResult Refund(Adventurer a, Effect e, string why)
        {
            a.RestoreFocusBy(e.FocusCost);
            return ActionResult.Refuse(why);
        }

        static string Hp(Creature c) => c.Down ? "倒れた" : $"HP {c.Hp}/{c.MaxHp}";
        static string Fell(Adventurer a) => a.Life == Life.Down ? "、倒れた" : "";
    }
}
