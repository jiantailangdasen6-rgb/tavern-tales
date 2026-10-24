using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tavern.Core.Ai;
using Tavern.Core.Gm;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Tests.Play
{
    /// <summary>A backend that answers with queued tool arguments (and counts the calls).</summary>
    sealed class ScriptedLlm : ILlmClient
    {
        readonly Queue<LlmReply> _replies = new Queue<LlmReply>();
        public List<IReadOnlyList<ChatMessage>> Seen { get; } = new List<IReadOnlyList<ChatMessage>>();
        public string Name => "scripted";
        public ScriptedLlm Tool(string name, string json) { _replies.Enqueue(new LlmReply("", new[] { new ToolCall(name, JObject.Parse(json)) })); return this; }
        public ScriptedLlm Text(string text) { _replies.Enqueue(new LlmReply(text, new ToolCall[0])); return this; }
        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            Seen.Add(messages);
            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : new LlmReply("", new ToolCall[0]));
        }
    }

    public class AiTests
    {
        static Evening Shrine(string at = "village", int act = 1)
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
            return e;
        }

        static List<Declaration> Decl(params (string who, string text)[] d) => d.Select(x => new Declaration { Who = x.who, Text = x.text }).ToList();

        [Test]
        public void Evening_BuildsTheWorldFromTheSheet()
        {
            var e = Shrine();
            Assert.That(e.World.Cast.Find("tom")!.Location, Is.EqualTo("dungeon"));
            Assert.That(e.World.WhereIs("candle_a"), Is.EqualTo("altar"));
            Assert.That(e.World.Clocks.Find("youths")!.Visible, Is.True);
            Assert.That(e.World.Clocks.Find("anger")!.Visible, Is.False);
            e.World.Events.Add("clue:c2_2");
            Assert.That(e.RevealClocks().Single().Id, Is.EqualTo("anger"));
            Assert.That(e.CluesHere("village").Select(c => c.Id), Is.EquivalentTo(new[] { "c1_1", "c3_1" }));
            Assert.That(e.CluesHere("elda_hut").Select(c => c.Id), Is.EqualTo(new[] { "c1_2" }), "c4_2 needs Elda friendly");
            e.World.Cast.Find("elda")!.Attitude = Attitude.Friendly;
            Assert.That(e.CluesHere("elda_hut").Count(), Is.EqualTo(2));
        }

        [Test]
        public void SceneText_ShowsWhatIsHere()
        {
            var e = Shrine("elda_hut");
            var text = SceneText.All(e);
            Assert.That(text, Does.Contain("[elda] 猟師エルダ: 態度 警戒"));
            Assert.That(text, Does.Contain("[c1_2]"));
            Assert.That(text, Does.Not.Contain("[c4_2]"), "not findable yet");
            Assert.That(text, Does.Contain("[youths] 若者たちの命（裏: 地下牢の水位）0/6"));
            Assert.That(text, Does.Not.Contain("[anger]"), "the anger clock does not run in act 1");
        }

        [Test]
        public async Task Classifier_ReadsStepsAndTargets()
        {
            var e = Shrine("corridor", 2);
            var llm = new ScriptedLlm().Tool(Classifier.ToolName, @"{""declarations"": [
                {""who"": ""gareth"", ""steps"": [{""text"": ""水門を回す"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal"", ""mechanism"": ""sluice"", ""important"": true, ""costs"": [""noticed"", ""clock""]}]},
                {""who"": ""rocco"", ""steps"": [{""text"": ""水底を探る"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal"", ""clue"": ""c3_3"", ""item"": ""moon_stone""}]}
            ]}");
            var c = await new Classifier(llm).ClassifyAsync(e, Decl(("gareth", "水門を回して開ける"), ("rocco", "水底を探る")), false);
            var g = c.For("gareth")!.Steps.Single();
            Assert.That(g.Mechanism, Is.EqualTo("sluice"));
            Assert.That(g.Check!.Important, Is.True);
            Assert.That(g.Check.Costs, Is.EqualTo(new[] { Cost.Noticed, Cost.Clock }));
            var r = c.For("rocco")!.Steps.Single();
            Assert.That(r.Clue, Is.EqualTo("c3_3"));
            Assert.That(r.Item, Is.Empty, "unknown ids are dropped");
            Assert.That(c.Dropped, Has.Some.Contains("moon_stone"));
            Assert.That(c.Calls, Is.EqualTo(1));
            Assert.That(llm.Seen[0][0].Content, Does.Contain("世界の決まり: 1. 死を覆す"), "the rules are filled into the prompt");
        }

        [Test]
        public async Task Classifier_MissingDeclaration_BecomesAPlainSuccess()
        {
            var e = Shrine();
            var llm = new ScriptedLlm().Tool(Classifier.ToolName, @"{""declarations"": [{""who"": ""gareth"", ""steps"": [{""text"": ""見回す"", ""sort"": ""auto""}]}]}");
            var c = await new Classifier(llm).ClassifyAsync(e, Decl(("gareth", "見回す"), ("rocco", "あくびをする")), false);
            Assert.That(c.For("rocco")!.Missing, Is.True);
            Assert.That(c.For("rocco")!.Steps.Single().Sort, Is.EqualTo(Sort.Auto));
        }

        [Test]
        public async Task Classifier_UnreadableAnswer_IsAskedOnceMore()
        {
            var e = Shrine();
            var llm = new ScriptedLlm().Text("すみません").Tool(Classifier.ToolName, @"{""declarations"": [{""who"": ""gareth"", ""steps"": [{""text"": ""見回す"", ""sort"": ""auto""}]}]}");
            var c = await new Classifier(llm).ClassifyAsync(e, Decl(("gareth", "見回す")), false);
            Assert.That(c.Calls, Is.EqualTo(2));
            Assert.That(c.Retried, Is.True);
            Assert.That(c.For("gareth")!.Missing, Is.False);
        }

        [Test]
        public void Classifier_TakesANearMissOfAName_ButNotOfANumberedId()
        {
            var e = Shrine("village", 1);
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""rocco"", ""steps"": [
                {""text"": ""ヘレンに聞く"", ""sort"": ""auto"", ""person"": ""helne""},
                {""text"": ""子どもらに聞く"", ""sort"": ""auto"", ""person"": ""childern"", ""clue"": ""c1_9""}]}]}");
            var c = new Classification();
            Classifier.Read(e, Decl(("rocco", "…")), false, args, c);
            Assert.That(c.For("rocco")!.Steps[0].Person, Is.EqualTo("helen"));
            Assert.That(c.For("rocco")!.Steps[1].Person, Is.EqualTo("children"));
            Assert.That(c.For("rocco")!.Steps[1].Clue, Is.EqualTo(""));
        }

        [Test]
        public void Classifier_AnAttackNamingAPlaceFoe_StartsTheFight()
        {
            var e = Shrine("forest_path", 1);
            e.FoeNames["wolf"] = "灰色狼";
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [
                {""text"": ""灰色狼に剣で斬りかかる"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal""}]}]}");
            var c = new Classification();
            Classifier.Read(e, Decl(("gareth", "…")), false, args, c);
            Assert.That(c.Fight, Is.Not.Null);
            Assert.That(c.Fight!.Enemies, Has.Member("wolf"));
            // looking at them is no attack
            var look = new Classification();
            Classifier.Read(e, Decl(("gareth", "…")), false, JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [
                {""text"": ""灰色狼の足跡を調べる"", ""sort"": ""auto""}]}]}"), look);
            Assert.That(look.Fight, Is.Null);
            // a story person who fights, named by the end of a titled name
            var crypt = Shrine("crypt", 3);
            var atk = new Classification();
            Classifier.Read(crypt, Decl(("gareth", "…")), false, JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [
                {""text"": ""バルドリックに斬りかかる"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal""}]}]}"), atk);
            Assert.That(atk.Fight!.Enemies, Has.Member("person:baldric"));
        }

        [Test]
        public void Classifier_ADeductionSaidAloud_IsTakenEvenIfTheModelMissedIt()
        {
            var e = Shrine("village", 1);
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [
                {""text"": ""「若者たちは祠の地下にいるはずだ」と皆に話す"", ""sort"": ""check"", ""ability"": ""MND"", ""tier"": ""normal""}]},
                {""who"": ""rocco"", ""steps"": [{""text"": ""「若者たちは森の小屋にいる」と言う"", ""sort"": ""auto""}]}]}");
            var c = new Classification();
            Classifier.Read(e, Decl(("gareth", "…"), ("rocco", "…")), false, args, c);
            Assert.That(c.For("gareth")!.Steps[0].Conclusion, Is.EqualTo("c1"));
            Assert.That(c.For("gareth")!.Steps[0].Correct, Is.True);
            Assert.That(c.For("gareth")!.Steps[0].Sort, Is.EqualTo(Sort.Auto), "a deduction is never rolled");
            Assert.That(c.For("rocco")!.Steps[0].Conclusion, Is.EqualTo(""), "a wrong guess is not matched");
            // the model copied it without the brackets
            var bare = new Classification();
            Classifier.Read(e, Decl(("gareth", "…")), false, JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [
                {""text"": ""騎士は名を呼ばれると剣を止めると皆に話す"", ""sort"": ""check"", ""ability"": ""INT"", ""tier"": ""normal""}]}]}"), bare);
            Assert.That(bare.For("gareth")!.Steps[0].Conclusion, Is.EqualTo("c4"));
        }

        [Test]
        public void Classifier_LimitsChecksPerTurn()
        {
            var e = Shrine("corridor", 2);
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""rocco"", ""steps"": [
                {""text"": ""壁を調べる"", ""sort"": ""auto""},
                {""text"": ""水底を探る"", ""sort"": ""check"", ""ability"": ""DEX"", ""tier"": ""normal""},
                {""text"": ""水門を回す"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal""},
                {""text"": ""奥へ走る"", ""sort"": ""check"", ""ability"": ""AGI"", ""tier"": ""normal""},
                {""text"": ""叫ぶ"", ""sort"": ""auto""}]}]}");
            var explore = new Classification();
            Classifier.Read(e, Decl(("rocco", "…")), false, args, explore);
            Assert.That(explore.For("rocco")!.Steps.Count, Is.EqualTo(3));
            Assert.That(explore.For("rocco")!.Deferred, Is.EqualTo(new[] { "奥へ走る", "叫ぶ" }));
            var fight = new Classification();
            Classifier.Read(e, Decl(("rocco", "…")), true, args, fight);
            Assert.That(fight.For("rocco")!.Steps.Count(s => s.Sort == Sort.Check), Is.EqualTo(1));
        }

        [Test]
        public void Classifier_WorldRuleWithoutANumber_BecomesAnExtremeCheck()
        {
            var e = Shrine();
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [{""text"": ""大岩を投げる"", ""sort"": ""impossible"", ""impossible_why"": ""world_rule""}]}]}");
            var c = new Classification();
            Classifier.Read(e, Decl(("gareth", "…")), false, args, c);
            var s = c.For("gareth")!.Steps.Single();
            Assert.That(s.Sort, Is.EqualTo(Sort.Check));
            Assert.That(s.Check!.Tier, Is.EqualTo(Tier.Extreme));
        }

        [Test]
        public void Classifier_FightOnlyWithEnemiesOfThePlace()
        {
            var e = Shrine("forest_path");
            var args = JObject.Parse(@"{""declarations"": [{""who"": ""gareth"", ""steps"": [{""text"": ""斬りかかる"", ""sort"": ""check"", ""ability"": ""STR"", ""tier"": ""normal""}]}],
                ""fight"": {""enemies"": [""wolf"", ""wyvern""], ""surprise"": true}}");
            var c = new Classification();
            Classifier.Read(e, Decl(("gareth", "…")), false, args, c);
            Assert.That(c.Fight!.Enemies, Is.EqualTo(new[] { "wolf" }));
            Assert.That(c.Fight.Surprise, Is.True);
        }

        [Test]
        public async Task Narrator_RetellsOnceWhenTheWatcherFindsSomething()
        {
            var e = Shrine();
            var llm = new ScriptedLlm()
                .Tool(Narrator.ToolName, @"{""text"": ""ガレスは 3 ダメージを与えた。""}")
                .Tool(Narrator.ToolName, @"{""text"": ""ガレスの剣が閃き、ロッコは影から見守った。""}");
            var t = new TurnTelling { Scene = SceneKind.Explore };
            t.Names.AddRange(new[] { "ガレス", "ロッコ" });
            var r = await new Narrator(llm).TellAsync(e, t);
            Assert.That(r.Problems, Has.Some.Contains("ロッコ"));
            Assert.That(r.Problems, Has.Some.Contains("ダメージ"));
            Assert.That(r.Retold, Is.True);
            Assert.That(r.Calls, Is.EqualTo(2));
            Assert.That(r.Text, Does.Contain("ロッコ"));
            Assert.That(llm.Seen[1].Last().Content, Does.Contain("直してほしい点"));
            Assert.That(e.LastEndings.Single(), Is.EqualTo("ガレスの剣が閃き、ロッコは影から見守った。"));
        }

        [Test]
        public void Watcher_FindsSecretLeaksAndMissingPrivateTellings()
        {
            var e = Shrine();
            var t = new TurnTelling();
            t.Names.Add("ロッコ");
            t.SecretWords.Add("銀の指輪");
            t.Secret["rocco"] = "指輪をくすねた";
            var r = new TellingResult { Text = "ロッコは銀の指輪を懐に入れた。" };
            var p = Narrator.Watch(e, t, r);
            Assert.That(p, Has.Some.Contains("銀の指輪"));
            Assert.That(p, Has.Some.Contains("だけの語りがない"));
        }

        [Test]
        public void Watcher_KeepsThePlainStyle_ButNotInsideSpeech()
        {
            var e = Shrine();
            var t = new TurnTelling();
            t.Names.Add("ガレス");
            Assert.That(Narrator.Watch(e, t, new TellingResult { Text = "ガレスは剣を抜きました。霧が晴れていく。" }), Has.Some.Contains("です・ます調"));
            Assert.That(Narrator.Watch(e, t, new TellingResult { Text = "ガレスは剣を抜いた。村長は「よく来てくださいました」と頭を下げた。" }), Is.Empty);
        }

        [Test]
        public async Task Narrator_ReadsAdditions_DropsTheUnknown()
        {
            var e = Shrine();
            var llm = new ScriptedLlm().Tool(Narrator.ToolName, @"{""text"": ""ガレスは村人と話した。"",
                ""facts"": [""村の井戸は涸れている""],
                ""extras"": [{""name"": ""粉屋のハンス"", ""portrait"": ""pt_dragon""}],
                ""items"": [{""name"": ""古い鍵"", ""kind"": ""tool"", ""to"": ""gareth""}, {""name"": ""宝"", ""kind"": ""treasure"", ""to"": ""nobody""}],
                ""attitudes"": [{""person"": ""elder"", ""reason"": ""gift""}, {""person"": ""ghost"", ""reason"": ""gift""}]}");
            var t = new TurnTelling();
            t.Names.Add("ガレス");
            var r = await new Narrator(llm).TellAsync(e, t);
            Assert.That(r.Facts.Single(), Is.EqualTo("村の井戸は涸れている"));
            Assert.That(r.Extras.Single().portrait, Is.EqualTo("pt_elder"), "an unknown portrait falls back to the list");
            Assert.That(r.Items.Single().name, Is.EqualTo("古い鍵"));
            Assert.That(r.Attitudes.Single().person, Is.EqualTo("elder"));
            Assert.That(r.Retold, Is.False);
        }

        [Test]
        public async Task Tape_RecordsAndReplays()
        {
            var file = Path.Combine(Path.GetTempPath(), "tavern_tape_" + System.Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var e = Shrine();
                var live = new ScriptedLlm().Tool(Classifier.ToolName, @"{""declarations"": [{""who"": ""gareth"", ""steps"": [{""text"": ""見回す"", ""sort"": ""auto""}]}]}");
                var first = await new Classifier(new RecordingLlm(live, file)).ClassifyAsync(e, Decl(("gareth", "見回す")), false);
                var replay = new ReplayLlm(file);
                var again = await new Classifier(replay).ClassifyAsync(Shrine(), Decl(("gareth", "見回す")), false);
                Assert.That(replay.Hits, Is.EqualTo(1));
                Assert.That(again.For("gareth")!.Steps.Single().Text, Is.EqualTo(first.For("gareth")!.Steps.Single().Text));
                // a request not on the tape is a miss; the classifier treats it like an unreadable answer (asks once more, then a plain success)
                var other = new ReplayLlm(file);
                var missed = await new Classifier(other).ClassifyAsync(Shrine(), Decl(("gareth", "別の宣言")), false);
                Assert.That(other.Misses, Is.EqualTo(2));
                Assert.That(missed.For("gareth")!.Missing, Is.True);
            }
            finally { File.Delete(file); }
        }

        [Test]
        public async Task CallMeter_CountsAndRunsLow()
        {
            var m = new CallMeter(new ScriptedLlm(), 20);
            for (int i = 0; i < 10; i++) await m.ChatAsync(new ChatMessage[0], new ToolDef[0]);
            Assert.That(m.Used, Is.EqualTo(10));
            Assert.That(m.Left, Is.EqualTo(10));
            Assert.That(m.Low, Is.True, "10 left is low");
        }
    }
}
