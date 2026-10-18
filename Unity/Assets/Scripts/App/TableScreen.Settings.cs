#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>How the narration reads on this PC: text size and how fast it types itself out (kept in PlayerPrefs).</summary>
    public static class Reading
    {
        public static readonly string[] SizeNames = { "小", "中", "大" };
        public static readonly string[] SpeedNames = { "ゆっくり", "ふつう", "はやい", "すぐ全部" };

        /// <summary>0..2 (default 1, the old size).</summary>
        public static int Size
        {
            get => Mathf.Clamp(int.TryParse(AppMain.Arg("-textsize"), out var t) ? t : PlayerPrefs.GetInt("text_size", 1), 0, 2);   // -textsize: screenshots, not saved
            set { PlayerPrefs.SetInt("text_size", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }

        /// <summary>0..3 (default 1, the old speed; 3 shows each narration at once).</summary>
        public static int Speed
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("text_speed", 1), 0, 3);
            set { PlayerPrefs.SetInt("text_speed", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }

        /// <summary>Scales the log's font sizes (the narration is 21 at "中").</summary>
        public static float Scale => new[] { 0.86f, 1f, 1.17f }[Size];
        public static int Font(int baseSize) => Mathf.RoundToInt(baseSize * Scale);

        /// <summary>Characters a second while the narration types out; infinity for "すぐ全部".</summary>
        public static float CharsPerSecond => new[] { 24f, 42f, 80f, float.PositiveInfinity }[Speed];
    }

    /// <summary>Sound, text size and narration speed for this PC (the table's top bar and the title screen).</summary>
    public static class SettingsPanel
    {
        /// <summary>Fills layer with the panel; onTextSize redraws text after a size change, close removes the layer.</summary>
        public static void Show(Transform layer, Action? onTextSize, Action close)
        {
            var shade = UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.6f));
            shade.rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Settings", 560, 300, 800, 400).transform;
            UiKit.Label(panel, "設定（この PC だけ）", 28, Theme.Gold, 30, 20, 600, 40, serif: true);

            Row(panel, 90, "音", new[] { "なし", "小", "中", "大" }, () => AudioDirector.Level, v => AudioDirector.Level = v);
            Row(panel, 160, "文字の大きさ", Reading.SizeNames, () => Reading.Size, v => { Reading.Size = v; onTextSize?.Invoke(); });
            Row(panel, 230, "語りの速さ", Reading.SpeedNames, () => Reading.Speed, v => Reading.Speed = v);

            UiKit.Button(panel, "閉じる", () => close(), 590, 330, 180, 50, 20, gold: true);
        }

        static void Row(Transform panel, float y, string label, string[] names, Func<int> get, Action<int> set)
        {
            UiKit.Label(panel, label, 19, Theme.Dim, 30, y + 8, 170, 32);
            var buttons = new List<Button>();
            void Paint() { for (int i = 0; i < buttons.Count; i++) buttons[i].GetComponent<Image>().color = i == get() ? Theme.Hex("6b4f24") : Theme.Hex("2e241a"); }
            for (int i = 0; i < names.Length; i++)
            {
                var v = i;
                buttons.Add(UiKit.Button(panel, names[i], () => { set(v); Paint(); }, 200 + i * 140, y, 130, 46, 18));
            }
            Paint();
        }
    }

    public sealed partial class TableScreen
    {
        void ShowSettings()
        {
            UiKit.Clear(_overlay.transform);
            SettingsPanel.Show(_overlay.transform, RebuildLog, () => UiKit.Clear(_overlay.transform));
        }

        /// <summary>Draws the log again at the new text size (the last 30 lines, at once).</summary>
        void RebuildLog()
        {
            if (_typing != null) { _typing = null; _typingFull = ""; }
            UiKit.Clear(_logContent);
            foreach (var e in _game.State.Log.Skip(Math.Max(0, _game.State.Log.Count - 30))) AppendLog(e, instant: true);
        }
    }
}
