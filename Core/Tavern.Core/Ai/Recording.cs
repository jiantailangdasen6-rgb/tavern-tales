#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;

namespace Tavern.Core.Ai
{
    /// <summary>
    /// 録音再生 (09_Spec 17): AI replies are recorded under a key made from the request, and replayed from the file afterwards —
    /// no key, anywhere, in seconds, the same result every time. One JSON line per reply.
    /// </summary>
    public static class Tape
    {
        /// <summary>The request's fingerprint: the messages and the tools offered (not the backend, so a tape replays on any).</summary>
        public static string Key(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool)
        {
            var b = new StringBuilder();
            foreach (var m in messages) b.Append((int)m.Role).Append('\u0001').Append(m.Content).Append('\u0002');
            foreach (var t in tools) b.Append(t.Name).Append(t.Parameters.ToString(Formatting.None));
            b.Append(forceTool);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
        }

        public static JObject ToJson(string key, LlmReply r, string backend, double seconds) => new JObject
        {
            ["key"] = key,
            ["backend"] = backend,
            ["seconds"] = Math.Round(seconds, 2),
            ["content"] = r.Content,
            ["calls"] = new JArray(r.ToolCalls.Select(c => new JObject { ["name"] = c.Name, ["args"] = c.Arguments })),
        };

        public static LlmReply FromJson(JObject o) => new LlmReply(
            (string?)o["content"] ?? "",
            (o["calls"] as JArray ?? new JArray()).OfType<JObject>().Select(c => new ToolCall((string?)c["name"] ?? "", c["args"] as JObject ?? new JObject())).ToList());
    }

    /// <summary>Passes calls through and appends each reply to the tape file.</summary>
    public sealed class RecordingLlm : ILlmClient
    {
        readonly ILlmClient _inner;
        readonly string _file;
        readonly object _lock = new object();
        public RecordingLlm(ILlmClient inner, string file) { _inner = inner; _file = file; }
        public string Name => _inner.Name;

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reply = await _inner.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false);
            var line = Tape.ToJson(Tape.Key(messages, tools, forceTool), reply, _inner.Name, sw.Elapsed.TotalSeconds).ToString(Formatting.None);
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
                File.AppendAllText(_file, line + "\n", new UTF8Encoding(false));
            }
            return reply;
        }
    }

    /// <summary>Answers from a tape. A request not on the tape throws (or goes to the live backend when one is given).</summary>
    public sealed class ReplayLlm : ILlmClient
    {
        readonly Dictionary<string, Queue<JObject>> _replies = new Dictionary<string, Queue<JObject>>();
        readonly ILlmClient? _live;
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public string Name => "replay";

        public ReplayLlm(string file, ILlmClient? live = null)
        {
            _live = live;
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Trim().Length > 0))
            {
                var o = JObject.Parse(line);
                var key = (string)o["key"]!;
                if (!_replies.TryGetValue(key, out var q)) _replies[key] = q = new Queue<JObject>();
                q.Enqueue(o);
            }
        }

        /// <summary>The recorded answer time of a request (to report waiting times from a tape).</summary>
        public double LastSeconds { get; private set; }

        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            var key = Tape.Key(messages, tools, forceTool);
            if (_replies.TryGetValue(key, out var q) && q.Count > 0)
            {
                // the same request asked twice gets the recorded answers in order; the last one repeats
                var o = q.Count > 1 ? q.Dequeue() : q.Peek();
                Hits++;
                LastSeconds = (double?)o["seconds"] ?? 0;
                return Task.FromResult(Tape.FromJson(o));
            }
            Misses++;
            if (_live != null) return _live.ChatAsync(messages, tools, forceTool, ct);
            throw new InvalidOperationException($"replay: no recorded answer for {key}");
        }
    }

    /// <summary>
    /// Counts AI calls per Gemini day (it resets at midnight Pacific time, 16-17 h in Japan) so the host can see what is left (09_Spec 13).
    /// </summary>
    public sealed class CallMeter : ILlmClient
    {
        readonly ILlmClient _inner;
        public int DailyLimit { get; }
        public int Used { get; private set; }
        public DateTimeOffset ResetsAt { get; private set; }
        public List<double> Seconds { get; } = new List<double>();
        public string Name => _inner.Name;

        public CallMeter(ILlmClient inner, int dailyLimit, int usedToday = 0, DateTimeOffset? now = null)
        {
            _inner = inner; DailyLimit = dailyLimit; Used = usedToday;
            ResetsAt = AiNotices.NextReset(now ?? DateTimeOffset.UtcNow);
        }

        public int Left => Math.Max(0, DailyLimit - Used);
        /// <summary>Low: retellings stop (a tenth of the day, at least 10 calls).</summary>
        public bool Low => Left <= Math.Max(10, DailyLimit / 10);

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            if (DateTimeOffset.UtcNow >= ResetsAt) { Used = 0; ResetsAt = AiNotices.NextReset(DateTimeOffset.UtcNow); }
            Used++;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { return await _inner.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false); }
            finally { Seconds.Add(sw.Elapsed.TotalSeconds); }
        }
    }
}
