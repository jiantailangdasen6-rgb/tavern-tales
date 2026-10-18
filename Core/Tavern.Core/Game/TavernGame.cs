#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Content;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Game
{
    /// <summary>What a player chose this turn: a card, a basic action, or free text (any combination with text).</summary>
    public sealed class Declaration
    {
        public string Character = "";
        public string Text = "";
        public string? CardId;
        /// <summary>attack / defend / help / revive / flee (combat), revive / help (outside combat).</summary>
        public string? Basic;
        /// <summary>Enemy uid or ally name.</summary>
        public string? Target;
    }

    public sealed class TurnReport
    {
        public List<LogEntry> Entries { get; } = new List<LogEntry>();
        public string Narration = "";
        public List<string> Suggestions = new List<string>();
        public string Model = "";
    }

    /// <summary>
    /// One evening: the scenario, the party, the state and the AI game master. The UI calls OpenAsync once,
    /// then SubmitAsync with everyone's declarations each turn. All rules run here; the AI narrates and proposes.
    /// </summary>
    public sealed class TavernGame
    {
        public ContentDb Db { get; }
        public ScenarioDef Scenario { get; }
        public Storyteller Teller { get; }
        public List<Character> Party { get; }
        public GameState State { get; }
        public GmSession Gm { get; }
        readonly IRandom _rng;

        /// <summary>Raised for every new log line (the UI can animate dice for rolls).</summary>
        public event Action<LogEntry>? Logged;

        /// <summary>
        /// Asked (outside fights) when someone failed a check and still has their fate coin: gets the failed roll entries,
        /// returns the ones to reroll. The UI shows the dice and waits for the players; null means never reroll.
        /// </summary>
        public Func<IReadOnlyList<LogEntry>, Task<IReadOnlyList<LogEntry>>>? ChooseRerolls { get; set; }

        bool CanReroll(CheckResult r) => !r.Succeeded && !r.Rerolled && Find(r.Request.CharacterName)?.FateCoins > 0;

        /// <summary>A note to the storyteller from outside the story ("shorter", "scarier"); sent with the next turn.</summary>
        public string? StorytellerRequest { get; set; }

        public bool InCombat => State.Combat != null;
        public bool Ended => State.Ending != null;
        public LocationDef Here => Scenario.Location(State.Location);

        TavernGame(ContentDb db, ScenarioDef scenario, Storyteller teller, List<Character> party, GameState state, GmSession gm, IRandom rng)
        {
            Db = db; Scenario = scenario; Teller = teller; Party = party; State = state; Gm = gm; _rng = rng;
            Gm.NarrateTool = GameTools.Narrate(scenario, db, party);
        }

        public static TavernGame New(ContentDb db, ScenarioDef scenario, Storyteller teller, List<Character> party,
            ILlmClient llm, ILlmClient? highlightLlm, IRandom rng)
        {
            var state = new GameState { ScenarioId = scenario.Id, StorytellerId = teller.Id, Location = scenario.Start };
            if (scenario.Id.StartsWith("gen_")) state.ScenarioJson = Newtonsoft.Json.JsonConvert.SerializeObject(scenario);
            state.Visited.Add(scenario.Start);
            var gm = new GmSession(llm, SystemPrompt(teller, party, scenario, db), party, rng, highlightLlm) { KeepTurns = RecentTurns };
            return new TavernGame(db, scenario, teller, party, state, gm, rng);
        }

        /// <summary>Player messages sent word for word each call (a combat round has two); older ones go as a recap (GmSession.KeepTurns).</summary>
        public const int RecentTurns = 10;

        internal static TavernGame Restore(ContentDb db, ScenarioDef scenario, Storyteller teller, List<Character> party, GameState state,
            IEnumerable<ChatMessage> history, ILlmClient llm, ILlmClient? highlightLlm, IRandom rng)
        {
            var gm = new GmSession(llm, SystemPrompt(teller, party, scenario, db), party, rng, highlightLlm, history) { KeepTurns = RecentTurns };
            return new TavernGame(db, scenario, teller, party, state, gm, rng);
        }

        static string SystemPrompt(Storyteller teller, List<Character> party, ScenarioDef s, ContentDb db)
            => Prompts.System(teller, party, ScenarioPrompt.Describe(s, db) + "\n" + ScenarioPrompt.TellerNotes(teller));

        /// <summary>Makes a new character from a class definition.</summary>
        public static Character CreateCharacter(ContentDb db, string name, string classId, string background, string portrait = "")
        {
            var c = db.Classes[classId];
            var ch = new Character(name, classId, c.Might, c.Skill, c.Wits, c.Spirit, c.Hp, c.Focus, background) { Portrait = portrait };
            ch.Cards.AddRange(c.Cards);
            return ch;
        }

        // ---------------------------------------------------------------- turns

        /// <summary>The storyteller opens the evening (the strong model tells it).</summary>
        public async Task<TurnReport> OpenAsync(CancellationToken ct = default)
        {
            var report = new TurnReport();
            var turn = await Gm.NarrateAsync(Status() + "\n物語を始めてください。依頼が舞い込む最初の場面を、語り部らしく語ってください。", highlight: true, ct);
            Finish(report, turn);
            return report;
        }

        public async Task<TurnReport> SubmitAsync(IReadOnlyList<Declaration> declarations, CancellationToken ct = default)
        {
            if (Ended) throw new InvalidOperationException("the story has ended");
            State.Turn++;
            var report = new TurnReport();
            var acting = declarations.Where(d => Find(d.Character)?.CanAct == true || d.Basic == "revive").ToList();
            foreach (var d in acting) Add(report, LogEntry.Line(LogKind.Declaration, d.Character, DescribeDeclaration(d)));
            if (InCombat) await CombatRoundAsync(acting, report, ct);
            else await ExploreAsync(acting, report, ct);
            return report;
        }

        async Task ExploreAsync(List<Declaration> decls, TurnReport report, CancellationToken ct)
        {
            // Cards and basic actions that need no AI judgement are applied first.
            var notes = new List<string>();
            foreach (var d in decls) ApplyOutOfCombat(d, report, notes);
            var text = new StringBuilder(Status());
            text.AppendLine("【宣言】");
            foreach (var d in decls) text.AppendLine($"{d.Character}: {DescribeDeclaration(d)}");
            if (notes.Count > 0) { text.AppendLine("【ゲームが処理したこと】"); foreach (var n in notes) text.AppendLine(n); }

            var turn = new GmTurn();
            var entries = await Gm.AskPlanAsync(text.ToString(), ct: ct);
            Gm.RollChecks(entries, turn);
            var rollEntries = turn.Checks.Select(RollEntry).ToList();
            foreach (var e in rollEntries) Add(report, e);

            // Fate coins: before the storyteller hears the dice, a player may spend their coin to reroll a failed check.
            if (ChooseRerolls != null && turn.Checks.Any(CanReroll))
            {
                var picks = await ChooseRerolls(rollEntries.Where((e, i) => CanReroll(turn.Checks[i])).ToList());
                for (int i = 0; i < turn.Checks.Count; i++)
                {
                    if (!picks.Contains(rollEntries[i]) || !CanReroll(turn.Checks[i])) continue;
                    var who = Find(turn.Checks[i].Request.CharacterName)!;
                    var again = Checks.Reroll(turn.Checks[i], who, _rng);
                    if (again == null) continue;
                    turn.Checks[i] = again;
                    Add(report, RollEntry(again));
                }
            }
            Gm.AnswerPlan(GmSession.ChecksAnswer(turn));
            var narrated = await Gm.NarrateAsync(null, ct: ct);
            Finish(report, narrated);
            await AfterNarrationAsync(report, narrated, ct);
        }

        /// <summary>Card and basic actions outside combat (healing, blessings, reviving, utility cards' +3).</summary>
        void ApplyOutOfCombat(Declaration d, TurnReport report, List<string> notes)
        {
            var who = Find(d.Character);
            if (who == null) return;
            if (d.Basic == "revive") { Revive(who, d.Target, report, notes); return; }
            if (d.CardId == null || !Db.Cards.TryGetValue(d.CardId, out var card)) return;
            if (!who.SpendFocus(card.Cost)) { notes.Add($"{who.Name} は気力が足りず「{card.Name}」を使えなかった。"); return; }
            switch (card.Kind)
            {
                case "heal":
                {
                    foreach (var target in HealTargets(card, who, d.Target))
                    {
                        target.Heal(card.Amount);
                        var line = $"{who.Name} の「{card.Name}」で {target.Name} の HP が {target.Hp}/{target.MaxHp} に。";
                        notes.Add(line); Add(report, LogEntry.Line(LogKind.Event, who.Name, line));
                    }
                    break;
                }
                case "bless":
                    foreach (var t in card.Target == "party" ? Party.Where(p => p.CanAct) : new[] { Find(d.Target) ?? who }) t.NextCheckBonus += card.Bonus;
                    notes.Add($"{who.Name} の「{card.Name}」: 次の判定 +{card.Bonus}。");
                    break;
                default:
                    // Utility (and attack cards used outside a fight): the card's skill makes the related check easier.
                    who.NextCheckBonus += 3;
                    notes.Add($"{who.Name} は「{card.Name}」を使った（この手の判定 +3）。");
                    break;
            }
        }

        void Revive(Character who, string? targetName, TurnReport report, List<string> notes)
        {
            var target = Find(targetName) ?? Party.FirstOrDefault(p => p.State == LifeState.Down);
            if (target == null || target.State != LifeState.Down || !who.CanAct) return;
            target.Heal(1);
            var line = $"{who.Name} が {target.Name} を助け起こした（HP 1）。";
            notes.Add(line); Add(report, LogEntry.Line(LogKind.Event, who.Name, line));
        }

        // ---------------------------------------------------------------- narration effects

        /// <summary>Applies the effects written in the narrate call (validated against the rules and the scenario).</summary>
        async Task AfterNarrationAsync(TurnReport report, GmTurn turn, CancellationToken ct)
        {
            var a = turn.NarrateArgs;
            foreach (var e in Entries(a, "damage"))
                if (Find((string?)e["character"]) is Character c && c.State != LifeState.Dead)
                {
                    var n = GameTools.SizeValue((string?)e["size"]);
                    c.TakeDamage(n);
                    Add(report, LogEntry.Line(LogKind.Event, c.Name, $"{c.Name} は {n} の傷を負った{(string.IsNullOrWhiteSpace((string?)e["cause"]) ? "" : "（" + (string?)e["cause"] + "）")}。HP {c.Hp}/{c.MaxHp}{(c.State == LifeState.Down ? "、倒れた" : "")}"));
                }
            foreach (var e in Entries(a, "heal"))
                if (Find((string?)e["character"]) is Character c && c.State != LifeState.Dead)
                {
                    c.Heal(GameTools.SizeValue((string?)e["size"]));
                    Add(report, LogEntry.Line(LogKind.Event, c.Name, $"{c.Name} の HP が {c.Hp}/{c.MaxHp} に回復した。"));
                }
            foreach (var e in Entries(a, "items_gained").Take(3))
                if (Find((string?)e["character"]) is Character c && (string?)e["item"] is string item && item.Length > 0)
                {
                    c.Items.Add(item);
                    Add(report, LogEntry.Line(LogKind.Event, c.Name, $"{c.Name} は「{item}」を手に入れた。"));
                }
            foreach (var e in Entries(a, "items_lost"))
                if (Find((string?)e["character"]) is Character c && (string?)e["item"] is string item && c.Items.Remove(item))
                    Add(report, LogEntry.Line(LogKind.Event, c.Name, $"{c.Name} は「{item}」を手放した。"));
            foreach (var id in (a["clues_found"] as JArray)?.Select(x => (string?)x) ?? Enumerable.Empty<string?>())
                if (id != null && Scenario.Clues.Any(c => c.Id == id) && !State.Clues.Contains(id))
                {
                    State.Clues.Add(id);
                    Add(report, LogEntry.Line(LogKind.Event, "", "手がかり: " + Scenario.Clues.First(c => c.Id == id).Text));
                }
            // One act at most per turn: "next act" and "moved into the next act's place" usually mean the same step.
            var actBefore = State.Act;
            if (a["next_act"]?.Type == JTokenType.Boolean && (bool)a["next_act"]! && State.Act < Scenario.Acts.Count)
                NextAct(report);
            var move = (string?)a["move_to"];
            if (move != null && move != "none" && move != State.Location && Scenario.Locations.Any(l => l.Id == move))
                MoveTo(move, report, raiseAct: State.Act == actBefore);

            var enemies = ((a["start_combat"] as JArray)?.Select(x => (string?)x).Where(x => x != null && Db.Enemies.ContainsKey(x!)).ToList()
                ?? new List<string?>())!;
            if (enemies.Count > 0 && !InCombat) await StartCombatAsync(enemies!, report, ct);

            var end = (string?)a["end_story"];
            if (end == "success" || end == "bitter" || end == "failure") await EndAsync(end, report, ct);
            else if (Party.All(p => !p.CanAct)) await EndAsync("failure", report, ct);
        }

        static IEnumerable<JObject> Entries(JObject args, string key) => (args[key] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

        void MoveTo(string id, TurnReport report, bool raiseAct = true)
        {
            State.Location = id;
            if (!State.Visited.Contains(id)) State.Visited.Add(id);
            var loc = Here;
            Add(report, LogEntry.Line(LogKind.Event, "", $"場所: {loc.Name}"));
            if (raiseAct && loc.Act > State.Act) NextAct(report);
        }

        void NextAct(TurnReport report)
        {
            State.Act++;
            State.ActStartedAt = State.PlayMinutes;
            foreach (var p in Party.Where(p => p.CanAct)) p.RestoreFocus(p.MaxFocus);
            var act = Scenario.Acts.FirstOrDefault(x => x.N == State.Act);
            Add(report, LogEntry.Line(LogKind.Event, "", $"第{State.Act}幕「{act?.Title}」（気力が回復した）"));
            // Growth at the end of acts 1 and 2: everyone alive picks an ability +1 or more max HP.
            foreach (var p in Party.Where(p => p.State != LifeState.Dead && !State.PendingGrowth.Contains(p.Name))) State.PendingGrowth.Add(p.Name);
        }

        public const int ToughenAmount = 3;

        /// <summary>Applies a growth choice: a stat label (力/技/知/心) or "hp".</summary>
        public bool Grow(string character, string choice)
        {
            var who = Find(character);
            if (who == null || !State.PendingGrowth.Remove(character)) return false;
            string text;
            if (choice == "hp") { who.Toughen(ToughenAmount); text = $"最大 HP +{ToughenAmount}（{who.MaxHp}）"; }
            else if (choice.StartsWith("card:") && GrowthCards(character).FirstOrDefault(c => c.Id == choice.Substring(5)) is CardDef learned) { who.Cards.Add(learned.Id); text = $"新しい技「{learned.Name}」"; }
            else if (RuleNames.TryParseStat(choice, out var stat)) { who.Improve(stat); text = $"{stat.Label()} +1（+{who.Modifier(stat)}）"; }
            else { State.PendingGrowth.Add(character); return false; }
            var entry = LogEntry.Line(LogKind.Event, who.Name, $"{who.Name} は成長した: {text}");
            State.Log.Add(entry);
            entry.Seq = State.Log.Count;
            Logged?.Invoke(entry);
            return true;
        }

        async Task EndAsync(string ending, TurnReport report, CancellationToken ct)
        {
            if (Ended) return;
            State.Ending = ending;
            State.Combat = null;
            var label = ending == "success" ? "成功" : ending == "bitter" ? "苦い成功" : "失敗";
            Add(report, LogEntry.Line(LogKind.Event, "", $"物語の結末: {label}"));
            var text = ending == "success" ? Scenario.Endings.Success : ending == "bitter" ? Scenario.Endings.Bitter : Scenario.Endings.Failure;
            var final = await Gm.NarrateAsync(Status() + $"\n物語は「{label}」の結末を迎えた（{text}）。冒険者一人ひとりの行いに触れながら、" +
                "この夜の物語の締めくくりを語り部として 6〜8 文で語ってください。行動案は空でよい。", highlight: true, ct);
            Finish(report, final);
        }

        // ---------------------------------------------------------------- combat

        async Task StartCombatAsync(List<string> enemyIds, TurnReport report, CancellationToken ct)
        {
            var combat = new CombatState { FleeTo = State.Visited.Count > 1 ? State.Visited[State.Visited.Count - 2] : State.Location };
            // Enemies are written for three adventurers; fewer face less of them (a lone hero against a full-strength boss lost 9 times in 10).
            var scale = Teller.EnemyHpScale * PartyScale;
            foreach (var group in enemyIds.Take(4).GroupBy(id => id))
            {
                int i = 0, count = group.Count();
                foreach (var id in group)
                {
                    var d = Db.Enemies[id];
                    i++;
                    combat.Enemies.Add(new EnemyInstance
                    {
                        Uid = id + i, DefId = id, Name = count > 1 ? $"{d.Name}{(char)('A' + i - 1)}" : d.Name, Art = d.Art,
                        Hp = Math.Max(2, (int)Math.Ceiling(d.Hp * scale)), MaxHp = Math.Max(2, (int)Math.Ceiling(d.Hp * scale)), Defense = d.Defense, Boss = d.Boss, Undead = d.Undead,
                    });
                }
            }
            State.Combat = combat;
            Add(report, LogEntry.Line(LogKind.Event, "", "戦闘開始: " + string.Join("・", combat.Enemies.Select(e => e.Name))));
            if (combat.Enemies.Any(e => e.Boss))
            {
                // The boss's entrance is a big moment: the strong model describes it.
                var entrance = await Gm.NarrateAsync(Status() + "\nボスが姿を現した。その登場を、語り部として 4〜6 文で迫力をもって語ってください。", highlight: true, ct);
                Finish(report, entrance);
            }
        }

        async Task CombatRoundAsync(List<Declaration> decls, TurnReport report, CancellationToken ct)
        {
            var combat = State.Combat!;
            var summary = new List<string>();
            var dodgeBonus = new Dictionary<string, int>();
            var guards = new Dictionary<string, string>();   // protected name -> guardian name
            _taunter = null;

            // Downed characters roll to stay alive.
            foreach (var c in Party.Where(p => p.State == LifeState.Down).ToList())
            {
                var face = Dice.D20(_rng);
                if (face < DeathSaves.HoldOn && ChooseRerolls != null && c.FateCoins > 0)
                {
                    // A failing death roll may be rerolled with the fate coin, like any other roll.
                    var first = new LogEntry { Kind = LogKind.Roll, Who = c.Name, Text = $"{c.Name} 死のサイコロ {face} — このままでは失敗", Natural = face, Total = face, Target = DeathSaves.HoldOn, Outcome = "失敗" };
                    Add(report, first);
                    var pick = await ChooseRerolls(new[] { first });
                    if (pick.Contains(first) && c.SpendFateCoin()) face = Dice.D20(_rng);
                }
                var outcome = DeathSaves.Apply(c, face);
                var text = outcome switch
                {
                    DeathSaveOutcome.StandsUp => "自力で立ち上がった（HP 1）",
                    DeathSaveOutcome.Holds => "持ちこたえた",
                    DeathSaveOutcome.Died => "息絶えた",
                    _ => $"失敗（{c.DeathSaveFailures}/3）",
                };
                Add(report, new LogEntry { Kind = LogKind.Roll, Who = c.Name, Text = $"{c.Name} 死のサイコロ {face} — {text}", Natural = face, Total = face, Target = DeathSaves.HoldOn, Outcome = text });
                summary.Add($"{c.Name} は倒れたまま死のサイコロを振り、{text}。");
            }

            // Free-text actions go through the AI (check + effect size); cards and basic actions are pure rules.
            var free = decls.Where(d => d.CardId == null && d.Basic == null && d.Text.Trim().Length > 0).ToList();
            if (free.Count > 0)
            {
                Gm.PlanTool = GameTools.CombatPlan(Party, combat.Enemies.Where(e => e.Alive).Select(e => e.Uid));
                var text = new StringBuilder(Status());
                text.AppendLine("【戦闘中の自由な行動】");
                foreach (var d in free) text.AppendLine($"{d.Character}: {d.Text}");
                var planned = await Gm.AskPlanAsync(text.ToString(), ct: ct);
                Gm.AnswerPlan(await ResolveFreeCombat(planned, report, summary));
                Gm.PlanTool = GmTools.PlanChecksTool(Party);
            }

            foreach (var d in decls.Where(d => !free.Contains(d)).OrderByDescending(d => Find(d.Character)?.Modifier(Stat.Skill) ?? 0))
            {
                var who = Find(d.Character);
                if (who == null) continue;
                if (d.Basic == "revive") { var notes = new List<string>(); Revive(who, d.Target, report, notes); summary.AddRange(notes); continue; }
                if (!who.CanAct) continue;
                if (d.Basic == "flee")
                {
                    var r = await Shown(Checks.RollAgainst(who, Stat.Skill, 13, "逃げる", _rng, dodgeBonus.TryGetValue(who.Name, out var sb) ? sb : 0), report);
                    if (r.Succeeded)
                    {
                        summary.Add($"{who.Name} の合図で一行は戦いから逃げ出した（{Scenario.Location(combat.FleeTo).Name}へ）。");
                        State.Combat = null;
                        MoveTo(combat.FleeTo, report);
                        break;
                    }
                    summary.Add($"{who.Name} は逃げ道を探したが、敵に回り込まれた。");
                    continue;
                }
                if (d.CardId != null && Db.Cards.TryGetValue(d.CardId, out var card)) await UseCombatCard(who, card, d.Target, report, summary, dodgeBonus, guards);
                else if (d.Basic == "defend") { dodgeBonus[who.Name] = (dodgeBonus.TryGetValue(who.Name, out var b) ? b : 0) + 3; summary.Add($"{who.Name} は身構えた（かわす +3）。"); }
                else if (d.Basic == "help" && Find(d.Target) is Character ally) { ally.NextCheckBonus += Checks.HelpBonusEach; summary.Add($"{who.Name} は {ally.Name} を援護した（次の判定 +2）。"); }
                else await Attack(who, d.Target, report, summary);
            }

            if (State.Combat == null) { await NarrateRoundAsync(summary, report, ct); return; }
            if (combat.Enemies.All(e => !e.Alive)) { await WinAsync(summary, report, ct); return; }

            await EnemyPhase(combat, report, summary, dodgeBonus, guards);
            combat.Round++;
            await NarrateRoundAsync(summary, report, ct);
            if (Party.All(p => !p.CanAct)) await EndAsync("failure", report, ct);
        }

        async Task<string> ResolveFreeCombat(IReadOnlyList<JObject> entries, TurnReport report, List<string> summary)
        {
            var combat = State.Combat!;
            var lines = new List<string>();
            foreach (var e in entries.Take(Party.Count))
            {
                var who = Find((string?)e["character"]);
                if (who == null || !who.CanAct) continue;
                bool success = true;
                if (!(e["needs_check"]?.Type == JTokenType.Boolean && !(bool)e["needs_check"]!))
                {
                    if (!RuleNames.TryParseStat((string?)e["stat"], out var stat)) stat = Stat.Skill;
                    RuleNames.TryParseDifficulty((string?)e["difficulty"], out var diff);
                    var r = await Shown(Checks.Roll(new CheckRequest(who.Name, stat, diff, (string?)e["reason"] ?? ""), who, _rng), report);
                    success = r.Succeeded;
                    lines.Add($"- {r.Describe()}");
                }
                var target = combat.Enemies.FirstOrDefault(x => x.Uid == (string?)e["target"] && x.Alive);
                var effect = (string?)e["effect"];
                if (!success || target == null || effect == null || effect == "none")
                {
                    summary.Add($"{who.Name} の「{(string?)e["reason"]}」は{(success ? "成功した" : "うまくいかなかった")}。");
                    continue;
                }
                if (effect == "damage")
                {
                    var n = GameTools.SizeValue((string?)e["size"]);
                    Hurt(target, n);
                    summary.Add($"{who.Name} の「{(string?)e["reason"]}」で {target.Name} に {n} ダメージ（{HpText(target)}）。");
                }
                else if (effect == "stun")
                {
                    if (target.Boss && combat.WeaknessUsed) summary.Add($"{target.Name} はもう同じ手には乗らない。");
                    else
                    {
                        if (target.Boss) combat.WeaknessUsed = true;
                        target.Stunned = 1;
                        summary.Add($"{who.Name} の「{(string?)e["reason"]}」で {target.Name} の動きが止まった（1 手番）。");
                    }
                }
            }
            return lines.Count == 0 ? "判定なし。" : "判定の結果:\n" + string.Join("\n", lines);
        }

        async Task UseCombatCard(Character who, CardDef card, string? target, TurnReport report, List<string> summary,
            Dictionary<string, int> dodgeBonus, Dictionary<string, string> guards)
        {
            if (!who.SpendFocus(card.Cost)) { summary.Add($"{who.Name} は気力が足りず「{card.Name}」を使えなかった。代わりに{Weapon(who)}で攻撃する。"); await Attack(who, target, report, summary); return; }
            var combat = State.Combat!;
            switch (card.Kind)
            {
                case "attack":
                {
                    var targets = card.Target == "all" ? combat.Enemies.Where(e => e.Alive).ToList()
                        : new List<EnemyInstance> { PickEnemy(target) }.Where(e => e != null).Select(e => e!).ToList();
                    // A card with no base damage only hurts the undead (e.g. turning the dead).
                    if (card.Damage == 0 && card.UndeadBonus > 0) targets = targets.Where(e => e.Undead).ToList();
                    if (targets.Count == 0) { summary.Add($"{who.Name} の「{card.Name}」は効く相手がいなかった。"); break; }
                    foreach (var e in targets)
                        for (int h = 0; h < Math.Max(1, card.Hits) && e.Alive; h++)
                        {
                            bool hit = card.AutoHit, crit = false;
                            if (!hit)
                            {
                                RuleNames.TryParseStat(card.Stat, out var st);
                                var r = await Shown(Checks.RollAgainst(who, st, e.Defense, $"{card.Name} → {e.Name}", _rng), report);
                                hit = r.Succeeded; crit = r.Outcome == Outcome.Critical;
                            }
                            if (!hit) { summary.Add($"{who.Name} の「{card.Name}」は {e.Name} に当たらなかった。"); continue; }
                            var dmg = Math.Max(1, card.Damage + (e.Undead ? card.UndeadBonus : 0)) * (crit ? 2 : 1);
                            Hurt(e, dmg);
                            summary.Add($"{who.Name} の「{card.Name}」が {e.Name} に {dmg} ダメージ{(crit ? "（大成功）" : "")}（{HpText(e)}）。");
                            if (card.StunOnHit && e.Alive && !e.Boss) { e.Stunned = 1; summary.Add($"{e.Name} はよろめいて動けない。"); }
                        }
                    break;
                }
                case "heal":
                {
                    foreach (var ally in HealTargets(card, who, target))
                    {
                        ally.Heal(card.Amount);
                        summary.Add($"{who.Name} の「{card.Name}」で {ally.Name} の HP が {ally.Hp}/{ally.MaxHp} に。");
                    }
                    break;
                }
                case "ward":
                {
                    var allies = card.Target == "party" ? Party.Where(p => p.CanAct).ToList()
                        : card.Target == "self" ? new List<Character> { who } : new List<Character> { Find(target) ?? who };
                    foreach (var a in allies) dodgeBonus[a.Name] = (dodgeBonus.TryGetValue(a.Name, out var b) ? b : 0) + card.Bonus;
                    summary.Add($"{who.Name} の「{card.Name}」: {string.Join("・", allies.Select(a => a.Name))} のかわす判定 +{card.Bonus}。");
                    break;
                }
                case "taunt":
                    _taunter = who.Name;
                    dodgeBonus[who.Name] = (dodgeBonus.TryGetValue(who.Name, out var tb) ? tb : 0) + card.Bonus;
                    summary.Add($"{who.Name} は敵を挑発し、攻撃を自分に引きつけた。");
                    break;
                case "guard":
                {
                    var ally = Find(target);
                    if (ally != null && ally != who) { guards[ally.Name] = who.Name; summary.Add($"{who.Name} は {ally.Name} の前に立ちはだかった。"); }
                    else { dodgeBonus[who.Name] = (dodgeBonus.TryGetValue(who.Name, out var b) ? b : 0) + 3; summary.Add($"{who.Name} は盾を構えた（かわす +3）。"); }
                    break;
                }
                case "bless":
                    foreach (var t in card.Target == "party" ? Party.Where(p => p.CanAct) : new[] { Find(target) ?? who }) t.NextCheckBonus += card.Bonus;
                    summary.Add($"{who.Name} の「{card.Name}」: {(card.Target == "party" ? "仲間全員" : (Find(target) ?? who).Name)}の次の判定 +{card.Bonus}。");
                    break;
                case "stun":
                {
                    var e = PickEnemy(target);
                    if (e == null) break;
                    if (e.Boss) { summary.Add($"{who.Name} の「{card.Name}」は {e.Name} には効かなかった。"); break; }
                    RuleNames.TryParseStat(card.Stat, out var st);
                    var r = await Shown(Checks.RollAgainst(who, st, e.Defense, $"{card.Name} → {e.Name}", _rng), report);
                    if (r.Succeeded) { e.Stunned = 1; summary.Add($"{who.Name} の「{card.Name}」で {e.Name} は眠りに落ちた。"); }
                    else summary.Add($"{e.Name} は「{card.Name}」を振り払った。");
                    break;
                }
                case "smoke":
                    foreach (var p in Party.Where(p => p.CanAct)) dodgeBonus[p.Name] = (dodgeBonus.TryGetValue(p.Name, out var b) ? b : 0) + 4;
                    summary.Add($"{who.Name} の「{card.Name}」で辺りが煙に包まれた（仲間全員かわす +4）。");
                    break;
                default:
                    who.NextCheckBonus += 3;
                    summary.Add($"{who.Name} は「{card.Name}」を使った。");
                    break;
            }
        }

        /// <summary>This round, the character every enemy attacks (the warrior's taunt).</summary>
        string? _taunter;

        /// <summary>Who a healing card reaches: itself, the whole living party, or one ally (downed allies stand up).</summary>
        List<Character> HealTargets(CardDef card, Character who, string? target) => card.Target switch
        {
            "self" => new List<Character> { who },
            "party" => Party.Where(p => p.State != LifeState.Dead).ToList(),
            _ => new List<Character> { Find(target) is Character t && t.State != LifeState.Dead ? t : who },
        };

        /// <summary>Growth: up to two cards of the character's class not learned yet (they change with each act).</summary>
        public List<CardDef> GrowthCards(string character)
        {
            var who = Find(character);
            if (who == null) return new List<CardDef>();
            var unknown = Db.Cards.Values.Where(c => c.ClassId == who.ClassId && !who.Cards.Contains(c.Id)).ToList();
            if (unknown.Count <= 2) return unknown;
            var start = (State.Act * 2 + who.Name.Length) % unknown.Count;
            return new List<CardDef> { unknown[start], unknown[(start + 1) % unknown.Count] };
        }

        /// <summary>Logs a roll; a failed one may be rerolled with the character's fate coin (the UI asks the players).</summary>
        async Task<CheckResult> Shown(CheckResult r, TurnReport report)
        {
            var entry = RollEntry(r);
            Add(report, entry);
            if (ChooseRerolls == null || !CanReroll(r)) return r;
            var pick = await ChooseRerolls(new[] { entry });
            if (!pick.Contains(entry)) return r;
            var again = Checks.Reroll(r, Find(r.Request.CharacterName)!, _rng);
            if (again == null) return r;
            Add(report, RollEntry(again));
            return again;
        }

        async Task Attack(Character who, string? target, TurnReport report, List<string> summary)
        {
            var e = PickEnemy(target);
            if (e == null) return;
            var cls = Db.Classes[who.ClassId];
            RuleNames.TryParseStat(cls.AttackStat, out var stat);
            var r = await Shown(Checks.RollAgainst(who, stat, e.Defense, $"{Weapon(who)} → {e.Name}", _rng), report);
            if (!r.Succeeded) { summary.Add($"{who.Name} の{Weapon(who)}は {e.Name} に{(r.Outcome == Outcome.Fumble ? "大きく空を切った" : "かわされた")}。"); return; }
            var dmg = cls.WeaponDamage * (r.Outcome == Outcome.Critical ? 2 : 1);
            Hurt(e, dmg);
            summary.Add($"{who.Name} の{Weapon(who)}が {e.Name} に {dmg} ダメージ{(r.Outcome == Outcome.Critical ? "（大成功）" : "")}（{HpText(e)}）。");
        }

        async Task EnemyPhase(CombatState combat, TurnReport report, List<string> summary, Dictionary<string, int> dodgeBonus, Dictionary<string, string> guards)
        {
            foreach (var e in combat.Enemies.Where(e => e.Alive).ToList())
            {
                if (e.Stunned > 0) { e.Stunned--; summary.Add($"{e.Name} は動けない。"); continue; }
                var def = Db.Enemies[e.DefId];
                var big = def.BigMove;
                if (big != null && combat.BigMovePending == e.Uid)
                {
                    combat.BigMovePending = null;
                    summary.Add($"{e.Name} の「{big.Name}」！");
                    foreach (var p in Party.Where(p => p.CanAct).ToList()) await EnemyHits(e, big.Name, big.Dc, big.Damage, p, report, summary, dodgeBonus, guards);
                    continue;
                }
                var attack = def.Attacks.Count == 1 ? def.Attacks[0] : def.Attacks[_rng.Next(0, def.Attacks.Count)];
                var target = _taunter != null && Find(_taunter) is Character t && t.CanAct ? t : PickTarget(def.Pattern);
                if (target != null) await EnemyHits(e, attack.Name, attack.Dc, attack.Damage, target, report, summary, dodgeBonus, guards);
                if (big != null && big.Every > 0 && (combat.Round + 1) % big.Every == 0)
                {
                    combat.BigMovePending = e.Uid;
                    Add(report, LogEntry.Line(LogKind.Warning, e.Name, big.Warning + "（次の手番に大技）"));
                    summary.Add(big.Warning + "（次の手番に大技「" + big.Name + "」が来る。全員を狙う）");
                }
            }
        }

        async Task EnemyHits(EnemyInstance e, string attack, int dc, int damage, Character target, TurnReport report, List<string> summary,
            Dictionary<string, int> dodgeBonus, Dictionary<string, string> guards)
        {
            // A guardian steps in front and takes the blow (1 less damage), but cannot dodge for the protected one.
            if (guards.TryGetValue(target.Name, out var gName) && Find(gName) is Character guardian && guardian.CanAct)
            {
                guardian.TakeDamage(Math.Max(0, damage - 1));
                summary.Add($"{e.Name} の{attack}を {guardian.Name} が {target.Name} の代わりに受けた（{damage - 1} ダメージ、HP {guardian.Hp}/{guardian.MaxHp}）。");
                return;
            }
            var r = await Shown(Checks.RollAgainst(target, Stat.Skill, dc + Teller.EnemyAttackBonus, $"{e.Name}の{attack}をかわす", _rng, dodgeBonus.TryGetValue(target.Name, out var b) ? b : 0), report);
            if (r.Succeeded) { summary.Add($"{target.Name} は {e.Name} の{attack}をかわした。"); return; }
            var dmg = damage * (r.Outcome == Outcome.Fumble ? 2 : 1);
            target.TakeDamage(dmg);
            summary.Add($"{e.Name} の{attack}が {target.Name} に {dmg} ダメージ（HP {target.Hp}/{target.MaxHp}{(target.State == LifeState.Down ? "、倒れた" : "")}）。");
        }

        async Task WinAsync(List<string> summary, TurnReport report, CancellationToken ct)
        {
            var bossDown = State.Combat!.Enemies.Any(e => e.Boss);
            State.Combat = null;
            summary.Add(bossDown ? "ボスを倒した。戦いは終わった。" : "敵をすべて倒した。戦いは終わった。");
            Add(report, LogEntry.Line(LogKind.Event, "", "勝利"));
            await NarrateRoundAsync(summary, report, ct, highlight: bossDown);
        }

        async Task NarrateRoundAsync(List<string> summary, TurnReport report, CancellationToken ct, bool highlight = false)
        {
            // The players see the rule results too (who hit whom for how much), before the storyteller's telling.
            foreach (var line in summary.Where(l => !l.Contains("死のサイコロ"))) Add(report, LogEntry.Line(LogKind.Event, "", line));
            var text = Status() + "\n【この手番に起きたこと（ゲームが決めた結果）】\n" + string.Join("\n", summary) +
                "\nこの結果のとおりに、戦いの一手番を短く迫力をもって語ってください（HP・ダメージなどの数字やゲームの言葉は書かず、傷や疲れの様子で描く）。";
            var turn = await Gm.NarrateAsync(text, highlight, ct);
            Finish(report, turn);
            // Effects other than combat (an ending, items) still apply after a fight.
            if (!InCombat) await AfterNarrationAsync(report, turn, ct);
        }

        // ---------------------------------------------------------------- helpers

        EnemyInstance? PickEnemy(string? uid)
        {
            var alive = State.Combat?.Enemies.Where(e => e.Alive).ToList() ?? new List<EnemyInstance>();
            return alive.FirstOrDefault(e => e.Uid == uid) ?? alive.FirstOrDefault();
        }

        Character? PickTarget(string pattern)
        {
            var targets = Party.Where(p => p.CanAct).ToList();
            if (targets.Count <= 1) return targets.FirstOrDefault();
            return pattern switch
            {
                "weakest" => targets.OrderBy(p => p.Hp).First(),
                "strongest" => targets.OrderByDescending(p => p.Hp).First(),
                _ => targets[_rng.Next(0, targets.Count)],
            };
        }

        /// <summary>Enemy HP for the party's size: 1 for three, 0.67 for two, 0.45 for one (the dead don't count).</summary>
        public double PartyScale => Math.Max(0.45, Party.Count(p => p.State != LifeState.Dead) / 3.0);

        static void Hurt(EnemyInstance e, int n) { e.Hp = Math.Max(0, e.Hp - n); }
        static string HpText(EnemyInstance e) => e.Alive ? $"残り {e.Hp}/{e.MaxHp}" : "倒れた";
        string Weapon(Character c) => Db.Classes[c.ClassId].Weapon;

        public Character? Find(string? name) => name == null ? null : Party.FirstOrDefault(p => p.Name == name);

        string DescribeDeclaration(Declaration d)
        {
            var parts = new List<string>();
            if (d.CardId != null && Db.Cards.TryGetValue(d.CardId, out var card)) parts.Add($"「{card.Name}」を使う");
            if (d.Basic != null) parts.Add(d.Basic switch
            {
                "attack" => $"{(Find(d.Character) is Character c ? Weapon(c) : "武器")}で攻撃", "defend" => "身を守る", "help" => "仲間を援護",
                "revive" => "倒れた仲間を起こす", "flee" => "逃げる", _ => d.Basic,
            });
            var target = State.Combat?.Enemies.FirstOrDefault(e => e.Uid == d.Target)?.Name ?? (Find(d.Target)?.Name);
            if (target != null && parts.Count > 0) parts[parts.Count - 1] += $"（{target}）";
            if (d.Text.Trim().Length > 0) parts.Add(d.Text.Trim());
            return string.Join("。", parts);
        }

        LogEntry RollEntry(CheckResult r) => new LogEntry
        {
            Kind = LogKind.Roll, Who = r.Request.CharacterName, Text = r.Describe(), Natural = r.Natural, Total = r.Total, Target = r.Dc,
            Reason = r.Request.Reason,
            Outcome = r.Outcome switch { Outcome.Critical => "大成功", Outcome.Success => "成功", Outcome.Failure => "失敗", _ => "大失敗" },
        };

        void Add(TurnReport report, LogEntry e)
        {
            State.Log.Add(e);
            e.Seq = State.Log.Count;
            report.Entries.Add(e);
            Logged?.Invoke(e);
        }

        void Finish(TurnReport report, GmTurn turn)
        {
            if (turn.Narration.Length > 0) Add(report, LogEntry.Line(LogKind.Narration, Teller.Name, turn.Narration));
            report.Narration = turn.Narration;
            report.Model = turn.Model;
            var suggestions = KnownOnly(turn.Suggestions);
            if (turn.Suggestions.Count > 0) { report.Suggestions = suggestions; State.Suggestions = suggestions; }
        }

        /// <summary>Words a suggestion may only use once the story has said them (a hint like "prepare to fight the undead" gives away what is ahead).</summary>
        static readonly string[] Telling = { "罠", "アンデッド", "不死", "待ち伏せ", "ボス", "亡霊", "幽霊", "死霊", "呪い", "隠し扉", "隠し通路", "裏切" };

        /// <summary>
        /// Drops suggested actions that name a foe or a danger the players haven't met: every enemy name and the words above must
        /// already appear in what was told or shown (the log, the current fight). The AI is told the same; this catches what it lets slip.
        /// </summary>
        List<string> KnownOnly(IEnumerable<string> suggestions)
        {
            var known = string.Join("\n", State.Log.Select(e => e.Text)) + string.Join("", State.Combat?.Enemies.Select(e => e.Name) ?? Enumerable.Empty<string>());
            var words = Db.Enemies.Values.Select(e => e.Name).Concat(Telling).Distinct().ToList();
            return suggestions.Where(s => !words.Any(w => s.Contains(w) && !known.Contains(w))).ToList();
        }

        /// <summary>The compact state the AI sees at the top of each request.</summary>
        public string Status()
        {
            var sb = new StringBuilder();
            var act = Scenario.Acts.FirstOrDefault(a => a.N == State.Act);
            var inAct = State.PlayMinutes - State.ActStartedAt;
            sb.AppendLine($"【状況】第{State.Act}幕「{act?.Title}」目標: {act?.Goal}。この幕 {inAct:0} 分経過（目安 {act?.Minutes} 分）、全体 {State.PlayMinutes:0} 分。");
            if (act != null && inAct > act.Minutes) sb.AppendLine("時間が押している。この幕の目標に向かう展開へ寄せ、ほどよいところで次の幕へ進めること。");
            var here = Here;
            sb.AppendLine($"場所: {here.Name}（{here.Id}）。行き先: {string.Join("・", here.Exits.Select(x => $"{Scenario.Location(x).Name}（{x}）"))}");
            sb.AppendLine("見つけた手がかり: " + (State.Clues.Count == 0 ? "なし" : string.Join(" / ", State.Clues.Select(id => Scenario.Clues.First(c => c.Id == id).Text))));
            foreach (var p in Party)
            {
                var st = p.State == LifeState.Dead ? "【死亡】" : p.State == LifeState.Down ? "【倒れている】" : "";
                sb.AppendLine($"{p.Name}（{Prompts.ClassLabel(p.ClassId)}）HP {p.Hp}/{p.MaxHp} 気力 {p.Focus}/{p.MaxFocus}{st}{(p.Items.Count > 0 ? " 持ち物: " + string.Join("・", p.Items) : "")}");
            }
            if (State.Combat != null)
                sb.AppendLine("戦闘中（第" + State.Combat.Round + "手番）: " + string.Join("・", State.Combat.Enemies.Select(e => $"{e.Name}({e.Uid}) {(e.Alive ? $"HP {e.Hp}/{e.MaxHp}" : "倒れた")}")));
            if (State.NarrationLength == "short") sb.AppendLine("【語りの長さ: 短め】いつもの語りは 1〜2 文・80 字まで。");
            else if (State.NarrationLength == "long") sb.AppendLine("【語りの長さ: 長め】いつもの語りは 4〜6 文。情景をたっぷり描いてよい。");
            if (!string.IsNullOrWhiteSpace(StorytellerRequest))
            {
                sb.AppendLine($"【語り部への注文（物語の外からの頼み。これからの語りに反映する）】{StorytellerRequest}");
                StorytellerRequest = null;
            }
            return sb.ToString();
        }
    }
}
