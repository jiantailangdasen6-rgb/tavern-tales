#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tavern.Core.Flow;
using Tavern.Core.Play;
using Tavern.Core.Story;
using Tavern.Unity.Net;
using UnityEngine;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// A room of friends on the new flow (09_Spec 14): the host owns the table; each friend brings one adventurer made on their own PC.
    /// Before the evening everyone pins a story on the shared map; during it, a friend who drops out comes back to their seat with the
    /// same code (matched by their sign-in id, else their name), or the host lets their adventurer leave for tonight.
    /// </summary>
    public sealed class NightRoom
    {
        readonly NightApp _n;
        public bool Host { get; }
        /// <summary>This player's name in the room (the host makes names unique).</summary>
        public string Me { get; private set; }
        public string Code { get; private set; } = "";
        public string Phase { get; private set; } = "map";
        public List<RoomPlayer> Players { get; private set; } = new List<RoomPlayer>();
        public Dictionary<string, string> Pins { get; private set; } = new Dictionary<string, string>();
        /// <summary>Adventurer id → player name, once the evening has begun.</summary>
        public Dictionary<string, string> Owners { get; } = new Dictionary<string, string>();
        public Difficulty Difficulty { get; set; } = Difficulty.Standard;
        readonly SavedAdventurer _mine;

        /// <summary>The room changed (players, pins, phase): the map redraws.</summary>
        public event Action? Changed;
        /// <summary>Host: a friend left (their player name) or came back to their seat (name, client id).</summary>
        public event Action<string>? Left;
        public event Action<string, ulong>? Back;

        NightRoom(NightApp n, bool host, string me, SavedAdventurer mine) { _n = n; Host = host; Me = me; _mine = mine; }

        public static async Task<NightRoom> HostAsync(NightApp n, string me, Adventurer mine)
        {
            var code = await NetSession.HostAsync();
            var room = new NightRoom(n, true, me, SavedAdventurer.From(mine)) { Code = code };
            room.Players.Add(new RoomPlayer { Id = NetSession.LocalId, Key = NetSession.PlayerKey, PlayerName = me, Version = GameVersion.Current, Host = true, Adventurer = room._mine });
            room.Listen();
            return room;
        }

        /// <summary>Reopens a saved online evening as a new room: the seats wait for their players to come back.</summary>
        public static async Task<NightRoom> ResumeAsync(NightApp n, NightSeats seats)
        {
            var code = await NetSession.HostAsync();
            var room = new NightRoom(n, true, seats.Host, new SavedAdventurer()) { Code = code, Phase = "table" };
            foreach (var kv in seats.Owners) room.Owners[kv.Key] = kv.Value;
            foreach (var name in seats.Owners.Values.Distinct())
                room.Players.Add(new RoomPlayer
                {
                    Id = name == seats.Host ? NetSession.LocalId : ulong.MaxValue, PlayerName = name, Host = name == seats.Host, Connected = name == seats.Host,
                    Key = seats.Keys.TryGetValue(name, out var k) ? k : "", Version = GameVersion.Current,
                });
            room.Listen();
            return room;
        }

        public static async Task<NightRoom> JoinAsync(NightApp n, string code, string me, Adventurer mine)
        {
            await NetSession.JoinAsync(code);
            var room = new NightRoom(n, false, me, SavedAdventurer.From(mine)) { Code = code.Trim().ToUpperInvariant() };
            room.Listen();
            NetSession.Send(NightMsg.Hello, OnlineView.Json(new NightHello { PlayerName = me, Key = NetSession.PlayerKey, Version = GameVersion.Current, Adventurer = room._mine }));
            return room;
        }

        // ---------------------------------------------------------------- messages

        void Listen()
        {
            NetSession.ClearHandlers();
            if (Host)
            {
                NetSession.On(NightMsg.Hello, OnHello);
                NetSession.On(NightMsg.Pin, (from, json) =>
                {
                    if (PlayerOf(from) is RoomPlayer p) { Pins[p.PlayerName] = OnlineView.Read<PinMsg>(json).StoryId; Send(); }
                });
                NetSession.ClientLeft += OnLeft;
            }
            else
            {
                NetSession.On(NightMsg.Room, (_, json) => OnRoom(OnlineView.Read<RoomMsg>(json)));
                NetSession.On(NightMsg.State, (_, json) => _n.OnGuestState(OnlineView.Read<StateMsg>(json)));
                NetSession.On(NightMsg.End, (_, json) => _n.OnGuestEnd(OnlineView.Read<EndMsg>(json)));
                NetSession.On(NightMsg.Closed, (_, _) => { Close(); _n.ShowError("ホストが今夜を区切りました（続きはホストが部屋を開き直してから、同じ手順で入れます）"); });
                NetSession.Disconnected += OnDisconnected;
            }
        }

        void OnDisconnected()
        {
            if (_n.Room != this) return;
            Close();
            _n.ShowError($"ホストとの接続が切れました。部屋のコード {Code} でもう一度入れば、自分の冒険者に戻れます");
        }

        void OnHello(ulong from, string json)
        {
            var m = OnlineView.Read<NightHello>(json);
            if (m.Version != GameVersion.Current) { Refuse(from, $"版が違います（ホスト {GameVersion.Current} / あなた {(m.Version.Length > 0 ? m.Version : "古い版")}）。最新の版で入ってください"); return; }
            var name = m.PlayerName.Trim().Length == 0 ? "旅人" : m.PlayerName.Trim();
            // a friend coming back to their seat: the same sign-in id (even before the host has noticed the old connection drop),
            // else the same name of a seat whose player is away
            var seat = Players.FirstOrDefault(p => !p.Host && m.Key.Length > 0 && p.Key == m.Key && p.Id != from)
                ?? Players.FirstOrDefault(p => !p.Connected && !p.Host && p.PlayerName == name);
            if (seat != null && Phase == "map" && seat.Connected) { Players.Remove(seat); Pins.Remove(seat.PlayerName); seat = null; }   // before the evening: just a new entry
            if (seat != null)
            {
                seat.Id = from; seat.Connected = true; seat.Key = m.Key.Length > 0 ? m.Key : seat.Key;
                Debug.Log($"[Online] {seat.PlayerName} came back");
                Send();
                Back?.Invoke(seat.PlayerName, from);
                Changed?.Invoke();
                return;
            }
            if (Phase != "map") { Refuse(from, "物語はもう始まっています（入れるのは、途中で抜けた人だけです）"); return; }
            if (Players.Count >= NetSession.MaxPlayers) { Refuse(from, "部屋がいっぱいです（4 人まで）"); return; }
            while (Players.Any(p => p.PlayerName == name)) name += "＊";
            Players.Add(new RoomPlayer { Id = from, Key = m.Key, PlayerName = name, Version = m.Version, Adventurer = m.Adventurer });
            Send();
            Changed?.Invoke();
        }

        void Refuse(ulong to, string why)
        {
            Debug.LogWarning("[Online] refused: " + why);
            NetSession.Send(NightMsg.Room, OnlineView.Json(new RoomMsg { Code = Code, Version = GameVersion.Current, Refused = why }), to);
        }

        void OnLeft(ulong id)
        {
            var p = PlayerOf(id);
            if (p == null) return;
            if (Phase == "map") { Players.Remove(p); Pins.Remove(p.PlayerName); }
            else p.Connected = false;
            Debug.Log($"[Online] {p.PlayerName} left");
            Send();
            if (Phase != "map") Left?.Invoke(p.PlayerName);
            Changed?.Invoke();
        }

        void OnRoom(RoomMsg m)
        {
            if (m.Refused.Length > 0) { Close(); _n.ShowError("部屋に入れませんでした: " + m.Refused); return; }
            var phaseBefore = Phase;
            Players = m.Players; Pins = m.Pins; Phase = m.Phase; Code = m.Code;
            if (Enum.TryParse<Difficulty>(m.Difficulty, out var d)) Difficulty = d;
            if (Players.FirstOrDefault(p => p.Id == NetSession.LocalId) is RoomPlayer self) Me = self.PlayerName;
            if (Phase == "handouts" && phaseBefore != "handouts") _n.ShowWaiting("語り部がハンドアウトを用意しています…");
            else if (Phase == "map") Changed?.Invoke();
        }

        /// <summary>Host: everyone gets the room as it is now.</summary>
        public void Send()
        {
            if (!Host) return;
            NetSession.Send(NightMsg.Room, OnlineView.Json(new RoomMsg
            {
                Code = Code, Version = GameVersion.Current, Phase = Phase, Players = Players, Pins = Pins, Difficulty = Difficulty.ToString(),
            }));
        }

        public void SetPin(string storyId)
        {
            Pins[Me] = storyId;
            if (Host) Send(); else NetSession.Send(NightMsg.Pin, OnlineView.Json(new PinMsg { StoryId = storyId }));
            Changed?.Invoke();
        }

        public void SetPhase(string phase) { Phase = phase; Send(); }

        /// <summary>The story with the most pins (a tie: the dice pick one of them); null when nobody pinned.</summary>
        public StoryDef? Chosen(IRandom rng)
        {
            var counts = Pins.Values.GroupBy(id => id).Select(g => (id: g.Key, n: g.Count())).ToList();
            if (counts.Count == 0) return null;
            var top = counts.Where(c => c.n == counts.Max(x => x.n)).Select(c => c.id).ToList();
            var id = top[top.Count == 1 ? 0 : rng.Next(0, top.Count)];
            return _n.Stories.FirstOrDefault(s => s.Id == id);
        }

        /// <summary>Tonight's party in room order (the host first), with unique ids and names; Owners filled.</summary>
        public List<Adventurer> Party()
        {
            var party = new List<Adventurer>();
            var used = new HashSet<string>();
            Owners.Clear();
            foreach (var (p, i) in Players.Select((p, i) => (p, i)))
            {
                var a = p.Adventurer.ToAdventurer(_n.Classes);
                var name = a.Name.Trim().Length == 0 ? "旅人" : a.Name;
                while (!used.Add(name)) name += "二";
                var saved = SavedAdventurer.From(a);
                // each PC names its own adventurer "pc1": keep an id only while it is unique in the room
                if (saved.Id.Length == 0 || saved.Id.StartsWith("pc") || party.Any(x => x.Id == saved.Id)) saved.Id = "pc" + (i + 1);
                while (party.Any(x => x.Id == saved.Id)) saved.Id += "_";
                saved.Name = name;
                var b = saved.ToAdventurer(_n.Classes);
                party.Add(b);
                Owners[b.Id] = p.PlayerName;
            }
            return party;
        }

        // ---------------------------------------------------------------- seats at the table

        public RoomPlayer? PlayerOf(ulong client) => Players.FirstOrDefault(p => p.Id == client && (p.Connected || p.Host));
        public RoomPlayer? PlayerNamed(string name) => Players.FirstOrDefault(p => p.PlayerName == name);
        public string OwnerOf(string adventurerId) => Owners.TryGetValue(adventurerId, out var o) ? o : "";
        public bool IsMine(string adventurerId) => OwnerOf(adventurerId) == Me;

        /// <summary>The client id of the friend playing this adventurer, when they are connected (null for the host's own and the away).</summary>
        public ulong? ClientOf(string adventurerId)
        {
            var p = PlayerNamed(OwnerOf(adventurerId));
            return p != null && !p.Host && p.Connected ? p.Id : null;
        }

        public bool Away(string adventurerId) => PlayerNamed(OwnerOf(adventurerId)) is RoomPlayer p && !p.Connected;
        public IEnumerable<RoomPlayer> Friends => Players.Where(p => !p.Host && p.Connected);
        public List<string> MineOf(string player) => Owners.Where(kv => kv.Value == player).Select(kv => kv.Key).ToList();

        public NightSeats Seats() => new NightSeats
        {
            Host = Me, Owners = new Dictionary<string, string>(Owners),
            Keys = Players.Where(p => p.Key.Length > 0).ToDictionary(p => p.PlayerName, p => p.Key),
        };

        public void Close()
        {
            NetSession.ClientLeft -= OnLeft;
            NetSession.Disconnected -= OnDisconnected;
            NetSession.ClearHandlers();
            NetSession.Leave();
            if (_n.Room == this) _n.Room = null;
        }
    }

    /// <summary>Who plays whom in a saved online evening (so the host can reopen the room and friends get their seats back).</summary>
    public sealed class NightSeats
    {
        public string Host = "";
        public Dictionary<string, string> Owners = new Dictionary<string, string>();
        public Dictionary<string, string> Keys = new Dictionary<string, string>();
        public bool HasFriends => Owners.Values.Any(o => o != Host);
    }
}
