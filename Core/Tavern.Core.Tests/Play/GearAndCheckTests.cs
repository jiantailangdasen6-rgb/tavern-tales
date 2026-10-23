using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tavern.Core.Play;
using static Tavern.Core.Tests.Play.PlayContent;

namespace Tavern.Core.Tests.Play
{
    public class GearTests
    {
        static Item Axe(string id = "axe") => new Item { Id = id, Name = "大斧", Kind = ItemKind.Weapon, Weapon = WeaponType.Axe };

        [Test]
        public void Grades_AndTraits_MustFit()
        {
            Assert.That(new Item { Id = "a", Kind = ItemKind.Weapon, Weapon = WeaponType.Sword, Grade = Grade.Fine, Bonus = 1 }.IsValid(out _), Is.True);
            Assert.That(new Item { Id = "a", Kind = ItemKind.Weapon, Weapon = WeaponType.Sword, Grade = Grade.Fine, Trait = Trait.Bane }.IsValid(out _), Is.True);
            Assert.That(new Item { Id = "a", Kind = ItemKind.Weapon, Weapon = WeaponType.Sword, Grade = Grade.Fine, Bonus = 1, Trait = Trait.Bane }.IsValid(out _), Is.False);
            Assert.That(new Item { Id = "a", Kind = ItemKind.Weapon, Weapon = WeaponType.Sword, Grade = Grade.Masterwork, Bonus = 2 }.IsValid(out _), Is.False, "masterworks have a trait");
            Assert.That(new Item { Id = "a", Kind = ItemKind.Body, Grade = Grade.Fine, Trait = Trait.Bane }.IsValid(out _), Is.False, "bane is for weapons");
            Assert.That(new Item { Id = "a", Kind = ItemKind.Tool, Grade = Grade.Plain }.IsValid(out _), Is.True);
        }

        [Test]
        public void TwoHandedWeapon_DropsTheShield_UnlessStrong()
        {
            var w = Warrior();   // STR +3
            w.Gear.Add(Axe());
            Assert.That(w.Gear.Equip("axe", w.Modifier(Ability.Str)), Is.Null);
            Assert.That(w.Gear.Shield, Is.Not.Null, "a strong arm keeps the shield");
            var p = w.Attack();
            Assert.That(p.OneHandedHeavy, Is.True);
            Assert.That(p.HitBonus, Is.EqualTo(-3));
            Assert.That(p.Damage, Is.EqualTo(4));

            var pr = Priest();   // STR +1
            pr.Gear.Add(Axe());
            pr.Gear.Equip("axe", pr.Modifier(Ability.Str));
            Assert.That(pr.Gear.Shield, Is.Null, "two hands on the axe");
            Assert.That(pr.Gear.Equip("small_shield", pr.Modifier(Ability.Str)), Is.Not.Null);
        }

        [Test]
        public void DualWield_NeedsTwoOneHanders()
        {
            var t = Thief();
            t.Gear.Add(new Item { Id = "bow", Name = "弓", Kind = ItemKind.Weapon, Weapon = WeaponType.Bow });
            Assert.That(t.Gear.Equip("bow", 0, offHand: true), Is.Not.Null);
            t.Gear.Equip("bow", 0);
            Assert.That(t.Gear.OffItem, Is.Null, "a bow takes both hands");
            Assert.That(t.Attack().FirstStrike, Is.True);
        }

        [Test]
        public void Mace_HitsHeavyArmourBetter()
        {
            var p = Priest().Attack();
            Assert.That(p.HitBonusAgainst(13), Is.EqualTo(0));
            Assert.That(p.HitBonusAgainst(14), Is.EqualTo(2));
        }

        [Test]
        public void Armour_ReducesByGrade_HeavyOneMore()
        {
            var w = Warrior();
            Assert.That(w.Gear.ArmorReduction, Is.EqualTo(0), "plain armour has no number");
            w.Gear.Add(new Item { Id = "plate", Kind = ItemKind.Body, Grade = Grade.Masterwork, Bonus = 2, Trait = Trait.Heavy });
            w.Gear.Equip("plate", 3);
            Assert.That(w.Gear.ArmorReduction, Is.EqualTo(3));
        }

        [Test]
        public void Pack_HoldsTen()
        {
            var w = Warrior();
            for (int i = 0; i < 20; i++) w.Gear.Add(new Item { Id = "t" + i, Kind = ItemKind.Tool });
            Assert.That(w.Gear.Items.Count, Is.EqualTo(Gear.MaxItems));
            Assert.That(w.Gear.Remove("sword")!.Name, Is.EqualTo("剣"));
            Assert.That(w.Gear.MainItem, Is.Null);
        }

        [Test]
        public void Shop_BuySellAndUse()
        {
            var p = Priest();
            Assert.That(Shop.Buy(p, "potion"), Is.Null);
            Assert.That(Shop.Buy(p, "potion"), Is.Null);
            Assert.That(p.Silver, Is.EqualTo(0));
            Assert.That(Shop.Buy(p, "torch"), Is.EqualTo("銀貨が足りない"));
            Assert.That(Shop.Buy(p, "sword"), Is.EqualTo("売っていない"));
            p.Gear.Add(new Item { Id = "idol", Kind = ItemKind.Treasure });
            Assert.That(Shop.Sell(p, "idol"), Is.Null);
            Assert.That(p.Silver, Is.EqualTo(10));
            var w = Warrior();
            w.TakeDamage(10);
            Assert.That(Consumables.Use(p, "potion_1", w, out _), Does.Contain("傷がふさがる"));
            Assert.That(w.Hp, Is.EqualTo(11));
            Assert.That(p.Gear.Has("potion_1"), Is.False);
            p.Gear.Add(new Item { Id = "holy_oil", Name = "聖灯の油", Kind = ItemKind.Consumable, Uses = 3 });
            Consumables.Use(p, "holy_oil", p, out _);
            Assert.That(p.Gear.Find("holy_oil")!.Uses, Is.EqualTo(2));
        }

        [Test]
        public void Trinket_AddsOneToItsAbility()
        {
            var m = Mage();
            m.Gear.Add(new Item { Id = "ring", Kind = ItemKind.Trinket, Ability = Ability.Int });
            m.Gear.Equip("ring", 0);
            Assert.That(m.Gear.TrinketBonus(Ability.Int), Is.EqualTo(1));
            Assert.That(m.Gear.TrinketBonus(Ability.Mnd), Is.EqualTo(0));
        }
    }

    public class CheckTests
    {
        static CheckSpec Spec(Ability a, Tier t, params Shift[] shifts)
        {
            var s = new CheckSpec { Ability = a, Tier = t };
            s.Shifts.AddRange(shifts);
            return s;
        }

        [Test]
        public void Shifts_AddUpToOneStepAtMost()
        {
            var t = Thief();
            Assert.That(new CheckRoll(t, Spec(Ability.Dex, Tier.Normal, Shift.NotSpecialty, Shift.Unfavorable)).Tier, Is.EqualTo(Tier.Hard));
            Assert.That(new CheckRoll(t, Spec(Ability.Dex, Tier.Normal, Shift.NotSpecialty, Shift.Favorable)).Tier, Is.EqualTo(Tier.Normal));
            Assert.That(new CheckRoll(t, Spec(Ability.Dex, Tier.Extreme, Shift.Unfavorable)).Tier, Is.EqualTo(Tier.Extreme), "stays at extreme");
            Assert.That(new CheckRoll(t, Spec(Ability.Dex, Tier.Easy, Shift.Favorable)).Tier, Is.EqualTo(Tier.Easy));
            var roll = new CheckRoll(t, Spec(Ability.Dex, Tier.Normal, Shift.NotSpecialty));
            Assert.That(roll.Heading, Is.EqualTo("DEX・難しい 16"));
            Assert.That(roll.Reasons, Has.Member("得意ではない: 1 段難しい"));
        }

        [Test]
        public void Weakness_ComesOnTopOfTheCap()
        {
            var t = Thief();
            t.Statuses.Add(StatusKind.Weak);
            Assert.That(new CheckRoll(t, Spec(Ability.Dex, Tier.Normal, Shift.Unfavorable)).Tier, Is.EqualTo(Tier.Extreme));
        }

        [Test]
        public void Help_IsTwoHoweverMany_BoostIsUsedUp()
        {
            var t = Thief();
            t.PendingBoost = 3;
            var r = new CheckRoll(t, Spec(Ability.Dex, Tier.Normal), helpers: 3);
            Assert.That(r.Bonus, Is.EqualTo(5));
            Assert.That(t.PendingBoost, Is.EqualTo(0));
            r.Apply(8);
            Assert.That(r.Total, Is.EqualTo(8 + 3 + 5));
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Success));
        }

        [Test]
        public void MarkAndCoin()
        {
            var t = Thief();
            t.EarnMark("灯を守った");
            var r = new CheckRoll(t, Spec(Ability.Dex, Tier.Hard));
            r.Apply(9);
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Failure));
            Assert.That(r.UseMark(t), Is.True);
            Assert.That(r.Target, Is.EqualTo(13));
            Assert.That(r.UseMark(t), Is.False);
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Failure), "9 + 3 = 12 < 13");
            Assert.That(r.Reroll(t, 15), Is.True);
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Success));
            Assert.That(r.Reroll(t, 2), Is.False, "one coin, one reroll");
            Assert.That(r.Describe(), Does.Contain("（振り直し）"));
        }

        [Test]
        public void ImportantChecks_GetThroughWithCosts()
        {
            var t = Thief();
            var spec = Spec(Ability.Dex, Tier.Hard);
            spec.Important = true;
            spec.Costs.AddRange(new[] { Cost.Noticed, Cost.Clock });
            var r = new CheckRoll(t, spec);
            r.Apply(5);
            Assert.That(r.GetsThrough, Is.True);
            Assert.That(r.CostsDue(), Is.EqualTo(new[] { Cost.Noticed }));
            r.Apply(1);
            Assert.That(r.CostsDue(), Is.EqualTo(new[] { Cost.Noticed, Cost.Clock }));
            r.Apply(20);
            Assert.That(r.CostsDue(), Is.Empty);
            var plain = new CheckRoll(t, Spec(Ability.Dex, Tier.Hard));
            plain.Apply(5);
            Assert.That(plain.GetsThrough, Is.False);
            Assert.That(plain.CostsDue(), Is.Empty);
        }

        [Test]
        public void FateDie_Thresholds()
        {
            Assert.That(FateDie.Answer(Likelihood.Even, 11), Is.EqualTo(FateAnswer.Yes));
            Assert.That(FateDie.Answer(Likelihood.Even, 10), Is.EqualTo(FateAnswer.No));
            Assert.That(FateDie.Answer(Likelihood.Remote, 17), Is.EqualTo(FateAnswer.No));
            Assert.That(FateDie.Answer(Likelihood.Certain, 1), Is.EqualTo(FateAnswer.NoAndWorse));
            Assert.That(FateDie.Answer(Likelihood.Remote, 20), Is.EqualTo(FateAnswer.YesAndBetter));
            Assert.That(new[] { Likelihood.Certain, Likelihood.Likely, Likelihood.Even, Likelihood.Unlikely, Likelihood.Remote }.Select(l => l.Needed()),
                Is.EqualTo(new[] { 4, 8, 11, 14, 18 }));
        }

        [Test]
        public void Contest_Rules()
        {
            ContestSide S(string id, int nat, int mod, bool def = false) => new ContestSide { Id = id, Natural = nat, Modifier = mod, Defender = def };
            Assert.That(Contest.Resolve(new[] { S("a", 12, 3), S("b", 10, 2, true) }).Winner.Id, Is.EqualTo("a"));
            Assert.That(Contest.Resolve(new[] { S("a", 12, 0), S("b", 10, 2, true) }).Winner.Id, Is.EqualTo("b"), "tie goes to the defender");
            Assert.That(Contest.Resolve(new[] { S("a", 20, 0), S("b", 19, 9, true) }).Winner.Id, Is.EqualTo("a"), "20 always wins");
            Assert.That(Contest.Resolve(new[] { S("a", 20, 0), S("b", 20, 0, true) }).Winner.Id, Is.EqualTo("b"), "two 20s: defender");
            var r = Contest.Resolve(new[] { S("a", 1, 9), S("b", 2, 0, true), S("c", 5, 0) });
            Assert.That(r.Winner.Id, Is.EqualTo("c"), "three: the highest");
            Assert.That(r.Mishaps.Select(m => m.Id), Is.EqualTo(new[] { "a" }));
            Assert.That(Contest.Resolve(new[] { S("a", 20, 0), S("b", 3, 0, true) }).WinnerBonus, Is.True);
            Assert.That(Contest.Unresisted(8, 3, 1), Is.True);
            Assert.That(Contest.Unresisted(7, 3, 1), Is.False);
        }

        [Test]
        public void Secrecy_TargetFromTheWatchers()
        {
            var me = Thief();
            var m = Mage();   // INT 3
            var w = Warrior();
            Assert.That(Secrecy.Target(new List<Adventurer>(), false), Is.Null, "nobody watching, no roll");
            Assert.That(Secrecy.Target(new[] { m, w }, false), Is.EqualTo(13));
            Assert.That(Secrecy.Target(new[] { m, w }, true), Is.EqualTo(15));
            m.TakeDamage(99);
            Assert.That(Secrecy.Target(new[] { m, w }, false), Is.EqualTo(10), "a downed mage sees nothing");
            Assert.That(Secrecy.Found(9, me.Modifier(Ability.Agi), 12), Is.True);
            Assert.That(Secrecy.Found(10, 2, 12), Is.False);
            Assert.That(Secrecy.Found(1, 9, 5), Is.True, "a fumble is found out");
        }
    }
}
