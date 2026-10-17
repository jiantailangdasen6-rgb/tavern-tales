#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Game;
using Tavern.Core.Rules;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;

namespace Tavern.Unity.App
{
    public enum NetRole { Solo, Host, Client }

    /// <summary>
    /// Online play on the table (Docs/04_Online.md). The host's game is the real one; it waits for everyone's declaration,
    /// plays the turn and sends the state. A friend's game is a mirror rebuilt from each state message.
    /// </summary>
    public sealed partial class TableScreen
    {
        sealed class StatusMsg { public List<string> Declared = new List<string>(); public bool Busy; }

        readonly NetRole _role;
        readonly string _me;
        /// <summary>Characters whose action is in for the coming turn.</summary>
        readonly HashSet<string> _declared = new HashSet<string>();
        /// <summary>Host: everyone's declarations for the coming turn.</summary>
        readonly Dictionary<string, Declaration> _hostDrafts = new Dictionary<string, Declaration>();
        /// <summary>Host: player name -> connection.</summary>
        readonly Dictionary<string, ulong> _players = new Dictionary<string, ulong>();
        readonly Dictionary<int, TaskCompletionSource<List<int>>> _rerollWaits = new Dictionary<int, TaskCompletionSource<List<int>>>();
        readonly HashSet<string> _askedGrowth = new HashSet<string>();
        /// <summary>Client: growth choices the host asked of this PC.</summary>
        readonly List<string> _growthAsks = new List<string>();
        int _rerollId;

        bool IsMine(Character c) => _role == NetRole.Solo || (_game.State.Owners.TryGetValue(c.Name, out var o) && o == _me);
        string OwnerOf(Character c) => _game.State.Owners.TryGetValue(c.Name, out var o) ? o : "";

        /// <summary>Host: who plays in this room (from the lobby).</summary>
        public void SetPlayers(IDictionary<string, ulong> players) { foreach (var kv in players) _players[kv.Key] = kv.Value; }

        void SetupNet()
        {
            _selected = Math.Max(0, _game.Party.FindIndex(IsMine));
            if (_role == NetRole.Solo) return;
            NetSession.ClearHandlers();
            if (_role == NetRole.Host)
            {
                NetSession.On("declare", OnDeclare);
                NetSession.On("reroll", (from, json) =>
                {
                    var m = NetJson.Read<RerollMsg>(json);
                    if (_rerollWaits.TryGetValue(m.Id, out var w)) w.TrySetResult(m.PickedSeq);
                });
                NetSession.On("growth", (from, json) =>
                {
                    var m = NetJson.Read<GrowthMsg>(json);
                    if (OwnerIs(m.Character, from) && _game.Grow(m.Character, m.Choice))
                    {
                        _askedGrowth.Remove(m.Character);
                        _app.Save(_game);
                        EnqueueNew(_game.State.Log);
                        BroadcastTurn();
                        RefreshParty();
                    }
                });
                NetSession.On("order", (from, json) =>
                {
                    var m = NetJson.Read<OrderMsg>(json);
                    if (m.Length.Length > 0) _game.State.NarrationLength = m.Length;
                    if (m.Text.Length > 0) { _game.StorytellerRequest = m.Text; Flash($"{PlayerOf(from)} が語り部に注文した: {m.Text}"); }
                });
                NetSession.On("hello", OnRejoin);
                NetSession.ClientLeft += OnClientLeft;
            }
            else
            {
                NetSession.On("turn", (_, json) => OnTurn(NetJson.Read<TurnMsg>(json)));
                NetSession.On("status", (_, json) =>
                {
                    var m = NetJson.Read<StatusMsg>(json);
                    _declared.Clear(); foreach (var n in m.Declared) _declared.Add(n);
                    SetBusy(m.Busy);
                    RefreshParty(); RefreshActions();
                });
                NetSession.On("ask_reroll", (from, json) => { _ = AnswerRerollAsync(NetJson.Read<AskRerollMsg>(json)); });
                NetSession.On("ask_growth", (_, json) => { var m = NetJson.Read<GrowthMsg>(json); if (!_growthAsks.Contains(m.Character)) _growthAsks.Add(m.Character); });
                NetSession.Disconnected += OnHostLost;
            }
        }

        void LeaveRoom()
        {
            NetSession.ClientLeft -= OnClientLeft;
            NetSession.Disconnected -= OnHostLost;
            NetSession.ClearHandlers();
            NetSession.Leave();
        }

        string PlayerOf(ulong id) => _players.FirstOrDefault(kv => kv.Value == id).Key ?? "?";
        bool OwnerIs(string character, ulong id) => _game.State.Owners.TryGetValue(character, out var o) && _players.TryGetValue(o, out var cid) && cid == id;

        void SetBusy(bool busy)
        {
            if (_role != NetRole.Client) return;
            _busy = busy;
            _busyText.text = busy ? "語り部が考えている…" : "";
        }

        // ---------------------------------------------------------------- declaring

        void DeclareOnline()
        {
            var mine = _game.Party.Where(p => IsMine(p) && (p.CanAct || (_drafts.TryGetValue(p.Name, out var d) && d.Basic == "revive")))
                .Select(Draft).Where(HasAction).ToList();
            if (mine.Count == 0) { Flash("自分の冒険者の行動を決めてください（カード、基本の行動、または文章）"); return; }
            if (_role == NetRole.Client)
            {
                NetSession.Send("declare", NetJson.Json(new DeclareMsg { Declarations = mine }));
                foreach (var d in mine) _declared.Add(d.Character);
            }
            else
            {
                foreach (var d in mine) { _hostDrafts[d.Character] = d; _declared.Add(d.Character); }
                BroadcastStatus();
                TrySubmitHost();
            }
            RefreshParty(); RefreshActions();
        }

        void OnDeclare(ulong from, string json)
        {
            var m = NetJson.Read<DeclareMsg>(json);
            foreach (var d in m.Declarations.Where(d => OwnerIs(d.Character, from)))
            {
                _hostDrafts[d.Character] = d;
                _declared.Add(d.Character);
            }
            BroadcastStatus();
            RefreshParty();
            TrySubmitHost();
        }

        /// <summary>Host: when every connected player's active characters have declared, the turn is played.</summary>
        void TrySubmitHost()
        {
            if (_role != NetRole.Host || _busy || _game.Ended) return;
            var waiting = _game.Party.Where(p => p.CanAct && Connected(OwnerOf(p))).Where(p => !_declared.Contains(p.Name)).ToList();
            if (waiting.Count > 0 || _hostDrafts.Count == 0) return;
            var decls = _hostDrafts.Values.ToList();
            _ = PlayHostTurnAsync(decls);
        }

        bool Connected(string player) => player == _me || _players.ContainsKey(player);

        async Task PlayHostTurnAsync(List<Declaration> decls)
        {
            var ok = await RunAsync(() => _game.SubmitAsync(decls));
            if (ok) { _drafts.Clear(); _input.SetTextWithoutNotify(""); }
        }

        /// <summary>Host, after a turn: the next turn starts with nobody declared.</summary>
        void AfterHostTurn()
        {
            if (_role != NetRole.Host) return;
            _hostDrafts.Clear();
            _declared.Clear();
        }

        // ---------------------------------------------------------------- state messages

        void BroadcastTurn()
        {
            if (_role != NetRole.Host) return;
            NetSession.Send("turn", NetJson.Json(TurnMsg.From(_game, _declared, _busy)));
        }

        void BroadcastStatus()
        {
            if (_role != NetRole.Host) return;
            NetSession.Send("status", NetJson.Json(new StatusMsg { Declared = _declared.ToList(), Busy = _busy }));
        }

        /// <summary>Client: the host's state after a turn (or on joining). Rebuild the mirror and play the new lines.</summary>
        void OnTurn(TurnMsg msg)
        {
            _game = Mirror.Build(msg, _app.Db, _app.Scenario);
            _declared.Clear(); foreach (var n in msg.Declared) _declared.Add(n);
            SetBusy(msg.Busy);
            EnqueueNew(_game.State.Log);
            if (!_declared.Any(n => _game.Find(n) is Character c && IsMine(c))) { _drafts.Clear(); _input.SetTextWithoutNotify(""); }
            RefreshAll();
        }

        void OnRejoin(ulong from, string json)
        {
            // A friend who dropped joins again under the same player name and gets their character back.
            var m = NetJson.Read<HelloMsg>(json);
            if (!_game.State.Owners.ContainsValue(m.PlayerName)) return;
            _players[m.PlayerName] = from;
            Flash($"{m.PlayerName} が戻ってきた");
            NetSession.Send("turn", NetJson.Json(TurnMsg.From(_game, _declared, _busy)), from);
        }

        void OnClientLeft(ulong id)
        {
            var name = PlayerOf(id);
            if (name == "?") return;
            _players.Remove(name);
            Flash($"{name} の接続が切れた（同じコードで入り直せば戻れる）");
            foreach (var w in _rerollWaits.Values) w.TrySetResult(new List<int>());
            TrySubmitHost();
        }

        void OnHostLost()
        {
            Flash("ホストとの接続が切れた。ホストが部屋を開き直したら、新しいコードで入り直してください。");
            _busy = true;
        }

        // ---------------------------------------------------------------- fate coins and growth across PCs

        /// <summary>Host: failed rolls of this PC's characters are asked here; a friend's are asked on their PC.</summary>
        async Task<IReadOnlyList<LogEntry>> HostChooseRerollsAsync(IReadOnlyList<LogEntry> failed)
        {
            var picked = new List<LogEntry>();
            foreach (var group in failed.GroupBy(e => _game.Find(e.Who) is Character c ? OwnerOf(c) : _me))
            {
                if (group.Key == _me || !_players.TryGetValue(group.Key, out var client))
                {
                    if (group.Key == _me) picked.AddRange(await ChooseRerollsAsync(group.ToList()));
                    continue;
                }
                var id = ++_rerollId;
                var wait = new TaskCompletionSource<List<int>>();
                _rerollWaits[id] = wait;
                var msg = new AskRerollMsg
                {
                    Id = id,
                    Rolls = _game.State.Log.Skip(Math.Max(0, _game.State.Log.Count - 20)).ToList(),
                    FailedSeq = group.Select(e => e.Seq).ToList(),
                };
                NetSession.Send("ask_reroll", NetJson.Json(msg), client);
                EnqueueNew(_game.State.Log);
                _busyText.text = $"{group.Key} が運命のコインを使うか考えている…";
                var done = await Task.WhenAny(wait.Task, Task.Delay(45000));
                _rerollWaits.Remove(id);
                _busyText.text = "語り部が考えている…";
                if (done == wait.Task) picked.AddRange(group.Where(e => wait.Task.Result.Contains(e.Seq)));
            }
            return picked;
        }

        /// <summary>Client: show the dice so far, then offer this PC's fate coins.</summary>
        async Task AnswerRerollAsync(AskRerollMsg msg)
        {
            EnqueueNew(msg.Rolls);
            var failed = msg.Rolls.Where(e => msg.FailedSeq.Contains(e.Seq)).ToList();
            var picks = failed.Count == 0 ? new List<LogEntry>() : (await ChooseRerollsAsync(failed)).ToList();
            NetSession.Send("reroll", NetJson.Json(new RerollMsg { Id = msg.Id, PickedSeq = picks.Select(e => e.Seq).ToList() }));
        }

        /// <summary>Host: a friend's character finished an act; ask their PC to choose the growth.</summary>
        void AskRemoteGrowth()
        {
            if (_role != NetRole.Host) return;
            foreach (var name in _game.State.PendingGrowth.ToList())
            {
                var c = _game.Find(name);
                if (c == null || IsMine(c) || _askedGrowth.Contains(name)) continue;
                if (!_players.TryGetValue(OwnerOf(c), out var client)) continue;
                _askedGrowth.Add(name);
                NetSession.Send("ask_growth", NetJson.Json(new GrowthMsg { Character = name }), client);
            }
        }

        /// <summary>The next growth choice this PC makes.</summary>
        string? MyPendingGrowth()
        {
            if (_role == NetRole.Client)
            {
                _growthAsks.RemoveAll(n => !_game.State.PendingGrowth.Contains(n));
                return _growthAsks.FirstOrDefault();
            }
            return _game.State.PendingGrowth.FirstOrDefault(n => _game.Find(n) is Character c && IsMine(c));
        }

        void SendGrowth(string name, string choice)
        {
            _growthAsks.Remove(name);
            NetSession.Send("growth", NetJson.Json(new GrowthMsg { Character = name, Choice = choice }));
        }

        void SendOrder(string text)
        {
            if (_role == NetRole.Client) NetSession.Send("order", NetJson.Json(new OrderMsg { Text = text, Length = _game.State.NarrationLength }));
            else if (text.Length > 0) _game.StorytellerRequest = text;
        }
    }
}

namespace Tavern.Unity.App
{
    public sealed partial class TableScreen
    {
        /// <summary>Host: sends the whole state to everyone (after reopening a saved evening).</summary>
        public void SendStateToAll() => BroadcastTurn();
    }
}

namespace Tavern.Unity.App
{
    public sealed partial class TableScreen
    {
        static string VolumeLabel() => "音: " + new[] { "なし", "小", "中", "大" }[AudioDirector.Level];
    }
}
