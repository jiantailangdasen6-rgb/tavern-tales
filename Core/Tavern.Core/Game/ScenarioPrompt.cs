#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tavern.Core.Content;

namespace Tavern.Core.Game
{
    /// <summary>Writes the scenario sheet as the secret part of the system prompt.</summary>
    public static class ScenarioPrompt
    {
        /// <summary>The fixed world, given to the storyteller with every scenario (set once by the app).</summary>
        public static string World = "";

        /// <summary>How many evenings each storyteller has told on this PC (from the chronicle); Veil reveals more each time.</summary>
        public static Dictionary<string, int> NightsByTeller = new Dictionary<string, int>();

        public static string TellerNotes(Gm.Storyteller t)
        {
            NightsByTeller.TryGetValue(t.Name, out var n);
            if (t.Id != "veil") return n > 0 ? $"（この酒場であなたが語るのは {n + 1} 晩目。前の夜のことに軽く触れてもよい）" : "";
            var stage = n == 0 ? "今夜はまだ何も明かさない。気配だけ。"
                : n == 1 ? "今夜は、あなたが百年前の辺境戦争を見てきたかのような口ぶりを一度だけ見せる。"
                : n == 2 ? "今夜は、あなたの指にある古い騎士団の指輪に冒険者が気づく場面を作ってよい。"
                : "今夜は、あなたが灰の災いの日に何かを失い、ずっと語り続けている存在だと、ほのめかしてよい（はっきりとは言わない）。";
            return $"（この酒場であなたが語るのは {n + 1} 晩目。{stage}）";
        }

        public static string Describe(ScenarioDef s, ContentDb db)
        {
            var sb = new StringBuilder();
            if (World.Length > 0) { sb.AppendLine("【世界】"); sb.AppendLine(World); }
            sb.AppendLine($"題: {s.Title}（型: {s.Template}）");
            sb.AppendLine($"導入: {s.Hook}");
            sb.AppendLine($"真相: {s.Truth}");
            sb.AppendLine($"弱点・鍵: {s.Weakness}");
            sb.AppendLine("幕:");
            foreach (var a in s.Acts) sb.AppendLine($"- 第{a.N}幕「{a.Title}」目標: {a.Goal}（目安 {a.Minutes} 分）");
            sb.AppendLine("場所（id: 名前 — 様子 / 秘密 / 行き先 / 敵）:");
            foreach (var l in s.Locations)
            {
                sb.Append($"- {l.Id}: {l.Name} — {l.Description}");
                if (!string.IsNullOrEmpty(l.Secret)) sb.Append($" / 秘密: {l.Secret}");
                sb.Append($" / 行き先: {string.Join("・", l.Exits)}");
                if (l.Encounter.Count > 0) sb.Append($" / 敵: {string.Join("・", l.Encounter.Select(e => db.Enemies.TryGetValue(e, out var d) ? $"{d.Name}({e})" : e))}");
                sb.AppendLine();
            }
            sb.AppendLine("人物:");
            foreach (var n in s.Npcs) sb.AppendLine($"- {n.Name}（{n.Where}）: {n.Role}");
            sb.AppendLine("手がかり（id: 内容 / ある場所）:");
            foreach (var c in s.Clues) sb.AppendLine($"- {c.Id}: {c.Text} / {c.Where}");
            sb.AppendLine($"ボス: {(db.Enemies.TryGetValue(s.Boss, out var b) ? b.Name : s.Boss)}（{s.Boss}）");
            sb.AppendLine($"結末: 成功(success) {s.Endings.Success} / 苦い成功(bitter) {s.Endings.Bitter} / 失敗(failure) {s.Endings.Failure}");
            sb.AppendLine();
            sb.AppendLine("## 効果の欄の使い方");
            sb.AppendLine("- 一行が別の場所に着いたら move_to。行き先は今いる場所の「行き先」から選ぶ。");
            sb.AppendLine("- 冒険者が手がかりを知ったら clues_found。秘密は、冒険者が調べたり聞いたりして見つけるまで語らない。");
            sb.AppendLine("- 敵が襲ってきたら start_combat に敵の id を入れる。戦いの中身（命中・ダメージ）はゲームが決めるので、語りでは始まりだけを描く。");
            sb.AppendLine("- 罠や落下で傷を負ったら damage、物を得たら items_gained。");
            sb.AppendLine("- 幕の目標を果たしたら next_act。若者を連れ帰るか、全滅・撤退で物語が終わるなら end_story。");
            return sb.ToString();
        }
    }
}
