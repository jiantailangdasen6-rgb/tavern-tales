#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tavern.Core.Ai;
using Tavern.Core.Play;

namespace Tavern.Core.Flow
{
    public enum RollKind { Check, Contest, Secret, Fate, Dodge, Attack, Initiative, DeathSave, Fear, Flee, Trap }

    /// <summary>A die a player rolls (09_Spec 4: players roll; all of a phase at once; 20 seconds then it rolls itself).</summary>
    public sealed class RollRequest
    {
        public string Who { get; set; } = "";
        public RollKind Kind { get; set; }
        /// <summary>"DEX・普通 13" and the reasons ("得意ではない: 1 段難しい").</summary>
        public string Heading { get; set; } = "";
        public List<string> Reasons { get; } = new List<string>();
        /// <summary>What the roll is for ("碑文を読む").</summary>
        public string For { get; set; } = "";
        /// <summary>Only the roller sees this die (secret actions).</summary>
        public bool Private { get; set; }
        /// <summary>The check behind it, when a veteran mark may be used (the UI calls UseMark before rolling).</summary>
        public CheckRoll? Check { get; set; }
        /// <summary>The modifier and target, for bots and the UI (natural + Bonus ≥ Target succeeds).</summary>
        public int Bonus { get; set; }
        public int Target { get; set; }
        /// <summary>A fate coin may roll this again (not for the fate die).</summary>
        public bool CoinAllowed { get; set; } = true;
        /// <summary>Important to the story (bots spend coins on these).</summary>
        public bool Important { get; set; }
    }

    /// <summary>
    /// The players' side of the table: the Unity screens, the online clients, or a bot. The game asks; the players answer.
    /// </summary>
    public interface IPlayers
    {
        /// <summary>Everyone's dice of one phase at once. Returns the naturals in order. Marks are used on the requests' checks beforehand.</summary>
        Task<IReadOnlyList<int>> RollAsync(IReadOnlyList<RollRequest> rolls, CancellationToken ct);

        /// <summary>Failed rolls whose owners still have a fate coin: returns which of them are rolled again.</summary>
        Task<IReadOnlyList<bool>> OfferCoinsAsync(IReadOnlyList<RollRequest> failed, CancellationToken ct);

        /// <summary>
        /// Declarations refused as impossible (reason and how): the players may declare again without losing the turn.
        /// Returns the new declarations (an empty text keeps them waiting this turn).
        /// </summary>
        Task<IReadOnlyList<Declaration>> RedeclareAsync(IReadOnlyList<(string who, string refusal)> refused, CancellationToken ct);
    }

    /// <summary>A bot at the table: rolls with the random source, uses marks when the odds are poor, coins on important failures.</summary>
    public sealed class BotPlayers : IPlayers
    {
        readonly IRandom _rng;
        readonly Func<string, Adventurer?> _find;
        public Func<string, string, string>? Redeclare { get; set; }
        public BotPlayers(IRandom rng, Func<string, Adventurer?> find) { _rng = rng; _find = find; }

        public Task<IReadOnlyList<int>> RollAsync(IReadOnlyList<RollRequest> rolls, CancellationToken ct)
        {
            foreach (var r in rolls)
                if (r.Check != null && r.Target - r.Bonus >= 15 && _find(r.Who) is Adventurer a && a.MarksLeft > 0)
                {
                    r.Check.UseMark(a);
                    r.Target = r.Check.Target;
                }
            return Task.FromResult<IReadOnlyList<int>>(rolls.Select(_ => _rng.D20()).ToList());
        }

        public Task<IReadOnlyList<bool>> OfferCoinsAsync(IReadOnlyList<RollRequest> failed, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<bool>>(failed.Select(r => r.Important || r.Kind == RollKind.DeathSave || r.Kind == RollKind.Dodge).ToList());

        public Task<IReadOnlyList<Declaration>> RedeclareAsync(IReadOnlyList<(string who, string refusal)> refused, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Declaration>>(refused.Select(x => new Declaration { Who = x.who, Text = Redeclare?.Invoke(x.who, x.refusal) ?? "" }).ToList());
    }
}
