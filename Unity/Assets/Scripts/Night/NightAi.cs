#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Ai;
using Tavern.Core.Gm;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The AI on the host for the new flow (09_Spec 13): Flash-Lite sorts and tells, Flash tells the boss's entrance and the ending.
    /// No local AI (owner decision 2026-10-24): when the day's calls run out, the evening is saved and ends.
    /// -demo uses canned answers (no quota); -testkey uses GEMINI_API_KEY_TEST.
    /// </summary>
    public sealed class NightAi
    {
        public Classifier Classifier { get; private set; } = null!;
        public Narrator Narrator { get; private set; } = null!;
        public Narrator Highlight { get; private set; } = null!;
        public Bookends Bookends { get; private set; } = null!;
        /// <summary>The everyday calls today (shown to the host).</summary>
        public CallMeter? Meter { get; private set; }
        public string Description { get; private set; } = "";
        public bool Demo { get; private set; }
        public bool Ready => Classifier != null;

        static HttpClient? _http;
        static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        public static NightAi Create(bool demo, bool testKey)
        {
            var ai = new NightAi();
            if (demo)
            {
                var d = new DemoNightLlm();
                ai.Set(d, d);
                ai.Demo = true;
                ai.Description = "AI: 見本（-demo）";
                return ai;
            }
            var name = testKey ? "GEMINI_API_KEY_TEST" : "GEMINI_API_KEY";
            var key = Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(key))
            {
                var d = new DemoNightLlm();
                ai.Set(d, d);
                ai.Demo = true;
                ai.Description = $"AI: {name} が見つからないので見本で動いています";
                return ai;
            }
            var meter = new CallMeter(new GeminiClient(Http, key!, "gemini-3.5-flash-lite", "minimal"), 500);
            var strong = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.8-flash", "low"), meter) { Patience = TimeSpan.FromSeconds(40) };
            ai.Set(meter, strong);
            ai.Meter = meter;
            ai.Description = "AI: Gemini（無料枠）" + (testKey ? "・試験用のキー" : "");
            return ai;
        }

        void Set(ILlmClient everyday, ILlmClient strong)
        {
            Classifier = new Classifier(everyday);
            Narrator = new Narrator(everyday);
            Highlight = new Narrator(strong);
            Bookends = new Bookends(everyday);
        }

        /// <summary>"AI 残り 431 回（16:00 に戻る）", or empty for the demo.</summary>
        public string QuotaText() => Meter == null ? "" :
            $"AI 残り {Meter.Left} 回（{Meter.ResetsAt.ToOffset(App.AiSetup.LocalOffset):HH:mm} に戻る）";
    }

    /// <summary>
    /// Canned answers in the shapes the classifier and narrator expect, read from the request text. Enough to drive the screens and
    /// the bot without quota: moves to a place named in the declaration, checks for looking and reading, attacks in a fight.
    /// </summary>
    public sealed class DemoNightLlm : ILlmClient
    {
        public string Name => "demo";

        public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
        {
            var user = messages.LastOrDefault(m => m.Role == Role.User)?.Content ?? "";
            var tool = forceTool ?? tools.FirstOrDefault()?.Name ?? "";
            JObject args = tool switch
            {
                Classifier.ToolName => Classify(user),
                Narrator.ToolName => new JObject { ["text"] = Tell(user) },
                "handouts" => Handouts(tools.First()),
                "chronicle" => new JObject { ["title"] = "見本の夜", ["story"] = "（見本の記録帳）一行は今夜の冒険を終え、眠る竜亭へ戻った。", ["rumors"] = new JArray("見本の噂が酒場に流れている。") },
                _ => new JObject(),
            };
            return Task.FromResult(new LlmReply("", new[] { new ToolCall(tool, args) }));
        }

        static JObject Classify(string user)
        {
            var places = Regex.Matches(user, @"\[([a-z_0-9]+)\] ([^\s/（]+)").Cast<Match>().Select(m => (id: m.Groups[1].Value, name: m.Groups[2].Value)).ToList();
            var ways = user.Split('\n').Where(l => l.StartsWith("行ける場所") || l.StartsWith("道をたどって")).SelectMany(l => Regex.Matches(l, @"\[([a-z_0-9]+)\] ([^\s/（]+)").Cast<Match>())
                .Select(m => (id: m.Groups[1].Value, name: m.Groups[2].Value)).ToList();
            var clue = Regex.Match(user, @"手がかり \[([a-z0-9_]+)\]").Groups[1].Value;
            var foe = Regex.Match(user, @"- \[([a-z_0-9]+)\] [^\n]*（敵").Groups[1].Value;
            var inFight = user.Contains("# いまは戦闘中");
            var decls = new JArray();
            var section = user.Substring(Math.Max(0, user.IndexOf("# 宣言", StringComparison.Ordinal)));
            foreach (Match m in Regex.Matches(section, @"- \[([a-z0-9_]+)\] [^:：\n]*[:：] ([^\n]+)"))
            {
                var who = m.Groups[1].Value;
                var text = m.Groups[2].Value;
                var step = new JObject { ["text"] = text, ["sort"] = "auto" };
                if (inFight)
                {
                    step["sort"] = "check"; step["ability"] = "STR"; step["tier"] = "normal"; step["combat_act"] = "attack";
                    if (foe.Length > 0) step["target"] = foe;
                }
                else if (ways.OrderByDescending(w => Overlap(w.name, text)).FirstOrDefault() is var dest && dest.id != null && Overlap(dest.name, text) >= 1 && (text.Contains("向か") || text.Contains("行") || text.Contains("入") || text.Contains("戻") || text.Contains("下り")))
                    step["move_to"] = dest.id;
                else if (text.Contains("調べ") || text.Contains("読") || text.Contains("探"))
                {
                    step["sort"] = "check"; step["ability"] = "INT"; step["tier"] = "normal"; step["reason"] = "調べる";
                    if (clue.Length > 0) step["clue"] = clue;
                }
                decls.Add(new JObject { ["who"] = who, ["steps"] = new JArray(step) });
            }
            return new JObject { ["declarations"] = decls };
        }

        /// <summary>Two-character pieces of a place name found in the text, plus one when one of its kanji appears (「村へ向かう」 → 「ヘザー村の広場」).</summary>
        static int Overlap(string name, string text) => Enumerable.Range(0, Math.Max(0, name.Length - 1)).Count(i => text.Contains(name.Substring(i, 2))) + (name.Any(ch => ch > 0x4e00 && text.Contains(ch)) ? 1 : 0);

        static string Tell(string user)
        {
            var names = Regex.Match(user, "名前を出す冒険者: ([^\n]+)").Groups[1].Value;
            return $"（見本の語り）{(names.Length > 0 ? names : "一行")}の行いを、炉端の老詩人が静かに語った。霧の向こうで、何かが一行を待っている。";
        }

        static JObject Handouts(ToolDef tool)
        {
            var item = tool.Parameters["properties"]!["handouts"]!["items"]!["properties"]!;
            var who = ((JArray)item["who"]!["enum"]!).Select(x => (string)x!).ToList();
            var goals = ((JArray)item["goal"]!["enum"]!).Select(x => (string)x!).ToList();
            return new JObject
            {
                ["handouts"] = new JArray(who.Select((w, i) => new JObject
                {
                    ["who"] = w, ["bond"] = "（見本の縁）この土地には、昔の思い出がある。", ["goal"] = goals.Count > 0 ? goals[i % goals.Count] : "", ["fragment"] = i + 1,
                })),
            };
        }
    }
}
