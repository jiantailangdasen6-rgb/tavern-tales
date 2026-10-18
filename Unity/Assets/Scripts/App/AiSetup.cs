#nullable enable
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Builds the AI backends on the host: Gemini Flash-Lite for everyday turns, Gemini Flash for big moments,
    /// the local Ollama as the last resort. The key is read from GEMINI_API_KEY only (never shown, never saved).
    /// </summary>
    public static class AiSetup
    {
        sealed class QuotaGone : ILlmClient
        {
            public string Name => "gemini:gemini-3.5-flash-lite";
            public System.Threading.Tasks.Task<LlmReply> ChatAsync(System.Collections.Generic.IReadOnlyList<ChatMessage> messages, System.Collections.Generic.IReadOnlyList<ToolDef> tools,
                string? forceTool = null, System.Threading.CancellationToken ct = default) => throw new LlmRateLimitException("fake daily quota", daily: true);
        }

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        public sealed class Backends
        {
            public ILlmClient Everyday = null!;
            public ILlmClient? Highlight;
            /// <summary>One line for the title screen.</summary>
            public string Description = "";
            public bool Demo;
            /// <summary>Notices for the host when a free quota runs out and another model takes over (filled from any thread).</summary>
            public readonly System.Collections.Concurrent.ConcurrentQueue<string> Notices = new System.Collections.Concurrent.ConcurrentQueue<string>();
            /// <summary>Every Gemini model is out for now and the local AI answers.</summary>
            public Func<bool> LocalOnly = () => false;

            internal void Watch(FallbackLlm f) =>
                f.SwitchedBackend += (from, to, why) => Notices.Enqueue(AiNotices.Switched(from, to, why, DateTimeOffset.UtcNow, LocalOffset));
        }

        /// <summary>This PC's offset from UTC (for "back at 16:00").</summary>
        public static TimeSpan LocalOffset => TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);

        public static Backends Create(bool demo)
        {
            if (demo)
            {
                var d = new DemoLlm();
                d.Extra[2] = new JObject { ["start_combat"] = new JArray("wolf", "wolf"), ["move_to"] = "forest_path" };
                if (int.TryParse(AppMain.Arg("-demoend"), out var endAt)) d.Extra[endAt] = new JObject { ["end_story"] = "success" };
                if (!AppMain.Flag("-fakequota")) return new Backends { Everyday = d, Description = "AI: 見本（-demo）", Demo = true };
                // -fakequota: a Gemini whose daily quota is gone, so the switch notice shows (screenshots)
                var chain = new FallbackLlm(new QuotaGone(), d);
                var fake = new Backends { Everyday = chain, Description = "AI: 見本（枠切れの試験）", Demo = true };
                fake.Watch(chain);
                return fake;
            }
            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User);
            var b = new Backends();
            var local = new StartingOllama(new OllamaClient(Http), b.Notices);
            if (string.IsNullOrWhiteSpace(key) || AppMain.Flag("-localai"))   // -localai: tests of the local AI
            {
                b.Everyday = local; b.Description = "AI: 手元の AI（Gemini の鍵が見つからない）";
                return b;
            }
            var everyday = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.5-flash-lite", "minimal"), local);
            var highlight = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.8-flash", "low"), everyday) { Patience = TimeSpan.FromSeconds(40) };
            b.Everyday = everyday; b.Highlight = highlight; b.Description = "AI: Gemini（無料枠）＋ 手元の AI";
            b.LocalOnly = () => everyday.Current is StartingOllama;
            b.Watch(everyday); b.Watch(highlight);
            return b;
        }
    }
}
