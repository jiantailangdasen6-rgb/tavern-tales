#nullable enable
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;
using Tavern.Core.Story;
using Tavern.Unity.App;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// The region map spread on the tavern table (09_Spec 11): a mark where each story happens; press one for its title and
    /// introduction, pick the difficulty, start. Played stories show their endings. In a room everyone's pin is on the map and the host
    /// starts the story with the most pins.
    /// </summary>
    public sealed class MapView
    {
        const float MapX = 60, MapY = 110, MapW = 1180, MapH = 860;
        readonly NightApp _n;
        readonly List<Adventurer> _party;
        readonly NightRoom? _room;
        StoryDef? _pick;
        Text _title = null!, _body = null!;
        Button _start = null!;
        readonly Dictionary<Difficulty, Button> _diffButtons = new Dictionary<Difficulty, Button>();
        TavernState _tavern = null!;

        public MapView(NightApp n, List<Adventurer> party) { _n = n; _party = party; _room = n.Room; }

        public void Build(Transform root)
        {
            _tavern = _n.Store.LoadTavern();
            UiKit.Box(root, "Bg", Theme.Bg).rectTransform.Fill();
            UiKit.Picture(root, ArtLibrary.Get("bg_tavern"), 0, 0, 1920, 1080).color = new Color(0.22f, 0.19f, 0.16f);
            UiKit.Title(root, "ヴァルデン辺境領の地図", 40, Theme.FrameGold, MapX, 34, 900, 56).gameObject.AddComponent<Shadow>();
            UiKit.Rule(root, MapX, 88, 480, Theme.FrameDim);
            if (_room == null)
                UiKit.Label(root, $"今宵の顔ぶれ: {string.Join("・", _party.Select(a => $"{a.Name}（{a.Class.Name}）"))}", 18, Theme.Ink, MapX + 520, 56, 1300, 30);
            else BuildRoomTop(root, _room);

            // the map: parchment with the regions
            var map = UiKit.Box(root, "Map", Theme.Hex("3a2e20"), frame: true);
            map.rectTransform.Place(MapX, MapY, MapW, MapH);
            var art = ArtLibrary.Get("bg_map_valden");
            var pic = UiKit.Picture(map.transform, art, 0, 0, MapW, MapH);
            pic.rectTransform.Fill(8);
            pic.uvRect = ArtLibrary.Cover(art, MapW - 16, MapH - 16);
            UiKit.Frame(map.transform, thin: false);
            // Region names first, then each story's sign where it overlaps nothing placed so far (eleven stories crowded the north).
            var taken = new List<Rect>();
            var spots = _n.Regions.Select(r => (r, stories: _n.Stories.Where(s => s.Region == r.Id).ToList(),
                x: MapX + 20 + r.X * (MapW - 40), y: MapY + 20 + r.Y * (MapH - 40))).ToList();
            foreach (var (r, stories, x, y) in spots)
            {
                var dot = UiKit.Box(root, "Spot", stories.Count > 0 ? Theme.Gold : Theme.Hex("d8c8a4"));
                dot.rectTransform.Place(x - 9, y - 9, 18, 18);
                var nameW = Mathf.Min(300, r.Name.Length * 18 + 16);
                var name = UiKit.Label(root, r.Name, stories.Count > 0 ? 18 : 15, stories.Count > 0 ? Theme.GoldLight : Theme.Hex("efe3c8"), x - 150, y + 12, 300, 44, align: TextAnchor.UpperCenter);
                name.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
                taken.Add(new Rect(x - nameW / 2f, y - 10, nameW, 50));
            }
            foreach (var (r, stories, x, y) in spots)
                foreach (var s in stories)
                {
                    var played = _tavern.Played.ContainsKey(s.Id);
                    var pins = _room?.Pins.Where(kv => kv.Value == s.Id).Select(kv => kv.Key).ToList() ?? new List<string>();
                    var label = (played ? "✓ " : "◆ ") + s.Title + (pins.Count > 0 ? $"　<color=#e8d9a8>ピン {pins.Count}</color>" : "");
                    var at = FreeSpot(taken, x, y);
                    taken.Add(at);
                    var pin = UiKit.Button(root, label, () => Pick(s, fromClick: true), at.x, at.y, at.width, at.height, 16, gold: true);
                    pin.name = "Pin_" + s.Id;
                }

            // the side panel: the picked story (below)
            BuildSide(root);
        }

        const float SignW = 250, SignH = 38;

        /// <summary>
        /// Where a story's sign goes: above its spot first (stacking upward), then below the region's name, then to the left or the
        /// right, at the first place that overlaps no sign or name already placed and stays on the map.
        /// </summary>
        static Rect FreeSpot(List<Rect> taken, float x, float y)
        {
            var tries = new List<Vector2>();
            for (int i = 0; i < 4; i++) tries.Add(new Vector2(x - SignW / 2, y - 54 - i * (SignH + 6)));
            for (int i = 0; i < 3; i++) tries.Add(new Vector2(x - SignW / 2, y + 44 + i * (SignH + 6)));
            for (int i = 0; i < 3; i++) { tries.Add(new Vector2(x - SignW - 24, y - SignH / 2 + i * (SignH + 6))); tries.Add(new Vector2(x + 24, y - SignH / 2 + i * (SignH + 6))); }
            for (int i = 0; i < 2; i++) { tries.Add(new Vector2(x - SignW - 10, y - 54 - i * (SignH + 6))); tries.Add(new Vector2(x + 10, y - 54 - i * (SignH + 6))); }
            Rect? best = null;
            var bestOverlap = float.MaxValue;
            foreach (var p in tries)
            {
                var r = new Rect(p.x, p.y, SignW, SignH);
                if (r.xMin < MapX + 6 || r.xMax > MapX + MapW - 6 || r.yMin < MapY + 6 || r.yMax > MapY + MapH - 6) continue;
                var overlap = taken.Sum(t => Mathf.Max(0, Mathf.Min(t.xMax, r.xMax) - Mathf.Max(t.xMin, r.xMin)) * Mathf.Max(0, Mathf.Min(t.yMax, r.yMax) - Mathf.Max(t.yMin, r.yMin)));
                if (overlap <= 0) return r;
                if (overlap < bestOverlap) { bestOverlap = overlap; best = r; }   // nowhere free: the least covered place
            }
            return best ?? new Rect(tries[0].x, tries[0].y, SignW, SignH);
        }

        void BuildSide(Transform root)
        {
            // the side panel: the picked story
            var side = UiKit.Panel(root, "Side", 1270, 110, 590, 860).transform;
            _title = UiKit.Title(side, "地図の印を押すと、物語の導入が出ます", 26, Theme.FrameGold, 28, 24, 534, 80);
            _body = UiKit.Label(side, "", 19, Theme.Ink, 24, 110, 540, 520);
            UiKit.Rule(side, 28, 636, 534, Theme.FrameDim);
            UiKit.Label(side, _room != null && !_room.Host ? "難易度（ホストが決める）" : "難易度", 18, Theme.Ink, 28, 650, 300, 26);
            foreach (var (d, i) in new[] { Difficulty.Gentle, Difficulty.Standard, Difficulty.Harsh }.Select((d, i) => (d, i)))
            {
                var b = UiKit.Button(side, d.Label(), () => SetDifficulty(d), 24 + i * 180, 680, 170, 48, 20);
                b.interactable = _room == null || _room.Host;
                _diffButtons[d] = b;
            }
            RefreshDiff();
            var startLabel = _room == null ? "この物語で始める" : _room.Host ? "ピンの多い物語で始める" : "ホストが始めます";
            _start = UiKit.Button(side, startLabel, Start, 24, 770, 542, 66, 26, gold: true);
            _start.interactable = false;
            if (_room == null) UiKit.Button(root, "顔ぶれを選び直す", _n.ShowParty, MapX, 990, 260, 56, 20);
            else UiKit.Button(root, "部屋を出る", _n.BackToTitle, MapX, 990, 260, 56, 20);

            var mine = _room != null && _room.Pins.TryGetValue(_room.Me, out var id) ? _n.Stories.FirstOrDefault(s => s.Id == id) : null;
            if (mine != null) Pick(mine, fromClick: false);
            else if (_n.Stories.Count == 1 && _room == null) Pick(_n.Stories[0], fromClick: false);
        }

        /// <summary>The room: the code to share (host), and who is in with their adventurer.</summary>
        void BuildRoomTop(Transform root, NightRoom room)
        {
            if (room.Host)
            {
                UiKit.Label(root, $"部屋のコード <color=#e8d9a8><size=30>{room.Code}</size></color>", 20, Theme.Ink, MapX + 520, 46, 420, 44);
                UiKit.Button(root, "コードをコピー", () => GUIUtility.systemCopyBuffer = room.Code, MapX + 940, 48, 170, 42, 16);
                UiKit.Button(root, "招待リンクをコピー", () => GUIUtility.systemCopyBuffer = InviteLink.For(room.Code), MapX + 1120, 48, 200, 42, 16, gold: true);
            }
            else UiKit.Label(root, $"部屋のコード {room.Code}", 20, Theme.Ink, MapX + 520, 56, 600, 30);
            var who = room.Players.Select(p =>
            {
                var a = p.Adventurer;
                var cls = _n.Classes.Classes.TryGetValue(a.ClassId, out var c) ? c.Name : a.ClassId;
                var pin = room.Pins.TryGetValue(p.PlayerName, out var s) ? _n.Stories.FirstOrDefault(x => x.Id == s)?.Title ?? s : "まだ";
                return $"{a.Name}（{cls}）— {p.PlayerName}{(p.Host ? "・ホスト" : "")}{(p.PlayerName == room.Me ? "（あなた）" : "")}　ピン: {pin}";
            });
            UiKit.Label(root, (room.Host && room.Players.Count < 2 ? "友人が入るのを待っています（ひとりでも始められます）\n" : "") + string.Join("\n", who),
                17, Theme.Ink, MapX + 290, 984, 1500, 96);
        }

        void SetDifficulty(Difficulty d)
        {
            if (_room != null && !_room.Host) return;
            _n.Difficulty = d;
            if (_room != null) { _room.Difficulty = d; _room.Send(); }
            RefreshDiff();
        }

        void RefreshDiff()
        {
            var current = _room?.Difficulty ?? _n.Difficulty;
            foreach (var kv in _diffButtons) kv.Value.GetComponent<Image>().color = kv.Key == current ? Theme.Hex("6e1f14") : Theme.Hex("241a12");
        }

        void Pick(StoryDef s, bool fromClick)
        {
            _pick = s;
            _title.text = s.Title;
            var played = _tavern.Played.TryGetValue(s.Id, out var list) ? "\n\nこれまで: " + string.Join(" / ", list) : "";
            _body.text = $"{(s.Long ? "中編（2 晩）" : "短編（一晩）")}・{s.Kind}・闇の深さ {s.Darkness}\n\n{s.Intro}{played}";
            _start.interactable = _room == null || _room.Host;
            if (fromClick && _room != null) _room.SetPin(s.Id);   // the room redraws the map with the pin
        }

        void Start()
        {
            if (_room == null) { if (_pick != null) _n.Begin(_pick, _party, _n.Difficulty); return; }
            if (!_room.Host) return;
            var story = _room.Chosen(new SeededRandom((ulong)System.DateTime.Now.Ticks)) ?? _pick;
            if (story != null) _n.Begin(story, _room.Party(), _room.Difficulty);
        }
    }
}
