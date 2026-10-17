#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tavern.Core.Gm
{
    public enum Role { System, User, Assistant, Tool }

    /// <summary>A function the AI may call. Parameters is a JSON schema object.</summary>
    public sealed class ToolDef
    {
        public string Name { get; }
        public string Description { get; }
        public JObject Parameters { get; }
        public ToolDef(string name, string description, JObject parameters) { Name = name; Description = description; Parameters = parameters; }
    }

    public sealed class ToolCall
    {
        public string Name { get; }
        public JObject Arguments { get; }
        public ToolCall(string name, JObject arguments) { Name = name; Arguments = arguments; }
    }

    public sealed class ChatMessage
    {
        public Role Role { get; }
        public string Content { get; }
        /// <summary>Tool calls made by the assistant in this message.</summary>
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        /// <summary>For Role.Tool: which tool this result answers.</summary>
        public string? ToolName { get; }

        /// <summary>
        /// The backend's own form of an assistant message, sent back unchanged on the next call
        /// (Gemini needs its thought signatures returned with function calls).
        /// </summary>
        public JToken? Raw { get; }

        public ChatMessage(Role role, string content, IReadOnlyList<ToolCall>? toolCalls = null, string? toolName = null, JToken? raw = null)
        {
            Role = role; Content = content; ToolCalls = toolCalls ?? new List<ToolCall>(); ToolName = toolName; Raw = raw;
        }
    }

    public sealed class LlmReply
    {
        public string Content { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public JToken? Raw { get; }
        public LlmReply(string content, IReadOnlyList<ToolCall> toolCalls, JToken? raw = null) { Content = content; ToolCalls = toolCalls; Raw = raw; }
    }

    /// <summary>One chat backend (local Ollama, Gemini free tier, a paid model later...). Only the host calls it.</summary>
    public interface ILlmClient
    {
        string Name { get; }
        Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, CancellationToken ct = default);
    }
}
