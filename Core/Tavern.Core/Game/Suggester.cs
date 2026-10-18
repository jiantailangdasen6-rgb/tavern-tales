#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;

namespace Tavern.Core.Game
{
    /// <summary>
    /// The suggested next actions (the buttons), made by a separate call that only sees what the players know
    /// (TavernGame.PublicView): the storyteller knows the truth, the traps and the foes ahead, and asking it not to hint at
    /// them depended on the model. What this call is never given, it cannot give away, whichever model answers.
    /// </summary>
    public static class Suggester
    {
        public const string ToolName = "suggest_actions";

        public static readonly ToolDef Tool = new ToolDef(ToolName, "冒険者たちが次にできる行動の案を 3 つ出す。",
            new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["suggestions"] = new JObject
                    {
                        ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["minItems"] = 3, ["maxItems"] = 3,
                        ["description"] = "次の行動の案を 3 つ。1 つ 25 字以内",
                    },
                },
                ["required"] = new JArray("suggestions"),
            });

        const string Rules =
            "あなたは剣と魔法の TRPG の卓で、冒険者たちに「次にできること」の案を 3 つ出す係です。\n" +
            "下の【冒険者が知っていること】だけを使います。そこに書かれていないことは、あなたも知りません。\n" +
            "- 1 つ 25 字以内。場面の様子・その場の人物・持ち物・手がかりに結びついた、具体的で試したくなる行動にする。\n" +
            "- まだ見ていない敵・罠・隠された事情を前提にしない。結果を決めつけない（「扉を開けて宝を手に入れる」でなく「扉をそっと開けてみる」）。\n" +
            "- 3 つは向きを変える（調べる・話す・進む・休む・備える など）。戦いの最中なら、周りの物の使い方や戦い方の工夫を含めてよい。\n" +
            "- 日本語で、冒険者の行動として書く（「〜する」）。";

        /// <summary>Three suggestions from the public view (empty when the answer had none).</summary>
        public static async Task<List<string>> AskAsync(ILlmClient llm, string publicView, CancellationToken ct = default)
        {
            var messages = new[] { new ChatMessage(Role.System, Rules), new ChatMessage(Role.User, publicView) };
            var reply = await llm.ChatAsync(messages, new[] { Tool }, ToolName, ct).ConfigureAwait(false);
            var call = reply.ToolCalls.FirstOrDefault(c => c.Name == ToolName);
            return (call?.Arguments["suggestions"] as JArray)?
                .Select(s => ((string?)s ?? "").Trim()).Where(s => s.Length > 0).Take(3).ToList() ?? new List<string>();
        }
    }
}
