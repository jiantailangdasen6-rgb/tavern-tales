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

        /// <summary>0..2 (default 1).</summary>
        public static int Size
        {
            get => Mathf.Clamp(int.TryParse(AppMain.Arg("-textsize"), out var t) ? t : PlayerPrefs.GetInt("text_size", 1), 0, 2);   // -textsize: screenshots, not saved
            set { PlayerPrefs.SetInt("text_size", Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }

        /// <summary>0..3 (default 1; 3 shows each narration at once).</summary>
        public static int Speed
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("text_speed", 1), 0, 3);
            set { PlayerPrefs.SetInt("text_speed", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }

        /// <summary>Scales the table log's font sizes (the narration is 21 at "中").</summary>
        public static float Scale => new[] { 0.86f, 1f, 1.17f }[Size];
        public static int Font(int baseSize) => Mathf.RoundToInt(baseSize * Scale);

        /// <summary>Characters a second while the narration types out; infinity for "すぐ全部".</summary>
        public static float CharsPerSecond => new[] { 18f, 28f, 56f, float.PositiveInfinity }[Speed];
    }

    /// <summary>
    /// Which Gemini key the host's storyteller uses (kept in PlayerPrefs on the host PC; the keys themselves stay in the
    /// environment): the free tier (GEMINI_API_KEY, 500 calls a day) or the paid key (GEMINI_API_KEY_PAID, prepaid credit).
    /// With the paid key the narration model can be chosen; the sorting stays on Flash-Lite, which the rules were tuned on.
    /// </summary>
    public static class AiChoice
    {
        public static readonly string[] SourceNames = { "無料枠", "有料キー" };
        public static readonly string[] ModelNames = { "Flash-Lite", "Flash", "Pro" };
        public static readonly string[] ModelNotes =
        {
            "いつもと同じ（調整済み）。いちばん安い",
            "語りを Flash で。語りが豊かになり、費用は数倍",
            "語りは Flash、大事な場面（ボスの登場と結末）は Pro。いちばん高い",
        };

        public static bool Paid
        {
            get => PlayerPrefs.GetInt("ai_paid", 0) == 1;
            set { PlayerPrefs.SetInt("ai_paid", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>0 Flash-Lite, 1 Flash, 2 Pro (paid key only).</summary>
        public static int Model
        {
            get => Mathf.Clamp(int.TryParse(AppMain.Arg("-aimodel"), out var m) ? m : PlayerPrefs.GetInt("ai_model", 0), 0, ModelNames.Length - 1);   // -aimodel: tests, not saved
            set { PlayerPrefs.SetInt("ai_model", Mathf.Clamp(value, 0, ModelNames.Length - 1)); PlayerPrefs.Save(); }
        }

        public static bool HasKey(string name) =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User));
    }

    /// <summary>Sound, text size and narration speed for this PC; for the host also the storyteller's AI.</summary>
    public static class SettingsPanel
    {
        /// <summary>
        /// Fills layer with the panel; onTextSize redraws text after a size change, close removes the layer. onAi (the host: on the
        /// title screen and at the table, for comparing models in a test play) adds the AI choice; it applies a change and returns
        /// the AI now in use, which aiNow shows at first.
        /// </summary>
        public static void Show(Transform layer, Action? onTextSize, Action close, Func<string>? onAi = null, string aiNow = "")
        {
            var shade = UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.6f));
            shade.rectTransform.Fill();
            var h = onAi != null ? 600f : 400f;
            var panel = UiKit.Panel(layer, "Settings", 560, (1080 - h) / 2, 800, h).transform;
            UiKit.Heading(panel, "設定（この PC だけ）", 30, 18, 740, "ic_settings", size: 28);

            Row(panel, 90, "音", new[] { "なし", "小", "中", "大" }, () => AudioDirector.Level, v => AudioDirector.Level = v);
            Row(panel, 160, "文字の大きさ", Reading.SizeNames, () => Reading.Size, v => { Reading.Size = v; onTextSize?.Invoke(); });
            Row(panel, 230, "語りの速さ", Reading.SpeedNames, () => Reading.Speed, v => Reading.Speed = v);

            if (onAi != null)
            {
                UiKit.Rule(panel, 30, 296, 740, Theme.FrameDim);
                var note = UiKit.Label(panel, "", 16, Theme.Ink, 200, 428, 570, 70);
                List<Button> models = null!;
                void Explain()
                {
                    foreach (var b in models) b.interactable = AiChoice.Paid;
                    var key = AiChoice.Paid ? "GEMINI_API_KEY_PAID" : "GEMINI_API_KEY";
                    note.text = !AiChoice.HasKey(key) ? $"<color=#f08a7c>この PC に {key} が見つかりません</color>"
                        : AiChoice.Paid ? AiChoice.ModelNotes[AiChoice.Model] + "。仕分け（ルールの判断）は調整済みの Flash-Lite のまま"
                        : "1 日 500 回まで（16:00 に戻る）。費用はかかりません";
                }
                var now = UiKit.Label(panel, "", 16, Theme.Dim, 30, h - 58, 540, 30);
                void Changed() { now.text = Now(onAi()); Explain(); }
                static string Now(string d) => "いま: " + d.Replace("AI: ", "");
                now.text = Now(aiNow);
                Row(panel, 316, "語り部の AI", AiChoice.SourceNames, () => AiChoice.Paid ? 1 : 0, v => { AiChoice.Paid = v == 1; Changed(); });
                models = Row(panel, 376, "有料のモデル", AiChoice.ModelNames, () => AiChoice.Model, v => { AiChoice.Model = v; Changed(); });
                Explain();
            }

            UiKit.Button(panel, "閉じる", () => close(), 590, h - 70, 180, 50, 20, gold: true);
        }

        static List<Button> Row(Transform panel, float y, string label, string[] names, Func<int> get, Action<int> set)
        {
            UiKit.Label(panel, label, 19, Theme.Ink, 30, y + 8, 170, 32);
            var buttons = new List<Button>();
            void Paint() { for (int i = 0; i < buttons.Count; i++) buttons[i].GetComponent<Image>().color = i == get() ? Theme.Hex("6e1f14") : Theme.Hex("241a12"); }
            for (int i = 0; i < names.Length; i++)
            {
                var v = i;
                buttons.Add(UiKit.Button(panel, names[i], () => { set(v); Paint(); }, 200 + i * 140, y, 130, 46, 18));
            }
            Paint();
            return buttons;
        }
    }
}
