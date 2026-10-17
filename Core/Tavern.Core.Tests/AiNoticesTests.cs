using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Tavern.Core.Gm;

namespace Tavern.Core.Tests
{
    public class AiNoticesTests
    {
        static readonly TimeSpan Japan = TimeSpan.FromHours(9);

        sealed class Named : ILlmClient
        {
            public Named(string name) { Name = name; }
            public string Name { get; }
            public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
                => throw new NotImplementedException();
        }

        [Test]
        public void DailyQuotaComesBackAtMidnightPacific()
        {
            // summer time: 16:00 in Japan; winter: 17:00
            Assert.That(AiNotices.ResetClock(new DateTimeOffset(2026, 10, 18, 6, 0, 0, Japan), Japan), Is.EqualTo("16:00"));
            Assert.That(AiNotices.ResetClock(new DateTimeOffset(2026, 12, 1, 10, 0, 0, Japan), Japan), Is.EqualTo("17:00"));
            // just after a reset the next one is a day later
            var next = AiNotices.NextReset(new DateTimeOffset(2026, 10, 18, 16, 30, 0, Japan));
            Assert.That(next.ToOffset(Japan), Is.EqualTo(new DateTimeOffset(2026, 10, 19, 16, 0, 0, Japan)));
        }

        [Test]
        public void ModelNamesReadNicely()
        {
            Assert.That(AiNotices.Model(new Named("gemini:gemini-3.8-flash")), Is.EqualTo("Gemini 3.8 Flash"));
            Assert.That(AiNotices.Model(new Named("gemini:gemini-3.5-flash-lite")), Is.EqualTo("Gemini 3.5 Flash-Lite"));
            Assert.That(AiNotices.Model(new Named("ollama:qwen3:8b")), Is.EqualTo("手元の AI"));
        }

        [Test]
        public void NoticesSayWhatHappenedAndWhenItComesBack()
        {
            var now = new DateTimeOffset(2026, 10, 18, 6, 0, 0, Japan);
            var text = AiNotices.Switched(new Named("gemini:gemini-3.8-flash"), new Named("gemini:gemini-3.5-flash-lite"),
                new LlmRateLimitException("429", daily: true), now, Japan);
            Assert.That(text, Is.EqualTo("今日の Gemini 3.8 Flash の無料枠を使い切ったので、ここからは Gemini 3.5 Flash-Lite が語ります。枠は 16:00 ごろに戻ります。"));
            Assert.That(AiNotices.Switched(new Named("gemini:gemini-3.5-flash-lite"), new Named("ollama:qwen3:8b"),
                new LlmRateLimitException("429"), now, Japan), Does.Contain("手元の AI で語ります"));

            Assert.That(AiNotices.Failed(new LlmRateLimitException("429", daily: true), false, now, Japan), Does.StartWith("今日の AI の無料枠を使い切りました（16:00 ごろに戻ります）"));
            Assert.That(AiNotices.Failed(new HttpRequestException("refused"), true, now, Japan), Does.StartWith("今日の AI の無料枠"));
            Assert.That(AiNotices.Failed(new HttpRequestException("dns"), false, now, Japan), Does.StartWith("AI につながりません"));
            Assert.That(AiNotices.Failed(new InvalidOperationException("bad json"), false, now, Japan), Is.Null);
        }
    }
}
