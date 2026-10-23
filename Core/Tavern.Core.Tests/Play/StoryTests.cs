using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Tests.Play
{
    public class StoryTests
    {
        static string ShrineJson() => File.ReadAllText(Path.Combine(PlayContent.Dir(), "Stories", "grey_shrine.json"));

        static string[] Errors(StoryDef s) => StoryCheck.Run(s, PlayContent.Enemies).Where(p => p.Error).Select(p => p.Message).ToArray();

        static StoryDef Mutated(System.Action<JObject> change)
        {
            var o = JObject.Parse(ShrineJson());
            change(o);
            return StoryLoader.Load(o.ToString());
        }

        [Test]
        public void EveryStorySheet_PassesTheChecker()
        {
            foreach (var file in Directory.GetFiles(Path.Combine(PlayContent.Dir(), "Stories"), "*.json"))
            {
                var s = StoryLoader.Load(File.ReadAllText(file));
                Assert.That(Errors(s), Is.Empty, Path.GetFileName(file));
            }
        }

        [Test]
        public void Shrine_ReadsAsWritten()
        {
            var s = StoryLoader.Load(ShrineJson());
            Assert.That(s.Conclusions.Count, Is.EqualTo(4));
            Assert.That(s.Clues.Count, Is.EqualTo(12));
            Assert.That(s.Clocks.Select(c => c.Def.Id), Is.EqualTo(new[] { "youths", "anger" }));
            Assert.That(s.ClockOf("anger")!.Def.Acts, Is.EqualTo(new[] { 2, 3 }));
            Assert.That(s.ClueOf("c2_2")!.Ability, Is.EqualTo(Ability.Int));
            Assert.That(s.ClueOf("c2_2")!.Tier, Is.EqualTo(Tier.Easy));
            Assert.That(s.ClueOf("c4_2")!.Requires.IsEmpty, Is.False);
            Assert.That(s.PersonOf("tom")!.Escort, Is.True);
            Assert.That(s.Acts[0].Advance.Groups.Count, Is.EqualTo(2), "conviction OR reaching the shrine");
            Assert.That(s.Climax.Solutions.Select(x => x.Id), Is.EqualTo(new[] { "defeat", "sleep", "escape" }));
            Assert.That(s.PlaceOf("corridor")!.Tense, Is.True);
        }

        [Test]
        public void Checker_FindsMissingClue()
        {
            var s = Mutated(o => ((JArray)o["clues"]!).RemoveAt(0));
            Assert.That(Errors(s), Has.Some.Contains("c1_1"));
        }

        [Test]
        public void Checker_WantsAPlaceConditionInEachAct()
        {
            var s = Mutated(o => o["acts"]![1]!["advance"] = JArray.Parse("[[{\"event\": \"cell_open\"}]]"));
            Assert.That(Errors(s), Has.Some.Contains("幕 2 の進む条件に場所の条件がない"));
        }

        [Test]
        public void Checker_FindsEventsNothingRaises()
        {
            var s = Mutated(o => o["goal"]!["when"] = JObject.Parse("{\"event\": \"dragon_slain\"}"));
            Assert.That(Errors(s), Has.Some.Contains("dragon_slain"));
        }

        [Test]
        public void Checker_FindsUnreachablePlacesAndBadLinks()
        {
            var s = Mutated(o =>
            {
                var village = ((JArray)o["places"]!).First(p => (string?)p["id"] == "village");
                village["links"] = JArray.Parse("[\"tavern\", \"moon\"]");
            });
            var errors = Errors(s);
            Assert.That(errors, Has.Some.Contains("moon"));
            Assert.That(errors, Has.Some.Contains("たどり着けない"));
        }

        [Test]
        public void Checker_FindsUnknownEnemiesAndBadItems()
        {
            var s = Mutated(o =>
            {
                ((JArray)((JArray)o["places"]!)[2]!["enemies"]!).Add("dragon");
                ((JArray)o["items"]!).Add(JObject.Parse("{\"id\": \"blade\", \"kind\": \"Weapon\", \"weapon\": \"Sword\", \"grade\": \"Masterwork\", \"at\": \"crypt\"}"));
            });
            var errors = Errors(s);
            Assert.That(errors, Has.Some.Contains("dragon"));
            Assert.That(errors, Has.Some.Contains("名品は +2 と特性 1 つ"));
        }

        [Test]
        public void Conditions_ReadInAllForms()
        {
            var w = StoryLoader.ReadWhen(JToken.Parse("[[{\"event\": \"a\"}], [{\"place\": \"inn\", \"who\": \"self\"}, {\"clock\": \"c\", \"atLeast\": 2}]]"), "t");
            Assert.That(w.Groups.Count, Is.EqualTo(2));
            Assert.That(w.Groups[1][0].Value, Is.EqualTo("self"));
            Assert.That(w.Groups[1][1].Count, Is.EqualTo(2));
            var world = new World();
            world.Move("rocco", "inn");
            world.Clocks.All.Add(new Clock(new ClockDef { Id = "c", Segments = 4 }));
            world.Clocks.Find("c")!.Push(2);
            Assert.That(w.Holds(world, "rocco"), Is.True);
            Assert.That(w.Holds(world, "gareth"), Is.False);
            Assert.Throws<System.FormatException>(() => StoryLoader.ReadCondition(JObject.Parse("{\"person\": \"x\", \"is\": \"flying\"}"), "t"));
        }

        [Test]
        public void PersonalGoals_CountOnlyTheOwnersThings()
        {
            var s = StoryLoader.Load(ShrineJson());
            var fortune = s.PersonalGoals.First(g => g.Id == "pg_fortune");
            var w = new World();
            w.ItemKinds["candle_a"] = ItemKind.Treasure;
            w.PutItem("candle_a", World.Pc("rocco"));
            Assert.That(fortune.When.Holds(w, "rocco"), Is.True);
            Assert.That(fortune.When.Holds(w, "gareth"), Is.False);
        }
    }
}
