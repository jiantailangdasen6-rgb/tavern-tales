using System.Linq;
using NUnit.Framework;
using Tavern.Core.Play;
using static Tavern.Core.Tests.Play.PlayContent;

namespace Tavern.Core.Tests.Play
{
    public class NpcTests
    {
        [Test]
        public void RequestTable()
        {
            Assert.That(Npcs.Need(Attitude.Hostile, RequestSize.Large).Possible, Is.False);
            Assert.That(Npcs.Need(Attitude.Hostile, RequestSize.Small).Tier, Is.EqualTo(Tier.Hard));
            Assert.That(Npcs.Need(Attitude.Wary, RequestSize.Normal).Tier, Is.EqualTo(Tier.Hard));
            Assert.That(Npcs.Need(Attitude.Neutral, RequestSize.Small).NeedsRoll, Is.False);
            Assert.That(Npcs.Need(Attitude.Neutral, RequestSize.Large).Tier, Is.EqualTo(Tier.Hard));
            Assert.That(Npcs.Need(Attitude.Friendly, RequestSize.Normal).Tier, Is.EqualTo(Tier.Easy));
            Assert.That(Npcs.Need(Attitude.Trusting, RequestSize.Normal).NeedsRoll, Is.False);
            Assert.That(Npcs.Need(Attitude.Trusting, RequestSize.Large).Tier, Is.EqualTo(Tier.Easy));
            Assert.That(Npcs.Need(Attitude.Wary, RequestSize.Large, sweetened: true).Tier, Is.EqualTo(Tier.Hard));
            Assert.That(Npcs.Need(Attitude.Friendly, RequestSize.Normal, sweetened: true).NeedsRoll, Is.False);
            Assert.That(Npcs.AbilityFor(Approach.Threaten), Is.EqualTo(Ability.Str));
            Assert.That(Npcs.AbilityFor(Approach.Trick), Is.EqualTo(Ability.Int));
        }

        [Test]
        public void AttitudeChanges()
        {
            var p = new Npc { Id = "elder", Attitude = Attitude.Neutral };
            Assert.That(p.Change(AttitudeReason.Gift), Is.EqualTo(1));
            Assert.That(p.Change(AttitudeReason.Gift), Is.EqualTo(0), "one gift per evening");
            p.Change(AttitudeReason.SavedLife);
            Assert.That(p.Attitude, Is.EqualTo(Attitude.Trusting), "capped");
            p.Change(AttitudeReason.Attacked);
            Assert.That(p.Attitude, Is.EqualTo(Attitude.Hostile));
            Assert.That(Npcs.AfterRequest(Attitude.Neutral, Approach.Threaten, Outcome.Success), Is.EqualTo(Attitude.Wary));
            Assert.That(Npcs.AfterRequest(Attitude.Neutral, Approach.Persuade, Outcome.Critical), Is.EqualTo(Attitude.Trusting));
            Assert.That(Npcs.AfterRequest(Attitude.Neutral, Approach.Persuade, Outcome.Fumble), Is.EqualTo(Attitude.Wary));
        }

        [Test]
        public void Companions_LimitAndLeaving()
        {
            var cast = new Cast();
            cast.People.Add(new Npc { Id = "a", Joins = JoinWillingness.Glad, Attitude = Attitude.Friendly });
            cast.People.Add(new Npc { Id = "b", Joins = JoinWillingness.Conditional, Attitude = Attitude.Friendly });
            cast.People.Add(new Npc { Id = "c", Joins = JoinWillingness.Glad, Attitude = Attitude.Friendly });
            cast.People.Add(new Npc { Id = "d", Joins = JoinWillingness.Never });
            Assert.That(cast.Join("a", 4), Is.True);
            Assert.That(cast.Join("c", 4), Is.False, "four players: one companion");
            Assert.That(cast.Join("b", 2), Is.False, "condition not met");
            Assert.That(cast.Join("b", 2, conditionMet: true), Is.True);
            Assert.That(cast.Join("d", 1), Is.False);
            cast.Find("a")!.Attitude = Attitude.Wary;
            Assert.That(cast.Departures().Select(p => p.Id), Is.EqualTo(new[] { "a" }));
            Assert.That(Npcs.Betrays(Attitude.Trusting), Is.False);
            Assert.That(Npcs.Betrays(Attitude.Friendly), Is.Null);
            Assert.That(Npcs.Betrays(Attitude.Neutral), Is.True);
        }

        [Test]
        public void Extras_EightAtMost()
        {
            var cast = new Cast();
            for (int i = 0; i < 10; i++) cast.AddExtra("村人" + i, "village", "npc_villager");
            Assert.That(cast.ExtrasAdded, Is.EqualTo(8));
            Assert.That(cast.People.All(p => p.Attitude == Attitude.Neutral && p.Build == Build.Weak), Is.True);
        }

        [Test]
        public void Builds()
        {
            var strong = Creature.FromBuild(Build.Strong, "x", "X", Side.Party);
            Assert.That((strong.MaxHp, strong.Defense, strong.Attacks[0].Damage), Is.EqualTo((14, 13, 3)));
        }
    }

    public class ClockTests
    {
        static Clock New(int segments, ClockSpeed speed, int every = 5, bool good = false)
        {
            var d = new ClockDef { Id = "c", Segments = segments, Speed = speed, Every = every, Good = good };
            d.Rewinds.Add(new ClockRewind { Id = "r", Amount = 2 });
            return new Clock(d);
        }

        [Test]
        public void EveryTurns_SlowerForSmallParties()
        {
            var clocks = new Clocks();
            var c = New(6, ClockSpeed.EveryTurns, 4);
            clocks.All.Add(c);
            for (int i = 0; i < 4; i++) clocks.Turn(1, 3);
            Assert.That(c.Filled, Is.EqualTo(1));
            var solo = new Clocks();
            var s = New(6, ClockSpeed.EveryTurns, 4);
            solo.All.Add(s);
            for (int i = 0; i < 5; i++) solo.Turn(1, 1);
            Assert.That(s.Filled, Is.EqualTo(0), "1.5 x 4 = 6 turns");
            solo.Turn(1, 1);
            Assert.That(s.Filled, Is.EqualTo(1));
        }

        [Test]
        public void EventsOnly_AndCombatRounds()
        {
            var clocks = new Clocks();
            var ev = New(4, ClockSpeed.EventsOnly);
            var cr = New(3, ClockSpeed.CombatRounds);
            clocks.All.Add(ev); clocks.All.Add(cr);
            for (int i = 0; i < 20; i++) clocks.Turn(1, 3);
            Assert.That(ev.Filled, Is.EqualTo(0));
            clocks.CombatRound(1); clocks.CombatRound(1);
            Assert.That(clocks.CombatRound(1), Has.Member(cr));
            Assert.That(clocks.TakeFired(), Is.EqualTo(new[] { cr }));
            Assert.That(clocks.TakeFired(), Is.Empty, "fires once");
        }

        [Test]
        public void ActsAndPausing()
        {
            var clocks = new Clocks();
            var c = New(6, ClockSpeed.EveryTurns, 1);
            c.Def.Acts.Add(2);
            clocks.All.Add(c);
            clocks.Turn(1, 3);
            Assert.That(c.Filled, Is.EqualTo(0));
            clocks.Turn(2, 3);
            Assert.That(c.Filled, Is.EqualTo(1));
            c.Paused = true;
            clocks.Turn(2, 3);
            Assert.That(c.Filled, Is.EqualTo(1));
        }

        [Test]
        public void Rewinds_OnceEach_GoodClocksGoForward()
        {
            var c = New(6, ClockSpeed.EventsOnly);
            c.Push(4);
            Assert.That(c.Rewind("r"), Is.True);
            Assert.That(c.Filled, Is.EqualTo(2));
            Assert.That(c.Rewind("r"), Is.False);
            c.Better();
            Assert.That(c.Filled, Is.EqualTo(1));
            var dawn = New(6, ClockSpeed.EventsOnly, good: true);
            dawn.Better();
            Assert.That(dawn.Filled, Is.EqualTo(1));
            var clocks = new Clocks();
            clocks.All.Add(dawn);
            clocks.PushAll(1, 2);
            Assert.That(dawn.Filled, Is.EqualTo(1), "rest and noise do not push good clocks");
        }

        [Test]
        public void Hints_StepUpWhenStalled()
        {
            var h = new Hints();
            for (int i = 0; i < 3; i++) h.Turn(false, Difficulty.Standard);
            Assert.That(h.Level, Is.EqualTo(0));
            h.Turn(false, Difficulty.Standard);
            Assert.That((h.Level, h.Due), Is.EqualTo((1, 1)));
            for (int i = 0; i < 3; i++) h.Turn(false, Difficulty.Standard);
            Assert.That(h.Level, Is.EqualTo(2));
            for (int i = 0; i < 3; i++) h.Turn(false, Difficulty.Standard);
            Assert.That(h.Level, Is.EqualTo(3));
            h.Turn(true, Difficulty.Standard);
            Assert.That(h.Level, Is.EqualTo(0));
            var harsh = new Hints();
            for (int i = 0; i < 30; i++) harsh.Turn(false, Difficulty.Harsh);
            Assert.That(harsh.Level, Is.EqualTo(2), "no third step on harsh");
            var asked = new Hints();
            asked.Ask(Difficulty.Standard);
            Assert.That(asked.Due, Is.EqualTo(1));
        }

        [Test]
        public void Tension_ThreeTurnsTwicePerEvening()
        {
            var t = new Tension();
            Assert.That(t.Trigger(), Is.True);
            Assert.That(t.Trigger(), Is.False, "already on");
            t.Turn(); t.Turn(); t.Turn();
            Assert.That(t.On, Is.False);
            Assert.That(t.Trigger(), Is.True);
            t.Turn(); t.Turn(); t.Turn();
            Assert.That(t.Trigger(), Is.False, "twice per evening");
            t.NewNight();
            Assert.That(t.Trigger(), Is.True);
        }
    }

    public class WorldTests
    {
        static World Village()
        {
            var w = new World();
            w.Links["inn"] = new() { "square" };
            w.Links["square"] = new() { "inn", "shrine" };
            w.Links["shrine"] = new() { "square" };
            w.Move("gareth", "inn");
            w.Move("rocco", "inn");
            w.Cast.People.Add(new Npc { Id = "elder", Location = "inn", Attitude = Attitude.Neutral });
            w.Cast.People.Add(new Npc { Id = "priest", Location = "shrine" });
            w.PutItem("lamp", World.Pc("gareth"));
            w.ItemKinds["lamp"] = ItemKind.Story;
            w.PutItem("idol", "shrine");
            w.ItemKinds["idol"] = ItemKind.Treasure;
            return w;
        }

        [Test]
        public void Handover_Checks()
        {
            var w = Village();
            Assert.That(w.CanGive("gareth", "lamp", "rocco", true), Is.Null);
            Assert.That(w.CanGive("rocco", "lamp", "gareth", true), Is.EqualTo("持っていない"));
            Assert.That(w.CanGive("gareth", "lamp", "elder", false), Is.Null);
            Assert.That(w.CanGive("gareth", "lamp", "priest", false), Is.EqualTo("相手がその場にいない"));
            Assert.That(w.CanGive("gareth", "lamp", "nobody", false), Is.EqualTo("相手がいない"));
            Assert.That(w.CanPlace("gareth", "lamp", "shrine"), Is.EqualTo("その場所にいない"));
            Assert.That(w.CanTake("rocco", "idol"), Is.EqualTo("ここにはない"));
            w.Move("rocco", "shrine");
            Assert.That(w.CanTake("rocco", "idol"), Is.Null);
        }

        [Test]
        public void Conditions_AllSixTypes()
        {
            var w = Village();
            Condition C(ConditionType t, string id, string isWhat = "", string value = "", int count = 0) =>
                new Condition { Type = t, Id = id, Is = isWhat, Value = value, Count = count };
            Assert.That(C(ConditionType.Place, "inn").Holds(w), Is.True);
            w.Move("rocco", "square");
            Assert.That(C(ConditionType.Place, "inn").Holds(w), Is.False, "the whole party");
            Assert.That(C(ConditionType.Place, "inn", "any").Holds(w), Is.True);
            Assert.That(C(ConditionType.Item, "lamp", "held_by", "party").Holds(w), Is.True);
            Assert.That(C(ConditionType.Item, "idol", "at", "shrine").Holds(w), Is.True);
            Assert.That(new Condition { Type = ConditionType.Item, Kind = ItemKind.Treasure, Count = 1 }.Holds(w), Is.False);
            w.PutItem("idol", World.Pc("rocco"));
            Assert.That(new Condition { Type = ConditionType.Item, Kind = ItemKind.Treasure, Count = 1 }.Holds(w), Is.True);
            Assert.That(C(ConditionType.Person, "elder", "at", "inn").Holds(w), Is.True);
            Assert.That(C(ConditionType.Person, "elder", "attitude", "Friendly").Holds(w), Is.False);
            w.Cast.Find("elder")!.Attitude = Attitude.Trusting;
            Assert.That(C(ConditionType.Person, "elder", "attitude", "friendly").Holds(w), Is.True);
            w.Cast.Find("priest")!.State = PersonState.Retreated;
            Assert.That(C(ConditionType.Person, "priest", "retreated").Holds(w), Is.True);
            Assert.That(C(ConditionType.Event, "gate_open").Holds(w), Is.False);
            w.Events.Add("gate_open");
            Assert.That(C(ConditionType.Event, "gate_open").Holds(w), Is.True);
            var clock = new Clock(new ClockDef { Id = "ritual", Segments = 4 });
            w.Clocks.All.Add(clock);
            clock.Push(3);
            Assert.That(C(ConditionType.Clock, "ritual", "at_least", count: 3).Holds(w), Is.True);
            Assert.That(C(ConditionType.Clock, "ritual", "full").Holds(w), Is.False);
            Assert.That(C(ConditionType.Conviction, "who").Holds(w), Is.False);
            w.Convictions.Add("who");
            Assert.That(w.All(new[] { C(ConditionType.Conviction, "who"), C(ConditionType.Event, "gate_open") }), Is.True);
            Assert.That(new Condition { Type = ConditionType.Event, Id = "gate_open", Not = true }.Holds(w), Is.False);
        }

        [Test]
        public void Clues_SecretUntilShown()
        {
            var w = Village();
            Assert.That(w.Learn("c1", "rocco", secret: true), Is.True);
            Assert.That(w.Knows("rocco", "c1"), Is.True);
            Assert.That(w.Knows("gareth", "c1"), Is.False);
            Assert.That(w.SharedClues, Is.EqualTo(0));
            w.ShowClue("c1");
            Assert.That(w.Knows("gareth", "c1"), Is.True);
            Assert.That(w.SharedClues, Is.EqualTo(1));
            Assert.That(w.Learn("c1", "gareth", false), Is.False, "not new");
        }

        [Test]
        public void Distance_OverLinks()
        {
            var w = Village();
            Assert.That(w.Distance("inn", "shrine"), Is.EqualTo(2));
            Assert.That(w.Distance("inn", "moon"), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Mechanisms_RaiseEvents_LimitedUses()
        {
            var w = Village();
            var m = new Mechanism(new MechanismDef { Id = "sluice", Place = "shrine", Ability = Ability.Str, Event = "water_drained", Uses = 1 });
            Assert.That(m.Trigger(w), Is.True);
            Assert.That(w.Events, Has.Member("water_drained"));
            Assert.That(m.Trigger(w), Is.False);
        }
    }
}
