#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tavern.Core.Rules;

namespace Tavern.Core.Gm
{
    /// <summary>What one exchange with the game master produced.</summary>
    public sealed class GmTurn
    {
        public string Narration { get; internal set; } = "";
        public List<CheckResult> Checks { get; } = new List<CheckResult>();
        public List<string> Suggestions { get; } = new List<string>();
        /// <summary>Tool calls the game refused (unknown character, bad arguments). Useful when tuning prompts.</summary>
        public List<string> Rejected { get; } = new List<string>();
        /// <summary>Which backend told this turn.</summary>
        public string Model { get; internal set; } = "";
    }

    /// <summary>
    /// Runs the conversation with the AI game master: players' declarations go in, the AI may call tools
    /// (checks are rolled by the game, not the AI), and the final narration comes out.
    /// </summary>
    public sealed class GmSession
    {
        readonly ILlmClient _llm;
        /// <summary>A stronger model for the big moments (opening, boss, ending); the free tier allows only a few calls a day.</summary>
        readonly ILlmClient? _highlightLlm;
        readonly IReadOnlyList<Character> _party;
        readonly IRandom _rng;
        readonly List<ChatMessage> _history = new List<ChatMessage>();
        readonly ToolDef _planTool;

        /// <summary>Optional hook to roll checks elsewhere (the 3D dice in Unity). Defaults to Checks.Roll.</summary>
        public Func<CheckRequest, Character, CheckResult>? RollCheck { get; set; }

        public IReadOnlyList<ChatMessage> History => _history;

        public GmSession(ILlmClient llm, Storyteller teller, IReadOnlyList<Character> party, string scenario, IRandom rng, ILlmClient? highlightLlm = null)
        {
            _llm = llm; _highlightLlm = highlightLlm; _party = party; _rng = rng;
            _planTool = GmTools.PlanChecksTool(party);
            _history.Add(new ChatMessage(Role.System, Prompts.System(teller, party, scenario)));
        }

        /// <summary>
        /// Sends the players' input (declarations, or the opening request) and returns the narrated result.
        /// highlight: a big moment, told by the highlight model when there is one. A whole turn stays on one model.
        /// </summary>
        public async Task<GmTurn> PlayAsync(string playerInput, bool highlight = false, CancellationToken ct = default)
            => await PlayAsync(playerInput, highlight, planChecks: true, ct).ConfigureAwait(false);

        /// <summary>The opening of the evening: narration only, nobody has acted yet.</summary>
        public async Task<GmTurn> OpenAsync(string request, bool highlight = true, CancellationToken ct = default)
            => await PlayAsync(request, highlight, planChecks: false, ct).ConfigureAwait(false);

        async Task<GmTurn> PlayAsync(string playerInput, bool highlight, bool planChecks, CancellationToken ct)
        {
            var turn = new GmTurn();
            var llm = highlight && _highlightLlm != null ? _highlightLlm : _llm;
            _history.Add(new ChatMessage(Role.User, playerInput));

            // 1. The game asks which actions need checks (forced, so nobody's declaration is skipped) and rolls them.
            if (planChecks)
            {
                var plan = await llm.ChatAsync(_history, new[] { _planTool }, GmTools.PlanChecks, ct).ConfigureAwait(false);
                var calls = plan.ToolCalls.Where(c => c.Name == GmTools.PlanChecks).ToList();
                _history.Add(new ChatMessage(Role.Assistant, plan.Content, calls, raw: plan.Raw));
                var results = calls.SelectMany(c => RollPlanned(c, turn)).ToList();
                var answer = results.Count == 0 ? "判定なし。宣言どおりに語ること。"
                    : "判定の結果:\n" + string.Join("\n", results) + "\nこの結果のとおりに語ること。";
                foreach (var c in calls) _history.Add(new ChatMessage(Role.Tool, answer, toolName: c.Name));
            }

            // 2. The narration and the next suggestions, always together.
            var reply = await llm.ChatAsync(_history, new[] { GmTools.NarrateTool }, GmTools.Narrate, ct).ConfigureAwait(false);
            turn.Model = llm is FallbackLlm fb ? fb.Current.Name : llm.Name;
            var narrate = reply.ToolCalls.FirstOrDefault(c => c.Name == GmTools.Narrate);
            if (narrate != null)
            {
                turn.Narration = ((string?)narrate.Arguments["text"] ?? "").Trim();
                if (narrate.Arguments["suggestions"] is Newtonsoft.Json.Linq.JArray arr)
                    turn.Suggestions.AddRange(arr.Select(a => ((string?)a ?? "").Trim()).Where(a => a.Length > 0).Take(3));
                _history.Add(new ChatMessage(Role.Assistant, reply.Content, new[] { narrate }, raw: reply.Raw));
                _history.Add(new ChatMessage(Role.Tool, "語りを表示した。冒険者たちの次の宣言を待つ。", toolName: GmTools.Narrate));
            }
            else
            {
                // A backend that cannot be forced (local model) may answer in plain text.
                turn.Narration = reply.Content.Trim();
                _history.Add(new ChatMessage(Role.Assistant, reply.Content, raw: null));
            }
            return turn;
        }

        /// <summary>Rolls every planned check (at most one per character per turn); returns one result line each.</summary>
        IEnumerable<string> RollPlanned(ToolCall call, GmTurn turn)
        {
            if (!(call.Arguments["actions"] is Newtonsoft.Json.Linq.JArray actions)) yield break;
            var done = new HashSet<string>();
            foreach (var c in actions.OfType<Newtonsoft.Json.Linq.JObject>().Take(_party.Count * 2))
            {
                if (c["needs_check"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && !(bool)c["needs_check"]!) continue;
                var name = (string?)c["character"];
                var who = _party.FirstOrDefault(p => p.Name == name);
                if (who == null) { yield return Reject(turn, $"「{name}」という冒険者はいない。"); continue; }
                if (who.State != LifeState.Active) { yield return Reject(turn, $"{who.Name} は倒れていて行動できない。"); continue; }
                if (!done.Add(who.Name)) continue;
                if (!RuleNames.TryParseStat((string?)c["stat"], out var stat)) stat = Stat.Skill;
                RuleNames.TryParseDifficulty((string?)c["difficulty"], out var difficulty);
                var request = new CheckRequest(who.Name, stat, difficulty, (string?)c["reason"] ?? "");
                var result = RollCheck != null ? RollCheck(request, who) : Checks.Roll(request, who, _rng);
                turn.Checks.Add(result);
                yield return $"- {result.Describe()}（{request.Reason}）";
            }
        }

        static string Reject(GmTurn turn, string why) { turn.Rejected.Add(why); return "受け付けられない: " + why; }
    }
}
