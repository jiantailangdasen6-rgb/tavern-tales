#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Content;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Game
{
    /// <summary>
    /// Makes an evening's scenario sheet from a story skeleton (Docs/00_Decisions.md "シナリオ"): the strong model fills the
    /// template with the fixed world, a drawn twist and boss, the players' backgrounds and an optional one-line request,
    /// in one forced call. The game then checks and repairs the sheet (only existing art and enemies, a connected map,
    /// the tavern first, the boss at the end), so a bad answer never breaks the evening.
    /// </summary>
    public static class ScenarioGenerator
    {
        public const string ToolName = "write_scenario";

        public static async Task<ScenarioDef> GenerateAsync(ILlmClient llm, StoryContent story, ContentDb db, TemplateDef template,
            IReadOnlyList<Character> party, string? request, IRandom rng, CancellationToken ct = default)
        {
            var bosses = template.Bosses.Where(b => db.Enemies.TryGetValue(b, out var e) && e.Boss).ToList();
            if (bosses.Count == 0) bosses = db.Enemies.Values.Where(e => e.Boss).Select(e => e.Id).ToList();
            var boss = bosses[rng.Next(0, bosses.Count)];
            var twist = template.Twists.Count > 0 ? template.Twists[rng.Next(0, template.Twists.Count)] : "";
            var tool = Tool(story, db, bosses);
            var messages = new List<ChatMessage>
            {
                new ChatMessage(Role.System, "あなたは酒場で冒険を語る語り部であり、今夜のテーブル RPG のシナリオを書く作家です。日本語で書きます。"),
                new ChatMessage(Role.User, Request(story, db, template, party, request, boss, twist)),
            };
            var reply = await llm.ChatAsync(messages, new[] { tool }, ToolName, ct).ConfigureAwait(false);
            var call = reply.ToolCalls.FirstOrDefault(c => c.Name == ToolName) ?? throw new InvalidOperationException("the AI did not write a scenario");
            var s = call.Arguments.ToObject<ScenarioDef>() ?? new ScenarioDef();
            s.Id = "gen_" + DateTime.Now.ToString("yyyyMMddHHmmss");
            s.Template = template.Name;
            Repair(s, story, db, boss);
            return s;
        }

        static string Request(StoryContent story, ContentDb db, TemplateDef t, IReadOnlyList<Character> party, string? request, string boss, string twist)
        {
            var sb = new StringBuilder();
            sb.AppendLine("今夜、最大 3 人の冒険者が 2〜3 時間で遊び終える一晩のシナリオの設定表を write_scenario で書いてください。");
            sb.AppendLine();
            sb.AppendLine("【世界（必ずこの世界の中の話にする）】");
            sb.AppendLine(story.World.Describe());
            sb.AppendLine($"【物語の型】{t.Name}: {t.Premise}");
            sb.AppendLine("幕の流れ: " + string.Join(" → ", t.Acts.Select((a, i) => $"第{i + 1}幕 {a}")));
            sb.AppendLine("登場人物の役: " + string.Join("、", t.Roles));
            sb.AppendLine($"ボス: {db.Enemies[boss].Name}（{boss}）。くじで決まった物語のひねり: {twist}");
            sb.AppendLine();
            sb.AppendLine("【冒険者たち】少なくとも一人の生い立ちを、事件や人物に結びつけて織り込むこと。");
            foreach (var p in party) sb.AppendLine($"- {p.Name}（{Prompts.ClassLabel(p.ClassId)}）{(string.IsNullOrWhiteSpace(p.Background) ? "" : "生い立ち: " + p.Background)}");
            if (!string.IsNullOrWhiteSpace(request)) sb.AppendLine($"【遊ぶ人からの注文】{request}（世界観を壊さない範囲で取り入れる）");
            sb.AppendLine();
            sb.AppendLine("【決まり】");
            sb.AppendLine("- 場所は 7〜9 か所。最初の場所は酒場「眠る竜亭」（id: tavern、art: bg_tavern、act: 1）で、依頼が舞い込む。");
            sb.AppendLine("- exits で場所をつなぎ、酒場から最後の場所までたどれる地図にする（ところどころ寄り道の枝分かれ）。各幕に 2〜3 か所。");
            sb.AppendLine("- 各場所の art は一覧から内容がいちばん近い絵を選ぶ（同じ絵を何か所で使ってもよい）。");
            sb.AppendLine("- 戦いは encounter に敵の id を並べる。第 1 幕に 1 回、第 2 幕に 1〜2 回（2〜3 体まで）。最後の場所の encounter の先頭にボス。");
            sb.AppendLine("- 人物は 2〜4 人（art は肖像の一覧から）。手がかりは 4〜6 個（where は見つかる場所の id）。");
            sb.AppendLine("- 人物の名前に、語り部（オルウェン・ブラム・ヴェイル・マルタ）や冒険者と同じ名前を使わない。");
            sb.AppendLine("- 弱点・鍵（weakness）: ボス戦を楽にする方法を一つ。手がかりで知ることができる。");
            sb.AppendLine("- secret には冒険者が調べて初めて分かることを書く。truth には物語の真相をまとめる。");
            sb.AppendLine("- 結末は success（成功）/ bitter（苦い成功）/ failure（失敗）の 3 つ。");
            sb.AppendLine("- id は半角の英小文字と _ だけ。名前の付け方: 固有名詞はカタカナ（灰色の森グレイウッドのように日本語と組み合わせてよい）。");
            return sb.ToString();
        }

        static ToolDef Tool(StoryContent story, ContentDb db, List<string> bosses)
        {
            JObject Str(string d = "") => new JObject { ["type"] = "string", ["description"] = d };
            JObject Enum(IEnumerable<string> v) => new JObject { ["type"] = "string", ["enum"] = new JArray(v.ToArray()) };
            JObject Arr(JObject item, int min = 0) { var o = new JObject { ["type"] = "array", ["items"] = item }; if (min > 0) o["minItems"] = min; return o; }
            JObject Obj(JObject props, params string[] req) => new JObject { ["type"] = "object", ["properties"] = props, ["required"] = new JArray(req) };
            var bg = story.Catalog.Backgrounds.Select(b => b.Id).ToList();
            var bgHelp = string.Join(" / ", story.Catalog.Backgrounds.Select(b => $"{b.Id}={b.About}"));
            var ptHelp = string.Join(" / ", story.Catalog.Portraits.Select(b => $"{b.Id}={b.About}"));
            var enemyHelp = string.Join(" / ", db.Enemies.Values.Select(e => $"{e.Id}={e.Name}{(e.Boss ? "（ボス）" : "")}"));
            return new ToolDef(ToolName, "一晩のシナリオの設定表を書く。",
                Obj(new JObject
                {
                    ["title"] = Str("題（例: 灰色の森の祠）"),
                    ["hook"] = Str("導入: 依頼が舞い込む経緯"),
                    ["truth"] = Str("真相（冒険者には最初は秘密）"),
                    ["weakness"] = Str("ボスの弱点・物語の鍵"),
                    ["acts"] = Arr(Obj(new JObject { ["title"] = Str(), ["goal"] = Str("この幕の目標") }, "title", "goal"), 3),
                    ["locations"] = Arr(Obj(new JObject
                    {
                        ["id"] = Str("半角英小文字と _"), ["name"] = Str(), ["art"] = new JObject { ["type"] = "string", ["enum"] = new JArray(bg.ToArray()), ["description"] = bgHelp },
                        ["act"] = new JObject { ["type"] = "integer", ["description"] = "1〜3" },
                        ["description"] = Str("見て分かる様子"), ["secret"] = Str("調べて分かること（なければ空）"), ["trap"] = Str("罠（なければ空）"),
                        ["exits"] = Arr(Str("つながる場所の id")),
                        ["encounter"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string", ["enum"] = new JArray(db.Enemies.Keys.ToArray()) }, ["description"] = enemyHelp },
                    }, "id", "name", "art", "act", "description", "exits"), 7),
                    ["npcs"] = Arr(Obj(new JObject
                    {
                        ["id"] = Str(), ["name"] = Str(), ["art"] = new JObject { ["type"] = "string", ["enum"] = new JArray(story.Catalog.Portraits.Select(p => p.Id).ToArray()), ["description"] = ptHelp },
                        ["where"] = Str("いる場所の id"), ["role"] = Str("役回りと、知っていること"),
                    }, "id", "name", "art", "where", "role"), 2),
                    ["clues"] = Arr(Obj(new JObject { ["id"] = Str(), ["text"] = Str(), ["where"] = Str("場所の id") }, "id", "text", "where"), 4),
                    ["boss"] = Enum(bosses),
                    ["endings"] = Obj(new JObject { ["success"] = Str(), ["bitter"] = Str(), ["failure"] = Str() }, "success", "bitter", "failure"),
                }, "title", "hook", "truth", "weakness", "acts", "locations", "npcs", "clues", "boss", "endings"));
        }

        static readonly Regex IdRx = new Regex("[^a-z0-9_]");

        /// <summary>Makes any sheet playable: ids, art, enemies, a connected map from the tavern, the boss at the end.</summary>
        public static void Repair(ScenarioDef s, StoryContent story, ContentDb db, string boss)
        {
            var bgIds = new HashSet<string>(story.Catalog.Backgrounds.Select(b => b.Id));
            var ptIds = new HashSet<string>(story.Catalog.Portraits.Select(p => p.Id));
            if (string.IsNullOrWhiteSpace(s.Title)) s.Title = "名もなき夜の物語";

            // acts
            while (s.Acts.Count < 3) s.Acts.Add(new ActDef { Title = $"第{s.Acts.Count + 1}幕", Goal = "物語を先へ進める" });
            if (s.Acts.Count > 3) s.Acts = s.Acts.Take(3).ToList();
            var minutes = new[] { 40, 50, 40 };
            for (int i = 0; i < 3; i++) { s.Acts[i].N = i + 1; if (s.Acts[i].Minutes <= 0) s.Acts[i].Minutes = minutes[i]; }

            // locations: unique slugs, known art, acts 1..3
            var seen = new HashSet<string>();
            var rename = new Dictionary<string, string>();
            foreach (var l in s.Locations)
            {
                var id = IdRx.Replace((l.Id ?? "").ToLowerInvariant(), "");
                if (id.Length == 0) id = "place";
                var unique = id; int n = 2;
                while (!seen.Add(unique)) unique = id + n++;
                if (l.Id != null && !rename.ContainsKey(l.Id)) rename[l.Id] = unique;
                l.Id = unique;
                if (!bgIds.Contains(l.Art)) l.Art = l.Act >= 3 ? "bg_crypt" : l.Act == 2 ? "bg_ruins_hall" : "bg_forest_path";
                l.Act = Math.Max(1, Math.Min(3, l.Act));
                l.Encounter = (l.Encounter ?? new List<string>()).Where(db.Enemies.ContainsKey).Take(3).ToList();
                if (string.IsNullOrWhiteSpace(l.Name)) l.Name = l.Id;
            }
            foreach (var l in s.Locations) l.Exits = (l.Exits ?? new List<string>()).Select(x => rename.TryGetValue(x, out var r) ? r : x).ToList();

            // the tavern comes first
            var tavern = s.Locations.FirstOrDefault(l => l.Id == "tavern");
            if (tavern == null)
            {
                tavern = new LocationDef { Id = "tavern", Name = "眠る竜亭", Art = "bg_tavern", Act = 1, Description = "冒険者たちの拠点の酒場。" };
                if (s.Locations.Count > 0) tavern.Exits.Add(s.Locations[0].Id);
                s.Locations.Insert(0, tavern);
            }
            else { s.Locations.Remove(tavern); s.Locations.Insert(0, tavern); }
            tavern.Encounter.Clear(); tavern.Act = 1; tavern.Art = "bg_tavern";
            s.Start = "tavern";

            // exits: only real places, both ways, everything reachable from the tavern
            var ids = new HashSet<string>(s.Locations.Select(l => l.Id));
            foreach (var l in s.Locations) l.Exits = l.Exits.Where(x => ids.Contains(x) && x != l.Id).Distinct().ToList();
            foreach (var l in s.Locations) foreach (var x in l.Exits.ToList()) { var o = s.Location(x); if (!o.Exits.Contains(l.Id)) o.Exits.Add(l.Id); }
            for (int i = 1; i < s.Locations.Count; i++)
                if (!Reachable(s, s.Locations[i].Id)) { s.Locations[i].Exits.Add(s.Locations[i - 1].Id); s.Locations[i - 1].Exits.Add(s.Locations[i].Id); }

            // the boss waits in the last place (act 3)
            if (!db.Enemies.TryGetValue(s.Boss ?? "", out var bd) || !bd.Boss) s.Boss = boss;
            var lair = s.Locations.LastOrDefault(l => l.Encounter.Contains(s.Boss)) ?? s.Locations.Last();
            lair.Act = 3;
            if (!lair.Encounter.Contains(s.Boss)) lair.Encounter.Insert(0, s.Boss);
            foreach (var l in s.Locations.Where(l => l != lair)) l.Encounter.RemoveAll(e => db.Enemies[e].Boss);

            // people and clues point at real places
            foreach (var npc in s.Npcs)
            {
                if (!ids.Contains(npc.Where)) npc.Where = "tavern";
                if (!ptIds.Contains(npc.Art)) npc.Art = "pt_elder";
                if (string.IsNullOrWhiteSpace(npc.Id)) npc.Id = "npc" + s.Npcs.IndexOf(npc);
            }
            var clueIds = new HashSet<string>();
            foreach (var c in s.Clues)
            {
                c.Id = IdRx.Replace((c.Id ?? "").ToLowerInvariant(), "");
                if (c.Id.Length == 0 || !clueIds.Add(c.Id)) { c.Id = "clue" + s.Clues.IndexOf(c); clueIds.Add(c.Id); }
                if (!ids.Contains(c.Where)) c.Where = s.Locations[Math.Min(1, s.Locations.Count - 1)].Id;
            }
            if (string.IsNullOrWhiteSpace(s.Endings.Success)) s.Endings.Success = "依頼を果たし、酒場に凱旋した。";
            if (string.IsNullOrWhiteSpace(s.Endings.Bitter)) s.Endings.Bitter = "依頼は果たしたが、大きな代償を払った。";
            if (string.IsNullOrWhiteSpace(s.Endings.Failure)) s.Endings.Failure = "一行は倒れるか逃げ帰り、事件は闇に消えた。";
        }

        static bool Reachable(ScenarioDef s, string target)
        {
            var seen = new HashSet<string> { "tavern" };
            var q = new Queue<string>(seen);
            while (q.Count > 0)
            {
                var id = q.Dequeue();
                if (id == target) return true;
                foreach (var x in s.Location(id).Exits) if (seen.Add(x)) q.Enqueue(x);
            }
            return false;
        }
    }
}

namespace Tavern.Core.Game
{
    public static class Scenarios
    {
        /// <summary>The evening's scenario: the AI-written one saved in the state, or the prepared one.</summary>
        public static ScenarioDef For(GameState state, ScenarioDef prepared)
            => string.IsNullOrWhiteSpace(state.ScenarioJson) ? prepared : ContentDb.LoadScenario(state.ScenarioJson);
    }
}
