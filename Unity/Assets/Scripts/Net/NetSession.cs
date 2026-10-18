#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Tavern.Unity.Net
{
    /// <summary>
    /// Online play over Unity Relay room codes (Docs/04_Online.md). Only named messages are used: the host owns the game,
    /// friends send declarations and receive the state. Messages are JSON, gzip-compressed, sent reliably and fragmented.
    /// </summary>
    public static class NetSession
    {
        public const int MaxPlayers = 4;   // PartyScreen.MaxSeats
        static NetworkManager? _nm;
        static readonly Dictionary<string, Action<ulong, string>> Handlers = new Dictionary<string, Action<ulong, string>>();

        public static bool Active => _nm != null && (_nm.IsHost || _nm.IsClient);
        public static bool IsHost => _nm != null && _nm.IsHost;
        public static bool IsClient => _nm != null && _nm.IsClient && !_nm.IsHost;
        public static ulong LocalId => _nm != null ? _nm.LocalClientId : 0;
        public static string? JoinCode { get; private set; }

        /// <summary>Host: someone joined / left (client id). Client: the connection to the host ended.</summary>
        public static event Action<ulong>? ClientJoined, ClientLeft;
        public static event Action? Disconnected;

        /// <summary>Online needs the build linked to a Unity Cloud project with Relay (ProjectSettings cloudProjectId).</summary>
        public static bool Available => !string.IsNullOrEmpty(Application.cloudProjectId);

        static NetworkManager Manager()
        {
            if (_nm != null) return _nm;
            var go = new GameObject("Network");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            // WebSockets for everyone: a browser can only speak WebSockets, and the exe host must use the same kind of Relay
            // connection as the browser friends it lets in.
            transport.UseWebSockets = true;
            transport.MaxPayloadSize = 256 * 1024;
            transport.MaxSendQueueSize = 512 * 1024;
            _nm = go.AddComponent<NetworkManager>();
            _nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
                ClientConnectionBufferTimeout = 30,
            };
            _nm.OnClientConnectedCallback += id => { if (_nm.IsHost && id != NetworkManager.ServerClientId) ClientJoined?.Invoke(id); };
            _nm.OnClientDisconnectCallback += id =>
            {
                if (_nm.IsHost) { if (id != NetworkManager.ServerClientId) ClientLeft?.Invoke(id); }
                else Disconnected?.Invoke();
            };
            return _nm;
        }

        static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                // Separate anonymous identities per -profile, so several copies on one PC are different players.
                var profile = new string((App.AppMain.Arg("-profile") ?? "default").Where(char.IsLetterOrDigit).Take(30).ToArray());
                options.SetProfile(profile.Length == 0 ? "default" : profile);
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        const string RelayKind = "wss";

        /// <summary>A wait driven by the player loop (Task.Delay needs a timer thread, which the browser build does not have).</summary>
        static async Task Delay(float seconds) => await Awaitable.WaitForSecondsAsync(seconds);

        /// <summary>Opens a room and returns its code.</summary>
        public static async Task<string> HostAsync()
        {
            if (!Available) throw new InvalidOperationException("オンラインの設定（Unity Cloud）が見つからない");
            await SignInAsync();
            var nm = Manager();
            var allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
            var code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            nm.GetComponent<UnityTransport>().SetRelayServerData(allocation.ToRelayServerData(RelayKind));
            if (!nm.StartHost()) throw new InvalidOperationException("部屋を作れなかった");
            RegisterHandlers();
            JoinCode = code;
            return code;
        }

        /// <summary>Joins a room by its code. Completes when connected.</summary>
        public static async Task JoinAsync(string code)
        {
            if (!Available) throw new InvalidOperationException("オンラインの設定（Unity Cloud）が見つからない");
            code = code.Trim().ToUpperInvariant();
            await SignInAsync();
            // Relay sometimes takes a while to let a second or third player in: try a few times with a fresh allocation.
            for (int attempt = 1; ; attempt++)
            {
                var nm = Manager();
                var join = await RelayService.Instance.JoinAllocationAsync(code);
                nm.GetComponent<UnityTransport>().SetRelayServerData(join.ToRelayServerData(RelayKind));
                var connected = new TaskCompletionSource<bool>();
                void OnConnected(ulong id) { if (id == nm.LocalClientId) connected.TrySetResult(true); }
                nm.OnClientConnectedCallback += OnConnected;
                if (!nm.StartClient()) throw new InvalidOperationException("接続を始められなかった");
                var done = await Task.WhenAny(connected.Task, Delay(25));
                nm.OnClientConnectedCallback -= OnConnected;
                if (done == connected.Task) break;
                Leave();
                Debug.LogWarning($"[Online] join attempt {attempt} timed out");
                if (attempt >= 3) throw new TimeoutException("部屋に入れなかった（コードを確かめてください）");
                await Delay(1.5f);
            }
            RegisterHandlers();
            JoinCode = code;
        }

        public static void Leave()
        {
            if (_nm != null && (_nm.IsHost || _nm.IsClient)) _nm.Shutdown();
            Registered.Clear();   // a new session gets a new messaging manager
            JoinCode = null;
        }

        // ---------------------------------------------------------------- messages

        /// <summary>Handles a message name (the payload is the JSON text). Handlers run on the main thread.</summary>
        public static void On(string name, Action<ulong, string> handler) => Handlers[name] = handler;
        public static void ClearHandlers() => Handlers.Clear();

        static readonly HashSet<string> Registered = new HashSet<string>();
        static readonly string[] Names = { "hello", "lobby", "start", "declare", "turn", "ask_reroll", "reroll", "ask_growth", "growth", "order", "status" };

        static void RegisterHandlers()
        {
            var cm = _nm!.CustomMessagingManager;
            foreach (var name in Names)
            {
                if (!Registered.Add(name)) continue;
                var n = name;
                cm.RegisterNamedMessageHandler(n, (sender, reader) =>
                {
                    reader.ReadValueSafe(out int length);
                    var bytes = new byte[length];
                    reader.ReadBytesSafe(ref bytes, length);
                    var json = Unzip(bytes);
                    if (Handlers.TryGetValue(n, out var h)) h(sender, json);
                });
            }
        }

        /// <summary>Host: to one client (or all with to == null). Client: to the host.</summary>
        public static void Send(string name, string json, ulong? to = null)
        {
            if (_nm == null || !(_nm.IsHost || _nm.IsClient)) return;
            var bytes = Zip(json);
            using var writer = new FastBufferWriter(bytes.Length + 8, Allocator.Temp, bytes.Length + 64);
            writer.WriteValueSafe(bytes.Length);
            writer.WriteBytesSafe(bytes, bytes.Length);
            var cm = _nm.CustomMessagingManager;
            if (_nm.IsHost)
            {
                if (to.HasValue) { if (to.Value != NetworkManager.ServerClientId) cm.SendNamedMessage(name, to.Value, writer, NetworkDelivery.ReliableFragmentedSequenced); }
                else
                {
                    var others = _nm.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToList();
                    if (others.Count > 0) cm.SendNamedMessage(name, others, writer, NetworkDelivery.ReliableFragmentedSequenced);
                }
            }
            else cm.SendNamedMessage(name, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
        }

        static byte[] Zip(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var b = Encoding.UTF8.GetBytes(text);
                gz.Write(b, 0, b.Length);
            }
            return ms.ToArray();
        }

        static string Unzip(byte[] data)
        {
            using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var r = new StreamReader(gz, Encoding.UTF8);
            return r.ReadToEnd();
        }
    }
}
