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

            float y = 560;
            UiKit.Button(root, "新しい夜をはじめる", _app.ShowParty, 760, y, 400, 70, 28, gold: true);
            y += 90;
            var cont = UiKit.Button(root, "続きから", _app.ContinueEvening, 760, y, 400, 64, 24);
            cont.interactable = _app.HasSave;
            y += 84;
            UiKit.Button(root, "終わる", Application.Quit, 760, y, 400, 56, 22);

            UiKit.Label(root, _app.Ai.Description, 18, Theme.Dim, 0, 1030, 1900, 30, align: TextAnchor.MiddleRight);
        }
    }
}
