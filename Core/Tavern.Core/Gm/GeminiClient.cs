#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tavern.Core.Gm
{
    /// <summary>Thrown when the backend says "too many requests" (per-minute or daily quota).</summary>
    public sealed class LlmRateLimitException : Exception
    {
        public LlmRateLimitException(string message) : base(message) { }
    }

    /// <summary>Thrown when the backend is overloaded or down for now.</summary>
    public sealed class LlmUnavailableException : Exception
    {
        public LlmUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Google Gemini API (generateContent, function calling). The key comes from the host only
    /// (environment variable GEMINI_API_KEY); it is never logged or saved by the game.
    /// </summary>
    public sealed class GeminiClient : ILlmClient
    {
        readonly HttpClient _http;
        readonly string _apiKey;
        readonly string _model;
        /// <summary>"minimal" / "low" / ... for models that think; null leaves the model default.</summary>
        readonly string? _thinkingLevel;

        public string Name => "gemini:" + _model;

        /// <summary>The last exchange, for debugging prompts (contains no key).</summary>
        public JObject? LastRequest { get; private set; }
        public string? LastResponse { get; private set; }

        public GeminiClient(HttpClient http, string apiKey, string model = "gemini-3.8-flash", string? thinkingLevel = "low")
        {
            _http = http; _apiKey = apiKey; _model = model; _thinkingLevel = thinkingLevel;
        }

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            var body = BuildRequest(messages, tools, _thinkingLevel, forceTool);
            for (int attempt = 0; ; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent")
                { Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json") };
                req.Headers.Add("x-goog-api-key", _apiKey);
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if ((int)res.StatusCode == 429)
                {
                    // The free tier has a small per-minute quota: wait as told and try again; a daily quota gives up.
                    var wait = RetryDelay(text);
                    if (attempt < MaxRateLimitRetries && wait != null && wait.Value <= TimeSpan.FromSeconds(90))
                    {
                        RateLimitWaits++;
                        await Task.Delay(wait.Value + TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                        continue;
                    }
                    throw new LlmRateLimitException("gemini 429: " + Trim(text));
                }
                // 503 "high demand" / 500: usually brief, try again a little later.
                if (((int)res.StatusCode == 503 || (int)res.StatusCode == 500) && attempt < MaxRateLimitRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(4 * (attempt + 1)), ct).ConfigureAwait(false);
                    continue;
                }
                if ((int)res.StatusCode == 503) throw new LlmUnavailableException("gemini 503: " + Trim(text));
                if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"gemini {(int)res.StatusCode}: {Trim(text)}");
                LastRequest = body; LastResponse = text;
                return Parse(JObject.Parse(text));
            }
        }

        const int MaxRateLimitRetries = 3;

        /// <summary>How many times a call waited for the per-minute quota (to judge the free tier).</summary>
        public int RateLimitWaits { get; private set; }

        /// <summary>Reads RetryInfo.retryDelay ("23s" or "23.5s") from a 429 body.</summary>
        internal static TimeSpan? RetryDelay(string errorBody)
        {
            try
            {
                var details = JObject.Parse(errorBody)["error"]?["details"] as JArray;
                var delay = (string?)details?.FirstOrDefault(d => ((string?)d["@type"] ?? "").EndsWith("RetryInfo"))?["retryDelay"];
                if (delay == null || !delay.EndsWith("s")) return null;
                return TimeSpan.FromSeconds(double.Parse(delay.TrimEnd('s'), System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (JsonException) { return null; }
        }

        internal static JObject BuildRequest(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? thinkingLevel, string? forceTool = null)
        {
            var system = string.Join("\n\n", messages.Where(m => m.Role == Role.System).Select(m => m.Content));
            var contents = new JArray();
            foreach (var m in messages.Where(m => m.Role != Role.System))
            {
                string role; JArray parts;
                if (m.Role == Role.Assistant)
                {
                    role = "model";
                    parts = m.Raw is JArray raw ? (JArray)raw.DeepClone() : AssistantParts(m);
                    // The session dropped these calls (suggestions only): send the text parts alone.
                    if (m.ToolCalls.Count == 0)
                        foreach (var p in parts.Where(p => p["functionCall"] != null).ToList()) p.Remove();
                    if (parts.Count == 0) continue;
                }
                else if (m.Role == Role.Tool)
                {
                    role = "user";
                    parts = new JArray(new JObject
                    {
                        ["functionResponse"] = new JObject { ["name"] = m.ToolName ?? "", ["response"] = new JObject { ["result"] = m.Content } },
                    });
                }
                else
                {
                    role = "user";
                    parts = new JArray(new JObject { ["text"] = m.Content });
                }
                // Gemini wants turns to alternate; merge consecutive parts of the same role (several tool results in a row).
                if (contents.Count > 0 && (string?)contents.Last!["role"] == role)
                    foreach (var p in parts) ((JArray)contents.Last!["parts"]!).Add(p);
                else
                    contents.Add(new JObject { ["role"] = role, ["parts"] = parts });
            }

            var body = new JObject
            {
                ["contents"] = contents,
                ["generationConfig"] = new JObject { ["temperature"] = 1.0 },
            };
            if (system.Length > 0) body["systemInstruction"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = system }) };
            if (thinkingLevel != null) body["generationConfig"]!["thinkingConfig"] = new JObject { ["thinkingLevel"] = thinkingLevel };
            if (tools.Count > 0)
                body["tools"] = new JArray(new JObject
                {
                    ["functionDeclarations"] = new JArray(tools.Select(t => new JObject
                    {
                        ["name"] = t.Name, ["description"] = t.Description, ["parametersJsonSchema"] = t.Parameters,
                    })),
                });
            if (forceTool != null)
                body["toolConfig"] = new JObject
                {
                    ["functionCallingConfig"] = new JObject { ["mode"] = "ANY", ["allowedFunctionNames"] = new JArray(forceTool) },
                };
            return body;
        }

        static JArray AssistantParts(ChatMessage m)
        {
            var parts = new JArray();
            if (m.Content.Length > 0) parts.Add(new JObject { ["text"] = m.Content });
            foreach (var c in m.ToolCalls) parts.Add(new JObject { ["functionCall"] = new JObject { ["name"] = c.Name, ["args"] = c.Arguments } });
            return parts;
        }

        internal static LlmReply Parse(JObject response)
        {
            var parts = response["candidates"]?[0]?["content"]?["parts"] as JArray;
            if (parts == null)
            {
                var reason = (string?)response["candidates"]?[0]?["finishReason"] ?? (string?)response["promptFeedback"]?["blockReason"] ?? "unknown";
                throw new InvalidOperationException("gemini returned no content: " + reason);
            }
            var text = new StringBuilder();
            var calls = new List<ToolCall>();
            foreach (var p in parts)
            {
                if (p["thought"]?.Value<bool>() == true) continue;   // thought summaries are not narration
                if (p["text"] is JValue t) text.Append((string?)t);
                if (p["functionCall"] is JObject fc) calls.Add(new ToolCall((string?)fc["name"] ?? "", fc["args"] as JObject ?? new JObject()));
            }
            return new LlmReply(text.ToString().Trim(), calls, parts);
        }

        static string Trim(string s) => s.Length > 400 ? s.Substring(0, 400) : s;
    }
}
