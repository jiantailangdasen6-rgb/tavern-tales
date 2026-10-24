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
        public Classifier Classifier { get; }
        public Narrator Narrator { get; }
        public Narrator Highlight { get; }
        public Bookends Bookends { get; }
        /// <summary>The everyday calls today (shown to the host).</summary>
        public CallMeter? Meter { get; private set; }
        public string Description { get; private set; } = "";
        public bool Demo { get; private set; }
        public bool Ready => Classifier != null;

        /// <summary>The model that told the last narration with the paid key ("Flash", or "Flash-Lite" when Flash was slow and it stood in).</summary>
        public string LastVoice { get; private set; } = "";

        // The table keeps these for the whole evening; Switch puts other backends behind them (09_Spec 13: the host compares models in a test play).
        readonly Swap _sort = new Swap(), _tell = new Swap(), _strong = new Swap();
        bool _testKey;
        Func<string, int>? _usedToday;
        /// <summary>The object whose LastVoice this one's backends set (the evening's after a Switch hands them over).</summary>
        NightAi _owner;

        NightAi()
        {
            _owner = this;
            Classifier = new Classifier(_sort);
            Narrator = new Narrator(_tell);
            Highlight = new Narrator(_strong);
            Bookends = new Bookends(_tell);
        }

        static HttpClient? _http;
        static HttpClient Http => _http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        /// <summary>The key's name in the call counter file ("play" or "test").</summary>
        public string KeyName { get; private set; } = "play";

        /// <summary>The narration models for the paid key (App.AiChoice.ModelNames): everyday narration, then the boss's entrance and the ending.</summary>
        static readonly (string everyday, string strong)[] PaidModels =
        {
            ("gemini-3.5-flash-lite", "gemini-3.8-flash"),
            ("gemini-3.8-flash", "gemini-3.8-flash"),
            ("gemini-3.8-flash", "gemini-pro-latest"),
        };

        /// <summary>
        /// paid: GEMINI_API_KEY_PAID (prepaid credit) instead of the free tier; model picks the narration (PaidModels). The sorting
        /// always stays on Flash-Lite with minimal thinking, which the classifier's prompt and safety nets were tuned on.
        /// </summary>
        public static NightAi Create(bool demo, bool testKey, Func<string, int>? usedToday = null, bool paid = false, int model = 0)
        {
            var ai = new NightAi { _testKey = testKey, _usedToday = usedToday };
            if (demo)
            {
                var d = new DemoNightLlm();
                ai.Set(d, d);
                ai.Demo = true;
                ai.Description = "AI: 見本（-demo）";
                return ai;
            }
            paid &= !testKey;
            var name = testKey ? "GEMINI_API_KEY_TEST" : paid ? "GEMINI_API_KEY_PAID" : "GEMINI_API_KEY";
            var key = Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(key))
            {
                var d = new DemoNightLlm();
                ai.Set(d, d);
                ai.Demo = true;
                ai.Description = $"AI: {name} が見つからないので見本で動いています";
                return ai;
            }
            ai.KeyName = testKey ? "test" : paid ? "paid" : "play";
            ai.Paid = paid;
            // the paid key has no daily quota; its meter is only a guard against a runaway evening
            var meter = new CallMeter(new GeminiClient(Http, key!, "gemini-3.5-flash-lite", "minimal"), paid ? 3000 : 500, usedToday?.Invoke(ai.KeyName) ?? 0);
            if (!paid)
            {
                var strong = new FallbackLlm(new GeminiClient(Http, key!, "gemini-3.8-flash", "low"), meter) { Patience = TimeSpan.FromSeconds(40) };
                ai.Set(meter, meter, strong);
                ai.Description = "AI: Gemini（無料枠）" + (testKey ? "・試験用のキー" : "");
            }
            else
            {
                var (everyday, strongModel) = PaidModels[Math.Clamp(model, 0, PaidModels.Length - 1)];
                // a richer narrator falls back to Flash-Lite when it is slow or fails, like the strong one always did
                var lite = ai.Voice("Flash-Lite", meter);
                ILlmClient tell = everyday == "gemini-3.5-flash-lite" ? lite
                    : new FallbackLlm(ai.Voice("Flash", ai.Count(new GeminiClient(Http, key!, everyday, "low"))), lite) { Patience = TimeSpan.FromSeconds(30) };
                var pro = strongModel.Contains("pro");
                var strong = new FallbackLlm(ai.Voice(pro ? "Pro" : "Flash", ai.Count(new GeminiClient(Http, key!, strongModel, "low"))), lite)
                    { Patience = TimeSpan.FromSeconds(pro ? 60 : 40) };
                ai.Set(meter, tell, strong);
                ai.Description = $"AI: Gemini（有料キー・{App.AiChoice.ModelNames[Math.Clamp(model, 0, PaidModels.Length - 1)]}）";
            }
            ai.Meter = meter;
            return ai;
        }

        /// <summary>The paid key is in use (no daily quota to show, only the count).</summary>
        public bool Paid { get; private set; }

        /// <summary>Flash and Pro calls with the paid key, counted apart from the Flash-Lite meter (shown with it).</summary>
        readonly List<CallMeter> _rich = new List<CallMeter>();
        CallMeter Count(ILlmClient inner) { var m = new CallMeter(inner, int.MaxValue); _rich.Add(m); return m; }

        ILlmClient Voice(string name, ILlmClient inner) => new Swap { Inner = inner, Answered = () => _owner.LastVoice = name };

        void Set(ILlmClient sort, ILlmClient tell, ILlmClient strong)
        {
            _sort.Inner = sort;
            _tell.Inner = tell;
            _strong.Inner = strong;
        }

        /// <summary>
        /// The host changed the AI at the table: the next call goes to the new one (a call already sent finishes on the old).
        /// False when the new choice has no key on this PC, or this is the -demo run; the evening then keeps its AI.
        /// </summary>
        public bool Switch(bool paid, int model)
        {
            if (Demo && Description.Contains("-demo")) return false;
            var next = Create(false, _testKey, _usedToday, paid, model);
            if (next.Demo) return false;
            next._owner = this;
            Set(next._sort.Inner, next._tell.Inner, next._strong.Inner);
            Meter = next.Meter;
            Description = next.Description;
            KeyName = next.KeyName;
            Paid = next.Paid;
            Demo = false;
            LastVoice = "";
            _rich.Clear();
            _rich.AddRange(next._rich);
            Narrator.AllowRetell = Highlight.AllowRetell = Meter?.Low != true;
            return true;
        }

        /// <summary>Passes calls to Inner, which Switch may replace between calls; Answered runs after each reply.</summary>
        sealed class Swap : ILlmClient
        {
            public ILlmClient Inner = null!;
            public Action? Answered;
            public string Name => Inner.Name;

            public async Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
            {
                var reply = await Inner.ChatAsync(messages, tools, forceTool, ct).ConfigureAwait(false);
                Answered?.Invoke();
                return reply;
            }
        }

        void Set(ILlmClient everyday, ILlmClient strong) => Set(everyday, everyday, strong);

        /// <summary>"AI 残り 431 回（16:00 に戻る）"; with the paid key "有料キー・今日 52 回"; empty for the demo.</summary>
        public string QuotaText() => Meter == null ? "" : Paid ? $"AI 有料キー・今日 {Meter.Used + _rich.Sum(m => m.Used)} 回" + (LastVoice.Length > 0 ? $"・直前の語り {LastVoice}" : "") :
            $"AI 残り {Meter.Left} 回（{Meter.ResetsAt.ToOffset(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)):HH:mm} に戻る）";
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
