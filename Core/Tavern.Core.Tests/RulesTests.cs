using NUnit.Framework;
using Tavern.Core.Rules;

namespace Tavern.Core.Tests
{
    /// <summary>Always returns the queued numbers (for d20: the face).</summary>
    sealed class FixedRandom : IRandom
    {
        readonly System.Collections.Generic.Queue<int> _faces;
        public FixedRandom(params int[] faces) { _faces = new(faces); }
        public int Next(int minInclusive, int maxExclusive) => _faces.Dequeue();
    }

    public class RulesTests
    {
        static Character Gareth() => new("ガレス", "warrior", might: 3, skill: 1, wits: 0, spirit: 1, maxHp: 18, maxFocus: 4);

        [Test]
        public void SeededRandom_SameSeed_SameRolls()
        {
            var a = new SeededRandom(42); var b = new SeededRandom(42);
            for (int i = 0; i < 100; i++) Assert.That(Dice.D20(a), Is.EqualTo(Dice.D20(b)));
        }

        [Test]
        public void SeededRandom_D20_CoversOneToTwenty()
        {
            var rng = new SeededRandom(7);
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 2000; i++) { var r = Dice.D20(rng); Assert.That(r, Is.InRange(1, 20)); seen.Add(r); }
            Assert.That(seen.Count, Is.EqualTo(20));
        }

        [Test]
        public void Check_TotalMeetsDc_Succeeds()
        {
            var r = Checks.Roll(new CheckRequest("ガレス", Stat.Might, Difficulty.Hard, "扉を破る"), Gareth(), new FixedRandom(13));
            Assert.That(r.Total, Is.EqualTo(16));
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Success));
        }

        [Test]
        public void Check_TotalBelowDc_Fails()
        {
            var r = Checks.Roll(new CheckRequest("ガレス", Stat.Wits, Difficulty.Normal, "古文書"), Gareth(), new FixedRandom(12));
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Failure));
        }

        [Test]
        public void Check_Natural20_IsCriticalEvenAgainstHeroic()
        {
            var r = Checks.Roll(new CheckRequest("ガレス", Stat.Wits, Difficulty.Heroic, "x"), Gareth(), new FixedRandom(20));
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Critical));
        }

        [Test]
        public void Check_Natural1_IsFumbleEvenWhenTotalWouldPass()
        {
            var r = Checks.Roll(new CheckRequest("ガレス", Stat.Might, Difficulty.Easy, "x", helpers: 2), Gareth(), new FixedRandom(1));
            Assert.That(r.Total, Is.GreaterThanOrEqualTo(8));
            Assert.That(r.Outcome, Is.EqualTo(Outcome.Fumble));
        }

        [Test]
        public void Check_Helpers_AddTwoEach()
        {
            var r = Checks.Roll(new CheckRequest("ガレス", Stat.Wits, Difficulty.Normal, "x", helpers: 2), Gareth(), new FixedRandom(9));
            Assert.That(r.Total, Is.EqualTo(13));
            Assert.That(r.Succeeded, Is.True);
        }

        [Test]
        public void Reroll_SpendsTheOnlyFateCoin()
        {
            var g = Gareth(); var rng = new FixedRandom(2, 15);
            var first = Checks.Roll(new CheckRequest("ガレス", Stat.Might, Difficulty.Hard, "x"), g, rng);
            var second = Checks.Reroll(first, g, rng);
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.Rerolled && second.Succeeded, Is.True);
            Assert.That(Checks.Reroll(second, g, rng), Is.Null);
        }

        [Test]
        public void Damage_ToZero_KnocksDown_HealStandsUp()
        {
            var g = Gareth();
            g.TakeDamage(30);
            Assert.That(g.State, Is.EqualTo(LifeState.Down));
            Assert.That(g.Hp, Is.EqualTo(0));
            g.Heal(5);
            Assert.That(g.State, Is.EqualTo(LifeState.Active));
        }

        [Test]
        public void DeathSaves_ThreeFailures_Die()
        {
            var g = Gareth(); g.TakeDamage(99);
            Assert.That(DeathSaves.Apply(g, 9), Is.EqualTo(DeathSaveOutcome.Fails));
            Assert.That(DeathSaves.Apply(g, 10), Is.EqualTo(DeathSaveOutcome.Holds));
            Assert.That(DeathSaves.Apply(g, 3), Is.EqualTo(DeathSaveOutcome.Fails));
            Assert.That(DeathSaves.Apply(g, 1), Is.EqualTo(DeathSaveOutcome.Died));
            Assert.That(g.State, Is.EqualTo(LifeState.Dead));
            g.Heal(10);
            Assert.That(g.State, Is.EqualTo(LifeState.Dead));
        }

        [Test]
        public void DeathSaves_Natural20_StandsUpWithOneHp()
        {
            var g = Gareth(); g.TakeDamage(99);
            Assert.That(DeathSaves.Apply(g, 20), Is.EqualTo(DeathSaveOutcome.StandsUp));
            Assert.That(g.State, Is.EqualTo(LifeState.Active));
            Assert.That(g.Hp, Is.EqualTo(1));
        }

        [TestCase("力", Stat.Might)] [TestCase("技", Stat.Skill)] [TestCase("知", Stat.Wits)] [TestCase("心", Stat.Spirit)] [TestCase("Wits", Stat.Wits)]
        public void TryParseStat_AcceptsLabelsAndNames(string text, Stat expected)
        {
            Assert.That(RuleNames.TryParseStat(text, out var s), Is.True);
            Assert.That(s, Is.EqualTo(expected));
        }
    }
}
