#nullable enable
using System;
using System.Linq;
using System.Text;
using Tavern.Core.Rules;
using Tavern.Unity.Ui;
using UnityEngine;

namespace Tavern.Unity.App
{
    public sealed partial class TableScreen
    {
        /// <summary>One adventurer at a glance: abilities, HP and focus, the fate coin, what they carry, their cards and story.</summary>
        void ShowSheet(Character p)
        {
            UiKit.Clear(_overlay.transform);
            var shade = UiKit.Box(_overlay.transform, "Shade", new Color(0, 0, 0, 0.6f));
            shade.rectTransform.Fill();
            var panel = UiKit.Panel(_overlay.transform, "Sheet", 360, 90, 1200, 900).transform;
            var cls = _app.Db.Classes[p.ClassId];

            var tex = ArtLibrary.Get(p.Portrait);
            var pic = UiKit.Picture(panel, tex, 30, 30, 240, 340);
            pic.uvRect = ArtLibrary.Cover(tex, 240, 340, 0.72f);
            UiKit.Label(panel, p.Name, 40, Theme.Gold, 300, 26, 600, 56, serif: true);
            UiKit.Label(panel, $"{cls.Name}　武器: {cls.Weapon}{(IsMine(p) ? "" : $"　<color=#a8977a>（{OwnerOf(p)}）</color>")}", 20, Theme.Ink, 300, 88, 860, 30);

            // abilities: the bonus added to the d20
            var stats = new[] { Stat.Might, Stat.Skill, Stat.Wits, Stat.Spirit };
            var hints = new[] { "腕っぷし", "器用さ・素早さ（かわす）", "知識・魔法", "意志・話術・信仰" };
            for (int i = 0; i < stats.Length; i++)
            {
                var box = UiKit.Box(panel, "Stat", Theme.Hex("271f18"), frame: true);
                box.rectTransform.Place(300 + i * 215, 130, 205, 92);
                UiKit.Label(box.transform, stats[i].Label(), 20, Theme.Dim, 12, 8, 60, 28, serif: true);
                var m = p.Modifier(stats[i]);
                UiKit.Label(box.transform, (m >= 0 ? "+" : "") + m, 36, Theme.GoldLight, 70, 0, 120, 50, serif: true, align: TextAnchor.UpperRight);
                UiKit.Label(box.transform, hints[i], 13, Theme.Dim, 12, 58, 185, 28);
            }

            var state = p.State == LifeState.Dead ? "死亡" : p.State == LifeState.Down ? $"倒れている（死のサイコロの失敗 {p.DeathSaveFailures} / 3）" : "動ける";
            var bonus = p.NextCheckBonus != 0 ? $"　次の判定 {(p.NextCheckBonus > 0 ? "+" : "")}{p.NextCheckBonus}" : "";
            UiKit.Label(panel, $"HP {p.Hp} / {p.MaxHp}　気力 {p.Focus} / {p.MaxFocus}　運命のコイン {p.FateCoins}　状態: {state}{bonus}", 20, Theme.Ink, 300, 240, 870, 30);

            UiKit.Label(panel, "持ち物", 22, Theme.Gold, 300, 290, 300, 32, serif: true);
            UiKit.Label(panel, p.Items.Count == 0 ? "（なし）" : string.Join("\n", p.Items.Select(x => "・" + x)), 18, Theme.Ink, 300, 326, 400, 200);

            UiKit.Label(panel, "技のカード", 22, Theme.Gold, 720, 290, 300, 32, serif: true);
            var cards = new StringBuilder();
            foreach (var id in p.Cards)
                if (_app.Db.Cards.TryGetValue(id, out var c)) cards.Append($"【{c.Name}】（気力 {c.Cost}）{c.Text}\n");
            var cardText = UiKit.Label(panel, "", 16, Theme.Ink, 720, 326, 450, 420);
            cardText.text = UiKit.Kinsoku(cards.ToString().TrimEnd(), Theme.Sans, 16, FontStyle.Normal, 445);   // Japanese line breaks, not at the spaces
            cardText.horizontalOverflow = HorizontalWrapMode.Overflow;
            cardText.lineSpacing = 1.1f;

            UiKit.Label(panel, "生い立ち・性格", 22, Theme.Gold, 30, 390, 240, 32, serif: true);
            UiKit.Label(panel, string.IsNullOrWhiteSpace(p.Background) ? "（書かれていない）" : p.Background, 16, Theme.Ink, 30, 426, 250, 380);

            UiKit.Button(panel, "閉じる", () => UiKit.Clear(_overlay.transform), 980, 830, 190, 50, 20, gold: true);
        }
    }
}
