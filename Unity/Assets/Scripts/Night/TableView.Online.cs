#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The table in a room (09_Spec 14). On the host: friends' declarations come in, the turn closes 5 seconds after everyone is in,
    /// each friend rolls their own dice, spends their own coins and picks their own growth, and gets the state cut to what they may see.
    /// On a friend's PC: the same screen drawn from the host's state; what the player does goes to the host.
    /// </summary>
    public sealed partial class TableView
    {
        const float CloseDelay = 5f;
        NightRoom? Room => _n.Room;
        bool Online => Room != null;
        bool Guest => Room != null && !Room.Host;
        bool HostOnline => Room != null && Room.Host;
        bool IsMine(string id) => Room == null || (Guest ? _guestMine.Contains(id) : Room.IsMine(id));
        List<Adventurer> MyLiving => Living.Where(a => IsMine(a.Id)).ToList();
        bool AllMineIn => MyLiving.Where(a => a.IsUp).All(a => _ready.Contains(a.Id));
        bool Away(string id) => Guest ? _away.Contains(id) : Room?.Away(id) == true;

        /// <summary>Declarations in for the coming turn (host: everyone's; a friend: as the host last said).</summary>
        readonly HashSet<string> _ready = new HashSet<string>();
        readonly Dictionary<string, Declaration> _decls = new Dictionary<string, Declaration>();
        float _closeAt;
        int _askId;
        readonly Dictionary<int, (ulong client, TaskCompletionSource<AnswerMsg?> tcs)> _asks = new Dictionary<int, (ulong, TaskCompletionSource<AnswerMsg?>)>();
        readonly HashSet<string> _growthAsked = new HashSet<string>();
        bool _listening;
        // a friend's side
        readonly HashSet<string> _guestMine = new HashSet<string>();
        Dictionary<string, string> _shown = new Dictionary<string, string>();
        readonly HashSet<string> _away = new HashSet<string>();
        StateMsg? _state;

        /// <summary>A wait driven by the player loop (the browser build has no timer thread for Task.Delay).</summary>
        static async Task Wait(float seconds) => await Awaitable.WaitForSecondsAsync(seconds);

        string OwnerName(string id) => Guest ? (_state != null && _state.Owners.TryGetValue(id, out var o) ? o : "") : Room?.OwnerOf(id) ?? "";

        /// <summary>The table is still the screen (after the ending or leaving, the room's events find it gone).</summary>
        bool OnScreen => _log != null && _log;

        static string Clip(string s, int n) => (s ?? "").Trim().Length > n ? s!.Trim().Substring(0, n) : (s ?? "").Trim();

        void Listen()
        {
            if (!Online || _listening) return;
            _listening = true;
            if (Guest)
            {
                NetSession.On(NightMsg.Report, (_, json) => OnReport(OnlineView.Read<ReportMsg>(json)));
                NetSession.On(NightMsg.Dice, (from, json) => { if (!OnScreen) return; var m = OnlineView.Read<DiceMsg>(json); _ = AnimateDice(m.Rolls.Select(v => v.ToRequest()).ToList(), m.Naturals); });
                NetSession.On(NightMsg.Ask, (from, json) => _ = AnswerAsync(OnlineView.Read<AskMsg>(json)));
                return;
            }
            var room = Room!;
            NetSession.On(NightMsg.Declare, OnDeclare);
            NetSession.On(NightMsg.Answer, (from, json) =>
            {
                var m = OnlineView.Read<AnswerMsg>(json);
                if (_asks.TryGetValue(m.Id, out var a) && a.client == from) a.tcs.TrySetResult(m);
            });
            NetSession.On(NightMsg.Order, OnOrder);
            NetSession.On(NightMsg.Clue, OnClue);
            room.Left += OnFriendLeft;
            room.Back += OnFriendBack;
        }

        // ================================================================ host

        void OnDeclare(ulong from, string json)
        {
            var p = OnScreen ? Room?.PlayerOf(from) : null;
            if (p == null) return;
            var m = OnlineView.Read<NightDeclare>(json);
            if (_busy || _ending) { SendStateTo(p); return; }
            foreach (var d in m.Declarations)
            {
                if (Room!.OwnerOf(d.Who) != p.PlayerName || E.Find(d.Who) is not Adventurer a || !a.IsUp || a.Withdrawn) continue;
                _decls[d.Who] = new Declaration { Who = d.Who, Text = Clip(d.Text, 200), Secret = d.Secret && E.Combat == null, Cover = Clip(d.Cover, 200) };
                if (m.Ready) _ready.Add(d.Who);
            }
            CheckClose();
            BroadcastState();
            RefreshCards();
            RefreshDeclState();
        }

        /// <summary>Everyone still with us has declared: the turn closes in 5 seconds (rewriting does not extend it).</summary>
        void CheckClose()
        {
            if (!HostOnline) return;
            var need = Living.Where(a => a.IsUp && !Away(a.Id)).ToList();
            if (need.Count > 0 && need.All(a => _ready.Contains(a.Id))) { if (_closeAt <= 0) _closeAt = Time.unscaledTime + (Auto ? 0.5f : CloseDelay); }
            else _closeAt = 0;
        }

        /// <summary>The turn runs with what is in; whoever wrote nothing waits and sees (in a fight, defends).</summary>
        void CloseTurn()
        {
            var decls = Living.Where(a => a.IsUp).Select(a => _decls.TryGetValue(a.Id, out var d) ? d
                : new Declaration { Who = a.Id, Text = E.Combat != null ? "身を守る" : "様子を見る" }).ToList();
            if (decls.Count == 0) return;
            _decls.Clear(); _ready.Clear(); _closeAt = 0;
            foreach (var line in Said(decls, Room!.MineOf(Room.Me))) AddLine(line, Theme.Hex("b9a989"), 15);
            foreach (var f in Room.Friends.ToList())
                NetSession.Send(NightMsg.Report, OnlineView.Json(new ReportMsg { Said = Said(decls, Room.MineOf(f.PlayerName)) }), f.Id);
            foreach (var a in MyLiving) { _drafts.Remove(a.Id); _covers.Remove(a.Id); _secret.Remove(a.Id); }
            _input.SetTextWithoutNotify(""); _cover.SetTextWithoutNotify("");
            _ = RunAsync(() => _t.ExploreAsync(decls));
        }

        /// <summary>The declarations as one reader sees them: the others' secret ones by their cover.</summary>
        List<string> Said(IEnumerable<Declaration> decls, ICollection<string> mine) => decls.Select(d =>
        {
            var name = E.Find(d.Who)?.Name;
            if (!d.Secret) return $"{name}: {d.Text}";
            return mine.Contains(d.Who) ? $"{name}（内緒）: {d.Text}" : $"{name}: {(d.Cover.Length > 0 ? d.Cover : "（何かしている）")}";
        }).ToList();

        void BroadcastState()
        {
            if (!HostOnline) return;
            foreach (var f in Room!.Friends.ToList()) SendStateTo(f);
        }

        void SendStateTo(RoomPlayer p)
        {
            var mine = Room!.MineOf(p.PlayerName);
            var up = mine.Where(id => E.Find(id) is Adventurer a && a.IsUp && !a.Withdrawn).ToList();
            var m = new StateMsg
            {
                Save = OnlineView.SaveFor(_t, mine).ToJson(), Mine = mine, Owners = new Dictionary<string, string>(Room.Owners),
                Declared = _ready.ToList(), Away = E.Party.Where(a => Room.Away(a.Id)).Select(a => a.Id).ToList(), Busy = _busy,
                Closing = _closeAt > 0 ? Mathf.Max(0, _closeAt - Time.unscaledTime) : 0,
            };
            if (up.Count > 0 && up.All(_ready.Contains))
                m.Shown = _decls.Where(kv => !mine.Contains(kv.Key) && _ready.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => ShownText(kv.Value));
            NetSession.Send(NightMsg.State, OnlineView.Json(m), p.Id);
        }

        static string ShownText(Declaration d) => d.Secret ? (d.Cover.Length > 0 ? d.Cover : "（内緒の行動）") : d.Text;

        /// <summary>What another player has declared, when this player may see it (once their own is in).</summary>
        string? DeclarationShown(string id)
        {
            if (!Online || IsMine(id)) return null;
            if (Guest) return _shown.TryGetValue(id, out var s) ? s : null;
            return AllMineIn && MyLiving.Any(a => a.IsUp) && _ready.Contains(id) && _decls.TryGetValue(id, out var d) ? ShownText(d) : null;
        }

        /// <summary>Asks one friend and waits for the answer (null when they time out or drop out).</summary>
        async Task<AnswerMsg?> Ask(ulong client, AskMsg m)
        {
            m.Id = ++_askId;
            m.Seconds = m.Kind == "roll" ? RollTimeout : 90;
            var tcs = new TaskCompletionSource<AnswerMsg?>();
            _asks[m.Id] = (client, tcs);
            NetSession.Send(NightMsg.Ask, OnlineView.Json(m), client);
            await Task.WhenAny(tcs.Task, Wait(m.Seconds + 15));
            _asks.Remove(m.Id);
            return tcs.Task.IsCompleted ? tcs.Task.Result : null;
        }

        void OnFriendLeft(string player)
        {
            if (_n.Room == null || !OnScreen) return;
            var p = Room!.PlayerNamed(player);
            foreach (var a in _asks.Values.Where(a => p != null && a.client == p.Id).ToList()) a.tcs.TrySetResult(null);
            AddLine($"（{player}の接続が切れた。部屋のコード {Room.Code} で入り直せば戻れる。戻れなければ、冒険者の欄の「今夜は抜けた」で物語から離せる）", Theme.Warn, 15);
            CheckClose();
            BroadcastState();
            RefreshAll();
            if (App.AppMain.Flag("-withdrawaway")) _ = WithdrawLater(player);   // tests: the host lets them leave after a while
        }

        async Task WithdrawLater(string player)
        {
            await Wait(20);
            if (Room?.PlayerNamed(player) is RoomPlayer p && !p.Connected)
                foreach (var id in Room.MineOf(player)) Withdraw(id);
        }

        void OnFriendBack(string player, ulong client)
        {
            if (_n.Room == null || !OnScreen) return;
            AddLine($"（{player}が戻ってきた）", Theme.Dim, 15);
            foreach (var id in Room!.MineOf(player))   // let go for tonight, but back after all: into the story again
                if (E.Find(id) is Adventurer back && back.Withdrawn && back.Life != Life.Dead) { back.Withdrawn = false; AddLine($"{back.Name}が物語に戻った", Theme.Dim, 15); Persist(); }
            foreach (var a in _asks.Values.Where(a => Room!.PlayerOf(a.client) == null).ToList()) a.tcs.TrySetResult(null);   // asked on the old connection
            CheckClose();
            BroadcastState();
            RefreshAll();
        }

        /// <summary>The host lets an away friend's adventurer leave for tonight: alive, out of the story, no veteran mark.</summary>
        void Withdraw(string id)
        {
            if (!HostOnline || E.Find(id) is not Adventurer a || a.Withdrawn) return;
            a.Withdrawn = true;
            _decls.Remove(id); _ready.Remove(id);
            var note = $"{a.Name}は今夜は抜けた（死なずに物語から離れる）";
            AddLine(note, Theme.Dim, 15);
            foreach (var f in Room!.Friends.ToList()) NetSession.Send(NightMsg.Report, OnlineView.Json(new ReportMsg { Note = note }), f.Id);
            Persist();
            CheckClose();
            BroadcastState();
            RefreshAll();
        }

        void OnOrder(ulong from, string json)
        {
            if (!OnScreen || Room?.PlayerOf(from) is not RoomPlayer p) return;
            var m = OnlineView.Read<NightOrder>(json);
            if (m.Hint) { E.Hints.Ask(E.Difficulty); AddLine($"（{p.PlayerName}が語り部に手がかりを頼んだ）", Theme.Dim, 14); }
            else if (Clip(m.Text, 100) is string text && text.Length > 0) { _t.Requests.Add(text); AddLine($"（{p.PlayerName}の注文: {text}）", Theme.Dim, 14); }
        }

        void OnClue(ulong from, string json)
        {
            if (!OnScreen || Room?.PlayerOf(from) is not RoomPlayer p) return;
            var id = OnlineView.Read<ClueMsg>(json).ClueId;
            var mine = Room.MineOf(p.PlayerName);
            if (!E.World.Clues.TryGetValue(id, out var who) || !who.Any(mine.Contains)) return;
            E.World.ShowClue(id);
            Persist();
            BroadcastState();
            RefreshAll();
        }

        /// <summary>After a turn: each friend's own report, then everyone's state.</summary>
        void SendReport(TurnReport r)
        {
            if (!HostOnline) return;
            foreach (var f in Room!.Friends.ToList())
                NetSession.Send(NightMsg.Report, OnlineView.Json(ReportMsg.For(r, Room.MineOf(f.PlayerName))), f.Id);
            BroadcastState();
        }

        /// <summary>A line in every friend's log (the host's screen has its own).</summary>
        void Notify(string note)
        {
            if (!HostOnline) return;
            foreach (var f in Room!.Friends.ToList()) NetSession.Send(NightMsg.Report, OnlineView.Json(new ReportMsg { Note = note }), f.Id);
        }

        void SendEnd(Chronicle? chronicle)
        {
            if (!HostOnline || _t.Ending == null) return;
            foreach (var f in Room!.Friends.ToList())
            {
                var mine = Room.MineOf(f.PlayerName);
                NetSession.Send(NightMsg.End, OnlineView.Json(new EndMsg { Save = OnlineView.SaveFor(_t, mine).ToJson(), Ending = _t.Ending, Chronicle = chronicle, Mine = mine }), f.Id);
            }
        }

        async Task AskGrowthAsync(string id)
        {
            var a = E.Find(id);
            if (a == null) return;
            var options = Table.GrowthOptions(a);
            var answer = Room!.ClientOf(id) is ulong c ? await Ask(c, new AskMsg { Kind = "growth", Who = { id } }) : null;
            var pick = answer != null && answer.Picks.Count > 0 && answer.Picks[0] >= 0 && answer.Picks[0] < options.Count ? options[answer.Picks[0]] : options.First(o => o.Toughen);
            if (_t.Grow(id, pick)) AddLine($"{a.Name}は成長した: {pick.Label}", Theme.GoldLight, 15);
            _growthAsked.Remove(id);
            Persist();
            BroadcastState();
            RefreshAll();
        }

        // ================================================================ a friend's side

        /// <summary>The host's state: the mirror of the table is rebuilt and the screen refreshed.</summary>
        public void OnState(StateMsg m, bool redraw = true)
        {
            Listen();
            _guestMine.Clear(); foreach (var id in m.Mine) _guestMine.Add(id);
            _ready.Clear(); foreach (var id in m.Declared) _ready.Add(id);
            _away.Clear(); foreach (var id in m.Away) _away.Add(id);
            _shown = m.Shown;
            _state = m;
            _busy = m.Busy;
            _closeAt = m.Closing > 0 ? Time.unscaledTime + m.Closing : 0;
            if (!redraw) return;
            var t = _n.Mirror(m.Save, this);
            if (t == null) return;
            _t = t;
            _busyText.text = _busy ? "語り部が考えている…" : "";
            RefreshAll();
        }

        void OnReport(ReportMsg m)
        {
            if (!OnScreen) return;
            if (m.Note.Length > 0) AddLine(m.Note, Theme.Dim, 15);
            if (m.Said.Count > 0)
            {
                foreach (var l in m.Said) AddLine(l, Theme.Hex("b9a989"), 15);
                foreach (var a in MyLiving) { _drafts.Remove(a.Id); _covers.Remove(a.Id); _secret.Remove(a.Id); }
                _input.SetTextWithoutNotify(""); _cover.SetTextWithoutNotify("");
            }
            var r = m.ToReport();
            if (r.Lines.Count > 0 || r.Narration.Length > 0 || r.PrivateLines.Count > 0 || r.PrivateNarration.Count > 0) Show(r);
        }

        void GuestDeclare(List<Declaration> decls)
        {
            NetSession.Send(NightMsg.Declare, OnlineView.Json(new NightDeclare { Declarations = decls, Ready = true }));
            foreach (var d in decls) _ready.Add(d.Who);
            RefreshCards();
            RefreshDeclState();
        }

        /// <summary>The host asks this player: their dice, coins, a new declaration, or a growth.</summary>
        async Task AnswerAsync(AskMsg m)
        {
            if (!OnScreen) return;
            var answer = new AnswerMsg { Id = m.Id };
            var rolls = m.Rolls.Select(v => v.ToRequest()).ToList();
            switch (m.Kind)
            {
                case "roll":
                    var used = new List<int>();
                    await RollDialog(rolls,
                        i => m.Rolls[i].MarkAllowed && E.Find(rolls[i].Who) is Adventurer a && a.MarksLeft - used.Count(u => rolls[u].Who == a.Id) > 0,
                        i => { used.Add(i); return true; }, m.Seconds);
                    answer.Picks = used;
                    break;
                case "coins":
                    var picks = await CoinsDialog(rolls);
                    answer.Picks = picks.Select((p, i) => (p, i)).Where(x => x.p).Select(x => x.i).ToList();
                    break;
                case "redeclare":
                    answer.Declarations = (await RedeclareDialog(m.Who.Zip(m.Refusals, (w, r) => (w, r)).ToList())).ToList();
                    break;
                case "growth":
                    if (E.Find(m.Who.FirstOrDefault() ?? "") is Adventurer g) answer.Picks = new List<int> { await GrowthDialog(g) };
                    break;
            }
            NetSession.Send(NightMsg.Answer, OnlineView.Json(answer));
        }
    }
}
