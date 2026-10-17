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

        /// <summary>Raised when the session moves to the next backend (the UI can show a small notice).</summary>
        public event Action<ILlmClient, ILlmClient, Exception>? SwitchedBackend;

        public FallbackLlm(params ILlmClient[] chain)
        {
            if (chain.Length == 0) throw new ArgumentException("no backend");
            _chain = chain;
        }

        public ILlmClient Current => _chain[_current];
        public string Name => Current.Name;

        public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            while (true)
            {
                try { return await Current.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false); }
                catch (Exception ex) when ((ex is LlmRateLimitException || ex is LlmUnavailableException) && _current + 1 < _chain.Count)
                {
                    var from = Current;
                    _current++;
                    SwitchedBackend?.Invoke(from, Current, ex);
                }
            }
        }
    }
}
