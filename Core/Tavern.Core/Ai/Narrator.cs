#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;
using Tavern.Core.Play;

namespace Tavern.Core.Ai
{
    /// <summary>The kind of scene, decided by the game (09_Spec 2); it sets how much the narrator writes.</summary>
    public enum SceneKind { Explore, NewPlace, Talk, Tense, Combat, Rest, Climax }

    public enum Telling { Short, Standard, Long }

    /// <summary>Everything the narrator is told about a turn whose results the game has already decided.</summary>
    public sealed class TurnTelling
    {
        public SceneKind Scene { get; set; }
        public Telling Length { get; set; } = Telling.Standard;
        /// <summary>The result lines already shown (who rolled what, clues, costs).</summary>
        public List<string> Results { get; } = new List<string>();
        /// <summary>Each adventurer's public action this turn (the cover for a secret one), by id.</summary>
        public Dictionary<string, string> Public { get; } = new Dictionary<string, string>();
        /// <summary>Secret actions and their true results, by adventurer id (told only to them).</summary>
        public Dictionary<string, string> Secret { get; } = new Dictionary<string, string>();
        /// <summary>What changed in the world (people, items, clocks), in words.</summary>
        public List<string> Changes { get; } = new List<string>();
        /// <summary>A hint to slip in: its level (1-3) and where or who has it. Never the clue itself.</summary>
        public int HintLevel { get; set; }
        public string HintWhere { get; set; } = "";
        /// <summary>Words the public narration must not contain (secretly taken things, secret techniques).</summary>
        public List<string> SecretWords { get; } = new List<string>();
        /// <summary>Players' requests on the telling (語り部への注文).</summary>
        public List<string> Requests { get; } = new List<string>();
        /// <summary>The adventurers who must appear by name.</summary>
        public List<string> Names { get; } = new List<string>();
        /// <summary>An extra instruction (boss entrance, ending, act start...).</summary>
        public string Extra { get; set; } = "";
    }

    public sealed class TellingResult
    {
        public string Text { get; set; } = "";
        /// <summary>Adventurer id → what only they are told.</summary>
        public Dictionary<string, string> Private { get; } = new Dictionary<string, string>();
        public List<string> Facts { get; } = new List<string>();
        public List<(string name, string portrait, string place)> Extras { get; } = new List<(string, string, string)>();
        public List<(string name, ItemKind kind, string to)> Items { get; } = new List<(string, ItemKind, string)>();
        public List<(string person, AttitudeReason reason)> Attitudes { get; } = new List<(string, AttitudeReason)>();
        /// <summary>What the watcher found in the first telling (empty = it passed).</summary>
        public List<string> Problems { get; } = new List<string>();
        public bool Retold { get; set; }
        public int Calls { get; set; }
    }

    /// <summary>
    /// The second AI call of a turn: tells what the game decided (09_Spec 2, 4, 13). Returns the public narration, the private ones
    /// for secret actions, and small additions (facts, extras, small items, the reason an attitude changed, from fixed lists).
    /// The watcher checks the names, game words and secret leaks and asks for one retelling.
    /// </summary>
    public sealed class Narrator
    {
        public const string ToolName = "narrate";
        readonly ILlmClient _llm;
        /// <summary>Whether a retelling may be asked for (off when the day's calls run low).</summary>
        public bool AllowRetell { get; set; } = true;
        /// <summary>Portrait ids an extra may use.</summary>
        public IReadOnlyList<string> Portraits { get; set; } = new[] { "pt_elder", "pt_merchant", "pt_guard", "pt_fisher", "pt_child", "pt_mercenary", "pt_blacksmith", "pt_sailor", "pt_scholar" };

        public Narrator(ILlmClient llm) { _llm = llm; }

        public async Task<TellingResult> TellAsync(Evening e, TurnTelling t, CancellationToken ct = default)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(Role.System, Prompt),
                new ChatMessage(Role.User, UserText(e, t)),
            };
            var tool = Tool(e);
            var r = new TellingResult();
            var args = await Call(messages, tool, r, ct).ConfigureAwait(false);
            Read(e, args, r);
            var problems = Watch(e, t, r);
            r.Problems.AddRange(problems);
            if (problems.Count > 0 && AllowRetell)
            {
                // asked as a new user turn (no tool-call history, which some backends want returned with signatures)
                messages.Add(new ChatMessage(Role.User, $"いまの語り:\n{r.Text}\n\n直してほしい点: {string.Join(" / ", problems)}。\n同じ出来事を、直して語り直してください（narrate を呼ぶ）。"));
                var again = await Call(messages, tool, r, ct).ConfigureAwait(false);
                if (again.Count > 0)
                {
                    var second = new TellingResult();
                    Read(e, again, second);
                    if (second.Text.Length > 0)
                    {
                        r.Text = second.Text;
                        r.Private.Clear();
                        foreach (var kv in second.Private) r.Private[kv.Key] = kv.Value;
                        r.Retold = true;
                    }
                }
            }
            if (r.Text.Length > 0) e.NoteEnding(r.Text);
            return r;
        }

        async Task<JObject> Call(List<ChatMessage> messages, ToolDef tool, TellingResult r, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                r.Calls++;
                try
                {
                    var reply = await _llm.ChatAsync(messages, new[] { tool }, ToolName, ct).ConfigureAwait(false);
                    var args = reply.ToolCalls.FirstOrDefault(c => c.Name == ToolName)?.Arguments;
                    if (args != null && args["text"] == null) args = args.Properties().Select(p => p.Value).OfType<JObject>().FirstOrDefault(o => o["text"] != null) ?? args;
                    if (args != null && ((string?)args["text"] ?? "").Length > 0) return args;
                    if (reply.Content.Trim().Length > 0) return new JObject { ["text"] = reply.Content.Trim() };
                }
                catch (InvalidOperationException) { }
                if (!AllowRetell) break;   // the retry uses the retelling's place in the budget
            }
            return new JObject();
        }

        // ---------------------------------------------------------------- prompt

        public const string Prompt = @"あなたは酒場の老吟遊詩人オルウェン。温かく、少し話を盛る語り部です。剣と魔法の冒険を、炉端で語るように語ります。
ゲームがすでに決めた結果だけを語ります。結果を変えない、足さない。必ず narrate を 1 回呼びます。
- 冒険者の台詞・気持ち・行動を勝手に決めない。宣言にない冒険者の動きを足さない。宣言した冒険者は全員、名前を出して触れる。
- 地の文は常体（〜だ・〜した・〜である）でそろえる。です・ます調は使わない。「」の中の台詞だけは、その人物の口調のまま。
- ゲームの言葉（HP、ダメージ、判定、成功・失敗の言い方、ダイス、出目、手番、第 1 幕のような幕の番号、気力、運命のコイン、歴戦の印）を使わない。結果は情景と手応えで描く。
- 長くするのは情景・五感・NPC の反応・空気で。1 文は 40 字ほど。指定の文の数を守る。
- 雰囲気は王道寄りで、ときどき深い闇。流血や死ははっきり、生々しい描写は避ける。
- 内緒の行動は、本人だけの語り（private）に本当のことを書く。表の語り（text）には表向きの行動だけを書き、内緒で得た物・技・手がかりの名前を出さない。世界に跡が残るなら、誰がやったかは語らずに描く。
- ヒントを頼まれたら、手がかりそのものは言わず、NPC の台詞・目に入る物・物音や匂いで、ありかへ気を向けさせる。
- 直前の語りの締めくくりと同じ締め方をしない。
- 付け足しは小さく: 事実メモ（facts）、端役（extras。場面にまだいない新しい人だけ。名前と立ち絵）、小さな物（items。名品は不可）、人物の態度が変わった理由（attitudes。一覧から）。なければ空で。";

        static readonly Dictionary<SceneKind, (string label, string[] sizes)> Lengths = new Dictionary<SceneKind, (string, string[])>
        {
            [SceneKind.Explore] = ("探索", new[] { "場面 1 文 + 1 人 1 文", "場面 1〜2 文 + 1 人 1〜2 文", "場面 2〜3 文 + 1 人 2〜3 文" }),
            [SceneKind.NewPlace] = ("初めて入る場所", new[] { "3 文", "5〜7 文", "8〜10 文" }),
            [SceneKind.Talk] = ("会話", new[] { "台詞 + 1 文", "台詞 + 2〜3 文", "台詞 + 4 文" }),
            [SceneKind.Tense] = ("緊迫", new[] { "1〜2 文", "2〜3 文", "3〜4 文" }),
            [SceneKind.Combat] = ("戦闘", new[] { "1 人 1 文", "1 人 1〜2 文 + まとめ", "1 人 2 文 + 情景" }),
            [SceneKind.Rest] = ("休息", new[] { "2〜3 文", "4〜6 文", "6〜8 文" }),
            [SceneKind.Climax] = ("山場", new[] { "5〜6 文", "8〜10 文", "10〜14 文" }),
        };

        public static string LengthText(SceneKind k, Telling len) => $"{Lengths[k].label}の場面。語りの量: {Lengths[k].sizes[(int)len]}";

        string UserText(Evening e, TurnTelling t)
        {
            var b = new StringBuilder();
            b.AppendLine(SceneText.All(e));
            b.AppendLine("# この手");
            b.AppendLine(LengthText(t.Scene, t.Length));
            if (t.Scene == SceneKind.Tense) b.AppendLine("緊迫: 短く切る。音と時間の圧で。");
            b.AppendLine("宣言（表向き）:");
            foreach (var kv in t.Public) b.AppendLine($"- {e.Find(kv.Key)?.Name ?? kv.Key}: {kv.Value}");
            b.AppendLine("ゲームが決めた結果:");
            foreach (var l in t.Results) b.AppendLine("- " + l);
            foreach (var c in t.Changes) b.AppendLine("- " + c);
            if (t.Secret.Count > 0)
            {
                b.AppendLine("内緒の行動（本人だけの語り private に。表の語りには出さない）:");
                foreach (var kv in t.Secret) b.AppendLine($"- [{kv.Key}] {e.Find(kv.Key)?.Name}: {kv.Value}");
            }
            if (t.HintLevel > 0)
                b.AppendLine($"ヒント（段 {t.HintLevel}: {(t.HintLevel == 1 ? "気配" : t.HintLevel == 2 ? "糸口" : "持ち主が向こうから来る")}）: {t.HintWhere} へ気を向けさせる。中身は言わない。");
            if (t.Names.Count > 0) b.AppendLine("名前を出す冒険者: " + string.Join("、", t.Names));
            if (e.LastEndings.Count > 0) b.AppendLine("直前の語りの締めくくり（同じ締め方を避ける）: " + string.Join(" / ", e.LastEndings));
            if (t.Requests.Count > 0) b.AppendLine("語り方への注文（語り方だけ変える。ルール・数字・秘密は変えない）: " + string.Join(" / ", t.Requests));
            if (e.FirstEvening) b.AppendLine("この一行の最初の晩: 語りの中で、宣言は文章で自由に書けることなどに、台詞でさりげなく触れてよい。");
            if (t.Extra.Length > 0) b.AppendLine(t.Extra);
            return b.ToString();
        }

        ToolDef Tool(Evening e)
        {
            JObject S(string d) => new JObject { ["type"] = "string", ["description"] = d };
            JObject Arr(JObject item) => new JObject { ["type"] = "array", ["items"] = item };
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["text"] = S("表の語り"),
                    ["private"] = Arr(new JObject { ["type"] = "object", ["properties"] = new JObject { ["who"] = S("冒険者の id"), ["text"] = S("本人だけの語り") }, ["required"] = new JArray("who", "text") }),
                    ["facts"] = Arr(S("語りで決まった小さな事実")),
                    ["extras"] = Arr(new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { ["name"] = S("端役の名前"), ["portrait"] = new JObject { ["type"] = "string", ["enum"] = new JArray(Portraits) }, ["place"] = S("場所の id") },
                        ["required"] = new JArray("name"),
                    }),
                    ["items"] = Arr(new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject { ["name"] = S("小さな物の名前"), ["kind"] = new JObject { ["type"] = "string", ["enum"] = new JArray("tool", "consumable", "treasure") }, ["to"] = S("受け取る冒険者の id") },
                        ["required"] = new JArray("name", "kind", "to"),
                    }),
                    ["attitudes"] = Arr(new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["person"] = S("人物の id"),
                            ["reason"] = new JObject { ["type"] = "string", ["enum"] = new JArray("saved_life", "granted_wish", "gift", "lie_found", "hurt_loved_one", "attacked", "killed_before_them") },
                        },
                        ["required"] = new JArray("person", "reason"),
                    }),
                },
                ["required"] = new JArray("text"),
            };
            return new ToolDef(ToolName, "この手の出来事を語る", schema);
        }

        void Read(Evening e, JObject a, TellingResult r)
        {
            r.Text = Clean((string?)a["text"] ?? "");
            foreach (var p in (a["private"] as JArray ?? new JArray()).OfType<JObject>())
                if ((string?)p["who"] is string who && e.Find(who) != null && (string?)p["text"] is string pt && pt.Length > 0) r.Private[who] = Clean(pt);
            foreach (var f in (a["facts"] as JArray ?? new JArray())) if ((string?)f is string fs && fs.Trim().Length > 0) r.Facts.Add(fs.Trim());
            foreach (var x in (a["extras"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var name = ((string?)x["name"] ?? "").Trim();
                // an extra is someone new: not a person of the sheet, not an enemy in the fight, not a short form of either
                bool Same(string other) => other.Length > 0 && (other == name || other.Contains(name) || name.Contains(other));
                if (name.Length == 0 || e.World.Cast.People.Any(p => Same(p.Name)) || (e.Combat?.Foes.Any(f => Same(f.Name)) ?? false) || e.Party.Any(a => Same(a.Name))) continue;
                var portrait = (string?)x["portrait"] ?? "";
                if (!Portraits.Contains(portrait)) portrait = Portraits[0];
                r.Extras.Add((name, portrait, (string?)x["place"] ?? ""));
            }
            foreach (var x in (a["items"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var name = ((string?)x["name"] ?? "").Trim();
                var to = (string?)x["to"] ?? "";
                if (name.Length == 0 || e.Find(to) == null) continue;
                var kind = ((string?)x["kind"]) switch { "consumable" => ItemKind.Consumable, "treasure" => ItemKind.Treasure, _ => ItemKind.Tool };
                r.Items.Add((name, kind, to));
            }
            foreach (var x in (a["attitudes"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var person = (string?)x["person"] ?? "";
                if (e.World.Cast.Find(person) == null) continue;
                AttitudeReason? why = ((string?)x["reason"]) switch
                {
                    "saved_life" => AttitudeReason.SavedLife, "granted_wish" => AttitudeReason.GrantedWish, "gift" => AttitudeReason.Gift,
                    "lie_found" => AttitudeReason.LieFound, "hurt_loved_one" => AttitudeReason.HurtLovedOne, "attacked" => AttitudeReason.Attacked,
                    "killed_before_them" => AttitudeReason.KilledBeforeThem, _ => null,
                };
                if (why != null) r.Attitudes.Add((person, why.Value));
            }
        }

        /// <summary>Strips stray markup the model sometimes adds.</summary>
        static string Clean(string s) => s.Replace("**", "").Trim();

        // ---------------------------------------------------------------- the watcher

        /// <summary>Polite endings in the narration (outside quoted speech): the telling keeps the plain style.</summary>
        static readonly Regex Polite = new Regex(@"(です|ます|ました|でした|ましょう|ません)(?=[。！？\n]|$)");
        static readonly Regex Quoted = new Regex(@"「[^」]*」|『[^』]*』");

        static readonly Regex GameWords = new Regex(@"HP|ＨＰ|ダメージ|判定|ダイス|出目|手番|ターン|気力|運命のコイン|歴戦の印|難易度|第\s*[0-9０-９一二三四五]\s*幕");

        /// <summary>
        /// 09_Spec 2: every named adventurer appears, no game words, nothing secret in the public telling.
        /// (Moves the players did not declare cannot be found by a program; the prompt says it strongly instead.)
        /// </summary>
        public static List<string> Watch(Evening e, TurnTelling t, TellingResult r)
        {
            var problems = new List<string>();
            if (r.Text.Length == 0) { problems.Add("語りが空"); return problems; }
            var missing = t.Names.Where(n => !Mentions(r.Text, n)).ToList();
            if (missing.Count > 0) problems.Add("名前が出ていない冒険者: " + string.Join("、", missing));
            var words = GameWords.Matches(r.Text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
            if (words.Count > 0) problems.Add("ゲームの言葉を使っている: " + string.Join("、", words));
            var polite = new[] { r.Text }.Concat(r.Private.Values).SelectMany(t => Polite.Matches(Quoted.Replace(t, "")).Cast<Match>().Select(m => m.Value)).Distinct().ToList();
            if (polite.Count > 0) problems.Add("地の文に です・ます調 が混ざっている（" + string.Join("、", polite) + "）。地の文は常体（〜だ・〜した）でそろえる");
            var leaks = t.SecretWords.Where(w => w.Length > 0 && r.Text.Contains(w)).ToList();
            if (leaks.Count > 0) problems.Add("表の語りに内緒のことが出ている: " + string.Join("、", leaks));
            foreach (var who in t.Secret.Keys) if (!r.Private.ContainsKey(who)) problems.Add($"内緒の行動をした {e.Find(who)?.Name} だけの語りがない");
            return problems;
        }

        /// <summary>A name counts when it appears whole or by its last part (「村長オズリック」 → 「オズリック」).</summary>
        static bool Mentions(string text, string name) => text.Contains(name) || (name.Length > 3 && text.Contains(name.Substring(name.Length - 3)));
    }
}
