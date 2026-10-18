#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Content;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Game
{
    /// <summary>
    /// The scenario-aware versions of the two GM tools. The narrate tool carries the effects of the story
    /// (damage, items, moving, clues, combat, acts, the ending); the game validates and applies them.
    /// Ids come as enums so the AI can only pick things that exist (and have art).
    /// </summary>
    public static class GameTools
    {
        public static readonly string[] Sizes = { "小", "中", "大" };

        public static int SizeValue(string? size) => size switch { "大" => 6, "中" => 4, _ => 2 };

        static JObject Str(string description) => new JObject { ["type"] = "string", ["description"] = description };
        static JObject Enum(IEnumerable<string> values, string description = "")
            => new JObject { ["type"] = "string", ["enum"] = new JArray(values.ToArray()), ["description"] = description };
        static JObject ArrayOf(JObject item, string description)
            => new JObject { ["type"] = "array", ["items"] = item, ["description"] = description };
        static JObject Obj(JObject props, params string[] required)
            => new JObject { ["type"] = "object", ["properties"] = props, ["required"] = new JArray(required) };

        /// <summary>The narrate tool; withSuggestions false when a separate, blind call makes the suggestions (Suggester).</summary>
        public static ToolDef Narrate(ScenarioDef s, ContentDb db, IEnumerable<Character> party, bool withSuggestions = true)
        {
            var names = party.Select(c => c.Name).ToArray();
            var who = Enum(names);
            var schema = Obj(new JObject
                {
                    ["text"] = Str("語り（地の文と台詞だけ）。いつもは 2〜4 文・150 字まで。場面の始まりや山場だけ 6 文まで。長すぎる語りは遊びを止めてしまう"),
                    ["suggestions"] = ArrayOf(new JObject { ["type"] = "string" },
                        "次の行動の案を 3 つ（ボタンになる。1 つ 25 字以内）。冒険者がまだ知らないこと（隠された設定、まだ姿を見せていない敵や人物）は書かない"),
                    ["damage"] = ArrayOf(Obj(new JObject { ["character"] = who, ["size"] = Enum(Sizes, "小=2 中=4 大=6"), ["cause"] = Str("原因") },
                        "character", "size"), "罠や落下など、戦闘の外で冒険者が受けた傷"),
                    ["heal"] = ArrayOf(Obj(new JObject { ["character"] = who, ["size"] = Enum(Sizes) }, "character", "size"), "休息や薬での回復"),
                    ["items_gained"] = ArrayOf(Obj(new JObject { ["character"] = who, ["item"] = Str("物の名前") }, "character", "item"), "手に入れた物"),
                    ["items_lost"] = ArrayOf(Obj(new JObject { ["character"] = who, ["item"] = Str("物の名前") }, "character", "item"), "失った・手放した物"),
                    ["move_to"] = Enum(new[] { "none" }.Concat(s.Locations.Select(l => l.Id)), "一行が別の場所に着いたときだけ、その場所の id"),
                    ["clues_found"] = ArrayOf(Enum(s.Clues.Select(c => c.Id)), "この手で冒険者が知った手がかりの id"),
                    ["start_combat"] = ArrayOf(Enum(db.Enemies.Keys), "戦いが始まるときだけ、現れた敵の id（場所の敵の顔ぶれを基本に）"),
                    ["next_act"] = new JObject { ["type"] = "boolean", ["description"] = "物語が次の幕へ進むとき true" },
                    ["end_story"] = Enum(new[] { "none", "success", "bitter", "failure" }, "物語が結末を迎えたときだけ"),
                }, withSuggestions ? new[] { "text", "suggestions" } : new[] { "text" });
            if (!withSuggestions) ((JObject)schema["properties"]!).Remove("suggestions");
            return new ToolDef(GmTools.Narrate,
                "判定の結果のとおりに全員の行動の結末を語る" + (withSuggestions ? "、次の行動の案を出す" : "") + "。語りの中で起きたことは、下の効果の欄にも必ず書く" +
                "（書かなければゲームの状態は変わらない）。何も起きなければ効果の欄は空でよい。", schema);
        }

        /// <summary>The plan tool used in combat: free-text actions may also deal damage or stun, by size, decided by the AI.</summary>
        public static ToolDef CombatPlan(IEnumerable<Character> party, IEnumerable<string> enemyIds)
        {
            var names = party.Select(c => c.Name).ToArray();
            return new ToolDef(GmTools.PlanChecks,
                "戦闘中の、カードや基本の行動ではない自由な行動を 1 人ずつ並べ、判定と、成功したときの効果を決める。" +
                "効果: damage（敵 1 体に 小2/中4/大6）、stun（敵 1 体を 1 手番止める。ボスは弱点を突いたときだけ、1 戦闘 1 回）、none。",
                Obj(new JObject
                {
                    ["actions"] = ArrayOf(Obj(new JObject
                    {
                        ["character"] = Enum(names),
                        ["needs_check"] = new JObject { ["type"] = "boolean" },
                        ["stat"] = Enum(new[] { "力", "技", "知", "心" }),
                        ["difficulty"] = Enum(new[] { "易しい", "普通", "難しい", "至難" }),
                        ["reason"] = Str("何の判定か"),
                        ["effect"] = Enum(new[] { "none", "damage", "stun" }),
                        ["size"] = Enum(Sizes),
                        ["target"] = Enum(new[] { "none" }.Concat(enemyIds), "効果を受ける敵"),
                    }, "character", "needs_check", "effect"), "自由な行動をした冒険者全員分"),
                }, "actions"));
        }
    }
}
