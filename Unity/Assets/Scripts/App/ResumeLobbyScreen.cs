#nullable enable
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Game;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// "Continue another day" online: the host reopens the saved evening as a new room; friends join with the same
    /// player name and get their characters back. The host can start before everyone is back (they can rejoin later).
    /// </summary>
    public sealed class ResumeLobbyScreen
    {
        readonly AppMain _app;
        readonly TavernGame _game;
        readonly Dictionary<string, ulong> _connected = new Dictionary<string, ulong>();
        Transform _list = null!;
        Text _status = null!;
        bool _started;
        readonly int _startWhen;

        public ResumeLobbyScreen(AppMain app, TavernGame game)
        {
            _app = app; _game = game;
            _startWhen = int.TryParse(AppMain.Arg("-startwhen"), out var n) ? n : 0;
        }

        string Me => _game.State.HostPlayer;
        IEnumerable<string> Friends => _game.State.Owners.Values.Distinct().Where(p => p != Me);

        public void Build(Transform root)
        {
            AudioDirector.Music("tavern");
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.78f)).rectTransform.Fill();
            UiKit.Label(root, "続きの部屋を開いた", 48, Theme.Gold, 0, 60, 1920, 70, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Label(root, "前と同じ名前で入ってもらえば、前のキャラクターに戻れます", 22, Theme.Dim, 0, 150, 1920, 36, align: TextAnchor.MiddleCenter);
            UiKit.Label(root, NetSession.JoinCode ?? "", 72, Theme.GoldLight, 0, 190, 1920, 100, serif: true, align: TextAnchor.MiddleCenter);
            UiKit.Button(root, "コードをコピー", () => GUIUtility.systemCopyBuffer = NetSession.JoinCode ?? "", 700, 300, 240, 50, 20);
            UiKit.Button(root, "招待リンクをコピー", () => GUIUtility.systemCopyBuffer = InviteLink.For(NetSession.JoinCode ?? ""), 960, 300, 260, 50, 20, gold: true);
            _list = UiKit.Rect("Players", root).Place(460, 390, 1000, 480);
            _status = UiKit.Label(root, "", 22, Theme.Ink, 0, 890, 1920, 40, align: TextAnchor.MiddleCenter);
            UiKit.Button(root, "部屋を閉じる", () => { NetSession.ClearHandlers(); NetSession.Leave(); _app.ShowTitle(); }, 90, 980, 240, 64, 22);
            UiKit.Button(root, "続きをはじめる", Begin, 1380, 980, 450, 70, 26, gold: true);

            NetSession.ClearHandlers();
            NetSession.On("hello", (from, json) =>
            {
                var m = NetJson.Read<HelloMsg>(json);
                if (!Friends.Contains(m.PlayerName)) return;   // only the evening's own players
                _connected[m.PlayerName] = from;
                Refresh();
            });
            NetSession.ClientLeft += OnLeft;
            Refresh();
        }

        void OnLeft(ulong id)
        {
            foreach (var k in _connected.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) _connected.Remove(k);
            Refresh();
        }

        void Refresh()
        {
            UiKit.Clear(_list);
            int i = 0;
            foreach (var c in _game.Party)
            {
                var owner = _game.State.Owners.TryGetValue(c.Name, out var o) ? o : Me;
                var here = owner == Me || _connected.ContainsKey(owner);
                var step = _game.Party.Count > 3 ? 112f : 150f;   // four: shorter rows
                var row = UiKit.Panel(_list, "Player", 0, i++ * step, 1000, step - 14).transform;
                var tex = ArtLibrary.Get(c.Portrait);
                var ph = step - 30; var pw = ph * 0.73f;
                var p = UiKit.Picture(row, tex, 10, 8, pw, ph);
                p.uvRect = ArtLibrary.Cover(tex, pw, ph, 0.72f);
                p.color = here ? Color.white : new Color(0.4f, 0.4f, 0.4f);
                UiKit.Label(row, c.Name, 30, Theme.Gold, pw + 30, step > 120 ? 18 : 6, 500, 44, serif: true);
                UiKit.Label(row, $"{owner}{(owner == Me ? "（あなた）" : "")}　{(here ? "<color=#9fd08a>いる</color>" : "まだ来ていない")}", 20, Theme.Ink, pw + 30, step > 120 ? 70 : 54, 800, 34);
            }
            var missing = Friends.Count(f => !_connected.ContainsKey(f));
            _status.text = missing == 0 ? "全員そろいました" : $"あと {missing} 人（来ていない人は、始めてからでも同じ名前で入れます）";
            if (_startWhen > 0 && 1 + _connected.Count >= _startWhen) Begin();
        }

        void Begin()
        {
            if (_started) return;
            _started = true;
            NetSession.ClientLeft -= OnLeft;
            _app.EnterAsHost(_game, Me, _connected, opening: false);
        }
    }
}
