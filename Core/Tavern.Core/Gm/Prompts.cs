#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tavern.Core.Rules;

namespace Tavern.Core.Gm
{
    /// <summary>Builds the system prompt: shared GM rules (Docs/00_Decisions.md "GM の語り口") + persona + party + scenario.</summary>
    public static class Prompts
    {
        public const string Rules =
            "## あなたの役目\n" +
            "あなたは剣と魔法の世界の物語を語るゲームマスターです。最大 3 人の冒険者（プレイヤー）が行動を宣言し、あなたが結果を語ります。\n" +
            "- 語りはいつも短く、2〜4 文。場面の始まり・強敵の登場・結末だけは 6〜8 文まで。\n" +
            "- 冒険者の台詞・気持ち・行動を勝手に決めない。決めるのはプレイヤー。\n" +
            "- ふざけた行動もまず受け入れ、ちゃんと結果と代償を描く。断るのは本当に不可能なことだけ。\n" +
            "- 雰囲気は王道の冒険で、ときどき深い闇がある。流血や死ははっきり描いてよいが、生々しい描写は避ける。\n" +
            "- 結果が不確かな行動は、必ず request_check でゲームに判定を頼む。成功か失敗かを自分で決めない。判定の結果を受け取ってから、その結果どおりに語る。大成功は見事に、大失敗は痛快にまずく。\n" +
            "- 判定の数字やルールの説明は語りに書かない（数字はゲームの画面に出る）。\n" +
            "- 語り終えたら、最後に suggest_actions で次の行動案を 2〜3 個出す。\n" +
            "- 冒険者が何をすればよいか迷っていそうなら、NPC の台詞や気付きの形で手がかりを出す。\n" +
            "- 名前の付け方: 職業と普通の技は日本語（戦士・かばう）、大技と固有名詞はカタカナ（ファイアボール、灰色の森グレイウッド）。\n" +
            "- 必ず日本語で語る。見出し・箇条書き・記号の装飾は使わず、地の文と台詞だけで語る。\n";

        public static string System(Storyteller teller, IEnumerable<Character> party, string scenario)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Rules);
            sb.AppendLine("## あなたの人物像");
            sb.AppendLine(teller.Persona);
            sb.AppendLine();
            sb.AppendLine("## 冒険者たち");
            foreach (var c in party) sb.AppendLine(Describe(c));
            sb.AppendLine();
            sb.AppendLine("## 今夜の物語（あなただけが知っている設定。冒険者が見つけるまで明かさない）");
            sb.AppendLine(scenario.Trim());
            return sb.ToString();
        }

        public static string Describe(Character c)
        {
            var stats = string.Join(" ", new[] { Stat.Might, Stat.Skill, Stat.Wits, Stat.Spirit }.Select(s => $"{s.Label()}+{c.Modifier(s)}"));
            var bg = string.IsNullOrWhiteSpace(c.Background) ? "" : $" 生い立ち: {c.Background}";
            return $"- {c.Name}（{ClassLabel(c.ClassId)}）{stats} HP {c.Hp}/{c.MaxHp}{bg}";
        }

        public static string ClassLabel(string classId) => classId switch
        {
            "warrior" => "戦士", "mage" => "魔法使い", "thief" => "盗賊", "priest" => "僧侶", _ => classId,
        };
    }
}
