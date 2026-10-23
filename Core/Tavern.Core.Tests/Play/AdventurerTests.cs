using System.Linq;
using NUnit.Framework;
using Tavern.Core.Play;
using static Tavern.Core.Tests.Play.PlayContent;

namespace Tavern.Core.Tests.Play
{
    public class AdventurerTests
    {
        [Test]
        public void Classes_MatchTheSpecTable()
        {
            Assert.That(Classes.Classes.Keys, Is.EquivalentTo(new[] { "warrior", "mage", "thief", "priest" }));
            var w = Warrior();
            Assert.That(new[] { w.Modifier(Ability.Str), w.Modifier(Ability.Dex), w.Modifier(Ability.Agi), w.Modifier(Ability.Int), w.Modifier(Ability.Mnd) },
                Is.EqualTo(new[] { 3, 1, 1, 0, 1 }));
            Assert.That((w.MaxHp, w.MaxFocus), Is.EqualTo((18, 4)));
            var t = Thief();
            Assert.That(new[] { t.Modifier(Ability.Str), t.Modifier(Ability.Dex), t.Modifier(Ability.Agi), t.Modifier(Ability.Int), t.Modifier(Ability.Mnd) },
                Is.EqualTo(new[] { 0, 3, 2, 1, 0 }));
            Assert.That((Mage().MaxHp, Mage().MaxFocus, Priest().MaxHp, Priest().MaxFocus), Is.EqualTo((12, 6, 15, 5)));
        }

        [Test]
        public void StartingGear_IsInTheSlots()
        {
            var w = Warrior();
            Assert.That(w.Gear.MainItem!.Name, Is.EqualTo("剣"));
            Assert.That(w.Gear.Shield!.Name, Is.EqualTo("木の盾"));
            Assert.That(w.Gear.BodyItem!.Name, Is.EqualTo("鎖帷子"));
            var t = Thief();
            var a = t.Attack();
            Assert.That(a.DualWield, Is.True);
            Assert.That(a.Ability, Is.EqualTo(Ability.Dex));
            Assert.That(a.Damage, Is.EqualTo(3), "dagger 2 + dual wield 1");
            Assert.That(Mage().Attack().Type, Is.EqualTo(WeaponType.Staff));
            Assert.That(Mage().MagicMissile().Damage, Is.EqualTo(2));
            Assert.That(Priest().Gear.Shield!.Name, Is.EqualTo("小盾"));
        }

        [Test]
        public void ClassText_GrowsWithASpecialty()
        {
            var w = Warrior();
            var text = w.ClassText();
            Assert.That(text, Does.StartWith("あなたは前に立つ者だ。"));
            Assert.That(text, Does.Contain("（全体攻撃）").And.Contain("（かばう）").And.Contain("（加護）"));
            Assert.That(w.HasSpecialty(EffectKind.Taunt), Is.False);
            var taunt = w.GrowthChoices().First(g => g.Kind == EffectKind.Taunt);
            Assert.That(w.Learn(taunt), Is.True);
            Assert.That(w.HasSpecialty(EffectKind.Taunt), Is.True);
            Assert.That(w.ClassText(), Does.EndWith("（挑発）。"));
            Assert.That(w.GrowthChoices(), Has.No.Member(taunt));
            Assert.That(Mage().ClassText(), Does.EndWith("大きな術ほど、気力を深く食う。"));
        }

        [Test]
        public void Growth_CapsAtFive_AndResetsEachEvening()
        {
            var w = Warrior();
            Assert.That(w.Improve(Ability.Str), Is.True);
            Assert.That(w.Improve(Ability.Str), Is.True);
            Assert.That(w.Modifier(Ability.Str), Is.EqualTo(5));
            Assert.That(w.Improve(Ability.Str), Is.False);
            w.Toughen();
            Assert.That((w.MaxHp, w.Hp), Is.EqualTo((21, 21)));
            w.EarnMark("骨の騎士を断った");
            w.UseMark();
            w.Gear.Add(new Item { Id = "rope", Name = "ロープ", Kind = ItemKind.Tool });
            w.StartEvening(Difficulty.Standard);
            Assert.That(w.Modifier(Ability.Str), Is.EqualTo(3));
            Assert.That(w.MaxHp, Is.EqualTo(18));
            Assert.That(w.Gear.Has("rope"), Is.False, "items are not carried over");
            Assert.That(w.Marks.Single().UsedTonight, Is.False, "marks stay and are fresh again");
            Assert.That(w.Silver, Is.EqualTo(10));
        }

        [Test]
        public void FateCoins_ByDifficulty()
        {
            var w = Warrior();
            w.StartEvening(Difficulty.Gentle);
            Assert.That(w.FateCoins, Is.EqualTo(2));
            w.StartEvening(Difficulty.Harsh);
            Assert.That(w.FateCoins, Is.EqualTo(1));
        }

        [Test]
        public void Marks_AtMostThree_OnePerUse()
        {
            var w = Warrior();
            for (int i = 0; i < 5; i++) w.EarnMark("印" + i);
            Assert.That(w.Marks.Count, Is.EqualTo(3));
            Assert.That(w.UseMark() && w.UseMark() && w.UseMark(), Is.True);
            Assert.That(w.UseMark(), Is.False);
        }

        [Test]
        public void NamedTechniques_FoundInBrackets_KeptOnce()
        {
            Assert.That(TechniqueNames.Find("【柱蹴りの一閃】で首を狙い、「待て！」と叫ぶ"), Is.EqualTo(new[] { "柱蹴りの一閃" }));
            Assert.That(TechniqueNames.Find("「バルドリック！」と叫ぶ"), Is.Empty, "speech brackets are not techniques");
            var t = Thief();
            var first = t.AddTechnique("柱蹴りの一閃", "柱を蹴って跳ぶ", EffectKind.Strike, EffectSize.Medium, Ability.Dex);
            var again = t.AddTechnique("柱蹴りの一閃", "別の説明", EffectKind.Sweep, EffectSize.Large, Ability.Str);
            Assert.That(again, Is.SameAs(first));
            Assert.That(again!.Kind, Is.EqualTo(EffectKind.Strike));
            Assert.That(t.AddTechnique("一二三四五六七八九十一二三", "", EffectKind.Strike, EffectSize.Small, Ability.Dex), Is.Null, "13 characters");
            for (int i = 0; i < 10; i++) t.AddTechnique("技" + i, "", EffectKind.Strike, EffectSize.Small, Ability.Dex);
            Assert.That(t.Techniques.Count, Is.EqualTo(8));
            Assert.That(t.ForgetTechnique("柱蹴りの一閃"), Is.True);
        }

        [Test]
        public void DeathSaves_ThreeFailuresDie_TwentyStandsUp()
        {
            var w = Warrior();
            w.TakeDamage(99);
            Assert.That(w.Life, Is.EqualTo(Life.Down));
            Assert.That(w.ApplyDeathSave(12), Is.EqualTo(DeathSave.Holds));
            Assert.That(w.ApplyDeathSave(5), Is.EqualTo(DeathSave.Fails));
            Assert.That(w.ApplyDeathSave(20), Is.EqualTo(DeathSave.StandsUp));
            Assert.That((w.Life, w.Hp), Is.EqualTo((Life.Up, 1)));
            w.TakeDamage(5);
            w.ApplyDeathSave(2); w.ApplyDeathSave(3);
            Assert.That(w.ApplyDeathSave(9), Is.EqualTo(DeathSave.Died));
            Assert.That(w.Life, Is.EqualTo(Life.Dead));
        }

        [Test]
        public void Rest_HalfHpAndAllFocus()
        {
            var w = Warrior();
            w.TakeDamage(15);
            w.SpendFocus(4);
            w.Rest();
            Assert.That((w.Hp, w.Focus), Is.EqualTo((12, 4)));
        }

        [Test]
        public void Finish_KillsTheDowned()
        {
            var w = Warrior();
            w.TakeDamage(99);
            w.Finish();
            Assert.That(w.Life, Is.EqualTo(Life.Dead));
        }
    }
}
