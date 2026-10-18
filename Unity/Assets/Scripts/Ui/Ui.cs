#nullable enable
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tavern.Unity.Ui
{
    /// <summary>Colours and fonts of the oil-painting / old tavern look (Docs/mock/layout_combat.png).</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("120e0b");
        public static readonly Color Panel = Hex("1d1712");
        public static readonly Color Panel2 = Hex("271f18");
        public static readonly Color Edge = Hex("5a4630");
        public static readonly Color Gold = Hex("c9a24a");
        public static readonly Color GoldLight = Hex("f3e2b5");
        public static readonly Color Ink = Hex("e9dcc0");
        public static readonly Color Dim = Hex("a8977a");
        public static readonly Color Red = Hex("a8382b");
        public static readonly Color Blue = Hex("4a6ea8");
        public static readonly Color Green = Hex("7a9a3a");
        public static readonly Color Warn = Hex("c4553f");

        static Font? _serif, _sans;
        /// <summary>Narration and titles (Windows has Yu Mincho; the browser cannot use the PC's fonts, so it gets Noto, SIL OFL).</summary>
        public static Font Serif => _serif ??= App.WebPage.IsBrowser ? Resources.Load<Font>("Fonts/NotoSerifJP-Regular")
            : Font.CreateDynamicFontFromOSFont(new[] { "Yu Mincho", "YuMincho", "MS Mincho" }, 32);
        /// <summary>Everything else.</summary>
        public static Font Sans => _sans ??= App.WebPage.IsBrowser ? Resources.Load<Font>("Fonts/NotoSansJP-Regular")
            : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "MS Gothic" }, 24);

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var c); return c; }
        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }

    /// <summary>Small helpers to build uGUI from code (scenes and prefabs are never edited by hand).</summary>
    public static class UiKit
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors a rect by its pixel box in the 1920x1080 reference layout (x, y from the top-left).</summary>
        public static RectTransform Place(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Fill(this RectTransform rt, float inset = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Box(Transform parent, string name, Color color, bool frame = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (frame)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Theme.Edge; o.effectDistance = new Vector2(1, -1);
            }
            return img;
        }

        /// <summary>A framed panel in the tavern style.</summary>
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            var img = Box(parent, name, Theme.Panel, frame: true);
            img.rectTransform.Place(x, y, w, h);
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, bool serif = false, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = serif ? Theme.Serif : Theme.Sans;
            t.fontSize = size; t.color = color; t.text = text; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        public static Text Label(Transform parent, string text, int size, Color color, float x, float y, float w, float h,
            bool serif = false, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = Label(parent, text, size, color, serif, align);
            t.rectTransform.Place(x, y, w, h);
            return t;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, int size = 20, bool gold = false)
        {
            var img = Box(parent, "Button", Theme.Hex(gold ? "3d2f1c" : "2e241a"), frame: true);
            if (gold) img.GetComponent<Outline>().effectColor = Theme.Gold;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.15f, 0.95f); colors.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            b.colors = colors;
            b.onClick.AddListener(() => Tavern.Unity.App.AudioDirector.Play("click", 0.6f));
            b.onClick.AddListener(onClick);
            var t = Label(img.transform, text, size, gold ? Theme.GoldLight : Theme.Ink, align: TextAnchor.MiddleCenter);
            t.rectTransform.Fill(6);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return b;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, float x, float y, float w, float h, int size = 20, bool gold = false)
        {
            var b = Button(parent, text, onClick, size, gold);
            ((RectTransform)b.transform).Place(x, y, w, h);
            return b;
        }

        public static void SetText(this Button b, string text) => b.GetComponentInChildren<Text>().text = text;

        public static InputField Input(Transform parent, string placeholder, float x, float y, float w, float h, int size = 20, bool multiline = false)
        {
            var bg = Box(parent, "Input", Theme.Hex("0e0b08"), frame: true);
            bg.rectTransform.Place(x, y, w, h);
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Label(bg.transform, "", size, Theme.Ink);
            text.rectTransform.Fill(8);
            text.supportRichText = false;
            var ph = Label(bg.transform, placeholder, size, Theme.WithAlpha(Theme.Dim, 0.8f));
            ph.rectTransform.Fill(8);
            ph.fontStyle = FontStyle.Italic;
            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.caretColor = Theme.Gold;
            field.selectionColor = Theme.WithAlpha(Theme.Gold, 0.35f);
            App.WebPage.UseHtmlInput(field);   // the browser types through an HTML field (Japanese IME)
            return field;
        }

        public static RawImage Picture(Transform parent, Texture? tex, float x, float y, float w, float h)
        {
            var rt = Rect("Picture", parent).Place(x, y, w, h);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A thin bar (HP, focus). Returns the fill image; set fillAmount.</summary>
        public static Image Bar(Transform parent, float x, float y, float w, float h, Color fill)
        {
            var back = Box(parent, "Bar", Theme.Hex("0c0907"));
            back.rectTransform.Place(x, y, w, h);
            var f = Box(back.transform, "Fill", fill);
            f.rectTransform.Fill(1);
            f.type = Image.Type.Filled; f.fillMethod = Image.FillMethod.Horizontal;
            f.sprite = WhiteSprite;
            return f;
        }

        static Sprite? _white;
        public static Sprite WhiteSprite => _white ??= Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

        /// <summary>A vertical scroll area; returns the content transform (children laid out top to bottom).</summary>
        public static RectTransform Scroll(Transform parent, float x, float y, float w, float h, out ScrollRect scroll, float spacing = 10)
        {
            var view = Rect("Scroll", parent).Place(x, y, w, h);
            view.gameObject.AddComponent<RectMask2D>();
            var img = view.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0);
            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
            var content = Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            layout.padding = new RectOffset(4, 12, 4, 4);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = view;
            return content;
        }

        const string NoLineStart = "。、，．・：；？！ー」』）】〉》〕ゝゞヽヾぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ…‥";
        const string NoLineEnd = "「『（【〈《〔";

        /// <summary>
        /// Wraps Japanese text by hand with simple kinsoku rules (no 。、」 at a line start, no 「 at a line end),
        /// for a Text that then uses HorizontalWrapMode.Overflow. Unity's own wrapping ignores these rules.
        /// </summary>
        public static string Kinsoku(string text, Font font, int size, FontStyle style, float width)
        {
            font.RequestCharactersInTexture(text, size, style);
            var sb = new System.Text.StringBuilder(text.Length + 16);
            foreach (var para in text.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                var line = new System.Text.StringBuilder();
                float w = 0;
                for (int i = 0; i < para.Length; i++)
                {
                    var ch = para[i];
                    var adv = font.GetCharacterInfo(ch, out var info, size, style) ? info.advance : size;
                    if (w + adv > width && line.Length > 0)
                    {
                        // Move the break back so the next line doesn't start with closing punctuation
                        // and this line doesn't end with an opening bracket.
                        int carry = 0;
                        if (NoLineStart.IndexOf(ch) >= 0) carry = 1;
                        while (carry < line.Length && NoLineEnd.IndexOf(line[line.Length - 1 - carry]) >= 0) carry++;
                        if (carry >= line.Length) carry = 0;
                        var moved = line.ToString(line.Length - carry, carry);
                        line.Length -= carry;
                        sb.Append(line).Append('\n');
                        line.Clear().Append(moved);
                        w = 0;
                        foreach (var m in moved) w += font.GetCharacterInfo(m, out var mi, size, style) ? mi.advance : size;
                    }
                    line.Append(ch);
                    w += adv;
                }
                sb.Append(line);
            }
            return sb.ToString();
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static void Hover(GameObject go, Action<bool> onHover)
        {
            var trigger = go.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => onHover(true));
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => onHover(false));
            trigger.triggers.Add(enter); trigger.triggers.Add(exit);
        }
    }
}
