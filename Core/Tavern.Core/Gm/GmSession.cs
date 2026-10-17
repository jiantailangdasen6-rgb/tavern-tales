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
    }

    /// <summary>
    /// Runs the conversation with the AI game master: players' declarations go in, the AI may call tools
    /// (checks are rolled by the game, not the AI), and the final narration comes out.
    /// </summary>
    public sealed class GmSession
    {
        const int MaxToolRounds = 6;

        readonly ILlmClient _llm;
        readonly IReadOnlyList<Character> _party;
        readonly IRandom _rng;
        readonly List<ChatMessage> _history = new List<ChatMessage>();
        readonly IReadOnlyList<ToolDef> _tools;

        /// <summary>Optional hook to roll checks elsewhere (the 3D dice in Unity). Defaults to Checks.Roll.</summary>
        public Func<CheckRequest, Character, CheckResult>? RollCheck { get; set; }

        public IReadOnlyList<ChatMessage> History => _history;

        public GmSession(ILlmClient llm, Storyteller teller, IReadOnlyList<Character> party, string scenario, IRandom rng)
        {
            _llm = llm; _party = party; _rng = rng;
            _tools = GmTools.For(party);
            _history.Add(new ChatMessage(Role.System, Prompts.System(teller, party, scenario)));
        }

        /// <summary>Sends the players' input (declarations, or "begin" for the opening) and returns the narrated result.</summary>
        public async Task<GmTurn> PlayAsync(string playerInput, CancellationToken ct = default)
        {
            var turn = new GmTurn();
            var narration = new StringBuilder();
            _history.Add(new ChatMessage(Role.User, playerInput));

            for (int round = 0; round < MaxToolRounds; round++)
            {
                var reply = await _llm.ChatAsync(_history, _tools, ct).ConfigureAwait(false);
                _history.Add(new ChatMessage(Role.Assistant, reply.Content, reply.ToolCalls));
                if (reply.Content.Length > 0) { if (narration.Length > 0) narration.Append('\n'); narration.Append(reply.Content); }
                if (reply.ToolCalls.Count == 0) break;

                bool needsAnswer = false;
                foreach (var call in reply.ToolCalls)
                {
                    var result = Handle(call, turn);
                    _history.Add(new ChatMessage(Role.Tool, result, toolName: call.Name));
                    if (call.Name != GmTools.SuggestActions) needsAnswer = true;
                }
                // Suggestions alone end the turn; a check result must be narrated, so ask again.
                if (!needsAnswer) break;
            }

            turn.Narration = narration.ToString().Trim();
            return turn;
        }

        string Handle(ToolCall call, GmTurn turn)
        {
            switch (call.Name)
            {
                case GmTools.RequestCheck:
                {
                    var name = (string?)call.Arguments["character"];
                    var who = _party.FirstOrDefault(c => c.Name == name);
                    if (who == null) return Reject(turn, $"「{name}」という冒険者はいない。いるのは {string.Join("・", _party.Select(c => c.Name))}。");
                    if (who.State != LifeState.Active) return Reject(turn, $"{who.Name} は倒れていて行動できない。");
                    if (!RuleNames.TryParseStat((string?)call.Arguments["stat"], out var stat))
                        return Reject(turn, "stat は 力・技・知・心 のどれか。");
                    RuleNames.TryParseDifficulty((string?)call.Arguments["difficulty"], out var difficulty);
                    var request = new CheckRequest(who.Name, stat, difficulty, (string?)call.Arguments["reason"] ?? "");
                    var result = RollCheck != null ? RollCheck(request, who) : Checks.Roll(request, who, _rng);
                    turn.Checks.Add(result);
                    return $"判定の結果: {result.Describe()}。この結果のとおりに語ること。";
                }
                case GmTools.SuggestActions:
                {
                    if (call.Arguments["actions"] is Newtonsoft.Json.Linq.JArray arr)
                        turn.Suggestions.AddRange(arr.Select(a => ((string?)a ?? "").Trim()).Where(a => a.Length > 0).Take(3));
                    return "行動案を表示した。";
                }
                default:
                    return Reject(turn, $"{call.Name} という道具はない。");
            }
        }

        static string Reject(GmTurn turn, string why) { turn.Rejected.Add(why); return "受け付けられない: " + why; }
    }
}
