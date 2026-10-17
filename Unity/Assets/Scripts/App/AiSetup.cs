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
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        public sealed class Backends
        {
            public ILlmClient Everyday = null!;
            public ILlmClient? Highlight;
            /// <summary>One line for the title screen.</summary>
            public string Description = "";
            public bool Demo;
        }

        public static Backends Create(bool demo)
        {
            if (demo)
            {
                var d = new DemoLlm();
                d.Extra[2] = new JObject { ["start_combat"] = new JArray("wolf", "wolf"), ["move_to"] = "forest_path" };
                if (int.TryParse(AppMain.Arg("-demoend"), out var endAt)) d.Extra[endAt] = new JObject { ["end_story"] = "success" };
                return new Backends { Everyday = d, Description = "AI: 見本（-demo）", Demo = true };
            }
            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User);
            var local = new OllamaClient(Http);
            if (string.IsNullOrWhiteSpace(key))
                return new Backends { Everyday = local, Description = "AI: 手元の AI（Gemini の鍵が見つからない）" };
            var everyday = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.5-flash-lite", "minimal"), local);
            var highlight = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.8-flash", "low"), everyday);
            return new Backends { Everyday = everyday, Highlight = highlight, Description = "AI: Gemini（無料枠）＋ 手元の AI" };
        }
    }
}
