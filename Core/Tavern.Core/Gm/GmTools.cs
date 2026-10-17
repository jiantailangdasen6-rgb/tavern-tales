#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Rules;

namespace Tavern.Core.Gm
{
    /// <summary>
    /// The functions the AI game master calls. A turn has a fixed order set by the game:
    /// 1. plan_checks: which declared actions need a check (all players at once; may be empty)
    /// 2. narrate: the story text and the next action suggestions, always together.
    /// Only tools change the game; free text never does.
    /// </summary>
    public static class GmTools
    {
        public const string PlanChecks = "plan_checks";
        public const string Narrate = "narrate";

        public static ToolDef PlanChecksTool(IEnumerable<Character> party) => new ToolDef(PlanChecks,
            "宣言した冒険者の行動を 1 人ずつすべて並べ、それぞれに判定が要るかを決める。" +
            "失敗したら面白いこと・危ないことが起きうる行動（罠・鍵・盗み・力ずく・説得・危険な場所の移動・戦い）は判定が要る。" +
            "誰でも確実にできる行動（見張る・一礼する・明かりを灯す・話しかける）は要らない。",
            new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["actions"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "宣言した冒険者全員分（1 人 1 つ）",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["character"] = new JObject { ["type"] = "string", ["enum"] = new JArray(party.Select(c => c.Name)) },
                                ["needs_check"] = new JObject { ["type"] = "boolean" },
                                ["stat"] = new JObject { ["type"] = "string", ["enum"] = new JArray("力", "技", "知", "心"),
                                    ["description"] = "判定が要るとき。力=腕っぷし、技=器用さ・素早さ、知=知識・魔法、心=意志・話術・信仰" },
                                ["difficulty"] = new JObject { ["type"] = "string", ["enum"] = new JArray("易しい", "普通", "難しい", "至難") },
                                ["reason"] = new JObject { ["type"] = "string", ["description"] = "何の判定か（短く。例: 扉の罠を外す）" },
                            },
                            ["required"] = new JArray("character", "needs_check"),
                        },
                    },
                },
                ["required"] = new JArray("actions"),
            });

        public static readonly ToolDef NarrateTool = new ToolDef(Narrate,
            "判定の結果のとおりに、全員の行動の結末を語り部として語り、次に取れそうな行動の案を出す。",
            new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["text"] = new JObject { ["type"] = "string", ["description"] = "語り（地の文と台詞だけ。いつもは 2〜4 文）" },
                    ["suggestions"] = new JObject
                    {
                        ["type"] = "array", ["items"] = new JObject { ["type"] = "string" },
                        ["description"] = "次の行動の案を 3 つ（ボタンになる。1 つ 25 字以内）。冒険者がまだ知らないこと（隠された設定、まだ姿を見せていない敵や人物）は書かない",
                    },
                },
                ["required"] = new JArray("text", "suggestions"),
            });
    }
}
