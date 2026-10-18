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
            AudioDirector.Music("tavern");
            var bg = ArtLibrary.Get("bg_tavern");
            var pic = UiKit.Picture(root, bg, 0, 0, 1920, 1080);
            pic.uvRect = ArtLibrary.Cover(bg, 1920, 1080);
            var shade = UiKit.Box(root, "Shade", new Color(0.05f, 0.03f, 0.02f, 0.55f));
            shade.rectTransform.Fill();
            shade.raycastTarget = false;
            var vignette = UiKit.Box(root, "Vignette", new Color(0, 0, 0, 0.0f));
            vignette.rectTransform.Fill();

            var title = UiKit.Label(root, "酒場の語り部", 120, Theme.Gold, 0, 250, 1920, 170, serif: true, align: TextAnchor.MiddleCenter);
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);
            UiKit.Label(root, "炉端の語り部が、今宵の冒険を語りはじめる", 30, Theme.Ink, 0, 420, 1920, 50, serif: true, align: TextAnchor.MiddleCenter);

            float y = 520;
            if (WebPage.IsBrowser) { BuildBrowserMenu(root, y); return; }
            UiKit.Button(root, "友人と遊ぶ: 部屋を作る", () => _app.ShowParty(OnlineMode.Host), 560, y, 400, 70, 26, gold: true);
            UiKit.Button(root, "友人の部屋に入る", () => _app.ShowParty(OnlineMode.Join), 980, y, 400, 70, 26, gold: true);
            y += 92;
            UiKit.Button(root, "ひとりで新しい夜をはじめる", _app.ShowParty, 760, y, 400, 60, 22);
            y += 76;
            var cont = UiKit.Button(root, _app.SaveIsOnline ? "続きから（部屋を開き直す）" : "続きから（ひとり）", _app.ContinueEvening, 760, y, 400, 56, 22);
            cont.interactable = _app.HasSave;
            y += 72;
            UiKit.Button(root, "冒険者の名簿", _app.ShowRoster, 760, y, 195, 56, 20);
            UiKit.Button(root, "語り部の記録帳", () => ShowChronicle(root), 965, y, 195, 56, 20);
            y += 72;
            UiKit.Button(root, "遊び方", () => ShowHelp(root), 760, y, 195, 56, 22);
            UiKit.Button(root, "クレジット", () => ShowCredits(root), 965, y, 195, 56, 22);
            y += 72;
            UiKit.Button(root, "終わる", Application.Quit, 760, y, 400, 56, 22);

            UiKit.Label(root, _app.Ai.Description, 18, Theme.Dim, 0, 1030, 1900, 30, align: TextAnchor.MiddleRight);
            UiKit.Button(root, "設定", () =>
            {
                var layer = UiKit.Rect("Settings", root).Fill();
                SettingsPanel.Show(layer, null, () => Object.Destroy(layer.gameObject));
            }, 1720, 30, 160, 50, 20);
            if (AppMain.Flag("-showcredits")) ShowCredits(root);   // for screenshots
        }

        /// <summary>The browser build only joins a friend's room (the host plays the Windows version, which calls the AI).</summary>
        void BuildBrowserMenu(Transform root, float y)
        {
            UiKit.Button(root, "友人の部屋に入る", () => _app.ShowParty(OnlineMode.Join), 760, y, 400, 76, 28, gold: true);
            y += 98;
            UiKit.Button(root, "冒険者の名簿", _app.ShowRoster, 760, y, 195, 56, 20);
            UiKit.Button(root, "語り部の記録帳", () => ShowChronicle(root), 965, y, 195, 56, 20);
            y += 72;
            UiKit.Button(root, "遊び方", () => ShowHelp(root), 760, y, 195, 56, 22);
            UiKit.Button(root, "クレジット", () => ShowCredits(root), 965, y, 195, 56, 22);
            y += 90;
            UiKit.Box(root, "NoteBack", new Color(0.05f, 0.03f, 0.02f, 0.8f)).rectTransform.Place(360, y - 6, 1200, 52);
            UiKit.Label(root, "ブラウザ版は、友人が作った部屋に入るためのものです。部屋を作る（語り部の AI を動かす）のは Windows 版です。",
                20, Theme.Ink, 360, y, 1200, 40, align: TextAnchor.MiddleCenter);
            UiKit.Button(root, "設定", () =>
            {
                var layer = UiKit.Rect("Settings", root).Fill();
                SettingsPanel.Show(layer, null, () => Object.Destroy(layer.gameObject));
            }, 1720, 30, 160, 50, 20);
        }

        const string Help =
            "語り部オルウェンが、今宵の冒険を語ります。あなたたちは冒険者になって、何をするかを言葉で決めてください。\n\n" +
            "■ 1 つの手番\n" +
            "左の冒険者のカードを押して、その人の行動を決めます。技のカード、基本の行動、語り部が出す行動案、自由な文章のどれでも構いません。" +
            "全員の行動が決まったら「全員の行動を宣言する」。結果が不確かな行動はサイコロ（20 面）で判定し、語り部がその結果のとおりに語ります。\n\n" +
            "■ 判定\n" +
            "20 面ダイス + 能力値（力・技・知・心）が目標以上なら成功。20 は大成功、1 は大失敗。" +
            "失敗した判定は、運命のコイン（1 晩 1 枚）で一度だけ振り直せます。\n\n" +
            "■ 戦い\n" +
            "敵の絵を押すと狙う敵を選べます。敵の攻撃は「かわす」判定。ボスが大技の構えを見せたら、身を守るか、かばうか、先に倒すか。\n" +
            "HP が 0 になると倒れ、手番ごとに死のサイコロ（10 以上で持ちこたえ、3 回失敗で死亡）。仲間が起こせば助かります。全員倒れたら物語は失敗で終わります。\n\n" +
            "■ そのほか\n" +
            "幕の終わりに成長できます。「語り部への注文」で、語りを短く・怖くなど物語の外から頼めます。手番ごとに自動で保存され、「続きから」で再開できます。右上の「設定」（タイトル画面にもあります）で、音量・文字の大きさ・語りの速さを変えられます。";

        /// <summary>The storyteller's chronicle: every finished evening on this PC, and the memorial of the fallen.</summary>
        void ShowChronicle(Transform root)
        {
            var layer = UiKit.Rect("Chronicle", root).Fill();
            UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.8f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 200, 80, 1520, 920).transform;
            UiKit.Label(panel, "語り部の記録帳", 36, Theme.Gold, 40, 24, 600, 50, serif: true);
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 1300, 24, 180, 48, 20, gold: true);

            var chronicle = _app.LoadChronicle();
            var list = UiKit.Scroll(panel, 30, 90, 560, 800, out _, 8);
            var story = UiKit.Label(panel, chronicle.Entries.Count == 0 ? "まだ記録はありません。一晩の冒険を終えると、ここに物語が残ります。" : "左の夜を選ぶと、語り部の締めくくりが読めます。",
                21, Theme.Ink, 620, 100, 860, 640, serif: true);
            story.lineSpacing = 1.2f;
            foreach (var e in chronicle.Entries.AsEnumerable().Reverse())
            {
                var entry = e;
                var b = UiKit.Button(list, $"{e.Date:yyyy/MM/dd}　{e.Scenario}　<color=#c9a24a>{e.EndingLabel}</color>\n<size=15>{string.Join("・", e.Party)}　語り部 {e.Teller}</size>",
                    () => story.text = UiKit.Kinsoku($"{entry.Scenario}（{entry.EndingLabel}）\n\n{entry.Story}" +
                        (entry.Fallen.Count > 0 ? $"\n\n倒れた者: {string.Join("・", entry.Fallen)}" : ""), Theme.Serif, 21, FontStyle.Normal, 850), 18);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 76;
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
            story.horizontalOverflow = HorizontalWrapMode.Overflow;

            var roster = _app.LoadRoster();
            var fallen = roster.Fallen.Select(f => $"{f.Name}（{(_app.Db.Classes.TryGetValue(f.ClassId, out var c) ? c.Name : f.ClassId)}、{f.Nights} 晩）").ToList();
            var living = roster.Living.Select(f => $"{f.Name}{(f.Titles.Count > 0 ? "「" + f.Titles.Last() + "」" : "")}").ToList();
            UiKit.Label(panel, "生き延びた冒険者: " + (living.Count > 0 ? string.Join("・", living) : "まだいない"), 17, Theme.Dim, 620, 770, 860, 56);
            UiKit.Label(panel, "墓碑: " + (fallen.Count > 0 ? string.Join("・", fallen) : "まだいない"), 17, Theme.Dim, 620, 830, 860, 40);
            var wall = chronicle.Entries.Where(e => e.Ending != "failure").Select(e => $"「{e.Scenario}」の記念品").ToList();
            UiKit.Label(panel, "酒場の壁: " + (wall.Count > 0 ? string.Join("・", wall) : "まだ何も飾られていない"), 17, Theme.Dim, 620, 870, 860, 40);
        }

        static void ShowHelp(Transform root)
        {
            var layer = UiKit.Rect("Help", root).Fill();
            var shade = UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.75f));
            shade.rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 360, 120, 1200, 840).transform;
            UiKit.Label(panel, "遊び方", 36, Theme.Gold, 40, 26, 600, 50, serif: true);
            UiKit.Label(panel, Help, 21, Theme.Ink, 40, 96, 1120, 660).lineSpacing = 1.2f;
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 960, 770, 200, 50, 20, gold: true);
        }

        /// <summary>Who made the music and sounds (Resources/Music/credits.txt; Tabletop Audio requires it, the rest is thanks).</summary>
        static void ShowCredits(Transform root)
        {
            var layer = UiKit.Rect("Credits", root).Fill();
            UiKit.Box(layer, "Shade", new Color(0, 0, 0, 0.75f)).rectTransform.Fill();
            var panel = UiKit.Panel(layer, "Panel", 360, 120, 1200, 840).transform;
            UiKit.Label(panel, "クレジット", 36, Theme.Gold, 40, 26, 600, 50, serif: true);
            var text = Resources.Load<TextAsset>("Music/credits")?.text ?? "";
            var content = UiKit.Scroll(panel, 30, 96, 1140, 650, out _);
            var label = UiKit.Label(content, text, 20, Theme.Ink);
            label.lineSpacing = 1.15f;
            UiKit.Button(panel, "閉じる", () => Object.Destroy(layer.gameObject), 960, 770, 200, 50, 20, gold: true);
        }
    }
}
