#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Pictures by art id (Resources/Art/&lt;id&gt;). Missing art falls back to a dark painted-looking gradient,
    /// so the game runs before every picture exists.
    /// </summary>
    public static class ArtLibrary
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>Whether the picture exists (missing ones fall back to a placeholder in Get).</summary>
        public static bool Has(string? id) => !string.IsNullOrEmpty(id) && (Cache.TryGetValue(id!, out var c) ? c.width > 4 : Resources.Load<Texture2D>("Art/" + id) != null);

        public static Texture2D Get(string? id)
        {
            id ??= "";
            if (Cache.TryGetValue(id, out var t)) return t;
            t = id.Length > 0 ? Resources.Load<Texture2D>("Art/" + id) : null;
            if (t == null) t = Placeholder(id);
            Cache[id] = t;
            return t;
        }

        static Texture2D Placeholder(string id)
        {
            var hue = (Mathf.Abs(id.GetHashCode()) % 360) / 360f;
            var top = Color.HSVToRGB(hue, 0.35f, 0.28f);
            var bottom = Color.HSVToRGB(hue, 0.4f, 0.08f);
            var tex = new Texture2D(4, 64) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();
            return tex;
        }

        /// <summary>UV rect that crops a texture to fill a box of the given aspect (like CSS object-fit: cover).</summary>
        public static Rect Cover(Texture tex, float boxW, float boxH, float focusY = 0.5f)
        {
            float texAspect = (float)tex.width / tex.height, boxAspect = boxW / boxH;
            if (texAspect > boxAspect)
            {
                var w = boxAspect / texAspect;
                return new Rect((1 - w) / 2, 0, w, 1);
            }
            var h = texAspect / boxAspect;
            return new Rect(0, Mathf.Clamp(focusY - h / 2, 0, 1 - h), 1, h);
        }
    }
}
