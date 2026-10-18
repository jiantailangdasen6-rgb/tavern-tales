// Browser helpers for the WebGL build (see WebPage.cs).
// Text input: Unity's own input field cannot take Japanese from an IME in the browser, so while a field is being edited
// an HTML <textarea>/<input> sits exactly over it and the game reads its text back every frame.
mergeInto(LibraryManager.library, {

  TavernWeb_Open: function (textPtr, x, y, w, h, fontPx, multiline, maxLength) {
    var text = UTF8ToString(textPtr);
    var w0 = window.tavernInput;
    if (w0 && w0.el) { w0.el.remove(); }
    var canvas = Module.canvas;
    var r = canvas.getBoundingClientRect();
    var sx = r.width / canvas.width, sy = r.height / canvas.height;
    var el = document.createElement(multiline ? "textarea" : "input");
    if (!multiline) el.type = "text";
    el.value = text;
    if (maxLength > 0) el.maxLength = maxLength;
    var s = el.style;
    s.position = "fixed";
    s.left = (r.left + x * sx) + "px";
    s.top = (r.top + (canvas.height - y - h) * sy) + "px";
    s.width = (w * sx) + "px";
    s.height = (h * sy) + "px";
    s.boxSizing = "border-box";
    s.margin = "0";
    s.padding = (8 * sy) + "px";
    s.fontSize = (fontPx * sy) + "px";
    s.lineHeight = "1.25";
    s.fontFamily = '"Yu Gothic UI", "Yu Gothic", "Hiragino Sans", "Meiryo", sans-serif';
    s.color = "#e9dcc0";
    s.background = "#0e0b08";
    s.border = "1px solid #c9a24a";
    s.outline = "none";
    s.resize = "none";
    s.zIndex = "10";
    var state = { el: el, version: 1, open: true, submitted: false };
    window.tavernInput = state;
    el.addEventListener("input", function () { state.version++; });
    el.addEventListener("blur", function () { state.open = false; });
    el.addEventListener("keydown", function (e) {
      e.stopPropagation();
      if (e.isComposing) return;
      if (e.key === "Escape" || (!multiline && e.key === "Enter")) {
        if (e.key === "Enter") state.submitted = true;
        e.preventDefault();
        el.blur();
      }
    });
    el.addEventListener("keyup", function (e) { e.stopPropagation(); });
    el.addEventListener("keypress", function (e) { e.stopPropagation(); });
    document.body.appendChild(el);
    setTimeout(function () { el.focus(); var n = el.value.length; try { el.setSelectionRange(n, n); } catch (e) {} }, 0);
  },

  // The number of edits so far (the game reads the text only when it changed); 0 when nothing is open.
  TavernWeb_Version: function () {
    var st = window.tavernInput;
    return st && st.el ? st.version : 0;
  },

  TavernWeb_Text: function () {
    var st = window.tavernInput;
    var s = st && st.el ? st.el.value : "";
    var size = lengthBytesUTF8(s) + 1;
    var buf = _malloc(size);
    stringToUTF8(s, buf, size);
    return buf;
  },

  // 1 while the field is being edited, 0 once it lost focus, 2 if that was Enter in a one-line field.
  TavernWeb_State: function () {
    var st = window.tavernInput;
    if (!st || !st.el) return 0;
    return st.open ? 1 : (st.submitted ? 2 : 0);
  },

  TavernWeb_Close: function () {
    var st = window.tavernInput;
    if (st && st.el) { st.el.remove(); st.el = null; }
    window.tavernInput = null;
  },

  // Writes under Application.persistentDataPath live in memory until they are copied to IndexedDB.
  TavernWeb_SyncFs: function () {
    if (typeof FS !== "undefined" && FS.syncfs) FS.syncfs(false, function (err) { if (err) console.warn("[TavernWeb] syncfs", err); });
  },
});
