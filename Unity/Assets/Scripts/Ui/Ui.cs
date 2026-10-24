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

        static Font? _display, _number;
        /// <summary>Titles, names, act headings: Zen Antique (SIL OFL), an old-style Mincho.</summary>
        public static Font Display => _display ??= Resources.Load<Font>("Fonts/ZenAntique-Regular") ?? Serif;
        /// <summary>Figures (HP, rolls, targets): Cinzel (SIL OFL), Roman inscription capitals.</summary>
        public static Font Number => _number ??= Resources.Load<Font>("Fonts/Cinzel") ?? Serif;

        // the gold of the frames (Kenney's white borders tinted), and the inks on parchment
        public static readonly Color FrameGold = Hex("d2ad5e");
        public static readonly Color FrameDim = Hex("8c6d38");
        public static readonly Color InkDark = Hex("2c1c0e");
        public static readonly Color InkBrown = Hex("4a3220");
        public static readonly Color InkRed = Hex("7e1910");
        public static readonly Color InkGreen = Hex("2e5f2a");

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
            if (frame) Frame(rt, thin: true, Theme.FrameDim, 6);
            return img;
        }

        /// <summary>
        /// A framed panel in the tavern style: dark leather in a gold double frame. `leather: false` leaves the plain dark ground,
        /// for a panel whose ground is a picture or a page (Paper).
        /// </summary>
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h, bool leather = true)
        {
            var img = Box(parent, name, Theme.Panel);
            img.rectTransform.Place(x, y, w, h);
            if (leather)
            {
                var ground = Ground(img.transform, "leather");
                ground.sprite = Part("leather", 0.09f, 0.53f, 0.47f, 0.91f) ?? ground.sprite;   // one quarter, without the stitched seams
                ground.color = new Color(0.6f, 0.55f, 0.5f);
            }
            Frame(img.transform, thin: false);
            return img;
        }

        /// <summary>A parchment page inside a framed panel (the storyteller's book); write on it in the ink colours (Theme.InkDark…).</summary>
        public static RectTransform Paper(Transform panel, float inset = 12)
        {
            var page = Rect("Page", panel).Fill(inset);
            page.SetAsFirstSibling();   // under the panel's frame and content
            Ground(page, "parchment", 90);
            Soft(page, new Color(0.95f, 0.89f, 0.75f, 0.55f), 40);
            return page;
        }

        /// <summary>A modal layer over everything: a dark veil that swallows clicks, and a panel (leather, or parchment with `paper`).</summary>
        public static (RectTransform layer, Transform panel) Modal(Transform root, string name, float x, float y, float w, float h, bool paper = false, float veil = 0.75f)
        {
            var layer = Rect(name, root).Fill();
            Box(layer, "Veil", new Color(0, 0, 0, veil)).rectTransform.Fill();
            var panel = Panel(layer, "Panel", x, y, w, h, leather: !paper).transform;
            if (paper) Paper(panel);
            return (layer, panel);
        }

        /// <summary>A heading: an optional icon, the display face, and a gold rule fading out at both ends under it.</summary>
        public static Text Heading(Transform parent, string text, float x, float y, float w, string? icon = null, bool paper = false, int size = 32)
        {
            var color = paper ? Theme.InkRed : Theme.FrameGold;
            var tx = x;
            if (icon != null) { Icon(parent, icon, x, y + 2, size + 6, color); tx += size + 18; }
            var t = Title(parent, text, size, color, tx, y, w - (tx - x), size + 14);
            if (paper) t.Inked();
            else t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            Rule(parent, x, y + size + 20, w, paper ? Theme.WithAlpha(Theme.InkBrown, 0.75f) : Theme.FrameDim);
            return t;
        }

        static Sprite? _rule;
        /// <summary>A thin ornamental line with a small diamond at its middle; its ends fade out.</summary>
        public static Image Rule(Transform parent, float x, float y, float w, Color color)
        {
            if (_rule == null)
            {
                var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 128; i++) tex.SetPixel(i, 0, new Color(1, 1, 1, Mathf.Clamp01(Mathf.Min(i, 127 - i) / 40f)));
                tex.Apply();
                _rule = Sprite.Create(tex, new Rect(0, 0, 128, 1), new Vector2(0.5f, 0.5f));
            }
            var img = Rect("Rule", parent).Place(x, y, w, 2).gameObject.AddComponent<Image>();
            img.sprite = _rule;
            img.color = color;
            img.raycastTarget = false;
            var gem = Box(parent, "Gem", color);
            gem.rectTransform.Place(x + w / 2 - 4, y - 3, 8, 8);
            gem.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            gem.rectTransform.anchoredPosition += new Vector2(4, -4);
            gem.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            gem.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Rich text written for dark panels, made readable on parchment: the light colours in its tags become inks.
        /// </summary>
        public static string OnPaper(string text) => text
            .Replace("#c9a24a", "#7e1910").Replace("#e8d9a8", "#2c1c0e").Replace("#a8977a", "#4a3220")
            .Replace("#a08080", "#5e3a34").Replace("#c9a0c0", "#5a2a5e").Replace("#c4553f", "#8e2a1c");

        /// <summary>A sprite of a part of a Resources/UI texture (fractions of its size, from the bottom-left).</summary>
        public static Sprite? Part(string name, float x0, float y0, float x1, float y1)
        {
            var key = $"{name}@{x0},{y0},{x1},{y1}";
            if (Sprites.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("UI/" + name);
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(tex.width * x0, tex.height * y0, tex.width * (x1 - x0), tex.height * (y1 - y0)), new Vector2(0.5f, 0.5f));
            Sprites[key] = s;
            return s;
        }

        // ---------------------------------------------------------------- interface pieces (Resources/UI)

        static readonly System.Collections.Generic.Dictionary<string, Sprite> Sprites = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>A sprite from Resources/UI; `border` (texture pixels) makes it a 9-slice.</summary>
        public static Sprite? UiSprite(string name, float border = 0)
        {
            var key = name + "#" + border;
            if (Sprites.TryGetValue(key, out var s)) return s;
            var tex = Resources.Load<Texture2D>("UI/" + name);
            if (tex == null) return null;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            Sprites[key] = s;
            return s;
        }

        /// <summary>
        /// A gold frame over the whole parent (Kenney's Fantasy UI Borders, CC0): the double one for panels, the single one
        /// for cards and buttons. `px` is how thick it is drawn. Clicks pass through.
        /// </summary>
        public static Image Frame(Transform parent, bool thin, Color? color = null, float px = 0)
        {
            var sprite = thin ? UiSprite("frame_single", 16) : UiSprite("frame_double", 32);
            var img = Rect("Frame", parent).Fill().gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = color ?? Theme.FrameGold;
            if (sprite == null) { img.color = Color.clear; return img; }
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            var drawn = px > 0 ? px : thin ? 9 : 16;
            img.pixelsPerUnitMultiplier = sprite.border.x / drawn;
            return img;
        }

        /// <summary>A white icon from Resources/UI (game-icons.net, CC BY 3.0), tinted.</summary>
        public static Image Icon(Transform parent, string name, float x, float y, float size, Color? color = null)
        {
            var img = Rect("Icon", parent).Place(x, y, size, size).gameObject.AddComponent<Image>();
            img.sprite = UiSprite(name);
            img.preserveAspect = true;
            img.color = color ?? Theme.FrameGold;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A heading in the display face.</summary>
        public static Text Title(Transform parent, string text, int size, Color color, float x, float y, float w, float h, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = Label(parent, text, size, color, x, y, w, h, serif: true, align: align);
            t.font = Theme.Display;
            return t;
        }

        /// <summary>A painted ground from Resources/UI (parchment, leather) over the whole parent, behind its other children.</summary>
        public static Image Ground(Transform parent, string name, float border = 0)
        {
            var img = Rect("Ground", parent).Fill().gameObject.AddComponent<Image>();
            img.sprite = UiSprite(name, border);
            img.type = border > 0 ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();
            return img;
        }


        static Sprite? _soft;
        /// <summary>A pale patch whose edges fade out (no hard rectangle), e.g. to calm a texture under small text.</summary>
        public static Image Soft(Transform parent, Color color, float inset = 0)
        {
            if (_soft == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var edge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y)) / 24f;
                        tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge))));
                    }
                tex.Apply();
                _soft = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(26, 26, 26, 26));
            }
            var img = Rect("Soft", parent).Fill(inset).gameObject.AddComponent<Image>();
            img.sprite = _soft;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 0.4f;   // the fade spans about 65 px on screen
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Dark text on a light page: the project blends in linear space, which thins dark glyphs on light grounds;
        /// a faint same-coloured outline gives the strokes their weight back.
        /// </summary>
        public static Text Inked(this Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(t.color.r, t.color.g, t.color.b, 0.35f);
            o.effectDistance = new Vector2(0.5f, 0.5f);
            return t;
        }
        /// <summary>Wraps a long Japanese text by hand (Kinsoku), so Unity never breaks it at a half-width space mid-sentence.</summary>
        public static Text Wrapped(this Text t, float width)
        {
            t.text = Kinsoku(t.text, t.font, t.fontSize, t.fontStyle, width);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        static Sprite? _fade;
        /// <summary>A dark fade rising from the bottom (names and bars over a portrait stay readable).</summary>
        public static Image Fade(Transform parent, float strength = 0.95f)
        {
            if (_fade == null)
            {
                var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 64; i++)
                {
                    var t = i / 63f;   // 0 at the bottom
                    tex.SetPixel(0, i, new Color(0.03f, 0.02f, 0.01f, t < 0.4f ? 1f : Mathf.Clamp01(1f - (t - 0.4f) / 0.35f)));
                }
                tex.Apply();
                _fade = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
            }
            var img = Rect("Fade", parent).Fill().gameObject.AddComponent<Image>();
            img.sprite = _fade;
            img.color = new Color(1, 1, 1, strength);
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, bool serif = false, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Serif;   // Mincho everywhere (the fantasy look); `serif` is kept for callers that meant headings
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
            // a dark plate in a thin gold frame; the "gold" one (the main action) is crimson in a bright frame
            var img = Box(parent, "Button", Theme.Hex(gold ? "6e1f14" : "241a12"));
            Frame(img.transform, thin: true, gold ? Theme.FrameGold : Theme.FrameDim, gold ? 9 : 7);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.15f, 0.95f); colors.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            b.colors = colors;
            b.onClick.AddListener(() => Tavern.Unity.App.AudioDirector.Play("click", 0.6f));
            b.onClick.AddListener(onClick);
            var t = Label(img.transform, text, size, gold ? Theme.GoldLight : Theme.Ink, serif: true, align: TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
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

        /// <summary>A button with an icon before its label.</summary>
        public static Button IconButton(Transform parent, string icon, string text, UnityAction onClick, float x, float y, float w, float h, int size = 16, bool gold = false)
        {
            var b = Button(parent, text, onClick, x, y, w, h, size, gold);
            var t = b.GetComponentInChildren<Text>();
            var s = Mathf.Min(h - 14, size + 6);
            Icon(b.transform, icon, 10, (h - s) / 2, s, gold ? Theme.GoldLight : Theme.FrameGold);
            t.rectTransform.offsetMin = new Vector2(s + 14, t.rectTransform.offsetMin.y);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return b;
        }

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

        /// <summary>A gauge (HP, focus) in a thin gold frame. Returns the fill image; set fillAmount.</summary>
        public static Image Bar(Transform parent, float x, float y, float w, float h, Color fill)
        {
            var back = Box(parent, "Bar", Theme.Hex("0c0907"));
            back.rectTransform.Place(x, y, w, h);
            var f = Box(back.transform, "Fill", fill);
            f.rectTransform.Fill(Mathf.Max(1, h * 0.22f));
            f.type = Image.Type.Filled; f.fillMethod = Image.FillMethod.Horizontal;
            f.sprite = WhiteSprite;
            if (h >= 10) Frame(back.transform, thin: true, Theme.FrameDim, Mathf.Clamp(h * 0.3f, 3, 6));
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

    /// <summary>
    /// Redraws every Text after a dynamic font's atlas was rebuilt. Typing new kanji through the IME grows the atlas, and uGUI's
    /// own tracking left some labels drawing stale glyphs (garbled) until something else redrew them.
    /// </summary>
    public sealed class FontRefresh : MonoBehaviour
    {
        static readonly System.Collections.Generic.HashSet<Font> Rebuilt = new System.Collections.Generic.HashSet<Font>();

        public static void Create(Transform parent)
        {
            Font.textureRebuilt += f => Rebuilt.Add(f);
            new GameObject("FontRefresh", typeof(FontRefresh)).transform.SetParent(parent);
        }

        void LateUpdate()
        {
            if (Rebuilt.Count == 0) return;
            foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None))
                if (Rebuilt.Contains(t.font)) t.FontTextureChanged();
            Rebuilt.Clear();
        }
    }
}
