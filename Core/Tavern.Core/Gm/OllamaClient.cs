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
    /// <summary>Local Ollama server (POST /api/chat, non-streaming, with tools). Started by Tools/Ollama.ps1.</summary>
    public sealed class OllamaClient : ILlmClient
    {
        readonly HttpClient _http;
        readonly string _baseUrl;
        readonly string _model;
        readonly int _contextTokens;

        public string Name => "ollama:" + _model;

        public OllamaClient(HttpClient http, string model = "qwen3:8b", string baseUrl = "http://127.0.0.1:11434", int contextTokens = 16384)
        {
            _http = http; _model = model; _baseUrl = baseUrl.TrimEnd('/'); _contextTokens = contextTokens;
        }

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            var body = new JObject
            {
                ["model"] = _model,
                ["stream"] = false,
                ["think"] = false,   // qwen3 would otherwise write a long hidden reasoning first
                ["messages"] = new JArray(messages.Select(ToJson)),
                ["options"] = new JObject { ["num_ctx"] = _contextTokens, ["temperature"] = 0.8 },
            };
            // Ollama cannot force a call; offering only the wanted tool is the closest thing.
            if (forceTool != null) tools = tools.Where(t => t.Name == forceTool).ToList();
            if (tools.Count > 0)
                body["tools"] = new JArray(tools.Select(t => new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Parameters },
                }));

            using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync(_baseUrl + "/api/chat", content, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"ollama {(int)res.StatusCode}: {Trim(text)}");
            return Parse(JObject.Parse(text));
        }

        internal static LlmReply Parse(JObject response)
        {
            var msg = response["message"] as JObject ?? throw new InvalidOperationException("ollama: no message");
            var calls = new List<ToolCall>();
            if (msg["tool_calls"] is JArray arr)
                foreach (var c in arr)
                {
                    var fn = c["function"];
                    if (fn == null) continue;
                    var args = fn["arguments"] switch
                    {
                        JObject o => o,
                        JValue v when v.Type == JTokenType.String => JObject.Parse((string)v!),
                        _ => new JObject(),
                    };
                    calls.Add(new ToolCall((string?)fn["name"] ?? "", args));
                }
            return new LlmReply(StripThinking((string?)msg["content"] ?? ""), calls);
        }

        /// <summary>Removes a leftover &lt;think&gt;...&lt;/think&gt; block if the model wrote one anyway.</summary>
        internal static string StripThinking(string s)
        {
            var end = s.IndexOf("</think>", StringComparison.Ordinal);
            return (end >= 0 ? s.Substring(end + "</think>".Length) : s).Trim();
        }

        static JObject ToJson(ChatMessage m)
        {
            var o = new JObject
            {
                ["role"] = m.Role switch { Role.System => "system", Role.User => "user", Role.Assistant => "assistant", _ => "tool" },
                ["content"] = m.Content,
            };
            if (m.ToolCalls.Count > 0)
                o["tool_calls"] = new JArray(m.ToolCalls.Select(c => new JObject
                {
                    ["function"] = new JObject { ["name"] = c.Name, ["arguments"] = c.Arguments },
                }));
            if (m.ToolName != null) o["tool_name"] = m.ToolName;
            return o;
        }

        static string Trim(string s) => s.Length > 300 ? s.Substring(0, 300) : s;
    }
}
