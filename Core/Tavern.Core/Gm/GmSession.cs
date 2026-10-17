#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Tavern.Core.Rules;

namespace Tavern.Core.Gm
{
    /// <summary>What one exchange with the game master produced.</summary>
    public sealed class GmTurn
    {
        public string Narration { get; internal set; } = "";
        public List<CheckResult> Checks { get; } = new List<CheckResult>();
        public List<string> Suggestions { get; } = new List<string>();
        /// <summary>Plan entries the game refused (unknown character, downed...). Useful when tuning prompts.</summary>
        public List<string> Rejected { get; } = new List<string>();
        /// <summary>Which backend told this turn.</summary>
        public string Model { get; internal set; } = "";
        /// <summary>The full arguments of the narrate call (effects such as damage, items, moves are read by the game).</summary>
        public JObject NarrateArgs { get; internal set; } = new JObject();
    }

    /// <summary>
    /// The conversation with the AI game master. The game fixes the order of a turn:
    /// PlanAsync (forced plan_checks; the game resolves the plan) then NarrateAsync (forced narrate).
    /// </summary>
    public sealed class GmSession
    {
        readonly ILlmClient _llm;
        /// <summary>A stronger model for the big moments (opening, boss, ending); the free tier allows only a few calls a day.</summary>
        readonly ILlmClient? _highlightLlm;
        readonly IReadOnlyList<Character> _party;
        readonly IRandom _rng;
        readonly List<ChatMessage> _history = new List<ChatMessage>();

        /// <summary>The plan tool (defaults to GmTools.PlanChecksTool; the game may add combat fields).</summary>
        public ToolDef PlanTool { get; set; }
        /// <summary>The narrate tool (defaults to GmTools.NarrateTool; the game adds scenario effects).</summary>
        public ToolDef NarrateTool { get; set; } = GmTools.NarrateTool;

        public IReadOnlyList<ChatMessage> History => _history;

        public GmSession(ILlmClient llm, Storyteller teller, IReadOnlyList<Character> party, string scenario, IRandom rng, ILlmClient? highlightLlm = null)
            : this(llm, Prompts.System(teller, party, scenario), party, rng, highlightLlm) { }

        public GmSession(ILlmClient llm, string systemPrompt, IReadOnlyList<Character> party, IRandom rng, ILlmClient? highlightLlm = null,
            IEnumerable<ChatMessage>? restoredHistory = null)
        {
            _llm = llm; _highlightLlm = highlightLlm; _party = party; _rng = rng;
            PlanTool = GmTools.PlanChecksTool(party);
            if (restoredHistory != null) _history.AddRange(restoredHistory.Where(m => m.Role != Role.System));
            _history.Insert(0, new ChatMessage(Role.System, systemPrompt));
        }

        ILlmClient Pick(bool highlight) => highlight && _highlightLlm != null ? _highlightLlm : _llm;
        static string NameOf(ILlmClient llm) => llm is FallbackLlm f ? f.Current.Name : llm.Name;

        /// <summary>
        /// Sends the players' input and asks the AI which actions need checks (forced). The game resolves the entries
        /// (rolls dice, applies card effects) and returns the text the AI gets back as the tool result.
        /// </summary>
        public async Task<IReadOnlyList<JObject>> PlanAsync(string userText, Func<IReadOnlyList<JObject>, string> resolve,
            bool highlight = false, CancellationToken ct = default)
        {
            var entries = await AskPlanAsync(userText, highlight, ct).ConfigureAwait(false);
            AnswerPlan(resolve(entries));
            return entries;
        }

        List<ToolCall> _openPlanCalls = new List<ToolCall>();

        /// <summary>First half of PlanAsync: asks for the plan. The game must call AnswerPlan before anything else.</summary>
        public async Task<IReadOnlyList<JObject>> AskPlanAsync(string userText, bool highlight = false, CancellationToken ct = default)
        {
            var llm = Pick(highlight);
            _history.Add(new ChatMessage(Role.User, userText));
            var plan = await llm.ChatAsync(_history, new[] { PlanTool }, PlanTool.Name, ct).ConfigureAwait(false);
            _openPlanCalls = plan.ToolCalls.Where(c => c.Name == PlanTool.Name).ToList();
            _history.Add(new ChatMessage(Role.Assistant, plan.Content, _openPlanCalls, raw: plan.Raw));
            return _openPlanCalls.SelectMany(c => c.Arguments["actions"] is JArray a ? a.OfType<JObject>() : Enumerable.Empty<JObject>()).ToList();
        }

        /// <summary>Second half: the dice results (or other outcome) go back to the AI as the tool result.</summary>
        public void AnswerPlan(string answer)
        {
            foreach (var c in _openPlanCalls) _history.Add(new ChatMessage(Role.Tool, answer, toolName: c.Name));
            if (_openPlanCalls.Count == 0) _history.Add(new ChatMessage(Role.User, answer));   // a backend that ignored the tool
            _openPlanCalls = new List<ToolCall>();
        }

        /// <summary>Asks for the narration and suggestions (forced). userText adds facts first (e.g. a combat round summary).</summary>
        public async Task<GmTurn> NarrateAsync(string? userText, bool highlight = false, CancellationToken ct = default)
        {
            var turn = new GmTurn();
            await NarrateIntoAsync(turn, userText, highlight, ct).ConfigureAwait(false);
            return turn;
        }

        async Task NarrateIntoAsync(GmTurn turn, string? userText, bool highlight, CancellationToken ct)
        {
            var llm = Pick(highlight);
            if (userText != null) _history.Add(new ChatMessage(Role.User, userText));
            var reply = await llm.ChatAsync(_history, new[] { NarrateTool }, NarrateTool.Name, ct).ConfigureAwait(false);
            turn.Model = NameOf(llm);
            var narrate = reply.ToolCalls.FirstOrDefault(c => c.Name == NarrateTool.Name);
            if (narrate != null)
            {
                turn.NarrateArgs = narrate.Arguments;
                turn.Narration = ((string?)narrate.Arguments["text"] ?? "").Trim();
                if (narrate.Arguments["suggestions"] is JArray arr)
                    turn.Suggestions.AddRange(arr.Select(a => ((string?)a ?? "").Trim()).Where(a => a.Length > 0).Take(3));
                _history.Add(new ChatMessage(Role.Assistant, reply.Content, new[] { narrate }, raw: reply.Raw));
                _history.Add(new ChatMessage(Role.Tool, "語りを表示した。冒険者たちの次の宣言を待つ。", toolName: NarrateTool.Name));
            }
            else
            {
                // A backend that cannot be forced (local model) may answer in plain text.
                turn.Narration = reply.Content.Trim();
                _history.Add(new ChatMessage(Role.Assistant, reply.Content));
            }
        }

        /// <summary>A whole exploration turn with the default resolver (every planned check is rolled).</summary>
        public async Task<GmTurn> PlayAsync(string playerInput, bool highlight = false, CancellationToken ct = default)
        {
            var turn = new GmTurn();
            await PlanAsync(playerInput, entries => ResolveChecks(entries, turn), highlight, ct).ConfigureAwait(false);
            await NarrateIntoAsync(turn, null, highlight, ct).ConfigureAwait(false);
            return turn;
        }

        /// <summary>The opening of the evening: narration only, nobody has acted yet.</summary>
        public Task<GmTurn> OpenAsync(string request, bool highlight = true, CancellationToken ct = default)
            => NarrateAsync(request, highlight, ct);

        /// <summary>Rolls every planned check (at most one per character); returns the tool-result text for the AI.</summary>
        public string ResolveChecks(IReadOnlyList<JObject> entries, GmTurn turn, Func<Character, int>? extraBonus = null)
        {
            RollChecks(entries, turn, extraBonus);
            return ChecksAnswer(turn);
        }

        /// <summary>The tool-result text for the checks in a turn (after any rerolls).</summary>
        public static string ChecksAnswer(GmTurn turn)
        {
            var lines = turn.Rejected.Select(r => "受け付けられない: " + r)
                .Concat(turn.Checks.Select(r => $"- {r.Describe()}（{r.Request.Reason}）")).ToList();
            return turn.Checks.Count == 0 && turn.Rejected.Count == 0 ? "判定なし。宣言どおりに語ること。"
                : "判定の結果:\n" + string.Join("\n", lines) + "\nこの結果のとおりに語ること。";
        }

        /// <summary>Rolls every planned check (at most one per character) into turn.Checks.</summary>
        public void RollChecks(IReadOnlyList<JObject> entries, GmTurn turn, Func<Character, int>? extraBonus = null)
        {
            var done = new HashSet<string>();
            foreach (var c in entries.Take(_party.Count * 2))
            {
                if (c["needs_check"]?.Type == JTokenType.Boolean && !(bool)c["needs_check"]!) continue;
                var name = (string?)c["character"];
                var who = _party.FirstOrDefault(p => p.Name == name);
                if (who == null) { turn.Rejected.Add($"「{name}」という冒険者はいない。"); continue; }
                if (!who.CanAct) { turn.Rejected.Add($"{who.Name} は倒れていて行動できない。"); continue; }
                if (!done.Add(who.Name)) continue;
                if (!RuleNames.TryParseStat((string?)c["stat"], out var stat)) stat = Stat.Skill;
                RuleNames.TryParseDifficulty((string?)c["difficulty"], out var difficulty);
                var request = new CheckRequest(who.Name, stat, difficulty, (string?)c["reason"] ?? "");
                turn.Checks.Add(Checks.Roll(request, who, _rng, extraBonus: extraBonus?.Invoke(who) ?? 0));
            }
        }
    }
}
