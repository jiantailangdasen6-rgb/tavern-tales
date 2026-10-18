#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Game;
using Tavern.Core.Gm;
using Tavern.Core.Rules;
using Tavern.Unity.Net;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The room before the evening: the host shows the code and starts when everyone is in; friends wait.
    /// Each player brings one character made on their own PC.
    /// </summary>
    public sealed class LobbyScreen
    {
        readonly AppMain _app;
        readonly bool _host;
        readonly string _me;
        readonly CharacterSave _myCharacter;
        readonly List<(LobbyPlayer info, CharacterSave character)> _players = new List<(LobbyPlayer, CharacterSave)>();
        Transform _list = null!;
        Text _status = null!;
        Button? _start;
        int _startWhen;
        Func<Storyteller>? _teller;
        Func<StoryChoice>? _story;
        // friends: what the host is choosing and doing
        Text? _hostView, _codeView;
        RawImage? _hostTeller;
        string _phase = "", _sentChoice = "";

        public LobbyScreen(AppMain app, bool host, string me, CharacterSave myCharacter)
        {
            _app = app; _host = host; _me = me; _myCharacter = myCharacter;
            _startWhen = int.TryParse(AppMain.Arg("-startwhen"), out var n) ? n : 0;
        }

        public void Build(Transform root)
        {
            AudioDirector.Music("tavern");
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.78f)).rectTransform.Fill();
            UiKit.Label(root, _host ? "部屋を作った" : "部屋に入った", 48, Theme.Gold, 0, 60, 1920, 70, serif: true, align: TextAnchor.MiddleCenter);
            if (_host)
            {
                UiKit.Label(root, "友人にコードか招待リンクを送ってください", 22, Theme.Dim, 0, 150, 1920, 36, align: TextAnchor.MiddleCenter);
                UiKit.Label(root, NetSession.JoinCode ?? "", 72, Theme.GoldLight, 0, 190, 1920, 100, serif: true, align: TextAnchor.MiddleCenter);
                UiKit.Button(root, "コードをコピー", () => GUIUtility.systemCopyBuffer = NetSession.JoinCode ?? "", 700, 300, 240, 50, 20);
            UiKit.Button(root, "招待リンクをコピー", () => GUIUtility.systemCopyBuffer = InviteLink.For(NetSession.JoinCode ?? ""), 960, 300, 260, 50, 20, gold: true);
            }
            if (!_host)
            {
                _codeView = UiKit.Label(root, "", 22, Theme.Dim, 0, 150, 1920, 36, align: TextAnchor.MiddleCenter);
                UiKit.Label(root, "ホストの準備", 18, Theme.Dim, 300, 826, 300, 26);
                var box = UiKit.Panel(root, "HostChoice", 300, 852, 1320, 110).transform;
                _hostTeller = UiKit.Picture(box, null, 10, 8, 70, 94);
                _hostView = UiKit.Label(box, "ホストが語り部と物語を選んでいます…", 20, Theme.Ink, 96, 12, 1210, 90);
            }
            _list = UiKit.Rect("Players", root).Place(460, 390, 1000, 480);
            _status = UiKit.Label(root, "", 20, Theme.Ink, 0, _host ? 922 : 972, 1920, 36, align: TextAnchor.MiddleCenter);   // friends: under the host box
            UiKit.Button(root, "部屋を出る", () => { NetSession.ClearHandlers(); NetSession.Leave(); _app.ShowTitle(); }, 90, 980, 240, 64, 22);
            if (_host) { _start = UiKit.Button(root, "この顔ぶれで冒険をはじめる", Begin, 1380, 980, 450, 70, 26, gold: true); _teller = StorytellerPicker.Build(root, 400, 966); _story = StoryChooser.Build(root, _app.Story, _app.Scenario.Title, 300, 852); }
            if (_host) { _app.ResetPrefetch(); _app.Tick = Tick; }

            NetSession.ClearHandlers();
            if (_host)
            {
                _players.Add((new LobbyPlayer { Id = NetSession.LocalId, PlayerName = _me, CharacterName = _myCharacter.Name, ClassId = _myCharacter.ClassId, Portrait = _myCharacter.Portrait }, _myCharacter));
                NetSession.On("hello", OnHello);
                NetSession.ClientLeft += OnLeft;
            }
            else
            {
                NetSession.On("lobby", (_, json) => { var m = NetJson.Read<LobbyMsg>(json); ShowPlayers(m.Players); ShowHost(m); });
                NetSession.On("turn", (_, json) => { Detach(); _app.EnterAsFriend(NetJson.Read<TurnMsg>(json), _me); });
                NetSession.Send("hello", NetJson.Json(new HelloMsg { PlayerName = _me, Character = _myCharacter }));
            }
            Refresh();
        }

        void Detach()
        {
            NetSession.ClientLeft -= OnLeft;
        }

        void OnHello(ulong from, string json)
        {
            var m = NetJson.Read<HelloMsg>(json);
            if (_players.Count >= NetSession.MaxPlayers) return;
            var name = m.PlayerName.Trim().Length == 0 ? "旅人" : m.PlayerName.Trim();
            while (_players.Any(p => p.info.PlayerName == name)) name += "＊";
            var c = m.Character;
            while (_players.Any(p => p.character.Name == c.Name)) c.Name += "二";
            _players.Add((new LobbyPlayer { Id = from, PlayerName = name, CharacterName = c.Name, ClassId = c.ClassId, Portrait = c.Portrait }, c));
            Refresh();
        }

        void OnLeft(ulong id)
        {
            _players.RemoveAll(p => p.info.Id == id);
            Refresh();
        }

        void Refresh()
        {
            ShowPlayers(_players.Select(p => p.info).ToList());
            if (!_host) return;
            NetSession.Send("lobby", NetJson.Json(Snapshot()));
            if (_startWhen > 0 && _players.Count >= _startWhen) Begin();
        }

        void ShowPlayers(List<LobbyPlayer> players)
        {
            UiKit.Clear(_list);
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var row = UiKit.Panel(_list, "Player", 0, i * 150, 1000, 136).transform;
                var tex = ArtLibrary.Get(p.Portrait);
                var pic = UiKit.Picture(row, tex, 10, 8, 88, 120);
                pic.uvRect = ArtLibrary.Cover(tex, 88, 120, 0.72f);
                UiKit.Label(row, p.CharacterName, 30, Theme.Gold, 120, 18, 500, 44, serif: true);
                UiKit.Label(row, $"{(_app.Db.Classes.TryGetValue(p.ClassId, out var cls) ? cls.Name : p.ClassId)}　—　{p.PlayerName}{(p.PlayerName == _me ? "（あなた）" : "")}", 20, Theme.Ink, 120, 70, 800, 34);
            }
            _status.text = _host
                ? (players.Count < 2 ? "友人が入るのを待っています…（ひとりで始めることもできます）" : $"{players.Count} 人がそろいました")
                : "ホストが冒険をはじめるのを待っています…";
        }

        // Writing the story ahead while friends sit in the lobby: once two or more are in and nobody came or left for a while.
        string _lastKey = "";
        float _stableSince;

        void Tick()
        {
            if (_start == null) { _app.Tick = null; return; }   // the lobby is gone
            if (Time.frameCount % 15 != 0) return;
            // friends see the host's choices as they change
            var choice = ChoiceKey();
            if (choice != _sentChoice) { _sentChoice = choice; Refresh(); }
            if (!_start.interactable || _story == null || _players.Count < 2) return;
            var story = _story();
            var key = string.Join("|", _players.Select(p => p.character.Name + "/" + p.character.ClassId)) + story.Prepared + story.TemplateId + story.Request;
            if (key != _lastKey) { _lastKey = key; _stableSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _stableSince > 8f) _app.PrefetchStory(_players.Select(p => p.character.ToCharacter()).ToList(), story);
        }

        string StoryLabel(StoryChoice s) => s.Prepared ? $"用意された物語「{_app.Scenario.Title}」"
            : s.TemplateId == null ? "おまかせ（語り部が作る）" : $"型「{_app.Story.Template(s.TemplateId)?.Name ?? s.TemplateId}」";

        string ChoiceKey() => (_teller?.Invoke().Id ?? "") + "|" + (_story?.Invoke() is StoryChoice s ? StoryLabel(s) + "|" + s.Request : "");

        LobbyMsg Snapshot()
        {
            var story = _story?.Invoke();
            return new LobbyMsg
            {
                Players = _players.Select(p => p.info).ToList(), Code = NetSession.JoinCode ?? "",
                Teller = _teller?.Invoke().Id ?? "", Story = story != null ? StoryLabel(story) : "", Request = story?.Request ?? "", Phase = _phase,
            };
        }

        /// <summary>Friends: the host's storyteller, story and request, and what the host is doing now.</summary>
        void ShowHost(LobbyMsg m)
        {
            _phase = m.Phase;
            if (_codeView != null && m.Code.Length > 0) _codeView.text = $"部屋のコード {m.Code}";
            _status.text = _phase == "writing" ? "語り部が今宵の物語を考えています…（長くても 1 分ほど）" : "ホストが語り部と物語を選んでいます。そろったらホストが始めます";
            if (_hostView == null || _hostTeller == null || m.Teller.Length == 0) return;
            var t = Storyteller.Find(m.Teller);
            var tex = ArtLibrary.Get(t.Portrait);
            _hostTeller.texture = tex;
            _hostTeller.uvRect = ArtLibrary.Cover(tex, 70, 94, 0.7f);
            _hostView.text = $"今宵の語り部: <color=#c9a24a>{t.Name}</color>　<size=16><color=#a8977a>{t.Blurb}</color></size>\n今宵の物語: {m.Story}" +
                (m.Request.Length > 0 ? $"　注文「{m.Request}」" : "");
        }

        async void Begin()
        {
            if (!_host || _start == null || !_start.interactable) return;
            _start.interactable = false;
            _phase = "writing";
            NetSession.Send("lobby", NetJson.Json(Snapshot()));   // friends: "the storyteller is writing tonight's story"
            Detach();
            var party = _players.Select(p => p.character.ToCharacter()).ToList();
            var teller = _teller?.Invoke() ?? Storyteller.Olwen;
            // A story the storyteller writes now (friends wait in the lobby); if that fails, the prepared one.
            var scenario = await _app.PrepareScenarioAsync(party, _story?.Invoke(), fallBackToPrepared: true) ?? _app.Scenario;
            var game = TavernGame.New(_app.Db, scenario, teller, party, _app.Ai.Everyday, _app.Ai.Highlight,
                new SeededRandom((ulong)DateTime.Now.Ticks));
            foreach (var p in _players) game.State.Owners[p.character.Name] = p.info.PlayerName;
            game.State.HostPlayer = _me;
            _app.EnterAsHost(game, _me, _players.Where(p => p.info.PlayerName != _me).ToDictionary(p => p.info.PlayerName, p => p.info.Id));
        }
    }
}
