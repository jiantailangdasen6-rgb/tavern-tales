#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Rules;

namespace Tavern.Core.Gm
{
    /// <summary>The functions the AI game master can call. Only these change the game; the narration text never does.</summary>
    public static class GmTools
    {
        public const string RequestCheck = "request_check";
        public const string SuggestActions = "suggest_actions";

        public static IReadOnlyList<ToolDef> For(IEnumerable<Character> party)
        {
            var names = new JArray(party.Select(c => c.Name));
            return new[]
            {
                new ToolDef(RequestCheck,
                    "結果が不確かな行動について、ゲームにサイコロでの判定を頼む。判定の結果が返ってくるまで成功・失敗を語らないこと。",
                    new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["character"] = new JObject { ["type"] = "string", ["enum"] = names, ["description"] = "判定する冒険者の名前" },
                            ["stat"] = new JObject { ["type"] = "string", ["enum"] = new JArray("力", "技", "知", "心"),
                                ["description"] = "力=腕っぷし、技=器用さ・素早さ、知=知識・魔法、心=意志・話術・信仰" },
                            ["difficulty"] = new JObject { ["type"] = "string", ["enum"] = new JArray("易しい", "普通", "難しい", "至難") },
                            ["reason"] = new JObject { ["type"] = "string", ["description"] = "何の判定か（短く。例: 扉の罠を外す）" },
                        },
                        ["required"] = new JArray("character", "stat", "difficulty", "reason"),
                    }),
                new ToolDef(SuggestActions,
                    "語りの最後に、冒険者たちが次に取れそうな行動の案を 2〜3 個出す（ボタンになる。1 つ 25 字以内）。",
                    new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["actions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                        },
                        ["required"] = new JArray("actions"),
                    }),
            };
        }
    }
}
