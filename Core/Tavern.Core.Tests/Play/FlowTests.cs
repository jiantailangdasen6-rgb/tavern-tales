using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Core.Story;
using Rolls = Tavern.Core.Play.FixedRandom;

namespace Tavern.Core.Tests.Play
{
    public class FlowTests
    {
        const string Ok = @"{""text"": ""ガレスとロッコの物語は続く。""}";

        static (Table table, ScriptedLlm llm, Evening e) New(string at = "village", int act = 1, params int[] rolls)
        {
            var s = StoryLoader.Load(File.ReadAllText(Path.Combine(PlayContent.Dir(), "Stories", "grey_shrine.json")));
            var party = new List<Adventurer>
            {
                new Adventurer(PlayContent.Classes["warrior"], "ガレス", "gareth"),
                new Adventurer(PlayContent.Classes["thief"], "ロッコ", "rocco"),
            };
            var e = Evening.Begin(s, party, Difficulty.Standard);
            e.World.Act = act;
            foreach (var p in party) e.World.Move(p.Id, at);
            var llm = new ScriptedLlm();
            var rng = new Rolls(rolls.Length > 0 ? rolls : new[] { 15 });
            var bot = new BotPlayers(rng, e.Find);
            var t = new Table(e, new Classifier(llm), new Narrator(llm), bot, rng, PlayContent.Enemies);
            return (t, llm, e);
        }

        static List<Declaration> D(params (string who, string text)[] d) => d.Select(x => new Declaration { Who = x.who, Text = x.text }).ToList();

        static string Cls(string declarations, string fight = "") =>
            @"{""declarations"": [" + declarations + "]" + (fight.Length > 0 ? @", ""fight"": " + fight : "") + "}";

        [Test]
        public async Task Explore_MoveAndFreeClue()
        {
            var (t, llm, e) = New("forest_path");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""獣道を調べる"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""hard"", ""clue"": ""c1_3""}]},
                {""who"": ""gareth"", ""steps"": [{""text"": ""小屋へ向かう"", ""sort"": ""auto"", ""move_to"": ""elda_hut""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("rocco", "獣道を調べる"), ("gareth", "小屋へ向かう")));
            Assert.That(e.World.Knows("gareth", "c1_3"), Is.True, "a free clue needs no roll (the sheet wins over the AI's hard check)");
            Assert.That(r.Lines, Has.Some.Contains("手がかり: 祠へ向かう獣道"));
            Assert.That(e.PlaceOf(e.Find("gareth")!), Is.EqualTo("elda_hut"));
            Assert.That(r.Scene, Is.EqualTo(SceneKind.NewPlace));
            Assert.That(r.Narration, Is.EqualTo("ガレスとロッコの物語は続く。"));
            Assert.That(r.Calls, Is.EqualTo(2));
        }

        [Test]
        public async Task Request_UsesTheAttitudeTable()
        {
            var (t, llm, e) = New("elda_hut", 1, 9);
            // Elda is wary: a normal request is hard (16); MND +1 with 9 fails, but the clue is important: it gets through with a cost
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""三日前の夜のことを聞く"", ""sort"": ""check"", ""ability"": ""MND"", ""tier"": ""easy"",
                ""person"": ""elda"", ""request"": ""normal"", ""approach"": ""ask"", ""clue"": ""c1_2"", ""important"": true, ""costs"": [""noticed""]}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "エルダに三日前の夜のことを聞く")));
            Assert.That(r.Lines.First(), Does.Contain("MND・難しい 16"));
            Assert.That(e.World.Knows("gareth", "c1_2"), Is.True);
            Assert.That(r.Lines, Has.Some.Contains("代償: 気付かれる"));
            Assert.That(e.World.Cast.Find("elda")!.Attitude, Is.EqualTo(Attitude.Hostile), "noticed: wary → hostile");
        }

        [Test]
        public async Task Mechanism_RaisesItsEvent_AndTheClockRewinds()
        {
            var (t, llm, e) = New("corridor", 2, 18);
            e.World.Clocks.Find("youths")!.Push(3);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""水門を回す"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""mechanism"": ""sluice""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "水門を回す")));
            Assert.That(e.World.Events, Has.Member("sluice_opened"));
            Assert.That(e.World.Clocks.Find("youths")!.Filled, Is.EqualTo(1), "3 - 2");
            Assert.That(r.Lines, Has.Some.Contains("「若者たちの命」が戻った"));
        }

        [Test]
        public async Task ActMoves_WhenThePartyReachesTheShrine()
        {
            var (t, llm, e) = New("forest_path");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""祠へ"", ""sort"": ""auto"", ""move_to"": ""shrine_front""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "祠へ向かう")));
            Assert.That(r.ActChanged, Is.True);
            Assert.That(e.World.Act, Is.EqualTo(2));
            Assert.That(r.Scene, Is.EqualTo(SceneKind.Climax), "an act start is told as a big moment");
        }

        [Test]
        public async Task Impossible_CanBeDeclaredAgain()
        {
            var (t, llm, e) = New("village");
            ((BotPlayers)t.Players).Redeclare = (who, why) => "広場を見回す";
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""死者を蘇らせる"", ""sort"": ""impossible"", ""impossible_why"": ""world_rule"", ""rule"": 1, ""how"": ""死者は戻らない""}]}"))
               .Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""見回す"", ""sort"": ""auto""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "死者を蘇らせる")));
            Assert.That(r.Lines, Has.Some.Contains("できない（世界の決まり 1"));
            Assert.That(r.Calls, Is.EqualTo(3), "classify, classify again, narrate");
        }

        [Test]
        public async Task Contest_OverTheSameCandle()
        {
            var (t, llm, e) = New("altar", 2, 5, 17);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""燭台をつかむ"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""item"": ""candle_a"", ""take"": true}]},
                {""who"": ""rocco"", ""steps"": [{""text"": ""燭台を懐に"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""item"": ""candle_a"", ""take"": true}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "燭台をつかむ"), ("rocco", "燭台を懐に入れる")));
            Assert.That(r.Lines, Has.Some.StartWith("対抗:"));
            Assert.That(e.World.WhereIs("candle_a"), Is.EqualTo("pc:rocco"), "17 + 3 beats 5 + 3");
            Assert.That(e.Find("rocco")!.Gear.Has("candle_a"), Is.True);
            Assert.That(e.Find("gareth")!.Gear.Has("candle_a"), Is.False);
        }

        [Test]
        public async Task Secret_TakeWithWatchers()
        {
            var (t, llm, e) = New("crypt", 3, 15, 15);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""指輪をくすねる"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""item"": ""grave_ring"", ""take"": true}], ""cover"": ""辺りを警戒する""}"))
               .Tool(Narrator.ToolName, @"{""text"": ""ロッコは辺りを警戒した。"", ""private"": [{""who"": ""rocco"", ""text"": ""指輪は懐に収まった。""}]}");
            var r = await t.ExploreAsync(new List<Declaration> { new Declaration { Who = "rocco", Text = "副葬品の指輪をくすねる", Secret = true } });
            var ring = e.Find("rocco")!.Gear.Find("grave_ring");
            Assert.That(ring!.Concealed, Is.True);
            Assert.That(r.Lines, Has.None.Contains("指輪"), "nothing public about it");
            Assert.That(r.PrivateLines["rocco"], Has.Some.Contains("見つかる判定"));
            Assert.That(r.PrivateNarration["rocco"], Is.EqualTo("指輪は懐に収まった。"));
            Assert.That(t.Secrets.Single().Found, Is.False, "15 + 2 beats 10 + Gareth's INT 0");
        }

        [Test]
        public async Task Deduction_WithTwoCluesIsAConviction()
        {
            var (t, llm, e) = New("shrine_front", 2);
            e.World.Learn("c2_1", "gareth", false);
            e.World.Learn("c2_2", "gareth", false);
            e.World.Clocks.Find("anger")!.Push(2);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""推理を話す"", ""sort"": ""auto"", ""conclusion"": ""c2"", ""correct"": true}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "燭台を動かしたから守り手が目覚めたんだ")));
            Assert.That(e.World.Convictions, Has.Member("c2"));
            Assert.That(r.Lines, Has.Some.StartWith("確信:"));
            Assert.That(e.World.Clocks.Find("anger")!.Filled, Is.EqualTo(1));
        }

        [Test]
        public async Task WholeClimax_FightToTheEnding()
        {
            // the party opens the cell; the knight and a skeleton come; the party wins with good rolls; the afterword ends the story
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            Assert.That(r.ClimaxStarted, Is.True);
            Assert.That(r.CombatStarted, Is.True);
            Assert.That(t.Phase, Is.EqualTo(Phase.Combat));
            Assert.That(e.Combat!.Foes.Select(f => f.Source), Is.EquivalentTo(new[] { "boneknight", "skeleton" }));

            var guard = 0;
            while (t.Phase == Phase.Combat && guard++ < 20)
            {
                var targets = e.Combat!.Foes.Where(f => f.InFight).ToList();
                llm.Tool(Classifier.ToolName, Cls(string.Join(",", new[] { "gareth", "rocco" }.Select(w =>
                    $@"{{""who"": ""{w}"", ""steps"": [{{""text"": ""斬る"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""combat_act"": ""attack"", ""target"": ""{targets[0].Id}""}}]}}"))))
                   .Tool(Narrator.ToolName, Ok);
                await t.ExploreAsync(D(("gareth", "斬る"), ("rocco", "斬る")));
            }
            Assert.That(t.SolvedBy, Is.EqualTo("defeat"));
            Assert.That(t.Phase, Is.EqualTo(Phase.Afterword));
            Assert.That(e.World.Events, Has.Member("boss_defeated"));

            // the afterword: they take Tom and Rina along (both escorted) and the story ends
            e.World.Cast.Find("tom")!.WithParty = true;
            e.World.Cast.Find("rina")!.WithParty = true;
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""村へ帰る"", ""sort"": ""auto""}]}"))
               .Tool(Narrator.ToolName, @"{""text"": ""ガレスとロッコは若者たちを連れて村へ帰った。""}");
            var end = await t.ExploreAsync(D(("gareth", "若者たちを連れて村へ帰る")));
            Assert.That(t.Phase, Is.EqualTo(Phase.Over));
            Assert.That(end.Ending!.Card, Is.EqualTo(Card.Success));
            Assert.That(end.Narration, Does.Contain("村へ帰った"));
        }

        [Test]
        public void Ending_HeavyLossIsBitter_GoalMissedIsFailure()
        {
            var (t, _, e) = New("dungeon", 3);
            e.World.Cast.Find("tom")!.WithParty = true;
            e.World.Cast.Find("rina")!.WithParty = true;
            e.Find("rocco")!.Kill();
            var r = t.Abandon();
            Assert.That(r.Ending!.Card, Is.EqualTo(Card.Failure), "leaving the map fails the request");
            var (t2, _, e2) = New("dungeon", 3);
            e2.World.Cast.Find("tom")!.WithParty = true;
            e2.World.Cast.Find("rina")!.WithParty = true;
            e2.Find("rocco")!.Kill();
            t2.PersonalGoals["gareth"] = "pg_tom";
            var report = new TurnReport();
            typeof(Table).GetMethod("Finish", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(t2, new object[] { EndingTrigger.Solved, report });
            Assert.That(report.Ending!.Card, Is.EqualTo(Card.Bitter), "a dead adventurer is always a heavy loss");
            Assert.That(report.Ending.Personal["gareth"].done, Is.True);
        }
    }
}
