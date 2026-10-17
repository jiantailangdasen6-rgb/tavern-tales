#nullable enable
using Tavern.Core.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Tavern.Unity.Boot
{
    /// <summary>
    /// M0 placeholder screen: proves the Core package, uGUI and the Japanese OS font work in a Windows build.
    /// Replaced by the real title / table screens in M1.
    /// </summary>
    public sealed class BootView : MonoBehaviour
    {
        void Start()
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = NewUi("Background", canvasGo.transform);
            Stretch(bg);
            bg.gameObject.AddComponent<Image>().color = new Color32(0x14, 0x10, 0x0c, 0xff);

            var serif = Font.CreateDynamicFontFromOSFont(new[] { "Yu Mincho", "MS Mincho" }, 64);
            var sans = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "MS Gothic" }, 28);

            Label(canvasGo.transform, "酒場の語り部", serif, 96, new Color32(0xc9, 0xa2, 0x4a, 0xff), new Vector2(0, 120));
            var roll = Dice.D20(new SeededRandom((ulong)System.DateTime.Now.Ticks));
            Label(canvasGo.transform, $"準備中（M0）　試しのサイコロ: {roll}", sans, 32, new Color32(0xe9, 0xdc, 0xc0, 0xff), new Vector2(0, -20));
        }

        static RectTransform NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static void Label(Transform parent, string text, Font font, int size, Color color, Vector2 pos)
        {
            var rt = NewUi("Label", parent);
            rt.sizeDelta = new Vector2(1600, size * 1.6f);
            rt.anchoredPosition = pos;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.text = text;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
