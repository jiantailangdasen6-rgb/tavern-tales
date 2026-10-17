#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tavern.Core.Gm
{
    /// <summary>
    /// Uses the first backend and falls back to the next one when it is out of quota or overloaded
    /// (Gemini free tier -> local Ollama), so an evening never stops on "try again later".
    /// </summary>
    public sealed class FallbackLlm : ILlmClient
    {
        readonly IReadOnlyList<ILlmClient> _chain;
        int _current;
        DateTimeOffset _backAt;

        /// <summary>Raised when the session moves to the next backend (the UI can show a small notice).</summary>
        public event Action<ILlmClient, ILlmClient, Exception>? SwitchedBackend;

        /// <summary>
        /// When set, a backend that hasn't answered in this time is given up for this one call (not for good) and the
        /// next one answers it. Gemini 3.8 Flash some days takes minutes for a reply that Flash-Lite writes in seconds.
        /// </summary>
        public TimeSpan? Patience { get; set; }

        public FallbackLlm(params ILlmClient[] chain)
        {
            if (chain.Length == 0) throw new ArgumentException("no backend");
            _chain = chain;
        }

        public ILlmClient Current => _chain[_current];
        public string Name => Current.Name;

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            // A backend left for its quota is tried again once that quota should be back (midnight Pacific for a day's, 10 min otherwise).
            if (_current > 0 && DateTimeOffset.UtcNow >= _backAt) _current = 0;
            for (int i = _current; ; i++)
            {
                var backend = _chain[i];
                var hasNext = i + 1 < _chain.Count;
                using var slow = Patience != null && hasNext ? CancellationTokenSource.CreateLinkedTokenSource(ct) : null;
                slow?.CancelAfter(Patience!.Value);
                try { return await backend.ChatAsync(messages, tools, forceTool, slow?.Token ?? ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (slow != null && !ct.IsCancellationRequested) { }
                catch (Exception ex) when ((ex is LlmRateLimitException || ex is LlmUnavailableException) && hasNext)
                {
                    if (i != _current) continue;
                    _current++;
                    _backAt = ex is LlmRateLimitException r && r.Daily ? AiNotices.NextReset(DateTimeOffset.UtcNow) : DateTimeOffset.UtcNow.AddMinutes(10);
                    SwitchedBackend?.Invoke(backend, Current, ex);
                }
            }
        }
    }
}
