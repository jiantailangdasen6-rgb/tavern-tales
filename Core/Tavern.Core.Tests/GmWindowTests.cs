using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Tests
{
    public class GmWindowTests
    {
        static LlmReply Plan() => new LlmReply("", new[] { new ToolCall(GmTools.PlanChecks, JObject.FromObject(new { actions = new object[0] })) });
        static LlmReply Narr(string text) => new LlmReply("", new[] { new ToolCall(GmTools.Narrate, JObject.FromObject(new { text, suggestions = new[] { "a" } })) });

        [Test]
        public async Task OldTurnsGoAsARecapAndRequestsStay()
        {
            var party = new List<Character> { new Character("ガレス", "warrior", 3, 1, 0, 1, 18, 4) };
            var replies = new List<LlmReply>();
            for (int i = 0; i < 12; i++) { replies.Add(Plan()); replies.Add(Narr($"第{i}手番の語り。二つ目の文はあらすじに入らない。")); }
            var llm = new ScriptedLlm(replies.ToArray());
            var gm = new GmSession(llm, "SYSTEM", party, new FixedRandom()) { KeepTurns = 3 };
            for (int i = 0; i < 12; i++)
                await gm.PlayAsync(i == 1 ? "状況\n【語り部への注文（物語の外からの頼み。これからの語りに反映する）】もっと短く" : $"宣言 {i}");

            var sent = llm.Sent[^1];
            Assert.That(sent[0].Role, Is.EqualTo(Role.System));
            Assert.That(sent[0].Content, Does.StartWith("SYSTEM"));
            Assert.That(sent[0].Content, Does.Contain("- 第0手番の語り。二つ目の文はあらすじに入らない。"));   // short enough to stay whole
            Assert.That(sent[0].Content, Does.Contain("（注文）【語り部への注文"));
            Assert.That(sent[0].Content, Does.Not.Contain("第11手番"));
            // the last three player messages and everything after them, starting at a player message, tool calls paired
            Assert.That(sent.Count(m => m.Role == Role.User), Is.EqualTo(3));
            Assert.That(sent[1].Role, Is.EqualTo(Role.User));
            Assert.That(sent[1].Content, Is.EqualTo("宣言 9"));   // the last call narrates turn 11: turns 9, 10 and 11 go word for word
            Assert.That(gm.History.Count(m => m.Role == Role.User), Is.EqualTo(12));   // the saved history is whole
        }
    }
}
