#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Unity.App;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    public sealed partial class TableView
    {
        RectTransform Sheet(string name, float w = 1300, float h = 900, bool paper = false)
        {
            var layer = Overlay(name, 0, 0, 1920, 1080, 0.7f);
            var panel = UiKit.Panel(layer, "Panel", (1920 - w) / 2, (1080 - h) / 2, w, h, leather: !paper);
            if (paper) UiKit.Paper(panel.transform);
            UiKit.Button(panel.transform, "閉じる", () => Close(layer), w - 200, h - 72, 180, 52, 20, gold: true);
            return (RectTransform)panel.transform;
        }

        /// <summary>The character sheet (09_Spec 15): abilities, HP and focus, coin and marks, class text, techniques, gear, statuses.</summary>
        void ShowSheet(string id)
        {
            var a = E.Find(id);
            if (a == null) return;
            var p = Sheet("Sheet");
            PersonCard(p, ArtLibrary.Get(a.Portrait), 30, 30, 200, 300, Theme.FrameGold);
            UiKit.Heading(p, $"{a.Name}<size=22>　{a.Class.Name}</size>", 250, 18, 1020, ClassIcon(a), size: 34);
            var stats = string.Join("　", new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd }.Select(x => $"{x.Code()} +{a.Modifier(x)}<size=13>（{x.Meaning()}）</size>"));
            UiKit.Label(p, stats, 22, Theme.Ink, 250, 84, 1020, 40);
            UiKit.Label(p, $"HP {a.Hp}/{a.MaxHp}　気力 {a.Focus}/{a.MaxFocus}　運命のコイン {a.FateCoins}　銀貨 {a.Silver}" +
                (a.Statuses.List.Count > 0 ? "　状態: " + string.Join("・", a.Statuses.List.Select(s => s.Kind.Label())) : ""), 20, Theme.Ink, 250, 128, 1020, 30);
            UiKit.Label(p, "歴戦の印: " + (a.Marks.Count == 0 ? "まだない" : string.Join("　", a.Marks.Select(m => (m.UsedTonight ? "✧" : "✦") + m.Name))), 18, Theme.GoldLight, 250, 166, 1020, 30);
            UiKit.Label(p, a.ClassText(), 18, Theme.Ink, 250, 204, 1020, 120);
            var gear = new List<string>();
            gear.Add($"利き手: {a.Gear.MainItem?.Name ?? "なし"}　逆の手: {a.Gear.OffItem?.Name ?? "なし"}　体: {a.Gear.BodyItem?.Name ?? "なし"}　小物: {a.Gear.TrinketItem?.Name ?? "なし"}");
            gear.Add("持ち物: " + string.Join("、", a.Gear.Items.Where(i => i.Id != a.Gear.MainHand && i.Id != a.Gear.OffHand && i.Id != a.Gear.Body && i.Id != a.Gear.Trinket)
                .Select(i => i.Name + (i.Concealed ? "（隠し持ち）" : "") + (i.Uses is int u ? $"（残り {u}）" : ""))));
            UiKit.Label(p, string.Join("\n", gear), 18, Theme.Ink, 30, 340, 1240, 60);
            UiKit.Label(p, "名付けた技: " + (a.Techniques.Count == 0 ? "まだない（宣言の中で【】に名前を書くと残る）" : string.Join("　", a.Techniques.Select(t => $"【{t.Name}】{t.Kind.Label()}・{t.Size.Label()}（{t.Note}）"))), 18, Theme.Ink, 30, 410, 1240, 60);
            UiKit.Label(p, "生い立ち: " + (a.Background.Length > 0 ? a.Background : "（なし）"), 17, Theme.Dim, 30, 480, 1240, 80);
            var h = _t.Handouts.FirstOrDefault(x => x.Who == a.Id);
            if (h != null && IsMine(a.Id))
            {
                var goal = Story.PersonalGoals.FirstOrDefault(g => g.Id == h.GoalId)?.Text ?? "";
                UiKit.Label(p, $"ハンドアウト（本人だけ）\n縁: {h.Bond}\n目的: {goal}" + (h.Fragment.Length > 0 ? $"\n断片: {h.Fragment}" : ""), 17, Theme.Hex("c9a0c0"), 30, 570, 1240, 200);
            }
        }

        /// <summary>The clue book: "手がかり 4 / 12", the shared clues, each one's own secret clues, convictions (never which question).</summary>
        void ShowClues()
        {
            var p = Sheet("Clues", 1200, 860, paper: true);
            UiKit.Heading(p, $"手がかり帳　{E.World.SharedClues} / {Story.Clues.Count}", 50, 40, 1000, "ic_clues", paper: true);
            var list = UiKit.Scroll(p, 50, 112, 1100, 630, out _);
            foreach (var c in Story.Clues.Where(c => E.World.Clues.TryGetValue(c.Id, out var w) && w.Contains(World.Shown)))
                UiKit.Label(list, "・" + c.Text, 19, Theme.InkDark).Inked();
            foreach (var a in E.Party.Where(a => IsMine(a.Id)))
                foreach (var c in Story.Clues.Where(c => E.World.Clues.TryGetValue(c.Id, out var w) && !w.Contains(World.Shown) && w.Contains(a.Id)))
                {
                    var row = UiKit.Label(list, $"・（{a.Name}だけが知っている）{c.Text}", 19, Theme.Hex("5a2a5e")).Inked();
                    var cid = c.Id;
                    var b = UiKit.Button(list, "見せる（仲間にも）", () =>
                    {
                        E.World.ShowClue(cid);
                        if (Guest) NetSession.Send(NightMsg.Clue, OnlineView.Json(new ClueMsg { ClueId = cid }));
                        Persist();
                        BroadcastState();
                        Close((RectTransform)p.parent);
                        ShowClues();
                    }, 17);
                    b.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
                }
            foreach (var c in Story.Conclusions.Where(c => E.World.Convictions.Contains(c.Id)))
                UiKit.Label(list, "確信: " + c.Text, 20, Theme.InkRed).Inked();
            if (E.World.Clues.Count == 0) UiKit.Label(list, "まだ何も見つけていない。", 19, Theme.InkBrown).Inked();
        }

        /// <summary>The handouts (each is the owner's; on one PC they are all here).</summary>
        void ShowHandouts()
        {
            var mine = _t.Handouts.Where(h => IsMine(h.Who)).ToList();
            if (mine.Count == 0) return;
            var p = Sheet("Handouts", 1300, 860, paper: true);
            UiKit.Heading(p, Online ? "ハンドアウト（あなただけが読む）" : "ハンドアウト（本来は本人だけが読む）", 50, 40, 1100, "ic_handout", paper: true);
            var list = UiKit.Scroll(p, 50, 112, 1200, 630, out _, spacing: 18);
            foreach (var h in mine)
            {
                var a = E.Find(h.Who);
                var goal = Story.PersonalGoals.FirstOrDefault(g => g.Id == h.GoalId)?.Text ?? "";
                UiKit.Label(list, UiKit.OnPaper($"<color=#c9a24a>{a?.Name}</color>\n縁: {h.Bond}\n個人の目的: {goal}" + (h.Fragment.Length > 0 ? $"\n断片: {h.Fragment}" : "")), 19, Theme.InkDark).Inked();
            }
        }

        /// <summary>語り部への注文: changes only how the story is told; and asking for a hint.</summary>
        void ShowRequests()
        {
            var p = Sheet("Requests", 1100, 640);
            UiKit.Heading(p, "語り部への注文", 30, 20, 1040, "ic_quill", size: 30);
            UiKit.Label(p, "変えられるのは語り方だけ（長さ・調子）。ルール・数字・物語の秘密は変わりません。", 18, Theme.Ink, 30, 84, 1040, 30);
            var input = UiKit.Input(p, "例: もっと短く / 情景を多めに", 30, 120, 1040, 110, 19, multiline: true);
            UiKit.Button(p, "次の語りに伝える", () =>
            {
                var text = input.text.Trim();
                if (text.Length == 0) return;
                if (Guest) NetSession.Send(NightMsg.Order, OnlineView.Json(new NightOrder { Text = text }));
                else _t.Requests.Add(text);
                AddLine("（注文: " + text + "）", Theme.Dim, 14);
            }, 30, 240, 300, 52, 19, gold: true);
            UiKit.Button(p, "手がかりがほしい", () =>
            {
                if (Guest) NetSession.Send(NightMsg.Order, OnlineView.Json(new NightOrder { Hint = true }));
                else E.Hints.Ask(E.Difficulty);
                AddLine("（語り部に、手がかりを頼んだ）", Theme.Dim, 14);
            }, 350, 240, 300, 52, 19);
            if (Guest) { UiKit.Label(p, "語りの量と難易度は、ホストが決めます。", 18, Theme.Dim, 30, 320, 800, 28); return; }
            UiKit.Label(p, "語りの量", 18, Theme.Dim, 30, 320, 200, 28);
            foreach (var (len, label, i) in new[] { (Telling.Short, "短め", 0), (Telling.Standard, "標準", 1), (Telling.Long, "長め", 2) })
                UiKit.Button(p, label + (_t.Length == len ? "（いま）" : ""), () => { _t.Length = len; Close((RectTransform)p.parent); ShowRequests(); }, 30 + i * 200, 352, 190, 48, 18);
            UiKit.Label(p, "難易度（幕の合間にも変えられる）", 18, Theme.Dim, 30, 420, 600, 28);
            foreach (var (d, i) in new[] { Difficulty.Gentle, Difficulty.Standard, Difficulty.Harsh }.Select((d, i) => (d, i)))
                UiKit.Button(p, d.Label() + (E.Difficulty == d ? "（いま）" : ""), () => { E.Difficulty = d; Close((RectTransform)p.parent); ShowRequests(); }, 30 + i * 200, 452, 190, 48, 18);
        }

        /// <summary>Growth at the end of an act: each standing adventurer picks one (an ability, HP, or a new specialty sentence).</summary>
        void ShowGrowth()
        {
            if (Guest) return;   // the host asks
            if (_t.GrowthDue.Count == 0) { if (_t.Phase == Phase.Break) ShowBreak(); return; }   // a long story's break waits for the growth
            foreach (var friend in _t.GrowthDue.Where(i => !IsMine(i) && _growthAsked.Add(i)).ToList()) _ = AskGrowthAsync(friend);
            var id = _t.GrowthDue.FirstOrDefault(IsMine);
            if (id == null || _growing) return;
            _ = GrowHere(id);
        }

        bool _growing;

        async Task GrowHere(string id)
        {
            _growing = true;
            var a = E.Find(id)!;
            var options = Table.GrowthOptions(a);
            var pick = await GrowthDialog(a);
            _growing = false;
            if (_t.Grow(id, options[pick])) AddLine($"{a.Name}は成長した: {options[pick].Label}", Theme.GoldLight, 15);
            Persist();
            BroadcastState();
            RefreshAll();
            ShowGrowth();
        }

        /// <summary>The growth choices of one adventurer; returns the picked option's index (Table.GrowthOptions).</summary>
        async Task<int> GrowthDialog(Adventurer a)
        {
            var options = Table.GrowthOptions(a);
            var toughen = options.FindIndex(o => o.Toughen);
            if (Auto) return toughen;
            var done = new TaskCompletionSource<int>();
            var layer = Overlay("Growth", 0, 0, 1920, 1080, 0.7f);
            var panel = UiKit.Panel(layer, "Panel", 360, 120, 1200, 120 + options.Count * 56).transform;
            UiKit.Heading(panel, $"{a.Name}の成長（幕の終わり）", 30, 16, 1140, ClassIcon(a), size: 28);
            AudioDirector.Play("growth");
            for (int i = 0; i < options.Count; i++)
            {
                var idx = i;
                UiKit.Button(panel, options[i].Label, () => done.TrySetResult(idx), 30, 76 + i * 56, 1140, 48, 17);
            }
            var pick = await done.Task;
            Close(layer);
            return pick;
        }

        /// <summary>Sound, text size and narration speed during the evening (the same panel as the title's).</summary>
        void ShowSettings()
        {
            var layer = Overlay("Settings", 0, 0, 1920, 1080, 0f);
            SettingsPanel.Show(layer, RebuildLog, () => Close(layer), aiNow: Guest ? "" : _n.Ai.Description);
        }

        /// <summary>The log again at the new text size: the last lines of the evening, at once.</summary>
        void RebuildLog()
        {
            _typing = null; _pending.Clear();   // what was still to come is in the log already
            UiKit.Clear(_log);
            foreach (var l in _t.Log.Skip(Math.Max(0, _t.Log.Count - 40)))
                AddLine(l.StartsWith("語り: ") ? l.Substring(4) : l, l.StartsWith("語り: ") ? Theme.Ink : Theme.Dim, l.StartsWith("語り: ") ? 20 : 15, serif: l.StartsWith("語り: "));
        }

        /// <summary>A long story's break: saved here; the next evening resumes from "続きから" (or right away).</summary>
        void ShowBreak()
        {
            Persist();
            Dialog("中休み", "物語は一晩明かします。続きは次の晩に、タイトルの「続きから」で。\n今夜このまま続けることもできます。",
                ("保存して酒場へ", () => _n.BackToTitle()), ("このまま次の晩へ", ResumeNight));
        }

        /// <summary>After the break (opened again from "続きから"): the host starts the next evening.</summary>
        void ShowNextNight()
        {
            var act = Story.Acts.FirstOrDefault(a => a.N == E.World.Act);
            Dialog("中休みの明け", $"{Story.Title}の続き、{act?.Title}から。\n顔ぶれがそろったら始めます。", ("次の晩を始める", ResumeNight));
        }

        /// <summary>Screenshots only (-showfight): a fight against the story's first foes, to see the foe cards without playing to one.</summary>
        void PreviewFight()
        {
            var ids = Story.Places.SelectMany(p => p.Enemies).Where(_n.Enemies.Has).Distinct().Take(2).ToList();
            ids.AddRange(_n.Enemies.Enemies.Values.Where(d => d.Boss).Select(d => d.Id).Take(1));
            var foes = ids.Select((id, i) => { var d = _n.Enemies[id]; return Creature.FromEnemy(d, $"{d.Id}_{i}", d.Name, d.Hp); }).ToList();
            foes.Add(Creature.FromEnemy(_n.Enemies[ids[0]], ids[0] + "_x", _n.Enemies[ids[0]].Name + "B", _n.Enemies[ids[0]].Hp / 2));
            E.Combat = Combat.Start(E.Party, Array.Empty<Creature>(), foes, E.Party.ToDictionary(a => a.Id, a => 10), false, E.Difficulty, _t.Rng);
        }

        /// <summary>Screenshots only (-showending): the ending screen with sample facts, without playing the story through.</summary>
        void PreviewEnding()
        {
            var end = new EndingResult { Card = Card.Bitter };
            end.Lost.Add("（見本）古城の礼拝堂");
            foreach (var a in E.Party.Take(2)) end.Personal[a.Id] = ("（見本の目的）", a == E.Party[0]);
            end.Convictions.Add("（見本の確信）");
            _t.Ending = end;
            var c = new Chronicle { Title = "（見本）灯りの消えた夜", Story = "（見本の記録）" + Story.Intro };
            c.Rumors.Add("（見本の噂）北の丘に、もう灯りはともらない。");
            _n.ShowEnding(_t, c);
        }

        void ResumeNight()
        {
            _t.ResumeAfterBreak(System.Array.Empty<Adventurer>());
            Persist();
            _ = RunAsync(() => _t.TellUntoldAsync());
        }
    }
}
