#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Gm;
using Tavern.Core.Play;
using Tavern.Core.Story;

namespace Tavern.Core.Ai
{
    /// <summary>A declaration as the game hands it to the classifier.</summary>
    public sealed class Declaration
    {
        public string Who { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>内緒の行動.</summary>
        public bool Secret { get; set; }
        /// <summary>"表向き: …" written by the player (for a secret action).</summary>
        public string Cover { get; set; } = "";
    }

    /// <summary>One step of a declaration after classification (09_Spec 4).</summary>
    public sealed class Step
    {
        public string Text { get; set; } = "";
        public Sort Sort { get; set; } = Sort.Auto;
        /// <summary>For Sort.Check.</summary>
        public CheckSpec? Check { get; set; }
        public ImpossibleReason? Why { get; set; }
        /// <summary>The world rule named (1-8) when Why is WorldRule.</summary>
        public int WorldRule { get; set; }
        /// <summary>How it could be done (always for a situation).</summary>
        public string How { get; set; } = "";
        public string Question { get; set; } = "";
        public Likelihood Likelihood { get; set; } = Likelihood.Even;
        // targets, by id (unknown ids are dropped)
        public string Mechanism { get; set; } = "";
        public string Item { get; set; } = "";
        public string GiveTo { get; set; } = "";
        public string PlaceAt { get; set; } = "";
        public bool Take { get; set; }
        public string Person { get; set; } = "";
        public RequestSize? Request { get; set; }
        public Approach? Approach { get; set; }
        public string Clue { get; set; } = "";
        public string MoveTo { get; set; } = "";
        /// <summary>A fellow adventurer this step acts on or against (contests are found from this).</summary>
        public string Against { get; set; } = "";
        /// <summary>Helping another adventurer's check (+2).</summary>
        public string Helps { get; set; } = "";
        /// <summary>Who an effect is aimed at (an adventurer or a person), when it is not the actor.</summary>
        public string Target { get; set; } = "";
        /// <summary>休む・野営 (once per act).</summary>
        public bool Rest { get; set; }
        /// <summary>Gives up the request and leaves the map (地図の外へ出た).</summary>
        public bool Abandon { get; set; }
        /// <summary>Asks a person to come along, or takes rescued people along.</summary>
        public bool Recruit { get; set; }
        /// <summary>A shop ware key (Shop.Wares) bought in this step.</summary>
        public string Buy { get; set; } = "";
        /// <summary>In a fight: attack / flee / revive / finish / defend / item / talk / other.</summary>
        public string CombatAct { get; set; } = "";
        /// <summary>An enemy of the place named as a target outside a fight (the game starts the fight from it).</summary>
        public string FoeHint { get; set; } = "";
        /// <summary>Silver handed over (a bribe, a fee, a gift).</summary>
        public int Pay { get; set; }
        public Effect? Effect { get; set; }
        public Ability? EffectAbility { get; set; }
        /// <summary>A 【name】 used in this step, and a one-line note when it is new.</summary>
        public string Technique { get; set; } = "";
        public string TechniqueNote { get; set; } = "";
        public string Conclusion { get; set; } = "";
        public bool Correct { get; set; }
        /// <summary>想定外の解き方 (an extreme check).</summary>
        public bool Unexpected { get; set; }
        public bool PlaceRule { get; set; }
        public bool Slow { get; set; }
        public bool Noisy { get; set; }
    }

    public sealed class Classified
    {
        public string Who { get; set; } = "";
        public List<Step> Steps { get; } = new List<Step>();
        /// <summary>Steps beyond the limit, left in the declaration box for the next turn.</summary>
        public List<string> Deferred { get; } = new List<string>();
        /// <summary>The bland public version of a secret action (the player's "表向き", or one the AI made).</summary>
        public string Cover { get; set; } = "";
        /// <summary>The AI left this declaration out (it became a plain success with no target).</summary>
        public bool Missing { get; set; }
    }

    public sealed class FightStart
    {
        public List<string> Enemies { get; } = new List<string>();
        public bool Surprise { get; set; }
    }

    public sealed class Classification
    {
        public List<Classified> Declarations { get; } = new List<Classified>();
        public FightStart? Fight { get; set; }
        /// <summary>The answer had to be asked for again.</summary>
        public bool Retried { get; set; }
        public int Calls { get; set; }
        /// <summary>Things dropped while reading (unknown ids...), for the log.</summary>
        public List<string> Dropped { get; } = new List<string>();
        public Classified? For(string who) => Declarations.FirstOrDefault(d => d.Who == who);
    }

    /// <summary>
    /// The first AI call of a turn: sorts every step of every declaration (09_Spec 4, 13). The model only picks from fixed choices and ids;
    /// the game rolls and decides. With a local model the answer is cut down to step, ability and difficulty.
    /// </summary>
    public sealed class Classifier
    {
        public const string ToolName = "classify";
        readonly ILlmClient _llm;
        /// <summary>The reduced form for local models (no targets, deductions or effects).</summary>
        public bool Reduced { get; }

        public Classifier(ILlmClient llm, bool reduced = false) { _llm = llm; Reduced = reduced; }

        public async Task<Classification> ClassifyAsync(Evening e, IReadOnlyList<Declaration> decls, bool inCombat, CancellationToken ct = default)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(Role.System, Reduced ? ReducedPrompt.Replace("{RULES}", SceneText.WorldRules) : PromptText),
                new ChatMessage(Role.User, UserText(e, decls, inCombat)),
            };
            var tool = Reduced ? ReducedTool(e) : Tool(e);
            var result = new Classification();
            JObject? args = null;
            for (int attempt = 0; attempt < 2 && args == null; attempt++)
            {
                result.Calls++;
                if (attempt > 0) result.Retried = true;
                try
                {
                    var reply = await _llm.ChatAsync(messages, new[] { tool }, ToolName, ct).ConfigureAwait(false);
                    args = Unwrap(reply.ToolCalls.FirstOrDefault(c => c.Name == ToolName)?.Arguments ?? TryJson(reply.Content));
                }
                catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException)
                {
                    result.Dropped.Add("読めない返事: " + ex.Message);
                }
            }
            Read(e, decls, inCombat, args ?? new JObject(), result);
            ApplySheet(e, decls, result);
            return result;
        }

        /// <summary>Some local models wrap the arguments one level down ({"object": {...}}).</summary>
        public static JObject? Unwrap(JObject? args)
        {
            if (args == null || args["declarations"] != null) return args;
            var inner = args.Properties().Select(p => p.Value).OfType<JObject>().FirstOrDefault(o => o["declarations"] != null);
            return inner ?? args;
        }

        static JObject? TryJson(string text)
        {
            var i = text.IndexOf('{');
            var j = text.LastIndexOf('}');
            if (i < 0 || j <= i) return null;
            try { return JObject.Parse(text.Substring(i, j - i + 1)); } catch (Newtonsoft.Json.JsonException) { return null; }
        }

        // ---------------------------------------------------------------- prompt

        public const string Prompt = @"あなたは TRPG の語り部の補佐です。冒険者たちの宣言を、ゲームのルールに沿って「仕分け」だけします。語りはしません。
必ず classify を 1 回呼び、宣言した冒険者全員の分を返してください。サイコロはゲームが振ります。あなたは決まった選択肢と [id] から選ぶだけです。

## 段に分ける
- 1 つの宣言に複数の行動があれば、段（steps）に分けます。判定の要らない動き（近づく、武器を抜く、台詞）は 1 段にまとめてかまいません。
- 判定の要る段は、探索中は 2 つまで、戦闘中は 1 つまで。それより後の行動は、捨てずに必ず deferred に文章で入れます（次の手に回す）。

## 段ごとの仕分け（sort）
- auto（そのまま成功）: 「失敗したら危険・損・時間の無駄がある」と「結果が確かでない」のどちらかが欠ける。例: 見回す、話しかける、鍵のない扉を開ける、急がずに梯子を登る、知っている道を歩く、判定なしの手がかりを調べる。
- check（判定）: 両方そろう。能力値（ability）と難しさ（tier）と理由（reason。画面に出す短い言葉）を付ける。
  - 能力値: STR 腕っぷし（剣・斧・槍・鎚、こじ開ける、脅す）/ DEX 手先（鍵・罠・細工、短剣・弓・投げ）/ AGI 身のこなし（かわす、逃げる、忍ぶ・隠れる）/
    INT 知識・魔法（魔法使いの呪文、謎解き、古い文字、騙す・言いくるめる）/ MND 意志・話術・信仰（僧侶の祈り、説得、頼む、恐怖に耐える、嘘を見抜く）。
  - 難しさ: easy 10（濡れた石段を駆け下りる、酔った客から噂を聞く、低い塀を越える）/ normal 13（普通の錠前、見張りの目を盗む、怯えた村人をなだめる、古い文字を読む）/
    hard 16（走りながら矢で縄を射切る、上等な錠前、衛兵長を買収する、崩れかけた橋を渡る）/ extreme 19（竜の鱗の隙間を射抜く、敵の首領を寝返らせる、燃える塔から人を担いで飛び降りる）。
  - 場面に書かれた手がかり・仕掛け・罠の難しさは、そのまま使う。
  - shift（段のずれ）: not_specialty（得意にない効果を技・呪文で起こす）/ class_weakness（職業の不得意。戦士の鍵開け、魔法使いの力仕事）/
    favorable（道具・準備・相手の油断）/ unfavorable（暗い、急ぐ、傷）/ none。
- impossible（できない）: 理由は 3 種類だけ。world_rule（世界の決まりの一覧の番号を rule に）/ lacking（持っていない・知らない。呪文を使えない職業の呪文も）/
  situation（縛られている、届かない、相手がいない。how に「どうすればできるか」を必ず書く）。
  どれにも当たらなければ、どんなに無茶でも check（最悪でも extreme）。「難しそう」は理由になりません。
  持ち物にない物を使う宣言は lacking。冒険者の状態（縛られ・眠り・動けない）で動けないなら situation。世界にない道具（銃など）は world_rule 8。
  移動の呪文で遠くへ一瞬で着くのは world_rule 3（呪文を使える職業でも）。
- fate（運命のダイス）: 行動ではなく、世界の事実を確かめるとき（「この部屋に窓はある？」「この商人は薬を売っている？」「町に叔父がいる」）。
  question に問いを、likelihood に見込み（certain / likely / even / unlikely / remote）を。「〜はある？」「〜はいる？」のような問いの形の宣言は fate。
  ただし真相や設定表の秘密に答えがある問い（「若者は生きているか」「犯人は誰か」）は fate にせず impossible の world_rule 5。名品を得る問いは world_rule 6。

## 向け先（id で。場面にない id は書かない）
- mechanism: 仕掛けを動かす段。item: 物を使う・取る・渡す・置く段（take=true は拾う・奪う、give_to は渡す相手の id、place_at は置く場所の id）。
- person: 人物に向けた段。頼みなら request（small: 道や噂を聞く / normal: 物を借りる・人に会わせる・知っていることを話す / large: 同行・危険を冒す・秘密を話す・主を裏切る）と
  approach（ask 頼む / persuade 説得 / threaten 脅す / trick 騙す・言いくるめる）。頼みの判定の要否と難しさはゲームが態度で決めるので、sort は check にして ability だけ合わせてください。
- clue: その段で手に入りうる手がかりの id（判定なしの段にも付ける）。move_to: 別の場所へ移る段の行き先 id。
- against: 仲間の冒険者に向けた行動（奪う、騙す、攻撃する）の相手の id。helps: 仲間の判定を手伝う段の相手の id。

## 失敗しても話が進む
- 結論の手がかりを得る・先へ進む道を開く判定は important=true。costs に失敗したときの代償を 2 つ、起こりそうな順に（clock 時が過ぎる / wound 傷 / noticed 気付かれる / lose_item 物を失う）。

## 技・呪文・戦闘の自由な行動
- effect に効果の種類（strike 1 体攻撃 / sweep 全体攻撃 / hold 動きを止める / cover かばう / boost 加護 / ward 守り / heal 回復 / taunt 挑発 / smoke 煙 / unlock 調べる・開ける）と
  大きさ（small / medium / large）、effect_ability に振る能力値。普通の武器での攻撃は effect なしの check（ability は武器の能力値）。
- 宣言に【名前】があれば technique にその名前。冒険者の名付けた技に同じ名前があれば中身はゲームが知っているので effect は不要。新しい名前なら effect と technique_note（ひとことの説明）。
- 呪文は魔法使いと僧侶だけ。ほかの職業は筋の通る道具や方法がなければ lacking。

## そのほか
- 推理: 冒険者が推理を口にする・突きつける・行動に移したら conclusion にどの結論の推理かを、correct に合っているかを。
- unexpected: 山場の最中に、ボスや山場を、設定表にない方法で一気に片付けようとする段（生き埋めにする、封じ込める、など）。extreme の check にする。
- place_rule: 場面の「場所の決まり」に書かれた行い（大声、水音など）に触れる段は必ず true。slow: 時間をかける段（丹念に調べる）。noisy: 大きな物音を立てる段。rest: 休む・野営する段（sort は auto）。
- target: 回復・加護・守り・かばうなど、効果を向ける相手の id（自分なら空）。
- recruit: 人物に一緒に来てもらう・助けた人を連れていく段（person にその人の id、request は large）。
- abandon: 依頼を捨てて、この土地から去る段（sort は auto）。
- お金: 銀貨を渡す段（買収・情報料・贈り物）は pay に枚数を。店で買う段は buy に品の鍵（torch 松明 / rope ロープ / oil 油 / antidote 解毒薬 / potion 薬 小 / potion_m 薬 中 / scroll 巻物）を。買い物は auto。
- 内緒の宣言（secret）も普通に仕分ける（物を取るなら item と take も）。表向きが書かれていなければ、必ず cover に、判定の要らない当たり障りのない行動を 1 文で書く。
- 物を使う・取る・渡す・置く段には、場面や持ち物にある物の id を必ず item に入れる（仲間の持ち物を奪うときも）。
- 戦闘が始まるべきとき（敵が襲う、冒険者が攻撃をしかける）は fight に、その場所に出うる敵の id を並べる。冒険者が気付かれずに先に仕掛けたら surprise=true。
- 戦闘中は、段ごとに combat_act（attack 武器や魔力の矢で攻撃 / flee 逃げる / revive 倒れた仲間を起こす / finish 倒れた相手にとどめ / defend 身を守る /
  item 持ち物を使う / talk 説得・降伏の呼びかけ / other ほか）と、狙う敵や相手の id を target に。技・呪文は effect も。
- 世界の決まり: " + "{RULES}" + @"
- 冒険者の台詞・気持ちを足さない。宣言にないことは仕分けない。";

        public static string PromptText => Prompt.Replace("{RULES}", SceneText.WorldRules);

        public const string ReducedPrompt = @"あなたは TRPG の補佐です。冒険者の宣言を段に分け、段ごとに sort を付けます。必ず classify を 1 回呼びます。
- auto（そのまま成功）: 見回す、話しかける、歩いて移る、物を渡す・置く、買い物。失敗しても損がないか、結果が確か。
- check（判定）: 失敗したら危険や損があり、結果が確かでないとき。ability（STR 腕っぷし・脅す / DEX 手先・鍵・罠 / AGI 身のこなし・忍ぶ / INT 知識・魔法・古い文字・騙す / MND 説得・祈り・頼む）と
  tier（easy 10 / normal 13 / hard 16 / extreme 19）と短い reason。無茶でも、人にできうるなら check（最悪 extreme）。
- impossible（できない）: impossible_why に world_rule（世界の決まり。rule に番号）/ lacking（持っていない・呪文を使えない職業の呪文）/ situation（縛られている、届かない。how にどうすればできるか）。
- fate（運命のダイス）: 行動ではなく世界の事実を確かめる問い（「裏口はある？」「叔父が住んでいる」）。
- 世界の決まり: {RULES}";

        string UserText(Evening e, IReadOnlyList<Declaration> decls, bool inCombat)
        {
            var b = new StringBuilder();
            b.AppendLine(Reduced ? SceneText.Places(e) + SceneText.Party(e) : SceneText.All(e));
            b.AppendLine(inCombat ? "# いまは戦闘中（判定の要る段は 1 つまで）" : "# いまは探索中（判定の要る段は 2 つまで）");
            b.AppendLine("# 宣言");
            foreach (var d in decls)
            {
                var who = e.Find(d.Who);
                b.AppendLine($"- [{d.Who}] {who?.Name}{(d.Secret ? "（内緒）" : "")}: {d.Text}{(d.Secret && d.Cover.Length > 0 ? $"（表向き: {d.Cover}）" : "")}");
            }
            return b.ToString();
        }

        // ---------------------------------------------------------------- tool schema

        static JObject Enum(params string[] values) => new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        static JObject Str(string description) => new JObject { ["type"] = "string", ["description"] = description };
        static JObject Bool(string description) => new JObject { ["type"] = "boolean", ["description"] = description };
        static readonly string[] Abilities = { "STR", "DEX", "AGI", "INT", "MND" };
        static readonly string[] Tiers = { "easy", "normal", "hard", "extreme" };

        ToolDef Tool(Evening e)
        {
            var who = e.Party.Where(a => a.Life != Life.Dead).Select(a => a.Id).ToArray();
            var step = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["text"] = Str("この段の行動（宣言の言葉を短く）"),
                    ["sort"] = Enum("auto", "check", "impossible", "fate"),
                    ["ability"] = Enum(Abilities),
                    ["tier"] = Enum(Tiers),
                    ["shift"] = Enum("none", "not_specialty", "class_weakness", "favorable", "unfavorable"),
                    ["reason"] = Str("判定の理由（画面に出す。15 字ほど）"),
                    ["important"] = Bool("結論の手がかりを得る・先へ進む道を開く判定"),
                    ["costs"] = new JObject { ["type"] = "array", ["items"] = Enum("clock", "wound", "noticed", "lose_item") },
                    ["impossible_why"] = Enum("world_rule", "lacking", "situation"),
                    ["rule"] = new JObject { ["type"] = "integer", ["description"] = "世界の決まりの番号（1〜8）" },
                    ["how"] = Str("できないとき、どうすればできるか"),
                    ["question"] = Str("運命のダイスの問い"),
                    ["likelihood"] = Enum("certain", "likely", "even", "unlikely", "remote"),
                    ["mechanism"] = Str("仕掛けの id"),
                    ["item"] = Str("物の id（持ち物の id も可）"),
                    ["take"] = Bool("item を拾う・奪う"),
                    ["give_to"] = Str("item を渡す相手の id（人物か冒険者）"),
                    ["place_at"] = Str("item を置く場所の id"),
                    ["person"] = Str("向けた人物の id"),
                    ["request"] = Enum("small", "normal", "large"),
                    ["approach"] = Enum("ask", "persuade", "threaten", "trick"),
                    ["clue"] = Str("この段で手に入りうる手がかりの id"),
                    ["move_to"] = Str("移る先の場所の id"),
                    ["against"] = Str("向けた仲間の冒険者の id"),
                    ["helps"] = Str("手伝う仲間の冒険者の id"),
                    ["target"] = Str("効果（回復・加護・守りなど）を向ける相手の id（冒険者・人物）"),
                    ["rest"] = Bool("休む・野営する"),
                    ["recruit"] = Bool("人物に一緒に来てもらう・助けた人を連れていく"),
                    ["abandon"] = Bool("依頼を捨てて、この土地から去る"),
                    ["combat_act"] = Enum("attack", "flee", "revive", "finish", "defend", "item", "talk", "other"),
                    ["buy"] = Enum(Shop.Wares.Select(w => w.Key).ToArray()),
                    ["pay"] = new JObject { ["type"] = "integer", ["description"] = "渡す銀貨の枚数（買収・情報料・贈り物）" },
                    ["effect"] = Enum("strike", "sweep", "hold", "cover", "boost", "ward", "heal", "taunt", "smoke", "unlock"),
                    ["size"] = Enum("small", "medium", "large"),
                    ["effect_ability"] = Enum(Abilities),
                    ["technique"] = Str("【】で付けた技の名前"),
                    ["technique_note"] = Str("新しい技のひとことの説明"),
                    ["conclusion"] = Str("推理した結論の id"),
                    ["correct"] = Bool("推理が合っている"),
                    ["unexpected"] = Bool("設定表にない山場の解き方"),
                    ["place_rule"] = Bool("場所の決まりに触れる"),
                    ["slow"] = Bool("時間をかける"),
                    ["noisy"] = Bool("大きな物音を立てる"),
                },
                ["required"] = new JArray("text", "sort"),
            };
            var decl = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["who"] = Enum(who),
                    ["steps"] = new JObject { ["type"] = "array", ["items"] = step },
                    ["deferred"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" }, ["description"] = "次の手に回す行動" },
                    ["cover"] = Str("内緒の宣言の表向きの行動"),
                },
                ["required"] = new JArray("who", "steps"),
            };
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["declarations"] = new JObject { ["type"] = "array", ["items"] = decl },
                    ["fight"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["enemies"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                            ["surprise"] = Bool("冒険者が気付かれずに先に仕掛けた"),
                        },
                    },
                },
                ["required"] = new JArray("declarations"),
            };
            return new ToolDef(ToolName, "宣言を段に分けて仕分ける", schema);
        }

        ToolDef ReducedTool(Evening e)
        {
            var who = e.Party.Where(a => a.Life != Life.Dead).Select(a => a.Id).ToArray();
            var step = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["text"] = Str("この段の行動"), ["sort"] = Enum("auto", "check", "impossible", "fate"),
                    ["impossible_why"] = Enum("world_rule", "lacking", "situation"), ["rule"] = new JObject { ["type"] = "integer" }, ["how"] = Str("どうすればできるか"),
                    ["ability"] = Enum(Abilities), ["tier"] = Enum(Tiers), ["reason"] = Str("短い理由"),
                },
                ["required"] = new JArray("text", "sort"),
            };
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["declarations"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject { ["who"] = Enum(who), ["steps"] = new JObject { ["type"] = "array", ["items"] = step } },
                            ["required"] = new JArray("who", "steps"),
                        },
                    },
                },
                ["required"] = new JArray("declarations"),
            };
            return new ToolDef(ToolName, "宣言を段に分けて仕分ける", schema);
        }

        // ---------------------------------------------------------------- reading the answer

        /// <summary>
        /// Turns the tool arguments into steps. Missing declarations become plain successes; unknown ids are dropped;
        /// steps past the limit (2 checks exploring, 1 in a fight) go to Deferred.
        /// </summary>
        public static void Read(Evening e, IReadOnlyList<Declaration> decls, bool inCombat, JObject args, Classification result)
        {
            var answers = Answers(args, decls);
            foreach (var d in decls)
            {
                var c = new Classified { Who = d.Who, Cover = d.Cover };
                var a = answers.FirstOrDefault(x => (string?)x["who"] == d.Who)
                    ?? answers.FirstOrDefault(x => e.Find(d.Who)?.Name is string n && (string?)x["who"] == n);
                if (a == null)
                {
                    c.Missing = true;
                    c.Steps.Add(new Step { Text = d.Text, Sort = Sort.Auto });
                    result.Dropped.Add($"{d.Who}: 宣言が抜けていた（そのまま成功）");
                    result.Declarations.Add(c);
                    continue;
                }
                if (c.Cover.Length == 0) c.Cover = (string?)a["cover"] ?? "";
                int checks = 0, limit = inCombat ? 1 : 2;
                foreach (var s in (a["steps"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var step = ReadStep(e, d.Who, s, result.Dropped);
                    if (d.Secret && c.Steps.Count > 0 && step.Sort == Sort.Check) { c.Deferred.Add(step.Text); continue; }   // a secret action is one step
                    if (step.Sort == Sort.Check || step.Sort == Sort.FateDie)
                    {
                        if (checks >= limit) { c.Deferred.Add(step.Text); continue; }
                        checks++;
                    }
                    if (c.Deferred.Count > 0) { c.Deferred.Add(step.Text); continue; }   // keep the order: everything after a cut waits
                    c.Steps.Add(step);
                }
                foreach (var x in (a["deferred"] as JArray ?? new JArray())) if ((string?)x is string t && t.Length > 0) c.Deferred.Add(t);
                if (c.Steps.Count == 0) c.Steps.Add(new Step { Text = d.Text, Sort = Sort.Auto });
                result.Declarations.Add(c);
            }
            if (result.Fight == null && e.Combat == null && !((args["fight"] as JObject)?["enemies"] is JArray { Count: > 0 }))
            {
                // no fight given, but someone attacks an enemy of this place: that starts it
                var hinted = result.Declarations.SelectMany(c => c.Steps).Where(s => s.FoeHint.Length > 0).Select(s => s.FoeHint).Distinct().ToList();
                if (hinted.Count > 0)
                {
                    var place = e.Story.PlaceOf(e.World.PlacesOfParty.First());
                    var fs = new FightStart();
                    fs.Enemies.AddRange(place?.Enemies.Where(x => hinted.Contains(x)) ?? hinted);
                    result.Fight = fs;
                }
            }
            if (args["fight"] is JObject f && f["enemies"] is JArray en && en.Count > 0)
            {
                var fight = new FightStart { Surprise = (bool?)f["surprise"] ?? false };
                var allowed = new HashSet<string>(e.World.PlacesOfParty.SelectMany(p => e.Story.PlaceOf(p)?.Enemies ?? new List<string>()));
                foreach (var id in en.Select(x => (string?)x ?? ""))
                    if (allowed.Contains(id) || e.Story.People.Any(p => p.Enemy == id)) fight.Enemies.Add(id);
                    else result.Dropped.Add($"敵 '{id}' はこの場所に出ない");
                if (fight.Enemies.Count > 0) result.Fight = fight;
            }
        }

        /// <summary>
        /// What the game knows better than the AI, applied right after reading (09_Spec 4, 13): the sheet's numbers for clues and
        /// mechanisms, adventurers who cannot move, taking what a fellow adventurer holds, a bland cover for a secret action.
        /// </summary>
        public static void ApplySheet(Evening e, IReadOnlyList<Declaration> decls, Classification result)
        {
            foreach (var c in result.Declarations)
            {
                var a = e.Find(c.Who);
                if (a == null) continue;
                var decl = decls.FirstOrDefault(d => d.Who == c.Who);
                if (decl != null && decl.Secret && c.Cover.Trim().Length == 0) c.Cover = "辺りの様子をうかがう";
                foreach (var s in c.Steps)
                {
                    if (!a.Statuses.CanAct && s.Sort != Sort.Impossible && !(a.Statuses.Has(StatusKind.Bound) && s.Sort == Sort.Auto && s.Person.Length > 0))
                    {
                        var st = a.Statuses.List.First(x => StatusInfo.Disables(x.Kind)).Kind;
                        s.Sort = Sort.Impossible; s.Why = ImpossibleReason.Situation; s.Check = null;
                        s.How = st == StatusKind.Bound ? "縄を解けば動ける（仲間が解くか、STR か DEX の難しい判定）" : $"{st.Label()}が解ければ動ける";
                        continue;
                    }
                    if (s.Clue.Length > 0 && s.Person.Length == 0 && e.Story.ClueOf(s.Clue) is Clue clue)
                    {
                        if (clue.Access == ClueAccess.Free && s.Sort == Sort.Check) { s.Sort = Sort.Auto; s.Check = null; }
                        else if (clue.Access == ClueAccess.Check)
                        {
                            s.Sort = Sort.Check;
                            s.Check ??= new CheckSpec { Reason = clue.How };
                            s.Check.Ability = clue.Ability!.Value; s.Check.Tier = clue.Tier!.Value; s.Check.Important = clue.Important;
                        }
                    }
                    if (s.Mechanism.Length > 0 && e.Story.Mechanisms.FirstOrDefault(m => m.Id == s.Mechanism) is MechanismDef md && !md.NoCheck)
                    {
                        s.Sort = Sort.Check;
                        s.Check ??= new CheckSpec { Reason = md.Name };
                        s.Check.Ability = md.Ability; s.Check.Tier = md.Tier;
                    }
                    if (s.Take && s.Item.Length > 0 && s.Against.Length == 0 && e.World.WhereIs(s.Item) is string w && w.StartsWith("pc:") && w.Substring(3) != c.Who)
                        s.Against = w.Substring(3);
                }
            }
        }

        /// <summary>
        /// The declarations in an answer, read forgivingly: every object that holds "steps", wherever a weak model put it, with "who"
        /// taken from the object or the nearest object around it. With a single declaration, an answer without "who" is theirs.
        /// </summary>
        static List<JObject> Answers(JObject args, IReadOnlyList<Declaration> decls)
        {
            var found = new List<JObject>();
            void Walk(JToken t, string? who)
            {
                if (t is JObject o)
                {
                    var here = (string?)(o["who"] as JValue) ?? who;
                    if (o["steps"] is JArray)
                    {
                        var copy = (JObject)o.DeepClone();
                        if (copy["who"] == null && here != null) copy["who"] = here;
                        found.Add(copy);
                        return;
                    }
                    foreach (var p in o.Properties()) Walk(p.Value, here);
                }
                else if (t is JArray a) foreach (var x in a) Walk(x, who);
            }
            Walk(args, null);
            if (decls.Count == 1)
                foreach (var f in found.Where(f => f["who"] == null)) f["who"] = decls[0].Who;
            return found;
        }

        static Step ReadStep(Evening e, string who, JObject s, List<string> dropped)
        {
            var step = new Step { Text = (string?)s["text"] ?? "" };
            step.Sort = ((string?)s["sort"]) switch { "check" => Sort.Check, "impossible" => Sort.Impossible, "fate" => Sort.FateDie, _ => Sort.Auto };
            string Id(string field, Func<string, bool> exists)
            {
                var v = ((string?)s[field] ?? "").Trim().Trim('[', ']');
                if (v.Length == 0) return "";
                if (exists(v)) return v;
                dropped.Add($"{who}: {field} '{v}' は知らない id");
                return "";
            }
            bool IsPlace(string id) => e.Story.PlaceOf(id) != null;
            bool IsPerson(string id) => e.Story.PersonOf(id) != null || e.World.Cast.Find(id) != null;
            bool IsAdventurer(string id) => e.Find(id) != null;
            bool IsItem(string id) => e.Story.ItemOf(id) != null || e.Party.Any(a => a.Gear.Has(id));
            step.Mechanism = Id("mechanism", id => e.Story.Mechanisms.Any(m => m.Id == id));
            step.Item = Id("item", IsItem);
            step.Take = (bool?)s["take"] ?? false;
            step.GiveTo = Id("give_to", id => IsPerson(id) || IsAdventurer(id));
            step.PlaceAt = Id("place_at", IsPlace);
            step.Person = Id("person", IsPerson);
            step.Clue = Id("clue", id => e.Story.ClueOf(id) != null);
            step.MoveTo = Id("move_to", IsPlace);
            // a person put in "against" (or "target" outside a fight) is the person the step is aimed at
            var raw = ((string?)s["against"] ?? "").Trim();
            if (step.Person.Length == 0 && raw.Length > 0 && !IsAdventurer(raw) && IsPerson(raw)) { step.Person = raw; s.Remove("against"); }
            var rawTarget = ((string?)s["target"] ?? "").Trim();
            if (step.Person.Length == 0 && e.Combat == null && rawTarget.Length > 0 && !IsAdventurer(rawTarget) && IsPerson(rawTarget)) step.Person = rawTarget;
            step.Against = Id("against", id => IsAdventurer(id) && id != who);
            step.Helps = Id("helps", id => IsAdventurer(id) && id != who);
            // outside a fight, an enemy of this place as the target means the fight starts (read in Read)
            if (e.Combat == null && rawTarget.Length > 0 && e.World.PlacesOfParty.Any(p => e.Story.PlaceOf(p)?.Enemies.Contains(rawTarget) == true)) { step.FoeHint = rawTarget; s.Remove("target"); }
            step.Target = Id("target", id => IsAdventurer(id) || IsPerson(id) || e.Combat?.Find(id) != null);
            step.CombatAct = (string?)s["combat_act"] ?? "";
            step.Rest = (bool?)s["rest"] ?? false;
            step.Recruit = (bool?)s["recruit"] ?? false;
            step.Abandon = (bool?)s["abandon"] ?? false;
            step.Buy = Shop.Wares.Any(w => w.Key == (string?)s["buy"]) ? (string)s["buy"]! : "";
            step.Pay = Math.Max(0, (int?)s["pay"] ?? 0);
            step.Conclusion = Id("conclusion", id => e.Story.Conclusions.Any(c => c.Id == id));
            step.Correct = (bool?)s["correct"] ?? false;
            step.Request = ((string?)s["request"]) switch { "small" => RequestSize.Small, "normal" => RequestSize.Normal, "large" => RequestSize.Large, _ => (RequestSize?)null };
            step.Approach = ((string?)s["approach"]) switch { "ask" => Play.Approach.Ask, "persuade" => Play.Approach.Persuade, "threaten" => Play.Approach.Threaten, "trick" => Play.Approach.Trick, _ => (Approach?)null };
            step.Unexpected = (bool?)s["unexpected"] ?? false;
            step.PlaceRule = (bool?)s["place_rule"] ?? false;
            step.Slow = (bool?)s["slow"] ?? false;
            step.Noisy = (bool?)s["noisy"] ?? false;
            step.Technique = ((string?)s["technique"] ?? "").Trim().Trim('【', '】');
            step.TechniqueNote = (string?)s["technique_note"] ?? "";
            if (ParseEffect((string?)s["effect"]) is EffectKind k)
            {
                var size = ((string?)s["size"]) switch { "large" => EffectSize.Large, "medium" => EffectSize.Medium, _ => EffectSize.Small };
                step.Effect = new Effect(k, Effects.Exists(k, size) ? size : EffectSize.Medium);
                if (AbilityNames.TryParse((string?)s["effect_ability"], out var ea)) step.EffectAbility = ea;
            }
            switch (step.Sort)
            {
                case Sort.Check:
                {
                    var spec = new CheckSpec { Reason = (string?)s["reason"] ?? "", Important = (bool?)s["important"] ?? false };
                    spec.Ability = AbilityNames.TryParse((string?)s["ability"], out var ab) ? ab : step.EffectAbility ?? Ability.Dex;
                    spec.Tier = AbilityNames.TryParseTier((string?)s["tier"], out var t) ? t : Tier.Normal;
                    if (step.Unexpected) spec.Tier = Tier.Extreme;
                    switch ((string?)s["shift"])
                    {
                        case "not_specialty": spec.Shifts.Add(Shift.NotSpecialty); break;
                        case "class_weakness": spec.Shifts.Add(Shift.ClassWeakness); break;
                        case "favorable": spec.Shifts.Add(Shift.Favorable); break;
                        case "unfavorable": spec.Shifts.Add(Shift.Unfavorable); break;
                    }
                    foreach (var x in (s["costs"] as JArray ?? new JArray()))
                        switch ((string?)x)
                        {
                            case "clock": spec.Costs.Add(Cost.Clock); break;
                            case "wound": spec.Costs.Add(Cost.Wound); break;
                            case "noticed": spec.Costs.Add(Cost.Noticed); break;
                            case "lose_item": spec.Costs.Add(Cost.LoseItem); break;
                        }
                    spec.Stealth = spec.Ability == Ability.Agi && (step.Text.Contains("忍") || step.Text.Contains("隠れ"));
                    spec.Spell = step.Effect != null && e.Find(who)?.Class.Spells == true;
                    step.Check = spec;
                    break;
                }
                case Sort.Impossible:
                    step.Why = ((string?)s["impossible_why"]) switch { "world_rule" => ImpossibleReason.WorldRule, "lacking" => ImpossibleReason.Lacking, _ => ImpossibleReason.Situation };
                    step.WorldRule = (int?)s["rule"] ?? 0;
                    step.How = (string?)s["how"] ?? "";
                    if (step.Why == ImpossibleReason.WorldRule && (step.WorldRule < 1 || step.WorldRule > 8))
                    {
                        // a world rule must be named; without one it is not a reason to refuse (09_Spec 12)
                        dropped.Add($"{who}: 世界の決まりの番号がない「できない」→ 至難の判定");
                        step.Sort = Sort.Check;
                        step.Why = null;
                        step.Check = new CheckSpec { Ability = step.EffectAbility ?? Ability.Dex, Tier = Tier.Extreme, Reason = "無茶な試み" };
                    }
                    break;
                case Sort.FateDie:
                    step.Question = (string?)s["question"] ?? step.Text;
                    step.Likelihood = ((string?)s["likelihood"]) switch
                    {
                        "certain" => Likelihood.Certain, "likely" => Likelihood.Likely, "unlikely" => Likelihood.Unlikely, "remote" => Likelihood.Remote, _ => Likelihood.Even,
                    };
                    break;
            }
            return step;
        }

        static EffectKind? ParseEffect(string? s) => s switch
        {
            "strike" => EffectKind.Strike, "sweep" => EffectKind.Sweep, "hold" => EffectKind.Hold, "cover" => EffectKind.Cover,
            "boost" => EffectKind.Boost, "ward" => EffectKind.Ward, "heal" => EffectKind.Heal, "taunt" => EffectKind.Taunt,
            "smoke" => EffectKind.Smoke, "unlock" => EffectKind.Unlock, _ => null,
        };
    }
}
