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

        // "おまかせ" (owner 2026-10-25): a name for the look and a background from the class's origins and a temperament
        static readonly string[] MaleNames = { "ガレス", "ロッコ", "アルド", "ベルン", "カスパル", "ディルク", "エドガー", "フーゴ", "ローラン", "マティス", "オスカー", "セドリック" };
        static readonly string[] FemaleNames = { "エリス", "ミーナ", "アニカ", "ブリギット", "クララ", "ダリア", "フレヤ", "イルザ", "リゼット", "マルタ", "ノーラ", "シルヴィア" };
        static readonly Dictionary<string, string[]> Origins = new Dictionary<string, string[]>
        {
            ["warrior"] = new[] { "没落した騎士の家の末っ子", "辺境の砦で育った元傭兵", "鍛冶屋の家に生まれ、自分の打った剣を振るう", "闘技場で名を上げた元剣闘士", "村の自警団をまとめていた", "戦で主君を失った従士" },
            ["mage"] = new[] { "王都の学院を追い出された見習い魔術師", "森の賢者に拾われて育った", "古書店の奥で禁書を読みふけっていた", "星を読む一族の生まれ", "師の遺した杖を受け継いだ", "火の魔法で村を焼きかけた過去がある" },
            ["thief"] = new[] { "港町の裏路地で育った", "盗賊ギルドを抜けた元鍵師", "旅の一座で軽業を覚えた", "貴族の屋敷に忍び込むのが趣味だった", "兄の借金を返すために腕を磨いた", "宝の地図を集めている" },
            ["priest"] = new[] { "太陽神の神殿で育った孤児", "疫病の村で人を看取り続けた", "巡礼の途中で酒場に流れ着いた", "破門された元司祭", "癒やしの手を持つ村娘として知られていた", "亡き師の墓守をしていた" },
        };
        static readonly string[] Tempers = { "口は悪いが面倒見がいい", "無口で、甘いものに目がない", "陽気で、歌うのが好き", "臆病だが、仲間のためなら踏みとどまる", "几帳面で、宿代の計算に厳しい", "お人好しで、頼まれると断れない", "皮肉屋だが、約束は必ず守る", "好奇心が強く、古い遺跡に目がない" };
        static readonly string[] Wishes = { "家名を取り戻したい", "名を上げて故郷に錦を飾りたい", "行方知れずの家族を捜している", "いつか自分の店を持ちたい", "過去の過ちを償いたい", "まだ見ぬ竜をこの目で見たい" };

        static string Pick(string[] from) => from[UnityEngine.Random.Range(0, from.Length)];

        static (string name, string background) Suggest(string classId, bool female) =>
            (Pick(female ? FemaleNames : MaleNames), $"{Pick(Origins[classId])}。{Pick(Tempers)}。{Pick(Wishes)}。");

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
            UiKit.Button(panel, "おまかせで書く", () =>
            {
                var (n2, b2) = Suggest(classId, female);
                name.text = n2; background.text = b2;
            }, 480, 506, 220, 48, 18);
            UiKit.Label(panel, "名前と生い立ちを、職業と見た目に合わせて適当に埋めます（書き換えてかまいません）", 15, Theme.Dim, 714, 512, 330, 44);
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
