#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Unity.App;
using Tavern.Unity.Dice;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The players' side of the table: everyone's dice rolled together, fate coins, declaring again. In a room each friend answers
    /// for their own adventurer on their own PC (TableView.Online); the host answers for the rest here.
    /// </summary>
    public sealed partial class TableView : IPlayers
    {
        const int MaxDice = 4;
        const float RollTimeout = 20f;
        static readonly List<DiceRoller> Pool = new List<DiceRoller>();
        readonly List<(RawImage pic, Text caption)> _diceViews = new List<(RawImage, Text)>();
        RectTransform? _dialog;
        TaskCompletionSource<bool>? _rollPressed;
        float _rollDeadline;
        Text? _rollTimer;

        void BuildDice(Transform scene)
        {
            while (Pool.Count < MaxDice)
            {
                var d = Pool.Count == 0 ? AppMain.Instance.Dice : DiceRoller.Create(AppMain.Instance.transform, Pool.Count);
                if (Pool.Count > 0) d.Bounced += s => AudioDirector.Play("dice_land", 0.2f + 0.5f * s);
                Pool.Add(d);
            }
            _diceViews.Clear();
            for (int i = 0; i < MaxDice; i++)
            {
                var x = 6 + (i % 2) * 410f;
                var y = 30 + (i / 2) * 250f;
                var pic = UiKit.Picture(scene, Pool[i].Texture, x, y, 404, 227);
                var cap = UiKit.Label(scene, "", 16, Theme.GoldLight, x, y + 200, 404, 46, align: TextAnchor.UpperCenter);
                cap.gameObject.AddComponent<Outline>().effectColor = Color.black;
                pic.gameObject.SetActive(false); cap.gameObject.SetActive(false);
                _diceViews.Add((pic, cap));
            }
        }

        /// <summary>Splits requests by who answers them: a connected friend (by client id), or this PC.</summary>
        (List<int> local, Dictionary<ulong, List<int>> byFriend) ByOwner(IReadOnlyList<string> who, bool awayHere)
        {
            var local = new List<int>();
            var byFriend = new Dictionary<ulong, List<int>>();
            for (int i = 0; i < who.Count; i++)
            {
                if (HostOnline && Room!.ClientOf(who[i]) is ulong c) { if (!byFriend.TryGetValue(c, out var l)) byFriend[c] = l = new List<int>(); l.Add(i); }
                else if (awayHere || !HostOnline || IsMine(who[i])) local.Add(i);
            }
            return (local, byFriend);
        }

        // ---------------------------------------------------------------- IPlayers

        public async Task<IReadOnlyList<int>> RollAsync(IReadOnlyList<RollRequest> rolls, CancellationToken ct)
        {
            // 1. everyone presses their own button (or 20 seconds): friends' dice are asked on their PCs, the rest here
            var (local, byFriend) = ByOwner(rolls.Select(r => r.Who).ToList(), awayHere: true);
            var waits = byFriend.Select(async kv =>
            {
                var answer = await Ask(kv.Key, new AskMsg { Kind = "roll", Rolls = kv.Value.Select(i => RollView.From(rolls[i])).ToList() });
                foreach (var p in answer?.Picks ?? new List<int>()) if (p >= 0 && p < kv.Value.Count) UseMark(rolls[kv.Value[p]]);
            }).ToList();
            if (local.Count > 0)
            {
                var here = local.Select(i => rolls[i]).ToList();
                waits.Add(RollDialog(here, j => here[j].Check is CheckRoll ch && !ch.MarkUsed && E.Find(here[j].Who)?.MarksLeft > 0, j => UseMark(here[j]), RollTimeout));
            }
            else _busyText.text = "友人がサイコロを振るのを待っている…";
            await Task.WhenAll(waits);
            _busyText.text = _busy ? "語り部が考えている…" : "";

            // 2. the host rolls them all; everyone watches (a secret die only its roller)
            var naturals = rolls.Select(_ => _t.Rng.D20()).ToList();
            if (HostOnline)
                foreach (var f in Room!.Friends.ToList())
                {
                    var seen = Enumerable.Range(0, rolls.Count).Where(i => !rolls[i].Private || Room.OwnerOf(rolls[i].Who) == f.PlayerName).ToList();
                    if (seen.Count > 0)
                        NetSession.Send(NightMsg.Dice, OnlineView.Json(new DiceMsg { Rolls = seen.Select(i => RollView.From(rolls[i])).ToList(), Naturals = seen.Select(i => naturals[i]).ToList() }), f.Id);
                }
            var shown = Enumerable.Range(0, rolls.Count).Where(i => !rolls[i].Private || IsMine(rolls[i].Who)).ToList();
            await AnimateDice(shown.Select(i => rolls[i]).ToList(), shown.Select(i => naturals[i]).ToList());
            return naturals;
        }

        bool UseMark(RollRequest r)
        {
            if (r.Check == null || E.Find(r.Who) is not Adventurer a || !r.Check.UseMark(a)) return false;
            r.Target = r.Check.Target;
            return true;
        }

        /// <summary>What these players roll, their veteran marks, one button (or the timer). useMark(i) spends the mark on roll i.</summary>
        async Task RollDialog(IReadOnlyList<RollRequest> rolls, Func<int, bool> canMark, Func<int, bool> useMark, float seconds)
        {
            _rollPressed = new TaskCompletionSource<bool>();
            var layer = Overlay("Roll", 284, 78, 840, 560, 0.55f);
            var panel = UiKit.Panel(layer, "Panel", 300, 96, 808, 90 + rolls.Count * 58).transform;
            UiKit.Icon(panel, "ic_d20", 18, 10, 28);
            UiKit.Title(panel, "サイコロを振る", 22, Theme.FrameGold, 54, 10, 300, 30);
            _rollTimer = UiKit.Label(panel, "", 15, Theme.Dim, 520, 16, 270, 24, align: TextAnchor.UpperRight);
            for (int i = 0; i < rolls.Count; i++)
            {
                var idx = i;
                var r = rolls[i];
                var a = E.Find(r.Who);
                var y = 48 + i * 58;
                var why = r.Reasons.Count > 0 ? "　<size=13><color=#a8977a>" + string.Join(" / ", r.Reasons) + "</color></size>" : "";
                var line = UiKit.Label(panel, $"{a?.Name}{(r.Private ? "（内緒）" : "")}: {r.For}\n<size=15>{r.Heading}{(r.Target > 0 && r.Kind != RollKind.Fate ? $"　{Odds(r)}" : "")}</size>{why}", 17, Theme.Ink, 18, y, 590, 56);
                if (a != null && canMark(idx))
                {
                    Button? mark = null;
                    mark = UiKit.Button(panel, $"歴戦の印（残り {a.MarksLeft}）", () =>
                    {
                        if (canMark(idx) && useMark(idx))
                            line.text = $"{a.Name}: {r.For}\n<size=15>{r.Check?.Heading ?? r.Heading}（印で 1 段易しく）{(r.Check != null ? "　" + Odds(r) : "")}</size>";
                        if (mark != null) mark.interactable = false;
                        RefreshCards();
                    }, 616, y + 6, 176, 42, 14);
                }
            }
            UiKit.Button(panel, "振る", () => _rollPressed?.TrySetResult(true), 300, 40 + rolls.Count * 58, 208, 44, 22, gold: true);
            _rollDeadline = Time.unscaledTime + (Auto ? 0.05f : seconds);
            await _rollPressed.Task;
            _rollPressed = null;
            Close(layer);
        }

        /// <summary>The dice, at most four at a time, with each result under its die.</summary>
        async Task AnimateDice(IReadOnlyList<RollRequest> rolls, IReadOnlyList<int> naturals)
        {
            for (int start = 0; start < rolls.Count; start += MaxDice)
            {
                var batch = Enumerable.Range(start, Math.Min(MaxDice, rolls.Count - start)).ToList();
                var done = 0;
                AudioDirector.Play("dice_roll");
                foreach (var (i, slot) in batch.Select((i, s) => (i, s)))
                {
                    var (pic, cap) = _diceViews[slot];
                    pic.gameObject.SetActive(true); cap.gameObject.SetActive(true);
                    cap.text = $"{E.Find(rolls[i].Who)?.Name}: {rolls[i].For}";
                    Pool[slot].Roll(naturals[i], Auto ? 0.2f : 1.6f, () => done++);
                }
                while (done < batch.Count) await Task.Yield();
                foreach (var (i, slot) in batch.Select((i, s) => (i, s)))
                {
                    var r = rolls[i];
                    var n = naturals[i];
                    var ok = r.Target <= 0 || n == 20 || (n != 1 && n + r.Bonus >= r.Target);
                    _diceViews[slot].caption.text = $"{E.Find(r.Who)?.Name}: {n}{(r.Bonus != 0 ? $"{r.Bonus:+0;-0} = {n + r.Bonus}" : "")}" +
                        (r.Kind == RollKind.Fate ? "" : r.Target > 0 ? (n == 20 ? "　大成功" : n == 1 ? "　大失敗" : ok ? "　成功" : "　失敗") : "");
                    if (n == 20) AudioDirector.Play("crit"); else if (n == 1) AudioDirector.Play("fumble");
                }
                await Wait(Auto ? 0.1f : 1.4f);
                foreach (var slot in Enumerable.Range(0, batch.Count))
                {
                    _diceViews[slot].pic.gameObject.SetActive(false);
                    _diceViews[slot].caption.gameObject.SetActive(false);
                    Pool[slot].Hide();
                }
            }
        }

        static string Odds(RollRequest r)
        {
            var need = Mathf.Clamp(r.Target - r.Bonus, 2, 20);
            return $"（{need} 以上で成功）";
        }

        public async Task<IReadOnlyList<bool>> OfferCoinsAsync(IReadOnlyList<RollRequest> failed, CancellationToken ct)
        {
            var picks = failed.Select(_ => false).ToList();
            var (local, byFriend) = ByOwner(failed.Select(r => r.Who).ToList(), awayHere: false);   // an away friend's coin stays unspent
            var waits = byFriend.Select(async kv =>
            {
                var answer = await Ask(kv.Key, new AskMsg { Kind = "coins", Rolls = kv.Value.Select(i => RollView.From(failed[i])).ToList() });
                foreach (var p in answer?.Picks ?? new List<int>()) if (p >= 0 && p < kv.Value.Count) picks[kv.Value[p]] = true;
            }).ToList();
            if (local.Count > 0) waits.Add(CoinsHere());
            await Task.WhenAll(waits);
            return picks;

            async Task CoinsHere()
            {
                var here = await CoinsDialog(local.Select(i => failed[i]).ToList());
                for (int j = 0; j < local.Count; j++) picks[local[j]] = here[j];
            }
        }

        async Task<List<bool>> CoinsDialog(IReadOnlyList<RollRequest> failed)
        {
            if (Auto) return failed.Select(f => f.Important || f.Kind == RollKind.DeathSave).ToList();
            var picks = failed.Select(_ => false).ToList();
            var done = new TaskCompletionSource<bool>();
            var layer = Overlay("Coins", 284, 78, 840, 560, 0.55f);
            var panel = UiKit.Panel(layer, "Panel", 300, 140, 808, 110 + failed.Count * 56).transform;
            UiKit.Label(panel, "失敗した判定を、運命のコイン（1 晩 1 枚）で振り直せます", 19, Theme.GoldLight, 18, 12, 770, 30);
            for (int i = 0; i < failed.Count; i++)
            {
                var idx = i;
                var f = failed[i];
                Button? b = null;
                b = UiKit.Button(panel, $"● {E.Find(f.Who)?.Name}: {f.For}　をコインで振り直す", () => { picks[idx] = !picks[idx]; b!.SetText((picks[idx] ? "✔ " : "● ") + $"{E.Find(f.Who)?.Name}: {f.For}　をコインで振り直す"); }, 18, 50 + i * 56, 772, 48, 17, gold: true);
            }
            UiKit.Button(panel, "決める", () => done.TrySetResult(true), 300, 56 + failed.Count * 56, 208, 44, 20);
            await done.Task;
            Close(layer);
            return picks;
        }

        public async Task<IReadOnlyList<Declaration>> RedeclareAsync(IReadOnlyList<(string who, string refusal)> refused, CancellationToken ct)
        {
            var result = refused.Select(r => new Declaration { Who = r.who, Text = "" }).ToList();
            var (local, byFriend) = ByOwner(refused.Select(r => r.who).ToList(), awayHere: false);   // an away friend waits this turn
            var waits = byFriend.Select(async kv =>
            {
                var answer = await Ask(kv.Key, new AskMsg { Kind = "redeclare", Who = kv.Value.Select(i => refused[i].who).ToList(), Refusals = kv.Value.Select(i => refused[i].refusal).ToList() });
                foreach (var d in answer?.Declarations ?? new List<Declaration>())
                    if (result.FirstOrDefault(x => x.Who == d.Who) is Declaration slot && Room!.ClientOf(d.Who) == kv.Key) slot.Text = Clip(d.Text, 200);
            }).ToList();
            if (local.Count > 0) waits.Add(RedeclareHere());
            await Task.WhenAll(waits);
            return result;

            async Task RedeclareHere()
            {
                var here = await RedeclareDialog(local.Select(i => refused[i]).ToList());
                for (int j = 0; j < local.Count; j++) result[local[j]].Text = here[j].Text;
            }
        }

        async Task<IReadOnlyList<Declaration>> RedeclareDialog(IReadOnlyList<(string who, string refusal)> refused)
        {
            if (Auto) return refused.Select(r => new Declaration { Who = r.who, Text = E.Combat != null ? "目の前の敵に攻撃する" : "辺りを見回す" }).ToList();
            var inputs = new List<InputField>();
            var done = new TaskCompletionSource<bool>();
            var layer = Overlay("Redo", 284, 78, 840, 990, 0.6f);
            var panel = UiKit.Panel(layer, "Panel", 300, 120, 808, 120 + refused.Count * 150).transform;
            UiKit.Label(panel, "できない行動がありました。手番は使わずに、宣言し直せます", 19, Theme.GoldLight, 18, 12, 770, 30);
            for (int i = 0; i < refused.Count; i++)
            {
                var (who, why) = refused[i];
                UiKit.Label(panel, $"{E.Find(who)?.Name}: できない（{why}）", 16, Theme.Hex("d8a090"), 18, 50 + i * 150, 770, 44);
                var input = UiKit.Input(panel, "新しい宣言（空なら、この手は様子を見る）", 18, 96 + i * 150, 772, 90, 17, multiline: true);
                input.characterLimit = 200;
                inputs.Add(input);
            }
            UiKit.Button(panel, "宣言し直す", () => done.TrySetResult(true), 300, 60 + refused.Count * 150, 208, 48, 20, gold: true);
            await done.Task;
            Close(layer);
            return refused.Select((r, i) => new Declaration { Who = r.who, Text = inputs[i].text.Trim() }).ToList();
        }

        void UpdateRoll()
        {
            if (_rollPressed == null) return;
            var left = _rollDeadline - Time.unscaledTime;
            if (_rollTimer != null) _rollTimer.text = $"{Mathf.CeilToInt(Mathf.Max(0, left))} 秒で自動で振る";
            if (left <= 0) _rollPressed.TrySetResult(true);
        }

        // ---------------------------------------------------------------- overlays

        /// <summary>A dimmed layer over an area of the table; dialogs go inside.</summary>
        RectTransform Overlay(string name, float x, float y, float w, float h, float dim)
        {
            var layer = UiKit.Rect(name, _root).Fill();
            UiKit.Box(layer, "Dim", new Color(0, 0, 0, dim)).rectTransform.Place(x, y, w, h);
            _dialog = layer;
            return layer;
        }

        void Close(RectTransform layer)
        {
            if (_dialog == layer) _dialog = null;
            UnityEngine.Object.Destroy(layer.gameObject);
        }

        /// <summary>A small message with buttons (a null action just closes it).</summary>
        void Dialog(string title, string body, params (string label, UnityAction? action)[] buttons)
        {
            var layer = Overlay("Dialog", 0, 0, 1920, 1080, 0.6f);
            var panel = UiKit.Panel(layer, "Panel", 560, 340, 800, 340).transform;
            UiKit.Heading(panel, title, 30, 20, 740, size: 28);
            UiKit.Label(panel, body, 19, Theme.Ink, 30, 80, 740, 160);
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, action) = buttons[i];
                UiKit.Button(panel, label, () => { Close(layer); action?.Invoke(); }, 30 + i * 250, 260, 230, 56, 20, gold: i == 0);
            }
        }
    }
}
