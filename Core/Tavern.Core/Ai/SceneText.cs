#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Ai
{
    /// <summary>
    /// What the AI is shown each call (09_Spec 13): the places the party stands in with their people, items, clues and mechanisms,
    /// the running clocks, the rules that matter, the conclusions (to judge deductions), the facts and rumours. Never the whole sheet.
    /// Ids are in [brackets]; the AI answers with them.
    /// </summary>
    public static class SceneText
    {
        public static string WorldRules =>
            "1. 死を覆す・避ける・なかったことにする / 2. 起きたことを変える、人より多く手番を得る / 3. 地図の線を通らずに、別の点へ着く / " +
            "4. 判定 1 回で、相手の意思を 1 手番より長く奪う / 5. 設定表の秘密を、手がかり以外の道で知る / 6. 名品を、設定表の外から得る / " +
            "7. 強い第三者に解決を任せる / 8. 世界にない技術（火薬・銃・機械）";

        /// <summary>The story frame: title, truth (the AI knows it), act and its goal, conclusions.</summary>
        public static string Story(Evening e)
        {
            var s = e.Story;
            var act = s.Acts.FirstOrDefault(a => a.N == e.World.Act);
            var b = new StringBuilder();
            b.AppendLine($"# 物語「{s.Title}」（{s.Kind}、闇の深さ: {s.Darkness}）");
            b.AppendLine($"真相（あなただけが知る。プレイヤーに明かさない）: {s.Truth}");
            b.AppendLine($"依頼の目的: {s.GoalText}");
            if (act != null) b.AppendLine($"いまは第 {act.N} 幕「{act.Title}」。この幕の目標: {act.Goal}");
            if (e.ClimaxStarted) b.AppendLine($"山場の最中: {s.Climax.Text}");
            b.AppendLine("結論（推理の照合に使う。プレイヤーには見せない）:");
            foreach (var c in s.Conclusions) b.AppendLine($"- [{c.Id}] {c.Text}{(e.World.Convictions.Contains(c.Id) ? "（確信済み）" : "")}");
            return b.ToString();
        }

        /// <summary>Each place where someone of the party stands: what is there now.</summary>
        public static string Places(Evening e)
        {
            var b = new StringBuilder();
            foreach (var place in e.World.PlacesOfParty.Distinct())
            {
                var p = e.Story.PlaceOf(place);
                if (p == null) continue;
                b.AppendLine($"## 場所 [{p.Id}] {p.Name}（いる冒険者: {string.Join("、", e.At(place).Select(a => a.Name))}）");
                b.AppendLine($"様子: {p.Look}");
                if (p.Secret.Length > 0) b.AppendLine($"（あなただけが知る）{p.Secret}");
                var ways = e.Ways(place).Select(w => $"[{w.link.To}] {e.Story.PlaceOf(w.link.To)?.Name}{(w.open ? "" : "（まだ通れない" + (w.link.Text.Length > 0 ? ": " + w.link.Text : "") + "）")}");
                b.AppendLine("行ける場所: " + string.Join(" / ", ways));
                foreach (var r in p.Rules) b.AppendLine($"場所の決まり: {r.Text}");
                if (p.Tense) b.AppendLine("ここは緊迫の場所。");
                if (p.Shop) b.AppendLine("店がある（消耗品と道具）。");
                if (p.Enemies.Count > 0) b.AppendLine("ここに出うる敵: " + string.Join("、", p.Enemies.Distinct().Select(x => $"[{x}]")));
                foreach (var t in p.Traps) b.AppendLine($"罠: {t.Text}（{t.Ability.Code()}・{t.Tier.Label()}で見抜く・外す）");
                var people = e.PeopleAt(place).ToList();
                foreach (var n in people)
                {
                    var d = e.Story.PersonOf(n.Id);
                    var line = $"人物 [{n.Id}] {n.Name}: 態度 {n.Attitude.Label()}";
                    if (n.WithParty) line += n.Escort ? "、一行が連れている" : "、一行の仲間";
                    if (n.State != PersonState.Alive) line += $"、{State(n.State)}";
                    if (d != null)
                    {
                        line += $"。{d.Role}。口調: {d.Voice}。望み: {d.Wants}。恐れ: {d.Fears}";
                        if (d.Secret.Length > 0) line += $"。秘密: {d.Secret}";
                        if (d.Lie.Length > 0) line += $"。嘘: {d.Lie}";
                        if (d.Joins != JoinWillingness.Never) line += $"。同行: {(d.Joins == JoinWillingness.Glad ? "喜んで" : "条件付き")}{(d.JoinNote.Length > 0 ? "（" + d.JoinNote + "）" : "")}";
                    }
                    b.AppendLine(line);
                }
                foreach (var it in e.ItemsAt(place)) b.AppendLine($"物 [{it.Item.Id}] {it.Item.Name}{(it.Note.Length > 0 ? "（" + it.Note + "）" : "")}");
                foreach (var m in e.MechanismsAt(place))
                    b.AppendLine($"仕掛け [{m.Id}] {m.Name}: {(m.NoCheck ? "判定なしで動かせる" : $"{m.Ability.Code()}・{m.Tier.Label()}")}");
                foreach (var c in e.CluesHere(place))
                {
                    var get = c.Access switch
                    {
                        ClueAccess.Free => "判定なし",
                        ClueAccess.Check => $"{c.Ability!.Value.Code()}・{c.Tier!.Value.Label()}",
                        _ => $"{RequestLabel(c.Request!.Value)}",
                    };
                    b.AppendLine($"手がかり [{c.Id}] {c.Text}（得かた: {c.How}。{get}{(c.Person.Length > 0 ? "。持ち主 [" + c.Person + "]" : "")}）");
                }
            }
            return b.ToString();
        }

        /// <summary>The party: abilities, specialties, named techniques, what they carry, how they are.</summary>
        public static string Party(Evening e)
        {
            var b = new StringBuilder("# 冒険者\n");
            foreach (var a in e.Party.Where(a => a.Life != Life.Dead && !a.Withdrawn))
            {
                var stats = string.Join(" ", new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd }.Select(x => $"{x.Code()}+{a.Modifier(x)}"));
                b.AppendLine($"- [{a.Id}] {a.Name}（{a.Class.Name}{(a.Class.Spells ? "・呪文を使える" : "")}）{stats} HP {a.Hp}/{a.MaxHp} 気力 {a.Focus}/{a.MaxFocus} 銀貨 {a.Silver} 場所 [{e.PlaceOf(a)}]" +
                    (a.Life == Life.Down ? " 倒れている" : "") + (a.Statuses.List.Count > 0 ? " 状態: " + string.Join("・", a.Statuses.List.Select(s => s.Kind.Label())) : ""));
                b.AppendLine($"  得意: {string.Join("、", a.Specialties.Select(s => s.Kind.Label() + (s.UndeadBane ? "（不死に強い）" : "") + (s.SelfOnly ? "（自分だけ）" : "")).Distinct())}");
                if (a.Techniques.Count > 0) b.AppendLine($"  名付けた技: {string.Join("、", a.Techniques.Select(t => $"【{t.Name}】{t.Kind.Label()}・{t.Size.Label()}・{t.Ability.Code()}"))}");
                b.AppendLine($"  持ち物: {string.Join("、", a.Gear.Items.Select(i => $"[{i.Id}] {i.Name}"))}");
                if (a.Background.Length > 0) b.AppendLine($"  生い立ち: {a.Background}");
            }
            return b.ToString();
        }

        /// <summary>Clocks (with the hidden names), facts, rumours, the companions.</summary>
        public static string Situation(Evening e)
        {
            var b = new StringBuilder();
            var clocks = e.World.Clocks.All.Where(c => c.RunsIn(e.World.Act)).ToList();
            if (clocks.Count > 0)
            {
                b.AppendLine("# 時計");
                foreach (var c in clocks)
                    b.AppendLine($"- [{c.Id}] {c.Def.Name}（裏: {c.Def.SecretName}）{c.Filled}/{c.Def.Segments}{(c.Visible ? "" : "（冒険者はまだ知らない）")}。満ちたら: {c.Def.FullText}。戻し方: {string.Join(" / ", c.Def.Rewinds.Select(r => r.Text))}");
            }
            if (e.Facts.Count > 0) { b.AppendLine("# 事実メモ（覆さない）"); foreach (var f in e.Facts) b.AppendLine("- " + f); }
            if (e.Rumors.Count > 0) { b.AppendLine("# 最近の噂（台詞で触れてよい）"); foreach (var r in e.Rumors) b.AppendLine("- " + r); }
            return b.ToString();
        }

        /// <summary>The fight: everyone in turn order with HP, statuses and the big moves announced.</summary>
        public static string Fight(Evening e)
        {
            var c = e.Combat;
            if (c == null) return "";
            var b = new StringBuilder($"# 戦闘（{c.Round} 手番目。行動順）\n");
            foreach (var f in c.Order.Where(f => f.InFight || f.Creature?.Down == true))
            {
                var side = f.Side == Side.Party ? (f.Adventurer != null ? "冒険者" : "仲間") : "敵";
                var hp = f.Creature != null ? (f.Creature.Down ? "倒れた" : $"HP {f.Hp}/{f.MaxHp}") : $"HP {f.Hp}/{f.MaxHp}";
                var st = f.Statuses.List.Count > 0 ? "、" + string.Join("・", f.Statuses.List.Select(s => s.Kind.Label())) : "";
                var extra = f.Creature != null && f.Creature.Side == Side.Foe ? $"、守り {f.Creature.Defense}{(f.Creature.Undead ? "、不死" : "")}{(f.Creature.Boss ? "、ボス" : "")}" : "";
                b.AppendLine($"- [{f.Id}] {f.Name}（{side}、{hp}{extra}{st}）");
            }
            foreach (var id in c.BigMoveDue) if (c.Find(id)?.Creature is Creature boss && boss.BigMove != null) b.AppendLine($"予告: {boss.Name} が次の手番に大技「{boss.BigMove.Name}」");
            return b.ToString();
        }

        public static string All(Evening e) => string.Join("\n", Story(e), Places(e), Party(e), Fight(e), Situation(e));

        static string State(PersonState s) => s switch
        {
            PersonState.Down => "倒れている", PersonState.Asleep => "眠っている", PersonState.Retreated => "退いた", PersonState.Dead => "死んでいる", _ => "",
        };

        static string RequestLabel(RequestSize r) => r switch { RequestSize.Small => "小さな頼み", RequestSize.Normal => "普通の頼み", _ => "大きな頼み" };
    }
}
