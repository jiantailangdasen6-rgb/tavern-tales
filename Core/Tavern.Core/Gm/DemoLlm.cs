#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tavern.Core.Gm
{
    /// <summary>
    /// A stand-in game master with canned answers, for screenshots and UI tests without spending the AI quota
    /// (Unity -demo). It follows the forced tool so the whole turn machinery runs.
    /// </summary>
    public sealed class DemoLlm : ILlmClient
    {
        int _narrations;
        public string Name => "demo";

        /// <summary>Optional scripted narrate arguments by narration index (e.g. start a fight on the 3rd).</summary>
        public Dictionary<int, JObject> Extra { get; } = new Dictionary<int, JObject>();

        static readonly string[] Lines =
        {
            "ぱちぱちと爆ぜる炉の火が、眠る竜亭の古い梁を赤く照らしている。雨に濡れた外套の老人が、すがるような足取りで一行の卓へ歩み寄った。「頼む、冒険者の方々。村の若者が二人、灰色の森から戻らんのじゃ」",
            "霧の立ちこめる小道を進むと、灰色の木々の間で何かが動いた。湿った落ち葉の上に、まだ新しい足跡が二つ、森の奥へと続いている。",
            "低い唸り声とともに、灰色の狼が二頭、霧の中から姿を現した。牙を剥き、じりじりと間合いを詰めてくる。",
            "剣が閃き、炎が霧を焼いた。狼の一頭が悲鳴を上げて退き、もう一頭が怒りにまかせて飛びかかる。",
            "最後の狼が草むらに倒れ伏し、森に静けさが戻った。遠くで、鎧の擦れるような音がかすかに響いた気がする。",
        };

        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            if (forceTool == GmTools.PlanChecks)
            {
                // Every declaring character rolls a normal skill check (names are read from the tool's enum).
                var names = (tools[0].Parameters["properties"]?["actions"]?["items"]?["properties"]?["character"]?["enum"] as JArray)?
                    .Select(n => (string?)n).Where(n => n != null).ToList() ?? new List<string?>();
                var last = messages.LastOrDefault(m => m.Role == Role.User)?.Content ?? "";
                var actions = new JArray(names.Where(n => last.Contains(n + ":")).Select(n => new JObject
                {
                    ["character"] = n, ["needs_check"] = true, ["stat"] = "技", ["difficulty"] = "普通", ["reason"] = "宣言した行動",
                    ["effect"] = "damage", ["size"] = "小", ["target"] = "none",
                }));
                return Task.FromResult(new LlmReply("", new[] { new ToolCall(GmTools.PlanChecks, new JObject { ["actions"] = actions }) }));
            }
            var args = new JObject
            {
                ["text"] = Lines[_narrations % Lines.Length],
                ["suggestions"] = new JArray("足跡を追って森の奥へ進む", "辺りを警戒しながら様子を見る", "村へ戻って話を聞き直す"),
            };
            if (Extra.TryGetValue(_narrations, out var extra)) foreach (var p in extra.Properties()) args[p.Name] = p.Value.DeepClone();
            _narrations++;
            return Task.FromResult(new LlmReply("", new[] { new ToolCall(GmTools.Narrate, args) }));
        }
    }
}
