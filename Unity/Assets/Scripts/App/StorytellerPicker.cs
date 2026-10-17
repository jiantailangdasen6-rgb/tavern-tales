#nullable enable
using System;
using System.Collections.Generic;
using Tavern.Core.Gm;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.App
{
    /// <summary>Four portraits to choose tonight's storyteller, with a one-line description of the chosen one.</summary>
    public static class StorytellerPicker
    {
        public static Func<Storyteller> Build(Transform parent, float x, float y)
        {
            var chosen = Storyteller.Olwen;
            var frames = new List<(Storyteller t, Outline o)>();
            UiKit.Label(parent, "今宵の語り部", 18, Theme.Dim, x, y + 4, 140, 28);
            var blurb = UiKit.Label(parent, "", 16, Theme.Ink, x, y + 72, 760, 26);
            for (int i = 0; i < Storyteller.All.Count; i++)
            {
                var t = Storyteller.All[i];
                var box = UiKit.Box(parent, "Teller", Theme.Panel2, frame: true);
                box.rectTransform.Place(x + 140 + i * 156, y, 148, 64);
                var tex = ArtLibrary.Get(t.Portrait);
                var pic = UiKit.Picture(box.transform, tex, 4, 4, 42, 56);
                pic.uvRect = ArtLibrary.Cover(tex, 42, 56, 0.75f);
                UiKit.Label(box.transform, t.Name, 18, Theme.GoldLight, 52, 18, 96, 30, serif: true);
                var o = box.GetComponent<Outline>();
                frames.Add((t, o));
                box.gameObject.AddComponent<Button>().onClick.AddListener(() => { chosen = t; Refresh(); });
            }
            void Refresh()
            {
                foreach (var (t, o) in frames)
                {
                    o.effectColor = t == chosen ? Theme.Gold : Theme.Edge;
                    o.effectDistance = t == chosen ? new Vector2(3, -3) : new Vector2(1, -1);
                }
                blurb.text = $"{chosen.Name}: {chosen.Blurb}";
            }
            var pick = AppMain.Arg("-teller");
            if (pick != null) chosen = Storyteller.Find(pick);
            Refresh();
            return () => chosen;
        }
    }
}
