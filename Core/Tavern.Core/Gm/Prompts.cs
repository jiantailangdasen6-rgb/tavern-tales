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
            "- 語りはいつも短く、2〜4 文・合わせて 120 字ほどまで。1 文は 40 字ほどで切る。場面の始まり・強敵の登場・結末だけは 6〜8 文まで。\n" +
            "- 冒険者の台詞・気持ち・行動を勝手に決めない。決めるのはプレイヤー。\n" +
            "- ふざけた行動もまず受け入れ、ちゃんと結果と代償を描く。断るのは本当に不可能なことだけ。\n" +
            "- 雰囲気は王道の冒険で、ときどき深い闇がある。流血や死ははっきり描いてよいが、生々しい描写は避ける。\n" +
            "- 1 つの手は 2 段で進む。まず plan_checks で、宣言した全員の行動を 1 人ずつ並べ、失敗に面白い結果や危険がありうるものすべてに判定を付ける。" +
            "ゲームがサイコロを振って結果を返すので、次に narrate で、その結果のとおりに全員の行動の結末をまとめて語り、次の行動案を 3 つ出す。\n" +
            "- 成功か失敗かを自分で決めない。判定しなかった行動は、無理のない範囲でそのまま起きたこととして語る。大成功は見事に、大失敗は痛快にまずく。\n" +
            "- 語りにゲームの言葉と数字を書かない。HP・気力・ダメージの数・判定・目標・大成功・手番などは使わず、物語の言葉で描く" +
            "（悪い例「骨の騎士の HP はあとわずか」「6 ダメージを与えた」、良い例「骨の騎士の鎧はひび割れ、膝をつきかけている」「刃が深く食い込んだ」）。" +
            "数字はゲームの画面に出る。語りの最後を毎回同じ問いかけで締めない。\n" +
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
