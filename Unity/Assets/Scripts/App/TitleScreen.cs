#nullable enable
using System.Linq;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>The tavern at night: title, new evening, continue, quit.</summary>
    public sealed class TitleScreen
    {
        readonly AppMain _app;
        public TitleScreen(AppMain app) { _app = app; }

        public void Build(Transform root)
        {
            AudioDirector.Music("title");   // its own tune (owner 2026-10-25: Nostalgic Jig); the tavern keeps Harvest
            // the title's own picture (owner 2026-10-25: the party over the valley); the menu sits on the left, over the open sky and hills
            var bg = ArtLibrary.Get("bg_title_valley");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080, focusX: 0.6f);
            var shade = UiKit.Picture(root, LeftShade, 0, 0, 1920, 1080);
            shade.raycastTarget = false;

            const float X = 110, W = 640;
            var title = UiKit.Title(root, "酒場の語り部", 112, Theme.Hex("e2bf6e"), X - 10, 150, 1000, 160, align: TextAnchor.MiddleLeft);
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0.12f, 0.06f, 0.02f, 0.9f);
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -5);
            UiKit.Label(root, "炉端の語り部が、今宵の冒険を語りはじめる", 28, Theme.Ink, X, 318, 900, 46, serif: true)
                .gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            UiKit.Rule(root, X, 380, W, Theme.FrameGold);

            float y = 420;
            if (WebPage.IsBrowser) { BuildBrowserMenu(root, X, y); return; }
            // the menu sits on a dark leather plate in a gold frame, like a tavern's notice board
            var plate = UiKit.Box(root, "Menu", new Color(0.06f, 0.04f, 0.03f, 0.8f));
            plate.rectTransform.Place(X - 30, y - 30, W + 60, 496);
            UiKit.Frame(plate.transform, thin: false);
            var half = (W - 12) / 2;
            UiKit.Button(root, "友人と遊ぶ: 部屋を作る", () => _app.Night.ShowParty(Night.RoomMode.Host), X, y, half, 70, 23, gold: true);
            UiKit.Button(root, "友人の部屋に入る", () => _app.Night.ShowParty(Night.RoomMode.Join), X + half + 12, y, half, 70, 23, gold: true);
            y += 90;
            UiKit.Button(root, "ひとりで新しい夜をはじめる", () => _app.Night.ShowParty(), X, y, W, 60, 22);
            y += 74;
            var seats = _app.Night.Store.HasEvening ? _app.Night.Store.LoadSeats() : null;
            var cont = UiKit.Button(root, seats?.HasFriends == true ? "続きから（部屋を開き直す）" : "続きから", () => _app.Night.Continue(), X, y, W, 56, 22);
            cont.interactable = _app.Night.Store.HasEvening;
            y += 72;
            var third = (W - 20) / 3;
            UiKit.Button(root, "冒険者の名簿", () => Night.TavernBook.ShowRoster(_app.Night, root), X, y, third, 56, 19);
            UiKit.Button(root, "記録帳", () => Night.TavernBook.ShowChronicle(_app.Night, root), X + third + 10, y, third, 56, 19);
            UiKit.Button(root, "酒場の壁", () => Night.TavernBook.ShowWall(_app.Night, root), X + 2 * (third + 10), y, third, 56, 19);
            y += 72;
            UiKit.Button(root, "遊び方", () => ShowHelp(root), X, y, half, 56, 22);
            UiKit.Button(root, "クレジット", () => ShowCredits(root), X + half + 12, y, half, 56, 22);
            y += 72;
            UiKit.Button(root, "終わる", Application.Quit, X, y, W, 56, 22);

            var aiText = UiKit.Label(root, _app.Night.Ai.Description, 18, Theme.Dim, 0, 1030, 1900, 30, align: TextAnchor.MiddleRight);
            void OpenSettings()
            {
                var layer = UiKit.Rect("Settings", root).Fill();
                SettingsPanel.Show(layer, null, () => Object.Destroy(layer.gameObject), () => { _app.Night.ResetAi(); return aiText.text = _app.Night.Ai.Description; }, _app.Night.Ai.Description);
            }
            UiKit.IconButton(root, "ic_settings", "設定", OpenSettings, 1720, 30, 160, 50, 20);
            if (AppMain.Flag("-showsettings")) OpenSettings();
            if (AppMain.Flag("-showcredits")) ShowCredits(root);   // for screenshots
            if (AppMain.Flag("-showhelp")) ShowHelp(root);
            if (AppMain.Flag("-showroster")) Night.TavernBook.ShowRoster(_app.Night, root);
            if (AppMain.Flag("-showregister")) Night.AdventurerForm.Show(_app.Night, root, _ => { });
            if (AppMain.Flag("-showchronicle")) Night.TavernBook.ShowChronicle(_app.Night, root);
            if (AppMain.Flag("-showwall")) Night.TavernBook.ShowWall(_app.Night, root);
        }

        /// <summary>The browser build only joins a friend's room (the host plays the Windows version, which calls the AI).</summary>
        void BuildBrowserMenu(Transform root, float x, float y)
        {
            const float W = 640;
            var half = (W - 12) / 2;
            UiKit.Button(root, "友人の部屋に入る", () => _app.Night.ShowParty(Night.RoomMode.Join), x, y, W, 76, 28, gold: true);
            y += 98;
            UiKit.Button(root, "冒険者の名簿", () => Night.TavernBook.ShowRoster(_app.Night, root), x, y, half, 56, 20);
            UiKit.Button(root, "語り部の記録帳", () => Night.TavernBook.ShowChronicle(_app.Night, root), x + half + 12, y, half, 56, 20);
            y += 72;
            UiKit.Button(root, "遊び方", () => ShowHelp(root), x, y, half, 56, 22);
            UiKit.Button(root, "クレジット", () => ShowCredits(root), x + half + 12, y, half, 56, 22);
            y += 90;
            UiKit.Box(root, "NoteBack", new Color(0.05f, 0.03f, 0.02f, 0.8f)).rectTransform.Place(x - 10, y - 6, W + 20, 92);
            UiKit.Label(root, "ブラウザ版は、友人が作った部屋に入るためのものです。部屋を作る（語り部の AI を動かす）のは Windows 版です。",
                20, Theme.Ink, x, y, W, 80, align: TextAnchor.MiddleLeft).Wrapped(W);
            UiKit.IconButton(root, "ic_settings", "設定", () =>
            {
                var layer = UiKit.Rect("Settings", root).Fill();
                SettingsPanel.Show(layer, null, () => Object.Destroy(layer.gameObject));
            }, 1720, 30, 160, 50, 20);
        }

        static Texture2D? _leftShade;
        /// <summary>Dark on the left under the menu, clearing towards the right so the picture shows.</summary>
        static Texture2D LeftShade
        {
            get
            {
                if (_leftShade != null) return _leftShade;
                const int n = 128;
                _leftShade = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < n; i++)
                {
                    var t = i / (n - 1f);
                    _leftShade.SetPixel(i, 0, new Color(0.04f, 0.025f, 0.015f, Mathf.Lerp(0.78f, 0.12f, Mathf.SmoothStep(0.25f, 0.75f, t))));
                }
                _leftShade.Apply();
                return _leftShade;
            }
        }

        const string Help =
            "語り部オルウェンが、今宵の冒険を語ります。あなたたちは冒険者になって、何をするかを文章で宣言してください（ボタンの行動案はありません）。\n\n" +
            "■ 1 つの手\n" +
            "下の欄に、その冒険者がすることを自由に書きます（200 字まで）。技に名前を付けるなら【】で囲むと、キャラシートに残って次からその名前で使えます。台詞は「」。" +
            "全員分を書いたら「行動を宣言する」。語り部が行動を仕分け、結果が不確かなものだけサイコロ（20 面）を振ります。本当に無理なことは「できない」と理由とやり方が出て、手番を使わずに書き直せます。\n\n" +
            "■ 判定\n" +
            "20 面 + 能力値（STR 腕っぷし・DEX 手先・AGI 身のこなし・INT 知識と魔法・MND 意志と話術）が目標以上なら成功。20 は大成功、1 は大失敗。" +
            "運命のコイン（1 晩 1 枚）で振り直せます。歴戦の印は、1 晩に 1 回、判定を 1 段易しくします。大事な判定は失敗しても話が進み、代わりに代償が付きます。\n\n" +
            "■ 謎と時計\n" +
            "手がかりを集め、推理を口にして確信に至ると、物語が有利に動きます。上の時計は危険の進み具合。話が止まると、語り部がさりげなく手がかりの在りかをほのめかします。\n\n" +
            "■ 戦い\n" +
            "行動順に動きます。敵の攻撃は AGI で「かわす」。ボスが大技の構えを見せたら、かばう・守り・止めるで備えます。HP 0 で倒れ、死のサイコロで 3 回失敗すると本当に死にます。\n\n" +
            "■ そのほか\n" +
            "「内緒にする」で仲間に隠れて行動できます（結末のあとに種明かし）。幕の終わりに成長。「語り部への注文」で語りの長さや難易度を変えられます。毎手、自動で保存され「続きから」で再開できます。\n\n" +
            "一人で遊ぶときは、物語の人物を仲間に誘う（頼みごと）か、難易度「易しい」がおすすめです。";

        static void ShowHelp(Transform root)
        {
            var (layer, panel) = UiKit.Modal(root, "Help", 360, 100, 1200, 880, paper: true);
            UiKit.Heading(panel, "遊び方", 56, 44, 1088, "ic_d20", paper: true, size: 36);
            var content = UiKit.Scroll(panel, 52, 124, 1100, 650, out _);
            var text = UiKit.Label(content, Help, 21, Theme.InkDark).Inked().Wrapped(1080);
            text.lineSpacing = 1.2f;
            UiKit.Label(panel, "（マウスのホイールで続きを読めます）", 16, Theme.InkBrown, 56, 812, 600, 26).Inked();
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 950, 796, 200, 52, 20, gold: true);
        }

        /// <summary>Who made the music, sounds, art pieces and fonts (Resources/Music/credits.txt; CC BY ones require it, the rest is thanks).</summary>
        static void ShowCredits(Transform root)
        {
            var (layer, panel) = UiKit.Modal(root, "Credits", 360, 100, 1200, 880, paper: true);
            UiKit.Heading(panel, "クレジット", 56, 44, 1088, "ic_quill", paper: true, size: 36);
            var text = Resources.Load<TextAsset>("Music/credits")?.text ?? "";
            var content = UiKit.Scroll(panel, 52, 124, 1100, 650, out _);
            var label = UiKit.Label(content, text, 19, Theme.InkDark).Inked().Wrapped(1080);
            label.lineSpacing = 1.15f;
            UiKit.Label(panel, "（マウスのホイールで続きを読めます）", 16, Theme.InkBrown, 56, 812, 600, 26).Inked();
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 950, 796, 200, 52, 20, gold: true);
        }
    }
}
