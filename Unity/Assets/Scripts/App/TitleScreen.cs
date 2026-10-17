#nullable enable
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
            UiKit.Button(root, "友人と遊ぶ: 部屋を作る", () => _app.ShowParty(OnlineMode.Host), 560, y, 400, 70, 26, gold: true);
            UiKit.Button(root, "友人の部屋に入る", () => _app.ShowParty(OnlineMode.Join), 980, y, 400, 70, 26, gold: true);
            y += 92;
            UiKit.Button(root, "ひとりで新しい夜をはじめる", _app.ShowParty, 760, y, 400, 60, 22);
            y += 76;
            var cont = UiKit.Button(root, _app.SaveIsOnline ? "続きから（部屋を開き直す）" : "続きから（ひとり）", _app.ContinueEvening, 760, y, 400, 56, 22);
            cont.interactable = _app.HasSave;
            y += 72;
            UiKit.Button(root, "遊び方", () => ShowHelp(root), 760, y, 400, 56, 22);
            y += 72;
            UiKit.Button(root, "終わる", Application.Quit, 760, y, 400, 56, 22);

            UiKit.Label(root, _app.Ai.Description, 18, Theme.Dim, 0, 1030, 1900, 30, align: TextAnchor.MiddleRight);
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
            "幕の終わりに成長できます。「語り部への注文」で、語りを短く・怖くなど物語の外から頼めます。手番ごとに自動で保存され、「続きから」で再開できます。";

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
    }
}
