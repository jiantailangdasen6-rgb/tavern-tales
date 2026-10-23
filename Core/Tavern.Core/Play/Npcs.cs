#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>An NPC's attitude to the party (09_Spec 8). The game keeps it.</summary>
    public enum Attitude { Hostile, Wary, Neutral, Friendly, Trusting }

    /// <summary>How big a request is: small = ask the way or a rumour; normal = lend, introduce, tell; large = come along, take risks, tell a secret, betray.</summary>
    public enum RequestSize { Small, Normal, Large }

    /// <summary>How the request is made, which picks the ability.</summary>
    public enum Approach { Ask, Persuade, Threaten, Trick }

    /// <summary>Why an attitude changes (the AI picks one of these; the game applies the number).</summary>
    public enum AttitudeReason
    {
        /// <summary>命を救う +2</summary>
        SavedLife,
        /// <summary>望みを叶える +1</summary>
        GrantedWish,
        /// <summary>説得に成功 +1（大成功 +2、大失敗 -1）— applied by the check, not by the AI.</summary>
        Persuaded,
        /// <summary>贈り物 +1（1 人に 1 晩 1 回）</summary>
        Gift,
        /// <summary>嘘がばれる -1</summary>
        LieFound,
        /// <summary>脅す -1</summary>
        Threatened,
        /// <summary>大切な人を傷つける -2</summary>
        HurtLovedOne,
        /// <summary>攻撃する → 敵対</summary>
        Attacked,
        /// <summary>目の前で人を殺す → その場の NPC 全員 -1</summary>
        KilledBeforeThem,
    }

    /// <summary>Whether a story person comes along when asked.</summary>
    public enum JoinWillingness { Glad, Conditional, Never }

    public static class Npcs
    {
        public static string Label(this Attitude a) => a switch
        {
            Attitude.Hostile => "敵対", Attitude.Wary => "警戒", Attitude.Neutral => "中立", Attitude.Friendly => "友好", _ => "信頼",
        };

        public static Attitude Move(this Attitude a, int steps) => (Attitude)Math.Max(0, Math.Min(4, (int)a + steps));

        public static Ability AbilityFor(Approach how) => how switch
        {
            Approach.Threaten => Ability.Str, Approach.Trick => Ability.Int, _ => Ability.Mnd,
        };

        /// <summary>
        /// The request table: null = no roll needed, Tier = the check, and impossible when hostile and large.
        /// An offer that fits their wish, a gift or money makes it one step easier (no-roll stays no-roll).
        /// </summary>
        public static RequestNeed Need(Attitude a, RequestSize size, bool sweetened = false)
        {
            Tier? t = (a, size) switch
            {
                (Attitude.Hostile, RequestSize.Small) => Tier.Hard,
                (Attitude.Hostile, RequestSize.Normal) => Tier.Extreme,
                (Attitude.Hostile, RequestSize.Large) => (Tier?)null,
                (Attitude.Wary, RequestSize.Small) => Tier.Normal,
                (Attitude.Wary, RequestSize.Normal) => Tier.Hard,
                (Attitude.Wary, RequestSize.Large) => Tier.Extreme,
                (Attitude.Neutral, RequestSize.Small) => null,
                (Attitude.Neutral, RequestSize.Normal) => Tier.Normal,
                (Attitude.Neutral, RequestSize.Large) => Tier.Hard,
                (Attitude.Friendly, RequestSize.Small) => null,
                (Attitude.Friendly, RequestSize.Normal) => Tier.Easy,
                (Attitude.Friendly, RequestSize.Large) => Tier.Normal,
                (Attitude.Trusting, RequestSize.Large) => Tier.Easy,
                _ => null,
            };
            if (a == Attitude.Hostile && size == RequestSize.Large) return RequestNeed.Impossible;
            if (t == null) return RequestNeed.Free;
            if (sweetened)
            {
                if (t == Tier.Easy) return RequestNeed.Free;
                return RequestNeed.Roll(t.Value.Shift(-1));
            }
            return RequestNeed.Roll(t.Value);
        }

        /// <summary>The usual price in silver that sweetens a request: 1 / 5 / 10-20.</summary>
        public static int Price(RequestSize size) => size switch { RequestSize.Small => 1, RequestSize.Normal => 5, _ => 10 };

        /// <summary>The attitude change for a reason (Attacked always makes them hostile).</summary>
        public static Attitude Apply(Attitude a, AttitudeReason why) => why switch
        {
            AttitudeReason.SavedLife => a.Move(2),
            AttitudeReason.GrantedWish => a.Move(1),
            AttitudeReason.Persuaded => a.Move(1),
            AttitudeReason.Gift => a.Move(1),
            AttitudeReason.LieFound => a.Move(-1),
            AttitudeReason.Threatened => a.Move(-1),
            AttitudeReason.HurtLovedOne => a.Move(-2),
            AttitudeReason.Attacked => Attitude.Hostile,
            AttitudeReason.KilledBeforeThem => a.Move(-1),
            _ => a,
        };

        /// <summary>A persuasion check's effect on the attitude: success +1, critical +2, fumble -1. A threat that works still costs -1.</summary>
        public static Attitude AfterRequest(Attitude a, Approach how, Outcome o)
        {
            if (how == Approach.Threaten) return o.Succeeded() ? a.Move(-1) : a;
            if (how != Approach.Persuade) return a;
            return o switch { Outcome.Critical => a.Move(2), Outcome.Success => a.Move(1), Outcome.Fumble => a.Move(-1), _ => a };
        }

        /// <summary>Companions with the party at once: two with 1-3 adventurers, one with four.</summary>
        public static int MaxCompanions(int partySize) => partySize >= 4 ? 1 : 2;

        /// <summary>Extras the AI may add in one evening.</summary>
        public const int MaxExtras = 8;

        /// <summary>
        /// When a companion's betrayal trigger happens: trusting never betrays, friendly asks the fate die (even),
        /// anything lower betrays. Returns null when the fate die decides (roll it with Likelihood.Even).
        /// </summary>
        public static bool? Betrays(Attitude a) => a switch
        {
            Attitude.Trusting => false,
            Attitude.Friendly => (bool?)null,
            _ => true,
        };

        /// <summary>A companion whose attitude falls below neutral leaves.</summary>
        public static bool Leaves(Attitude a) => a < Attitude.Neutral;

        /// <summary>Seeing through a lie: INT or MND normal (hard for the mastermind). Only "it is a lie" is learnt.</summary>
        public static Tier LieTier(bool mastermind) => mastermind ? Tier.Hard : Tier.Normal;

        /// <summary>Hiring again at the tavern: friendly or better with no roll, neutral as a large request, below that not at all.</summary>
        public static RequestNeed Rehire(Attitude lastBond) =>
            lastBond >= Attitude.Friendly ? RequestNeed.Free : lastBond == Attitude.Neutral ? Need(Attitude.Neutral, RequestSize.Large) : RequestNeed.Impossible;
    }

    public readonly struct RequestNeed
    {
        public bool Possible { get; }
        public Tier? Tier { get; }
        RequestNeed(bool possible, Tier? tier) { Possible = possible; Tier = tier; }
        public static RequestNeed Free => new RequestNeed(true, null);
        public static RequestNeed Impossible => new RequestNeed(false, null);
        public static RequestNeed Roll(Tier t) => new RequestNeed(true, t);
        public bool NeedsRoll => Possible && Tier != null;
        public override string ToString() => !Possible ? "できない" : Tier == null ? "判定なし" : Tier.Value.Label();
    }

    /// <summary>A person in tonight's story (from the story sheet, or an extra the AI added).</summary>
    public sealed class Npc
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Portrait { get; set; } = "";
        public string Location { get; set; } = "";
        public Attitude Attitude { get; set; } = Attitude.Neutral;
        public Build Build { get; set; } = Build.Weak;
        public CompanionRole Role { get; set; } = CompanionRole.Attack;
        public JoinWillingness Joins { get; set; } = JoinWillingness.Never;
        /// <summary>With the party as a companion.</summary>
        public bool WithParty { get; set; }
        public bool Extra { get; set; }
        /// <summary>生きている / 倒れた / 眠った / 退いた (also enemies and creatures in conditions).</summary>
        public PersonState State { get; set; } = PersonState.Alive;
        /// <summary>Got a gift tonight (+1 only once).</summary>
        public bool GiftTaken { get; set; }
        /// <summary>The betrayal trigger already happened.</summary>
        public bool Betrayed { get; set; }
        /// <summary>The creature while it fights (companion or turned enemy).</summary>
        public Creature? Fighting { get; set; }

        /// <summary>Applies a reason; a gift counts once per evening. Returns the change in steps.</summary>
        public int Change(AttitudeReason why)
        {
            if (why == AttitudeReason.Gift)
            {
                if (GiftTaken) return 0;
                GiftTaken = true;
            }
            var before = Attitude;
            Attitude = Npcs.Apply(Attitude, why);
            return (int)Attitude - (int)before;
        }
    }

    public enum PersonState { Alive, Down, Asleep, Retreated, Dead }

    /// <summary>The people of the evening, the companions with the party, and the extras the AI added.</summary>
    public sealed class Cast
    {
        public List<Npc> People { get; } = new List<Npc>();
        public Npc? Find(string id) => People.FirstOrDefault(p => p.Id == id);
        public IEnumerable<Npc> Companions => People.Where(p => p.WithParty && p.State == PersonState.Alive);
        public int ExtrasAdded => People.Count(p => p.Extra);

        /// <summary>Whether another companion may join (the limit by party size).</summary>
        public bool RoomForCompanion(int partySize) => Companions.Count() < Npcs.MaxCompanions(partySize);

        /// <summary>
        /// Takes someone along. False when they never come, the party is full, or they are not there.
        /// Conditional ones come only when the caller says the condition holds.
        /// </summary>
        public bool Join(string id, int partySize, bool conditionMet = false)
        {
            var p = Find(id);
            if (p == null || p.WithParty || p.State != PersonState.Alive) return false;
            if (p.Joins == JoinWillingness.Never || (p.Joins == JoinWillingness.Conditional && !conditionMet)) return false;
            if (!RoomForCompanion(partySize)) return false;
            p.WithParty = true;
            return true;
        }

        /// <summary>
        /// Adds an extra (端役): neutral, weak, with a portrait from the list. Null when tonight already has eight.
        /// </summary>
        public Npc? AddExtra(string name, string location, string portrait)
        {
            if (ExtrasAdded >= Npcs.MaxExtras || string.IsNullOrWhiteSpace(name)) return null;
            var id = "extra_" + (ExtrasAdded + 1);
            var p = new Npc { Id = id, Name = name.Trim(), Location = location, Portrait = portrait, Extra = true, Attitude = Attitude.Neutral, Build = Build.Weak };
            People.Add(p);
            return p;
        }

        /// <summary>Companions below neutral leave; returns who left.</summary>
        public List<Npc> Departures()
        {
            var gone = Companions.Where(p => Npcs.Leaves(p.Attitude)).ToList();
            foreach (var p in gone) p.WithParty = false;
            return gone;
        }

        /// <summary>Killing someone in front of others: every NPC at that place goes down a step.</summary>
        public void KilledAt(string location, string victimId)
        {
            foreach (var p in People.Where(p => p.Location == location && p.Id != victimId && p.State == PersonState.Alive))
                p.Change(AttitudeReason.KilledBeforeThem);
        }
    }
}
