#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;
using Tavern.Core.Play;

namespace Tavern.Core.Ai
{
    /// <summary>One adventurer's handout: a bond, a personal goal from the sheet's candidates, a fragment from the sheet's list.</summary>
    public sealed class Handout
    {
        public string Who { get; set; } = "";
        public string Bond { get; set; } = "";
        public string GoalId { get; set; } = "";
        public string Fragment { get; set; } = "";
        /// <summary>Shown to the other players with "見せる".</summary>
        public bool Shown { get; set; }
    }

    /// <summary>A loose end the next evenings may bring back (09_Spec 11). Kinds are fixed; the game lists the candidates.</summary>
    public sealed class Bond
    {
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Evenings since it was made without being used (gone after three).</summary>
        public int Idle { get; set; }
    }

    /// <summary>What the end-of-evening call writes: the record book's story, rumours, bonds, the veteran marks' names.</summary>
    public sealed class Chronicle
    {
        public string Title { get; set; } = "";
        public string Story { get; set; } = "";
        public List<string> Rumors { get; } = new List<string>();
        public List<Bond> Bonds { get; } = new List<Bond>();
        /// <summary>Adventurer id → the name of the mark they earned tonight.</summary>
        public Dictionary<string, string> Marks { get; } = new Dictionary<string, string>();
        public int Calls { get; set; }
    }

    /// <summary>
    /// The two calls around the evening (09_Spec 11, 13): handouts for everyone in one call when the story is chosen,
    /// and the chronicle, rumours, bonds and mark names in one call at the end.
    /// </summary>
    public sealed class Bookends
    {
        readonly ILlmClient _llm;
        public Bookends(ILlmClient llm) { _llm = llm; }

        public const string HandoutPrompt = @"あなたは酒場の語り部オルウェンです。今夜の物語を始める前に、冒険者一人ずつに、本人だけが読むハンドアウトを配ります。必ず handouts を 1 回呼びます。
- 入れてよいもの: 縁（物語の人や場所と、その冒険者とのつながり。生い立ちに沿って 1〜2 文）/ 個人の目的（候補の id から 1 つ）/ 断片（一覧の番号から 0〜1 つ。全員に配らなくてよい）。
- 入れないもの: 真相、結論そのもの、弱点の使い方、黒幕の名前。
- 個人の目的は、仲間とぶつかるものは一晩に 1 つまで。同じ目的を二人に配らない。";

        public async Task<List<Handout>> HandoutsAsync(Evening e, CancellationToken ct = default, IReadOnlyList<Bond>? bonds = null)
        {
            var s = e.Story;
            var b = new StringBuilder();
            b.AppendLine($"# 物語「{s.Title}」（{s.Kind}）\n導入: {s.Intro}\n依頼の目的: {s.GoalText}");
            b.AppendLine("人物: " + string.Join("、", s.People.Where(p => p.Enemy.Length == 0).Select(p => $"{p.Name}（{p.Role}）")));
            b.AppendLine("場所: " + string.Join("、", s.Places.Select(p => p.Name)));
            b.AppendLine("個人の目的の候補:");
            foreach (var g in s.PersonalGoals) b.AppendLine($"- [{g.Id}] {g.Text}");
            b.AppendLine("断片:");
            for (int i = 0; i < s.Fragments.Count; i++) b.AppendLine($"- {i + 1}. {s.Fragments[i]}");
            if (bonds != null && bonds.Count > 0 && e.Story.Hooks.Count > 0)
            {
                b.AppendLine("前の晩からの因縁（1 つだけ、差し込み口に味付けとして入れてよい。本筋や黒幕にはしない）:");
                for (int i = 0; i < bonds.Count; i++) b.AppendLine($"- {i + 1}. {bonds[i].Kind}: {bonds[i].Text}");
                b.AppendLine("差し込み口: " + string.Join(" / ", e.Story.Hooks.Select((h, i) => $"{i + 1}. {e.Story.PlaceOf(h.Place)?.Name}: {h.Role}")));
            }
            b.AppendLine("冒険者:");
            foreach (var a in e.Party) b.AppendLine($"- [{a.Id}] {a.Name}（{a.Class.Name}）生い立ち: {(a.Background.Length > 0 ? a.Background : "（なし）")}");
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["handouts"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["who"] = new JObject { ["type"] = "string", ["enum"] = new JArray(e.Party.Select(a => a.Id)) },
                                ["bond"] = new JObject { ["type"] = "string" },
                                ["goal"] = new JObject { ["type"] = "string", ["enum"] = new JArray(s.PersonalGoals.Select(g => g.Id)) },
                                ["fragment"] = new JObject { ["type"] = "integer", ["description"] = "断片の番号（なしは 0）" },
                            },
                            ["required"] = new JArray("who", "bond", "goal"),
                        },
                    },
                    ["hook"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "因縁を差し込むなら 1 つだけ",
                        ["properties"] = new JObject { ["bond"] = new JObject { ["type"] = "integer" }, ["hook"] = new JObject { ["type"] = "integer" }, ["text"] = new JObject { ["type"] = "string", ["description"] = "その場所に置く脇役や出来事を 1〜2 文" } },
                    },
                },
                ["required"] = new JArray("handouts"),
            };
            var reply = await _llm.ChatAsync(new[] { new ChatMessage(Role.System, HandoutPrompt), new ChatMessage(Role.User, b.ToString()) },
                new[] { new ToolDef("handouts", "ハンドアウトを配る", schema) }, "handouts", ct).ConfigureAwait(false);
            var args = Classifier.Unwrap(reply.ToolCalls.FirstOrDefault()?.Arguments) ?? new JObject();
            var list = new List<Handout>();
            var usedGoals = new HashSet<string>();
            foreach (var h in (args["handouts"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var who = (string?)h["who"] ?? "";
                if (e.Find(who) == null || list.Any(x => x.Who == who)) continue;
                var goal = (string?)h["goal"] ?? "";
                if (!s.PersonalGoals.Any(g => g.Id == goal) || !usedGoals.Add(goal)) goal = "";
                var f = (int?)h["fragment"] ?? 0;
                list.Add(new Handout { Who = who, Bond = ((string?)h["bond"] ?? "").Trim(), GoalId = goal, Fragment = f >= 1 && f <= s.Fragments.Count ? s.Fragments[f - 1] : "" });
            }
            // a bond from earlier evenings, placed at one of the story's hooks as flavour (the narrator is told through the facts)
            if (bonds != null && args["hook"] is JObject hk && (int?)hk["bond"] is int bi && bi >= 1 && bi <= bonds.Count && (int?)hk["hook"] is int hi && hi >= 1 && hi <= s.Hooks.Count)
            {
                var hook = s.Hooks[hi - 1];
                e.Facts.Add($"因縁（{bonds[bi - 1].Kind}: {bonds[bi - 1].Text}）が{s.PlaceOf(hook.Place)?.Name}に差し込まれている: {((string?)hk["text"] ?? hook.Role).Trim()}");
                bonds[bi - 1].Idle = -1;   // used: settled after tonight
            }
            // anyone the answer left out (or without a goal) gets a free candidate, so every handout has a goal
            foreach (var a in e.Party.Where(a => list.All(x => x.Who != a.Id))) list.Add(new Handout { Who = a.Id });
            foreach (var h in list.Where(h => h.GoalId.Length == 0))
            {
                var free = s.PersonalGoals.FirstOrDefault(g => usedGoals.Add(g.Id));
                if (free != null) h.GoalId = free.Id;
            }
            return list;
        }

        public const string ChroniclePrompt = @"あなたは酒場の語り部オルウェンです。今夜の冒険が終わりました。記録帳に残す短い物語と、酒場に流れる噂、次の晩へ持ち越す因縁、
生き延びた冒険者の「歴戦の印」の名前を書きます。必ず chronicle を 1 回呼びます。
- 記録帳（story）: 今夜の出来事を 6〜10 文で。結末の札と事実を変えない。失敗した晩も、死者も、正直に。
- 噂（rumors）: 酒場で客が話す 1〜2 行。
- 因縁（bonds）: 候補の種類から 0〜2 件。次の晩の物語に味付けとして差し込む種（本筋や黒幕にはしない）。
- 歴戦の印（marks）: 生き延びた冒険者ごとに、今夜の出来事から 12 字ほどの名前（例: 骨の騎士を断った）。";

        public async Task<Chronicle> ChronicleAsync(Evening e, string endingSummary, IReadOnlyList<string> log, IReadOnlyList<string> bondCandidates, CancellationToken ct = default)
        {
            var b = new StringBuilder();
            b.AppendLine($"# 物語「{e.Story.Title}」\n{endingSummary}");
            b.AppendLine("今夜の流れ（抜粋）:");
            foreach (var l in log.Skip(Math.Max(0, log.Count - 40))) b.AppendLine("- " + l);
            b.AppendLine("因縁の候補: " + (bondCandidates.Count > 0 ? string.Join(" / ", bondCandidates) : "（なし）"));
            b.AppendLine("生き延びた冒険者: " + string.Join("、", e.Party.Where(a => a.Life != Life.Dead).Select(a => $"[{a.Id}] {a.Name}")));
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "記録帳の題" },
                    ["story"] = new JObject { ["type"] = "string" },
                    ["rumors"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                    ["bonds"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["kind"] = new JObject { ["type"] = "string", ["enum"] = new JArray("逃がした敵", "交わした約束", "恨みを買った", "恩を売った", "解けずに残った謎") },
                                ["text"] = new JObject { ["type"] = "string" },
                            },
                            ["required"] = new JArray("kind", "text"),
                        },
                    },
                    ["marks"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject { ["who"] = new JObject { ["type"] = "string" }, ["name"] = new JObject { ["type"] = "string" } },
                            ["required"] = new JArray("who", "name"),
                        },
                    },
                },
                ["required"] = new JArray("title", "story"),
            };
            var reply = await _llm.ChatAsync(new[] { new ChatMessage(Role.System, ChroniclePrompt), new ChatMessage(Role.User, b.ToString()) },
                new[] { new ToolDef("chronicle", "記録帳・噂・因縁・印の名前を書く", schema) }, "chronicle", ct).ConfigureAwait(false);
            var o = reply.ToolCalls.FirstOrDefault()?.Arguments ?? new JObject();
            if (o["story"] == null) o = o.Properties().Select(p => p.Value).OfType<JObject>().FirstOrDefault(x => x["story"] != null) ?? o;
            var c = new Chronicle { Title = ((string?)o["title"] ?? e.Story.Title).Trim(), Story = ((string?)o["story"] ?? "").Trim(), Calls = 1 };
            foreach (var r in (o["rumors"] as JArray ?? new JArray()).Take(2)) if ((string?)r is string rs && rs.Trim().Length > 0) c.Rumors.Add(rs.Trim());
            foreach (var x in (o["bonds"] as JArray ?? new JArray()).OfType<JObject>().Take(2))
                if ((string?)x["kind"] is string k && bondCandidates.Any(bc => bc.StartsWith(k))) c.Bonds.Add(new Bond { Kind = k, Text = (string?)x["text"] ?? "" });
            foreach (var m in (o["marks"] as JArray ?? new JArray()).OfType<JObject>())
                if ((string?)m["who"] is string who && e.Find(who)?.Life != Life.Dead && e.Find(who) != null)
                    c.Marks[who] = Trim((string?)m["name"] ?? "", 16);
            foreach (var a in e.Party.Where(a => a.Life != Life.Dead && !c.Marks.ContainsKey(a.Id))) c.Marks[a.Id] = Trim(e.Story.Title + "を生き延びた", 16);
            if (c.Story.Length == 0) c.Story = endingSummary;
            return c;
        }

        /// <summary>
        /// After an evening: a bond used tonight is settled, one unused for three evenings fades; the rest wait one more evening.
        /// New bonds from tonight's chronicle are added by the caller.
        /// </summary>
        public static void AgeBonds(List<Bond> bonds)
        {
            bonds.RemoveAll(b => b.Idle < 0 || b.Idle >= 2);
            foreach (var b in bonds) b.Idle++;
        }

        static string Trim(string s, int max) => s.Trim().Length > max ? s.Trim().Substring(0, max) : s.Trim();
    }
}
