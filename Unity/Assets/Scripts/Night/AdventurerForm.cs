#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Tavern.Core.Play;
using Tavern.Unity.App;
using Tavern.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace Tavern.Unity.Night
{
    /// <summary>
    /// Registering an adventurer in the roster ahead of any evening (owner 2026-10-25: made from the title, then picked for a
    /// night): class, look, name and background. The class text is shown, not a list of specialties.
    /// </summary>
    public static class AdventurerForm
    {
        static readonly string[] ClassIds = { "warrior", "mage", "thief", "priest" };

        /// <summary>Opens the form over root; saved gets the new roster entry (already stored), cancelled the "やめる".</summary>
        public static void Show(NightApp n, Transform root, Action<RosterEntry> saved, Action? cancelled = null)
        {
            var (layer, panel) = UiKit.Modal(root, "NewAdventurer", 420, 70, 1080, 940);
            UiKit.Heading(panel, "冒険者を登録する", 34, 22, 1012, "ic_warrior", size: 32);
            var classId = "warrior";
            var female = false;
            Text classText = null!;

            var card = UiKit.Box(panel, "Card", Theme.Hex("0c0907"));
            card.rectTransform.Place(40, 96, 240, 360);
            var portrait = UiKit.Picture(card.transform, null, 3, 3, 234, 354);
            UiKit.Frame(card.transform, thin: true, Theme.FrameGold, 7);
            var className = UiKit.Title(panel, "", 34, Theme.FrameGold, 312, 90, 400, 46);
            var stats = UiKit.Label(panel, "", 17, Theme.Ink, 312, 140, 720, 54);
            var buttons = new Dictionary<string, Image>();
            for (int c = 0; c < ClassIds.Length; c++)
            {
                var id = ClassIds[c];
                buttons[id] = UiKit.Button(panel, n.Classes[id].Name, () => { classId = id; Refresh(); }, 312 + c * 134, 200, 126, 46, 19).GetComponent<Image>();
            }
            UiKit.Button(panel, "男 / 女", () => { female = !female; Refresh(); }, 312, 256, 260, 38, 16);
            classText = UiKit.Label(panel, "", 16, Theme.Dim, 312, 306, 728, 150);

            UiKit.Label(panel, "名前", 17, Theme.Dim, 40, 478, 300, 26);
            var name = UiKit.Input(panel, "例: ガレス", 40, 506, 420, 48, 21);
            name.characterLimit = 12;
            UiKit.Label(panel, "生い立ち・性格（自由に。語り部が冒険の中で拾います）", 17, Theme.Dim, 40, 572, 900, 26);
            var background = UiKit.Input(panel, "例: 没落した騎士の家の三男。家名を取り戻したい", 40, 600, 1000, 140, 18, multiline: true);
            background.characterLimit = 300;
            var note = UiKit.Label(panel, "", 18, Theme.Warn, 40, 756, 1000, 30);

            UiKit.Button(panel, "やめる", () => { UnityEngine.Object.Destroy(layer.gameObject); cancelled?.Invoke(); }, 40, 840, 220, 62, 22);
            UiKit.Button(panel, "名簿に登録する", () =>
            {
                var given = name.text.Trim();
                if (given.Length == 0) { note.text = "名前を入れてください"; return; }
                var roster = n.Store.LoadRoster();
                if (roster.Any(r => !r.Dead && r.Name == given)) { note.text = $"「{given}」はもう名簿にいます。別の名前にしてください"; return; }
                var entry = new RosterEntry
                {
                    Id = Guid.NewGuid().ToString("N"), Name = given, ClassId = classId, Background = background.text.Trim(),
                    Portrait = $"pc_{classId}_{(female ? "f" : "m")}",
                };
                roster.Add(entry);
                n.Store.SaveRoster(roster);
                UnityEngine.Object.Destroy(layer.gameObject);
                saved(entry);
            }, 760, 840, 280, 62, 24, gold: true);
            Refresh();

            void Refresh()
            {
                var cls = n.Classes[classId];
                portrait.texture = ArtLibrary.Get($"pc_{classId}_{(female ? "f" : "m")}");
                if (portrait.texture != null) portrait.uvRect = ArtLibrary.Cover(portrait.texture, 234, 354, 0.7f);
                className.text = cls.Name;
                stats.text = string.Join("  ", new[] { Ability.Str, Ability.Dex, Ability.Agi, Ability.Int, Ability.Mnd }.Select(a => $"{a.Code()}+{cls.Stats[a]}")) + $"\nHP {cls.Hp}　気力 {cls.Focus}";
                classText.text = new Adventurer(cls, "probe").ClassText();
                foreach (var kv in buttons) kv.Value.color = kv.Key == classId ? Theme.Hex("6e1f14") : Theme.Hex("241a12");
            }
        }
    }
}
