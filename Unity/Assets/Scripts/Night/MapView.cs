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
    /// introduction, pick the difficulty, start. Played stories show their endings. (The map picture comes in stage 7.)
    /// </summary>
    public sealed class MapView
    {
        const float MapX = 60, MapY = 110, MapW = 1180, MapH = 860;
        readonly NightApp _n;
        readonly List<Adventurer> _party;
        StoryDef? _pick;
        Text _title = null!, _body = null!;
        Button _start = null!;
        readonly Dictionary<Difficulty, Image> _diffButtons = new Dictionary<Difficulty, Image>();
        TavernState _tavern = null!;

        public MapView(NightApp n, List<Adventurer> party) { _n = n; _party = party; }

        public void Build(Transform root)
        {
            _tavern = _n.Store.LoadTavern();
            UiKit.Box(root, "Bg", Theme.Bg).rectTransform.Fill();
            UiKit.Label(root, "ヴァルデン辺境領の地図", 38, Theme.Gold, MapX, 40, 900, 56, serif: true);
            UiKit.Label(root, $"今宵の顔ぶれ: {string.Join("・", _party.Select(a => $"{a.Name}（{a.Class.Name}）"))}", 18, Theme.Ink, MapX + 520, 56, 1300, 30);

            // the map: parchment with the regions
            var map = UiKit.Box(root, "Map", Theme.Hex("3a2e20"), frame: true);
            map.rectTransform.Place(MapX, MapY, MapW, MapH);
            UiKit.Box(map.transform, "Inner", Theme.Hex("6b5638"), frame: false).rectTransform.Fill(10);
            foreach (var r in _n.Regions)
            {
                var stories = _n.Stories.Where(s => s.Region == r.Id).ToList();
                var x = MapX + 20 + r.X * (MapW - 40);
                var y = MapY + 20 + r.Y * (MapH - 40);
                var dot = UiKit.Box(root, "Spot", stories.Count > 0 ? Theme.Gold : Theme.Hex("8a7756"));
                dot.rectTransform.Place(x - 9, y - 9, 18, 18);
                UiKit.Label(root, r.Name, stories.Count > 0 ? 18 : 15, stories.Count > 0 ? Theme.GoldLight : Theme.Hex("d8c8a4"), x - 150, y + 12, 300, 44, align: TextAnchor.UpperCenter);
                foreach (var (s, i) in stories.Select((s, i) => (s, i)))
                {
                    var played = _tavern.Played.ContainsKey(s.Id);
                    var pin = UiKit.Button(root, (played ? "✓ " : "◆ ") + s.Title, () => Pick(s), x - 120, y - 58 - i * 46, 240, 40, 17, gold: true);
                    pin.name = "Pin_" + s.Id;
                }
            }

            // the side panel: the picked story
            var side = UiKit.Panel(root, "Side", 1270, 110, 590, 860).transform;
            _title = UiKit.Label(side, "地図の印を押すと、物語の導入が出ます", 28, Theme.Gold, 24, 20, 540, 80, serif: true);
            _body = UiKit.Label(side, "", 19, Theme.Ink, 24, 110, 540, 520);
            UiKit.Label(side, "難易度", 18, Theme.Dim, 24, 650, 200, 26);
            foreach (var (d, i) in new[] { Difficulty.Gentle, Difficulty.Standard, Difficulty.Harsh }.Select((d, i) => (d, i)))
            {
                var b = UiKit.Button(side, d.Label(), () => { _n.Difficulty = d; RefreshDiff(); }, 24 + i * 180, 680, 170, 48, 20);
                _diffButtons[d] = b.GetComponent<Image>();
            }
            RefreshDiff();
            _start = UiKit.Button(side, "この物語で始める", () => { if (_pick != null) _n.Begin(_pick, _party, _n.Difficulty); }, 24, 770, 542, 66, 26, gold: true);
            _start.interactable = false;
            UiKit.Button(root, "顔ぶれを選び直す", _n.ShowParty, MapX, 990, 260, 56, 20);
            if (_n.Stories.Count == 1) Pick(_n.Stories[0]);
        }

        void RefreshDiff()
        {
            foreach (var kv in _diffButtons) kv.Value.color = kv.Key == _n.Difficulty ? Theme.Hex("6a5228") : Theme.Hex("2e241a");
        }

        void Pick(StoryDef s)
        {
            _pick = s;
            _title.text = s.Title;
            var played = _tavern.Played.TryGetValue(s.Id, out var list) ? "\n\nこれまで: " + string.Join(" / ", list) : "";
            _body.text = $"{(s.Long ? "中編（2 晩）" : "短編（一晩）")}・{s.Kind}・闇の深さ {s.Darkness}\n\n{s.Intro}{played}";
            _start.interactable = true;
        }
    }
}
