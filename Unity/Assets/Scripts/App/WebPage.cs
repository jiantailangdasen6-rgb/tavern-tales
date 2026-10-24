#nullable enable
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>
    /// The browser build (WebGL) is for friends joining a room: no AI, no hosting, no local files beyond the browser's own
    /// storage. Its page is https://…/tavern-tales/play/#CODE (Tools/DeployPages.ps1), so an invite link can open it directly.
    /// </summary>
    public static class WebPage
    {
        public static bool IsBrowser => Application.platform == RuntimePlatform.WebGLPlayer;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TavernWeb_Open(string text, float x, float y, float w, float h, float fontPx, bool multiline, int maxLength);
        [DllImport("__Internal")] static extern int TavernWeb_Version();
        [DllImport("__Internal")] static extern string TavernWeb_Text();
        [DllImport("__Internal")] static extern int TavernWeb_State();
        [DllImport("__Internal")] static extern void TavernWeb_Close();
        [DllImport("__Internal")] static extern void TavernWeb_SyncFs();
#else
        static void TavernWeb_Open(string text, float x, float y, float w, float h, float fontPx, bool multiline, int maxLength) { }
        static int TavernWeb_Version() => 0;
        static string TavernWeb_Text() => "";
        static int TavernWeb_State() => 0;
        static void TavernWeb_Close() { }
        static void TavernWeb_SyncFs() { }
#endif

        /// <summary>The room code in the page address (…/play/#CODE or ?join=CODE), else null.</summary>
        public static string? CodeFromUrl()
        {
            if (!IsBrowser) return null;
            var m = Regex.Match(Application.absoluteURL ?? "", @"(?:#|[?&]join=)([A-Za-z0-9]{4,12})\b");
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>The page was opened with ?mute (automated checks in a browser): no sound at all.</summary>
        public static bool MutedByUrl => IsBrowser && Regex.IsMatch(Application.absoluteURL ?? "", @"[?&]mute\b");

        /// <summary>Copies files written under persistentDataPath to the browser's storage, so they survive a reload.</summary>
        public static void Flush() { if (IsBrowser) TavernWeb_SyncFs(); }

        /// <summary>Called once at start: let keys reach HTML fields (the IME input) instead of all going to the game.</summary>
        public static void Setup()
        {
            if (!IsBrowser) return;
            // WebGLInput lives in the WebGL module, which the script compile does not reference here; it is kept by link.xml.
            var input = System.Type.GetType("UnityEngine.WebGLInput, UnityEngine.WebGLModule");
            var capture = input?.GetProperty("captureAllKeyboardInput");
            if (capture != null) capture.SetValue(null, false);
            else Debug.LogWarning("[WebPage] WebGLInput.captureAllKeyboardInput not found");
        }

        /// <summary>Makes a field edit through an HTML input laid over it (browser only; elsewhere nothing changes).</summary>
        public static void UseHtmlInput(InputField field)
        {
            if (!IsBrowser) return;
            field.readOnly = true;   // Unity's field only shows the text; typing happens in the HTML element
            field.gameObject.AddComponent<HtmlInput>().Field = field;
        }

        /// <summary>Opens the HTML editor over its field on click and mirrors the text back while it is open.</summary>
        sealed class HtmlInput : MonoBehaviour, IPointerClickHandler
        {
            public InputField Field = null!;
            static HtmlInput? _open;
            int _version;

            public void OnPointerClick(PointerEventData e)
            {
                if (!Field.interactable || !Field.IsActive()) return;
                if (_open != null && _open != this) _open.Finish();
                var rt = (RectTransform)Field.transform;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);   // screen pixels on the overlay canvas, from the bottom left
                var canvas = Field.GetComponentInParent<Canvas>().rootCanvas;
                var fontPx = Field.textComponent.fontSize * canvas.scaleFactor;
                TavernWeb_Open(Field.text, corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y, fontPx,
                    Field.lineType != InputField.LineType.SingleLine, Field.characterLimit);
                _open = this;
                _version = 0;
                // Unity's own field must not stay in edit mode under the HTML one: it would not redraw the text sent back to it.
                Field.DeactivateInputField();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }

            void Update()
            {
                if (_open != this) return;
                var v = TavernWeb_Version();
                if (v != _version) { _version = v; Field.text = TavernWeb_Text(); }
                var state = TavernWeb_State();
                if (state == 1) return;
                Finish();
                if (state == 2) Field.onEndEdit.Invoke(Field.text);
            }

            void Finish()
            {
                if (_open != this) return;
                if (TavernWeb_Version() != _version) Field.text = TavernWeb_Text();
                TavernWeb_Close();
                _open = null;
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) EventSystem.current.SetSelectedGameObject(null);
            }

            void OnDisable() => Finish();
        }
    }
}
