using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tavern.Core.Gm;

namespace Tavern.Core.Tests
{
    public class FallbackTests
    {
        /// <summary>Answers only when cancelled (a model that takes minutes).</summary>
        sealed class SlowLlm : ILlmClient
        {
            public string Name => "slow";
            public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            }
        }

        sealed class LimitedLlm : ILlmClient
        {
            public string Name => "limited";
            public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
                => throw new LlmRateLimitException("429");
        }

        static readonly List<ChatMessage> Hi = new() { new ChatMessage(Role.User, "hi") };

        [Test]
        public async Task SlowBackendIsSkippedForThatCallOnly()
        {
            var slow = new SlowLlm();
            var fallback = new FallbackLlm(slow, new ScriptedLlm(new LlmReply("quick", new List<ToolCall>()))) { Patience = TimeSpan.FromMilliseconds(100) };
            var reply = await fallback.ChatAsync(Hi, new List<ToolDef>());
            Assert.That(reply.Content, Is.EqualTo("quick"));
            Assert.That(fallback.Current, Is.SameAs(slow));   // tried first again next time
        }

        [Test]
        public async Task RateLimitedBackendIsLeftForGood()
        {
            var next = new ScriptedLlm(new LlmReply("a", new List<ToolCall>()), new LlmReply("b", new List<ToolCall>()));
            var fallback = new FallbackLlm(new LimitedLlm(), next);
            Assert.That((await fallback.ChatAsync(Hi, new List<ToolDef>())).Content, Is.EqualTo("a"));
            Assert.That(fallback.Current, Is.SameAs(next));
            Assert.That((await fallback.ChatAsync(Hi, new List<ToolDef>())).Content, Is.EqualTo("b"));
        }
    }
}
