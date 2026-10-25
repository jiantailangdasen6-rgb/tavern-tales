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
            e.SettleOpening();
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
        public async Task Fight_ASpellMarkedAsAnItem_IsCastAnyway()
        {
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            var foe = e.Combat!.Foes.First(f => f.InFight).Id;
            llm.Tool(Classifier.ToolName, Cls($@"{{""who"": ""gareth"", ""steps"": [{{""text"": ""眠りの霧で動きを止める"", ""sort"": ""check"", ""ability"": ""INT"", ""tier"": ""hard"",
                ""combat_act"": ""item"", ""effect"": ""hold"", ""effect_ability"": ""INT"", ""target"": ""{foe}""}}]}}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "眠りの霧で動きを止める")));
            Assert.That(r.Lines, Has.None.Contains("使える物を持っていない"));
        }

        [Test]
        public async Task TheClientWaitsInTheTavern_AndGoesHomeWhenThePartyLeaves()
        {
            var (t, llm, e) = New("tavern");
            Assert.That(e.PeopleAt("tavern").Select(p => p.Id), Does.Contain("elder"), "the opening has him walk into the tavern");
            e.World.Move("rocco", "village");   // gone ahead
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""村へ行く"", ""sort"": ""auto"", ""move_to"": ""village""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("gareth", "村へ行く")));
            Assert.That(e.PlaceOf(e.Find("gareth")!), Is.EqualTo("village"));
            Assert.That(e.World.Cast.Find("elder")!.Location, Is.EqualTo("village"));
        }

        [Test]
        public async Task AnObjectTheStoryNeverListed_GoesInTheBagWithNoEffect()
        {
            var (t, llm, e) = New("tavern");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""村長の杖を奪う"", ""sort"": ""auto"", ""take"": true, ""new_item"": ""オズリックの杖""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "村長の杖を奪う")));
            var staff = e.Find("gareth")!.Gear.Items.FirstOrDefault(i => i.Name == "オズリックの杖");
            Assert.That(staff, Is.Not.Null, string.Join(" / ", r.Lines));
            Assert.That(staff!.Kind, Is.EqualTo(ItemKind.Story));
            Assert.That(staff.Bonus, Is.EqualTo(0));
            Assert.That(r.Lines, Has.Some.Contains("効果のない品"));
        }

        [Test]
        public async Task AListedThingNamedInWords_IsThatThing_NotASecondOne()
        {
            var (t, llm, e) = New("altar", 2);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""祭壇の燭台を手に取る"", ""sort"": ""auto"", ""take"": true, ""new_item"": ""金の燭台""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("gareth", "祭壇の燭台を手に取る")));
            var gear = e.Find("gareth")!.Gear.Items;
            Assert.That(gear.Any(i => i.StoryId == "candle_a"), Is.True, string.Join(",", gear.Select(i => i.Id)));
            Assert.That(gear.Any(i => i.Id.StartsWith("found_")), Is.False);
        }

        [Test]
        public async Task AnUnlistedThingNamedInTheItemField_IsStillTaken()
        {
            var (t, llm, e) = New("tavern");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""村長の杖を奪う"", ""sort"": ""auto"", ""item"": ""杖""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("gareth", "村長の杖を奪う")));
            Assert.That(e.Find("gareth")!.Gear.Items.Any(i => i.Name == "杖"), Is.True);
        }

        [Test]
        public async Task LongStory_PlacesOpenOneActAhead()
        {
            var (t, llm, e) = New("village", 1);
            e.Story.Long = true;
            e.Story.PlaceOf("forest_path")!.Acts.Clear();
            e.Story.PlaceOf("forest_path")!.Acts.Add(3);
            Assert.That(e.OpenInAct("forest_path"), Is.False);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""森の小道へ行く"", ""sort"": ""auto"", ""move_to"": ""forest_path""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("gareth", "森の小道へ行く")));
            Assert.That(e.PlaceOf(e.Find("gareth")!), Is.EqualTo("village"));
            e.World.Act = 2;
            Assert.That(e.OpenInAct("forest_path"), Is.True);
        }

        [Test]
        public async Task Fight_WhoFellGetsUpWhenItIsWon()
        {
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            var rocco = e.Find("rocco")!;
            rocco.TakeDamage(rocco.Hp);
            Assert.That(rocco.Life, Is.EqualTo(Life.Down));
            var guard = 0;
            while (t.Phase == Phase.Combat && guard++ < 30)
            {
                var foe = e.Combat!.Foes.First(f => f.InFight).Id;
                llm.Tool(Classifier.ToolName, Cls($@"{{""who"": ""gareth"", ""steps"": [{{""text"": ""斬る"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""combat_act"": ""attack"", ""target"": ""{foe}""}}]}}"))
                   .Tool(Narrator.ToolName, Ok);
                await t.ExploreAsync(D(("gareth", "斬る")));
            }
            Assert.That(e.Combat, Is.Null);
            Assert.That(rocco.Life, Is.EqualTo(Life.Up));
            Assert.That(rocco.Hp, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task Fight_AnAllyWhoFell_BringsTensionOnceItIsOver()
        {
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            var rocco = e.Find("rocco")!;
            rocco.TakeDamage(rocco.Hp);
            var guard = 0;
            var duringFight = false;
            while (t.Phase == Phase.Combat && guard++ < 30)
            {
                var foe = e.Combat!.Foes.First(f => f.InFight).Id;
                llm.Tool(Classifier.ToolName, Cls($@"{{""who"": ""gareth"", ""steps"": [{{""text"": ""斬る"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""combat_act"": ""attack"", ""target"": ""{foe}""}}]}}"))
                   .Tool(Narrator.ToolName, Ok);
                await t.ExploreAsync(D(("gareth", "斬る")));
                if (e.Combat != null) duringFight |= e.Tension.On;
            }
            Assert.That(e.Combat, Is.Null);
            Assert.That(duringFight, Is.False, "the fight's own pace comes first");
            Assert.That(e.Tension.On, Is.True, "the fallen are carried on under tension");
            Assert.That(e.Tension.AfterFight, Is.False);
        }

        [Test]
        public async Task ASecretClue_ToldToTheOthers_BecomesTheParty()
        {
            var (t, llm, e) = New("village", 1, 2);
            e.World.Learn("c1_3", "rocco", secret: true);
            Assert.That(e.World.Knows("gareth", "c1_3"), Is.False);
            // told somewhere else, and the AI asks for a roll: neither stops it (and the roll of 2 would fail)
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""見つけたことを皆に話す"", ""sort"": ""check"", ""ability"": ""MND"", ""tier"": ""normal"", ""clue"": ""c1_3""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("rocco", "見つけたことを皆に話す")));
            Assert.That(e.World.Knows("gareth", "c1_3"), Is.True);
            Assert.That(r.Lines, Has.Some.StartWith("手がかりを皆に明かした"));
        }

        [Test]
        public async Task Fight_AnItemCanBePutDown()
        {
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            e.World.PutItem("candle_b", World.Pc("gareth"));
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""燭台を床に置く"", ""sort"": ""auto"", ""combat_act"": ""other"", ""item"": ""candle_b"", ""place_at"": ""dungeon""}]}"))
               .Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "燭台を床に置く")));
            Assert.That(e.World.WhereIs("candle_b"), Is.EqualTo("dungeon"));
            Assert.That(r.Lines, Has.Some.Contains("金の燭台を置いた"));
        }

        [Test]
        public async Task SaveAndLoad_InTheMiddleOfAFight()
        {
            var (t, llm, e) = New("dungeon", 2, 19);
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""rocco"", ""steps"": [{""text"": ""錠前を開ける"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""mechanism"": ""cell_lock""}]}"))
               .Tool(Narrator.ToolName, Ok);
            await t.ExploreAsync(D(("rocco", "錠前を開ける")));
            e.Find("gareth")!.TakeDamage(5);
            e.Find("rocco")!.Gear.Add(new Item { Id = "rope_1", Name = "ロープ", Kind = ItemKind.Tool });
            e.Find("rocco")!.AddTechnique("柱蹴りの一閃", "跳ぶ", EffectKind.Strike, EffectSize.Medium, Ability.Dex);
            e.Combat!.Foes.First().TakeDamage(4);
            var json = SaveGame.From(t).ToJson();
            Assert.That(SaveGame.IsCurrent(json), Is.True);

            var back = SaveGame.FromJson(json).Load(e.Story, PlayContent.Classes, PlayContent.Enemies, new Classifier(llm), new Narrator(llm), t.Players);
            var e2 = back.E;
            Assert.That(back.Phase, Is.EqualTo(Phase.Combat));
            Assert.That(e2.Find("gareth")!.Hp, Is.EqualTo(13));
            Assert.That(e2.Find("rocco")!.Gear.Has("rope_1"), Is.True);
            Assert.That(e2.Find("rocco")!.Attack().DualWield, Is.True, "slots come back");
            Assert.That(e2.Find("rocco")!.FindTechnique("柱蹴りの一閃"), Is.Not.Null);
            Assert.That(e2.World.Events, Has.Member("cell_open"));
            Assert.That(e2.ClimaxStarted, Is.True);
            Assert.That(e2.Combat!.Foes.Select(f => f.Hp), Is.EqualTo(e.Combat.Foes.Select(f => f.Hp)));
            Assert.That(e2.World.Cast.Find("baldric")!.Fighting, Is.Not.Null, "the boss is linked to its fighter again");
            Assert.That(e2.Mechanisms["cell_lock"].Spent, Is.True);
            var first = Newtonsoft.Json.Linq.JObject.Parse(json); var again = Newtonsoft.Json.Linq.JObject.Parse(SaveGame.From(back).ToJson());
            first.Remove("RngState"); again.Remove("RngState");   // the test rolls are fixed, not a seeded generator
            Assert.That(Newtonsoft.Json.Linq.JToken.DeepEquals(first, again), Is.True, "a loaded game saves the same");
        }

        [Test]
        public void OldSaves_AreMovedAway()
        {
            var dir = Path.Combine(Path.GetTempPath(), "tavern_old_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "evening.json"), "{\"Version\": 1}");
                File.WriteAllText(Path.Combine(dir, "roster.json"), "{\"Version\": 2}");
                Assert.That(SaveGame.MoveOldAway(dir, new[] { "evening.json", "roster.json", "missing.json" }), Is.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(dir, "old", "evening.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(dir, "roster.json")), Is.True);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public async Task Handouts_OneCallForEveryone_GoalsFromTheSheet()
        {
            var (t, llm, e) = New();
            llm.Tool("handouts", @"{""handouts"": [{""who"": ""gareth"", ""bond"": ""村長は恩人だ"", ""goal"": ""pg_debt"", ""fragment"": 1},
                {""who"": ""rocco"", ""bond"": ""トムは弟分"", ""goal"": ""pg_debt""}]}");
            var hs = await t.DealHandoutsAsync(new Bookends(llm));
            Assert.That(hs.Count, Is.EqualTo(2));
            Assert.That(t.PersonalGoals["gareth"], Is.EqualTo("pg_debt"));
            Assert.That(t.PersonalGoals["rocco"], Is.Not.EqualTo("pg_debt"), "the same goal is not dealt twice");
            Assert.That(hs.First(h => h.Who == "gareth").Fragment, Does.StartWith("古い戦記の写本"));
        }

        [Test]
        public async Task WrapUp_NamesTheMarks()
        {
            var (t, llm, e) = New("dungeon", 3);
            e.World.Cast.Find("tom")!.WithParty = true;
            e.World.Cast.Find("rina")!.WithParty = true;
            e.Find("rocco")!.Gear.Add(new Item { Id = "candle_b", Name = "金の燭台", Kind = ItemKind.Treasure });
            t.Abandon();
            llm.Tool("chronicle", @"{""title"": ""灰色の森の祠"", ""story"": ""一行は…"", ""rumors"": [""祠に灯がともったらしい""],
                ""bonds"": [{""kind"": ""解けずに残った謎"", ""text"": ""騎士の名""}, {""kind"": ""逃がした敵"", ""text"": ""なし""}],
                ""marks"": [{""who"": ""gareth"", ""name"": ""森の霧を抜けた""}, {""who"": ""ロッコ"", ""name"": ""燭台を抱えて帰った""}]}");
            var c = await t.WrapUpAsync(new Bookends(llm));
            Assert.That(c.Rumors.Single(), Is.EqualTo("祠に灯がともったらしい"));
            Assert.That(c.Bonds.Select(b => b.Kind), Is.EqualTo(new[] { "解けずに残った謎" }), "only kinds the game offered");
            Assert.That(e.Find("gareth")!.Marks.Single().Name, Is.EqualTo("森の霧を抜けた"));
            Assert.That(e.Find("rocco")!.Marks.Single().Name, Is.EqualTo("燭台を抱えて帰った"), "the AI may name the adventurer instead of the id");
            Assert.That(llm.Seen.Last().Last().Content, Does.Contain("持ち帰った宝: ロッコの金の燭台"), "the chronicle is told what was brought home, not left to guess");
        }

        [Test]
        public async Task EveryoneAbandoning_LeavesTheMap()
        {
            var (t, llm, e) = New("village");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""去る"", ""sort"": ""auto"", ""abandon"": true}]},
                {""who"": ""rocco"", ""steps"": [{""text"": ""去る"", ""sort"": ""auto"", ""abandon"": true}]}"))
               .Tool(Narrator.ToolName, @"{""text"": ""ガレスとロッコは村を去った。""}");
            var r = await t.ExploreAsync(D(("gareth", "依頼を捨てて去る"), ("rocco", "依頼を捨てて去る")));
            Assert.That(r.Ending!.Trigger, Is.EqualTo(EndingTrigger.LeftMap));
            Assert.That(r.Ending.Card, Is.EqualTo(Card.Failure));
            Assert.That(t.Phase, Is.EqualTo(Phase.Over));
        }

        [Test]
        public async Task LongStory_PausesAfterActThree_AndResumes()
        {
            var (t, llm, e) = New("dungeon", 3);
            e.Story.Long = true;
            e.Story.Acts.Add(new Act { N = 4, Title = "四" });
            e.Story.Acts.Add(new Act { N = 5, Title = "五" });
            e.Story.Acts[2].Advance = StoryLoader.ReadWhen(Newtonsoft.Json.Linq.JToken.Parse("[{\"place\": \"dungeon\"}]"), "t");
            e.Story.Climax.Starts = StoryLoader.ReadWhen(Newtonsoft.Json.Linq.JToken.Parse("[{\"event\": \"never\"}]"), "t");
            llm.Tool(Classifier.ToolName, Cls(@"{""who"": ""gareth"", ""steps"": [{""text"": ""見回す"", ""sort"": ""auto""}]}")).Tool(Narrator.ToolName, Ok);
            var r = await t.ExploreAsync(D(("gareth", "見回す")));
            Assert.That(t.Phase, Is.EqualTo(Phase.Break));
            Assert.That(r.Lines, Has.Some.StartWith("中休み"));
            Assert.That(e.World.Clocks.All.All(c => c.Paused), Is.True);
            var coins = e.Find("gareth")!.FateCoins;
            var newcomer = new Adventurer(PlayContent.Classes["priest"], "ミーナ", "mina");
            var next = t.ResumeAfterBreak(new[] { newcomer }, new[] { "rocco" });
            Assert.That(t.Phase, Is.EqualTo(Phase.Explore));
            Assert.That(e.Find("gareth")!.FateCoins, Is.EqualTo(coins + 1));
            Assert.That(e.Find("gareth")!.Marks.Count, Is.EqualTo(1));
            Assert.That(e.PlaceOf(newcomer), Is.EqualTo("dungeon"));
            Assert.That(e.Find("rocco")!.Withdrawn, Is.True);
            Assert.That(e.World.Clocks.All.Any(c => c.Paused), Is.False);
            Assert.That(next.Lines, Has.Some.Contains("四"), "the next act is announced");
            Assert.That(t.Untold, Is.Not.Empty, "the next night opens with a telling");
            llm.Tool(Narrator.ToolName, Ok);
            var told = await t.TellUntoldAsync();
            Assert.That(told.Narration, Is.Not.Empty);
            Assert.That(t.Untold, Is.Empty);
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

        [Test]
        public void OnlineView_ShowsOnlyWhatTheReaderMaySee()
        {
            var (t, _, e) = New("village");
            e.World.Learn("c1_1", "gareth", secret: true);
            e.World.Learn("c1_2", "rocco", secret: true);
            e.World.Learn("c1_3", "rocco", secret: false);
            t.Handouts.Add(new Handout { Who = "gareth", Bond = "ガレスの縁" });
            t.Handouts.Add(new Handout { Who = "rocco", Bond = "ロッコの縁" });
            t.Secrets.Add(new SecretRecord { Who = "rocco", Text = "銀貨をくすねる" });
            t.PersonalGoals["rocco"] = "g1";
            for (int i = 0; i < 80; i++) t.Log.Add("行 " + i);

            var mine = new[] { "gareth" };
            var s = OnlineView.SaveFor(t, mine);
            Assert.That(s.Handouts.Select(h => h.Who), Is.EqualTo(new[] { "gareth" }));
            Assert.That(s.Secrets, Is.Empty);
            Assert.That(s.PersonalGoals, Is.Empty);
            Assert.That(s.Clues.Keys, Is.EquivalentTo(new[] { "c1_1", "c1_3" }), "rocco's secret clue is not sent to gareth's player");
            Assert.That(s.Log, Has.Count.EqualTo(OnlineView.LogLines));
            Assert.That(s.RngState, Is.EqualTo(0UL));

            var back = OnlineView.Read<SaveGame>(OnlineView.Json(s));
            Assert.That(back.Party.Select(p => p.Id), Is.EqualTo(new[] { "gareth", "rocco" }));

            var r = new TurnReport { Narration = "語り" };
            r.Lines.Add("みんなの行");
            r.Private("rocco", "ロッコだけの行");
            r.PrivateNarration["rocco"] = "ロッコだけの語り";
            var forGareth = OnlineView.Read<ReportMsg>(OnlineView.Json(ReportMsg.For(r, mine))).ToReport();
            Assert.That(forGareth.Lines, Is.EqualTo(new[] { "みんなの行" }));
            Assert.That(forGareth.PrivateLines, Is.Empty);
            Assert.That(forGareth.PrivateNarration, Is.Empty);
            Assert.That(ReportMsg.For(r, new[] { "rocco" }).PrivateNarration["rocco"], Is.EqualTo("ロッコだけの語り"));
        }

        [Test]
        public void OnlineView_EndingSurvivesTheTrip()
        {
            var end = new EndingResult { Card = Card.Bitter };
            end.Personal["gareth"] = ("村を守る", true);
            end.Lost.Add("★トム");
            var back = OnlineView.Read<EndMsg>(OnlineView.Json(new EndMsg { Ending = end })).Ending;
            Assert.That(back.Card, Is.EqualTo(Card.Bitter));
            Assert.That(back.Personal["gareth"].done, Is.True);
            Assert.That(back.Lost, Is.EqualTo(new[] { "★トム" }));
        }
    }
}
