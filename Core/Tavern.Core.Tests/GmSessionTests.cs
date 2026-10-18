using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Tests
{
    /// <summary>Plays back canned replies and records what it was sent.</summary>
    sealed class ScriptedLlm : ILlmClient
    {
        readonly Queue<LlmReply> _replies;
        public List<List<ChatMessage>> Sent { get; } = new();
        public List<string?> Forced { get; } = new();
        public ScriptedLlm(params LlmReply[] replies) { _replies = new(replies); }
        public string Name => "scripted";
        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            Sent.Add(new List<ChatMessage>(messages));
            Forced.Add(forceTool);
            return Task.FromResult(_replies.Dequeue());
        }
    }

    public class GmSessionTests
    {
        static List<Character> Party() => new()
        {
            new Character("ロッコ", "thief", 0, 3, 1, 1, 14, 4, "港町の孤児"),
            new Character("エリス", "mage", 0, 1, 3, 1, 12, 6),
        };

        static ToolCall Call(string name, object args) => new(name, JObject.FromObject(args));

        static ToolCall Plan(params object[] actions) => Call(GmTools.PlanChecks, new { actions });
        static ToolCall Narrate(string text, params string[] suggestions) => Call(GmTools.Narrate, new { text, suggestions });

        [Test]
        public async Task EveryPlannedCheckIsRolledByTheGame_ThenNarrated()
        {
            var llm = new ScriptedLlm(
                new LlmReply("", new[] { Plan(
                    new { character = "ロッコ", needs_check = true, stat = "技", difficulty = "難しい", reason = "罠を外す" },
                    new { character = "ガレス", needs_check = false },
                    new { character = "エリス", stat = "知", difficulty = "普通", reason = "碑文を読む" }) }),
                new LlmReply("", new[] { Narrate("カチリ、と罠が外れた。", "扉を開ける", "奥を覗く", "碑文を写す", "帰る") }));
            var gm = new GmSession(llm, Storyteller.Olwen, Party(), "森の祠", new FixedRandom(15, 4));

            var turn = await gm.PlayAsync("ロッコ: 扉の罠を外す\nエリス: 碑文を読む");

            Assert.That(turn.Checks, Has.Count.EqualTo(2));
            Assert.That(turn.Checks[0].Total, Is.EqualTo(18));
            Assert.That(turn.Checks[0].Succeeded, Is.True);
            Assert.That(turn.Checks[1].Succeeded, Is.False);
            Assert.That(turn.Narration, Is.EqualTo("カチリ、と罠が外れた。"));
            Assert.That(turn.Suggestions, Is.EqualTo(new[] { "扉を開ける", "奥を覗く", "碑文を写す" }));
            // the turn order is forced: plan first, then narrate with the dice results in the history
            Assert.That(llm.Forced, Is.EqualTo(new[] { GmTools.PlanChecks, GmTools.Narrate }));
            var lastSent = llm.Sent[1][^1];
            Assert.That(lastSent.Role, Is.EqualTo(Role.Tool));
            Assert.That(lastSent.Content, Does.Contain("成功").And.Contain("失敗"));
        }

        [Test]
        public async Task UnknownCharacter_IsRejected_NotRolled()
        {
            var llm = new ScriptedLlm(
                new LlmReply("", new[] { Plan(new { character = "ガレス", stat = "力", difficulty = "普通", reason = "x" }) }),
                new LlmReply("", new[] { Narrate("ロッコが代わりに挑む。") }));
            var gm = new GmSession(llm, Storyteller.Olwen, Party(), "", new FixedRandom());

            var turn = await gm.PlayAsync("x");

            Assert.That(turn.Checks, Is.Empty);
            Assert.That(turn.Rejected, Has.Count.EqualTo(1));
            Assert.That(llm.Sent[1][^1].Content, Does.Contain("いない"));
        }

        [Test]
        public async Task Opening_SkipsThePlan_AndPlainTextIsAccepted()
        {
            var llm = new ScriptedLlm(new LlmReply("夕暮れの森。", new ToolCall[0]));
            var gm = new GmSession(llm, Storyteller.Olwen, Party(), "", new FixedRandom());

            var turn = await gm.OpenAsync("始めて");

            Assert.That(turn.Narration, Is.EqualTo("夕暮れの森。"));
            Assert.That(llm.Forced, Is.EqualTo(new[] { GmTools.Narrate }));
        }

        [Test]
        public void SystemPrompt_ContainsPersonaPartyAndScenario()
        {
            var text = Prompts.System(Storyteller.Olwen, Party(), "沈んだ神殿の秘密");
            Assert.That(text, Does.Contain("オルウェン").And.Contain("ロッコ（盗賊）").And.Contain("技+3").And.Contain("沈んだ神殿の秘密"));
        }

        [Test]
        public void OllamaParse_ReadsToolCallsAndStripsThinking()
        {
            var json = JObject.Parse("{\"message\":{\"role\":\"assistant\",\"content\":\"<think>hmm</think>\\n語り\",\"tool_calls\":[{\"function\":{\"name\":\"suggest_actions\",\"arguments\":{\"actions\":[\"a\"]}}}]}}");
            var reply = OllamaClient.Parse(json);
            Assert.That(reply.Content, Is.EqualTo("語り"));
            Assert.That(reply.ToolCalls[0].Name, Is.EqualTo("suggest_actions"));
        }

        [Test]
        public void Clean_DropsSuggestionListsWrittenIntoTheTelling()
        {
            Assert.That(GmSession.Clean("雨音の響く酒場で、老商人が声をかけた。\n（行動案）\n1. 話を聞く\n2. 出発する"),
                Is.EqualTo("雨音の響く酒場で、老商人が声をかけた。"));
            Assert.That(GmSession.Clean("扉が開いた。\n- 中へ進む"), Is.EqualTo("扉が開いた。"));
            Assert.That(GmSession.Clean("  そのまま。  "), Is.EqualTo("そのまま。"));
        }
    }
}

