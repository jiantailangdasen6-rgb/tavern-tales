using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tavern.Core.Play;
using static Tavern.Core.Tests.Play.PlayContent;
using Rolls = Tavern.Core.Play.FixedRandom;

namespace Tavern.Core.Tests.Play
{
    public class CombatTests
    {
        static Combat Fight(IEnumerable<Adventurer> party, IEnumerable<Creature> foes, Dictionary<string, int>? init = null, bool surprise = false,
            IEnumerable<Creature>? companions = null, Tavern.Core.Play.IRandom? rng = null, Difficulty d = Difficulty.Standard) =>
            Combat.Start(party, companions ?? new Creature[0], foes, init ?? new Dictionary<string, int>(), surprise, d, rng ?? new SeededRandom(1));

        [Test]
        public void Initiative_PlayersRoll_EnemiesUseSpeed_TiesToTheParty()
        {
            var w = Warrior(); var t = Thief();
            var wolf = Enemy("wolf");          // fast 14
            var troll = Enemy("troll");        // slow 6
            var c = Fight(new[] { w, t }, new[] { wolf, troll }, new Dictionary<string, int> { [w.Id] = 3, [t.Id] = 12 });
            // thief 12+2 = 14 ties the wolf and goes first; warrior 3+1 = 4 after the troll
            Assert.That(c.Order.Select(f => f.Name), Is.EqualTo(new[] { t.Name, wolf.Name, troll.Name, w.Name }));
        }

        [Test]
        public void Surprise_PartyFirstInTheFirstRoundOnly()
        {
            var w = Warrior();
            var wolf = Enemy("wolf");
            var c = Fight(new[] { w }, new[] { wolf }, new Dictionary<string, int> { [w.Id] = 1 }, surprise: true);
            Assert.That(c.NextTurn()!.Name, Is.EqualTo(w.Name));
            Assert.That(c.NextTurn()!.Name, Is.EqualTo(wolf.Name));
            Assert.That(c.NextTurn(), Is.Null);
            c.EndRound();
            Assert.That(c.NextTurn()!.Name, Is.EqualTo(wolf.Name), "speed 14 beats 1+1 from round two");
        }

        [Test]
        public void Attack_HitsMissesAndCrits()
        {
            var w = Warrior();
            var gob = Enemy("goblin");   // defense 11, hp 5
            var c = Fight(new[] { w }, new[] { gob });
            var miss = c.Attack(w, gob.Id, 7);   // 7+3 = 10 < 11
            Assert.That(miss.Outcome, Is.EqualTo(Outcome.Failure));
            var hit = c.Attack(w, gob.Id, 8);
            Assert.That(hit.Outcome, Is.EqualTo(Outcome.Success));
            Assert.That(gob.Hp, Is.EqualTo(2), "sword 3");
            var crit = c.Attack(w, gob.Id, 20);
            Assert.That(gob.Down, Is.True);
            Assert.That(c.State(), Is.EqualTo(CombatEnd.Won));
        }

        [Test]
        public void Dagger_DoublesInTheSurpriseRound()
        {
            var t = Thief();
            var bear = Enemy("dire_bear", 30);
            var c = Fight(new[] { t }, new[] { bear }, surprise: true);
            c.Attack(t, bear.Id, 15);
            Assert.That(bear.Hp, Is.EqualTo(30 - 6), "(2 + 1 dual) x2");
        }

        [Test]
        public void HelplessTargets_TakeCriticals()
        {
            var w = Warrior();
            var gob = Enemy("goblin", 20);
            gob.Statuses.Add(StatusKind.Asleep);
            var c = Fight(new[] { w }, new[] { gob });
            c.Attack(w, gob.Id, 2);
            Assert.That(gob.Hp, Is.EqualTo(14));
            Assert.That(gob.Statuses.Has(StatusKind.Asleep), Is.False, "the hit wakes it");
        }

        [Test]
        public void BaneWeapon_DoublesOnTheUndead()
        {
            var w = Warrior();
            w.Gear.Add(new Item { Id = "holy", Name = "聖剣", Kind = ItemKind.Weapon, Weapon = WeaponType.Sword, Grade = Grade.Masterwork, Bonus = 2, Trait = Trait.Bane });
            w.Gear.Equip("holy", 3);
            var sk = Enemy("skeleton", 30);
            var c = Fight(new[] { w }, new[] { sk });
            c.Attack(w, sk.Id, 15);
            Assert.That(sk.Hp, Is.EqualTo(30 - 10), "(3 + 2) x2");
        }

        [Test]
        public void Enemy_TargetsByPattern_TauntAndSmokeOverride()
        {
            var w = Warrior(); var m = Mage();
            var wolf = Enemy("wolf");   // weakest
            var c = Fight(new[] { w, m }, new[] { wolf });
            Assert.That(c.PlanEnemy(wolf).Blows.Single().TargetId, Is.EqualTo(m.Id), "the mage has the fewest HP");
            c.Hidden.Add(m.Id);
            Assert.That(c.PlanEnemy(wolf).Blows.Single().TargetId, Is.EqualTo(w.Id));
            c.Hidden.Clear();
            c.Taunts[wolf.Id] = (w.Id, 1);
            Assert.That(c.PlanEnemy(wolf).Blows.Single().TargetId, Is.EqualTo(w.Id));
            c.EndRound();
            Assert.That(c.Taunts, Is.Empty);
        }

        [Test]
        public void Dodge_WithShield_ArmourAndWard()
        {
            var w = Warrior();   // AGI 1, plain shield +0
            var bandit = Enemy("bandit");   // dc 12, dmg 2
            var c = Fight(new[] { w }, new[] { bandit }, d: Difficulty.Harsh);
            var blow = c.PlanEnemy(bandit).Blows.Single();
            Assert.That(blow.Dc, Is.EqualTo(13), "harsh +1");
            Assert.That(c.Resolve(blow, 12).Outcome, Is.EqualTo(Outcome.Success), "12+1 dodges 13");
            w.Ward = 4;
            c.Resolve(blow, 3);
            Assert.That(w.Hp, Is.EqualTo(17), "at least 1 even through the ward");
            Assert.That(w.Ward, Is.EqualTo(0), "the ward is used up");
        }

        [Test]
        public void Cover_TheCovererTakesTheBlow()
        {
            var w = Warrior(); var m = Mage();
            var bandit = Enemy("bandit");
            var c = Fight(new[] { w, m }, new[] { bandit });
            Assert.That(c.Cover(w, m.Id, EffectSize.Small).Done, Is.True);
            Assert.That(w.Focus, Is.EqualTo(3));
            var blow = new Blow { Attacker = bandit, TargetId = m.Id, Attack = "短剣", Dc = 12, Damage = 3 };
            c.Resolve(blow, 1);
            Assert.That(m.Hp, Is.EqualTo(12));
            Assert.That(w.Hp, Is.EqualTo(16), "3 - 1");
            c.EndRound();
            Assert.That(c.Covers, Is.Empty);
        }

        [Test]
        public void SupportWithoutSpecialty_NeedsARoll()
        {
            var t = Thief();   // no heal specialty
            var w = Warrior();
            w.TakeDamage(10);
            var c = Fight(new[] { t, w }, new[] { Enemy("wolf") });
            Assert.That(c.HealEffect(t, w.Id, EffectSize.Small).Refused, Is.EqualTo("得意ではないので判定が要る"));
            Assert.That(t.Focus, Is.EqualTo(4), "nothing spent");
            var r = c.HealEffect(t, w.Id, EffectSize.Small, natural: 15);   // MND 0: 15 < 16
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Failure));
            Assert.That(t.Focus, Is.EqualTo(3));
            var p = Priest();
            var c2 = Fight(new[] { p, w }, new[] { Enemy("wolf") });
            c2.HealEffect(p, w.Id, EffectSize.Medium);
            Assert.That(w.Hp, Is.EqualTo(13));
            Assert.That(p.Focus, Is.EqualTo(3));
        }

        [Test]
        public void Effects_RefusedWithoutFocus()
        {
            var w = Warrior();
            w.SpendFocus(3);
            var c = Fight(new[] { w }, new[] { Enemy("wolf") });
            var r = c.Offense(w, new Effect(EffectKind.Sweep, EffectSize.Medium), Ability.Str, "", 15);
            Assert.That(r.Done, Is.False);
            Assert.That(r.Refused, Is.EqualTo("気力が足りない"));
            Assert.That(w.Focus, Is.EqualTo(1));
        }

        [Test]
        public void Sweep_HitsEveryoneItBeats()
        {
            var w = Warrior();
            var gob = Enemy("goblin");    // def 11
            var knight = Enemy("boneknight", 24);   // def 14
            var c = Fight(new[] { w }, new[] { gob, knight });
            c.Offense(w, new Effect(EffectKind.Sweep, EffectSize.Small), Ability.Str, "", 9);   // 12
            Assert.That(gob.Hp, Is.EqualTo(3));
            Assert.That(knight.Hp, Is.EqualTo(24));
        }

        [Test]
        public void Strike_UndeadBaneSpecialty_IsOneSizeBigger_NotSpecialtyHarder()
        {
            var p = Priest();   // MND 3, holy light is a strike with undead bane
            var sk = Enemy("skeleton", 30);   // def 13
            var c = Fight(new[] { p }, new[] { sk });
            c.Offense(p, new Effect(EffectKind.Strike, EffectSize.Small), Ability.Mnd, sk.Id, 10);
            Assert.That(sk.Hp, Is.EqualTo(25), "small becomes medium (5)");
            var w = Warrior();   // no strike specialty: defense +3
            var gob = Enemy("goblin", 20);
            var c2 = Fight(new[] { w }, new[] { gob });
            Assert.That(c2.Offense(w, new Effect(EffectKind.Strike, EffectSize.Small), Ability.Str, gob.Id, 10).Outcome, Is.EqualTo(Outcome.Failure), "13 < 11 + 3");
            Assert.That(c2.Offense(w, new Effect(EffectKind.Strike, EffectSize.Small), Ability.Str, gob.Id, 11).Outcome, Is.EqualTo(Outcome.Success));
        }

        [Test]
        public void Hold_SizesAndTheBossOnce()
        {
            var m = Mage();
            var gob = Enemy("goblin");
            var knight = Enemy("boneknight");
            var c = Fight(new[] { m }, new[] { gob, knight });
            Assert.That(c.Offense(m, new Effect(EffectKind.Hold, EffectSize.Small), Ability.Int, knight.Id, 15).Done, Is.False, "small holds only weak enemies");
            Assert.That(m.Focus, Is.EqualTo(6), "refunded");
            c.Offense(m, new Effect(EffectKind.Hold, EffectSize.Small), Ability.Int, gob.Id, 15);
            Assert.That(gob.Statuses.Has(StatusKind.Stunned), Is.True);
            c.Offense(m, new Effect(EffectKind.Hold, EffectSize.Large), Ability.Int, knight.Id, 15);
            Assert.That(knight.Statuses.Has(StatusKind.Stunned), Is.True);
            Assert.That(c.Offense(m, new Effect(EffectKind.Hold, EffectSize.Large), Ability.Int, knight.Id, 15).Done, Is.False, "once per fight");
        }

        [Test]
        public void BigMove_WarnedThenHitsEveryone()
        {
            var w = Warrior(); var m = Mage();
            var knight = Enemy("boneknight");   // every 3
            var c = Fight(new[] { w, m }, new[] { knight });
            Assert.That(c.PlanEnemy(knight).Lines.Count, Is.EqualTo(0));
            c.EndRound();
            var warned = c.PlanEnemy(knight);   // round 2: warns for round 3
            Assert.That(warned.Lines.Single(), Does.Contain("次の手番に大技"));
            c.EndRound();
            var big = c.PlanEnemy(knight);
            Assert.That(big.Blows.Count, Is.EqualTo(2));
            Assert.That(big.Blows.All(b => b.BigMove), Is.True);
        }

        [Test]
        public void Captain_SummonsGuards()
        {
            var w = Warrior();
            var cap = Enemy("guard_captain");
            var c = Fight(new[] { w }, new[] { cap });
            c.EndRound(); c.PlanEnemy(cap); c.EndRound();
            var plan = c.PlanEnemy(cap);
            Assert.That(plan.Blows, Is.Empty);
            var added = c.Summon(cap, Enemies["guard"], 1);
            Assert.That(added.Count, Is.EqualTo(2));
            Assert.That(c.Foes.Count(), Is.EqualTo(3));
        }

        [Test]
        public void Assassin_PoisonsOnAHit()
        {
            var t = Thief();
            var a = Enemy("assassin");
            var c = Fight(new[] { t }, new[] { a });
            c.Resolve(c.PlanEnemy(a).Blows.Single(), 2);
            Assert.That(t.Statuses.Has(StatusKind.Poison), Is.True);
            var lines = new List<string>();
            var hp = t.Hp;
            c.BeginTurn(c.Find(t.Id)!, lines);
            Assert.That(t.Hp, Is.EqualTo(hp - 1));
        }

        [Test]
        public void Companion_HealerHealsTheHurt()
        {
            var w = Warrior();
            w.TakeDamage(12);
            var nurse = Creature.FromBuild(Build.Normal, "mira", "ミラ", Side.Party, CompanionRole.Heal);
            var c = Fight(new[] { w }, new[] { Enemy("wolf") }, companions: new[] { nurse });
            c.CompanionTurn(nurse);
            Assert.That(w.Hp, Is.EqualTo(9));
        }

        [Test]
        public void Finish_Revive_Flee_AndEnds()
        {
            var w = Warrior(); var m = Mage();
            var wolf = Enemy("wolf");
            var c = Fight(new[] { w, m }, new[] { wolf });
            m.TakeDamage(99);
            Assert.That(c.Revive(w, m.Id).Done, Is.True);
            Assert.That(m.Hp, Is.EqualTo(1));
            wolf.TakeDamage(99);
            Assert.That(c.Finish(w.Name, wolf.Id).Done, Is.True);
            Assert.That(wolf.Killed, Is.True);

            var w2 = Warrior("ボルド");
            var troll = Enemy("troll");
            var c2 = Fight(new[] { w2 }, new[] { troll });
            Assert.That(c2.Flee(w2, 11).Outcome, Is.EqualTo(Outcome.Failure), "11+1 < 13");
            Assert.That(c2.Flee(w2, 12).Outcome, Is.EqualTo(Outcome.Success));
            Assert.That(c2.State(), Is.EqualTo(CombatEnd.Fled));

            var w3 = Warrior("カイ");
            var c3 = Fight(new[] { w3 }, new[] { Enemy("troll") });
            w3.TakeDamage(99);
            Assert.That(c3.State(), Is.EqualTo(CombatEnd.Wiped));
        }

        [Test]
        public void ScaledHp_ByPartyAndDifficulty()
        {
            Assert.That(Combat.ScaledHp(24, 3, Difficulty.Standard), Is.EqualTo(24));
            Assert.That(Combat.ScaledHp(24, 1, Difficulty.Standard), Is.EqualTo(11));
            Assert.That(Combat.ScaledHp(24, 4, Difficulty.Standard), Is.EqualTo(32));
            Assert.That(Combat.ScaledHp(24, 3, Difficulty.Gentle), Is.EqualTo(19));
            Assert.That(Combat.ScaledHp(24, 3, Difficulty.Harsh), Is.EqualTo(30));
        }

        [Test]
        public void DeterministicWithTheSameRolls()
        {
            string Run()
            {
                var w = Warrior(); var m = Mage();
                var a = Enemy("wolf"); var b = Enemy("goblin");
                var c = Fight(new[] { w, m }, new[] { a, b }, rng: new Rolls(3, 17, 8, 12, 5, 19, 2, 11));
                var log = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    log.AddRange(c.PlanEnemy(a).Blows.Select(x => x.TargetId));
                    log.AddRange(c.PlanEnemy(b).Blows.Select(x => x.TargetId));
                    c.EndRound();
                }
                return string.Join(",", log);
            }
            Assert.That(Run(), Is.EqualTo(Run()));
        }
    }
}
