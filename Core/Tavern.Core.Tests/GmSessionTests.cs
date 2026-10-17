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
        public ScriptedLlm(params LlmReply[] replies) { _replies = new(replies); }
        public string Name => "scripted";
        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, CancellationToken ct = default)
        {
            Sent.Add(new List<ChatMessage>(messages));
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

        [Test]
        public async Task CheckIsRolledByTheGame_AndResultIsSentBack()
        {
            var llm = new ScriptedLlm(
                new LlmReply("ロッコが錠に指をかける。", new[] { Call(GmTools.RequestCheck, new { character = "ロッコ", stat = "技", difficulty = "難しい", reason = "罠を外す" }) }),
                new LlmReply("カチリ、と罠が外れた。", new[] { Call(GmTools.SuggestActions, new { actions = new[] { "扉を開ける", "奥を覗く" } }) }));
            var gm = new GmSession(llm, Storyteller.Olwen, Party(), "森の祠", new FixedRandom(15));

            var turn = await gm.PlayAsync("ロッコ: 扉の罠を外す");

            Assert.That(turn.Checks, Has.Count.EqualTo(1));
            Assert.That(turn.Checks[0].Total, Is.EqualTo(18));
            Assert.That(turn.Checks[0].Succeeded, Is.True);
            Assert.That(turn.Narration, Does.Contain("錠").And.Contain("カチリ"));
            Assert.That(turn.Suggestions, Is.EqualTo(new[] { "扉を開ける", "奥を覗く" }));
            // second call must carry the dice result as a tool message
            var lastSent = llm.Sent[1][^1];
            Assert.That(lastSent.Role, Is.EqualTo(Role.Tool));
            Assert.That(lastSent.Content, Does.Contain("成功"));
        }

        [Test]
        public async Task UnknownCharacter_IsRejected_NotRolled()
        {
            var llm = new ScriptedLlm(
                new LlmReply("", new[] { Call(GmTools.RequestCheck, new { character = "ガレス", stat = "力", difficulty = "普通", reason = "x" }) }),
                new LlmReply("ロッコが代わりに挑む。", new ToolCall[0]));
            var gm = new GmSession(llm, Storyteller.Olwen, Party(), "", new FixedRandom());

            var turn = await gm.PlayAsync("x");

            Assert.That(turn.Checks, Is.Empty);
            Assert.That(turn.Rejected, Has.Count.EqualTo(1));
            Assert.That(llm.Sent[1][^1].Content, Does.Contain("いない"));
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
    }
}
