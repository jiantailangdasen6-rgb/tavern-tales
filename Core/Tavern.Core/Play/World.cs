#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>
    /// The state of tonight's story world that conditions read (09_Spec 9, condition types): where everyone is, who holds which
    /// story item, which events happened, the clocks, the convictions, the clues found.
    /// </summary>
    public sealed class World
    {
        public int Act { get; set; } = 1;
        /// <summary>Adventurer id → place id (they can split up).</summary>
        public Dictionary<string, string> PartyAt { get; } = new Dictionary<string, string>();
        /// <summary>Story item id → where it is: a place id, or "pc:{id}" / "npc:{id}" for whoever holds it, or "gone".</summary>
        public Dictionary<string, string> ItemAt { get; } = new Dictionary<string, string>();
        public HashSet<string> Events { get; } = new HashSet<string>();
        public Clocks Clocks { get; } = new Clocks();
        public HashSet<string> Convictions { get; } = new HashSet<string>();
        public Cast Cast { get; } = new Cast();
        /// <summary>Clue id → who knows it (adventurer ids). "*" = the whole party (shown).</summary>
        public Dictionary<string, HashSet<string>> Clues { get; } = new Dictionary<string, HashSet<string>>();
        /// <summary>Places the party has been to.</summary>
        public HashSet<string> Visited { get; } = new HashSet<string>();
        /// <summary>Story item id → its kind (for "種類と数" conditions).</summary>
        public Dictionary<string, ItemKind> ItemKinds { get; } = new Dictionary<string, ItemKind>();
        /// <summary>Place graph (for hints: the nearest unfound clue).</summary>
        public Dictionary<string, List<string>> Links { get; } = new Dictionary<string, List<string>>();

        public const string Shown = "*";
        public static string Pc(string id) => "pc:" + id;
        public static string NpcHolder(string id) => "npc:" + id;

        // ---- places ----

        public IEnumerable<string> PlacesOfParty => PartyAt.Values.Distinct();
        public IEnumerable<string> At(string place) => PartyAt.Where(kv => kv.Value == place).Select(kv => kv.Key);

        public void Move(string adventurerId, string place)
        {
            PartyAt[adventurerId] = place;
            Visited.Add(place);
        }

        /// <summary>Whether this is the first time anyone of the party stands here.</summary>
        public bool FirstVisit(string place) => !Visited.Contains(place);

        /// <summary>Steps between two places over the links (int.MaxValue when not connected).</summary>
        public int Distance(string from, string to)
        {
            if (from == to) return 0;
            var seen = new HashSet<string> { from };
            var q = new Queue<(string, int)>();
            q.Enqueue((from, 0));
            while (q.Count > 0)
            {
                var (p, d) = q.Dequeue();
                if (!Links.TryGetValue(p, out var next)) continue;
                foreach (var n in next)
                {
                    if (n == to) return d + 1;
                    if (seen.Add(n)) q.Enqueue((n, d + 1));
                }
            }
            return int.MaxValue;
        }

        // ---- clues ----

        /// <summary>Someone found a clue. Secret finds stay with the finder until shown; otherwise everyone knows.</summary>
        public bool Learn(string clueId, string adventurerId, bool secret)
        {
            if (!Clues.TryGetValue(clueId, out var who)) Clues[clueId] = who = new HashSet<string>();
            var isNew = who.Count == 0 || (!who.Contains(Shown) && !who.Contains(adventurerId));
            who.Add(secret ? adventurerId : Shown);
            return isNew;
        }

        public void ShowClue(string clueId) { if (Clues.TryGetValue(clueId, out var who)) who.Add(Shown); }

        public bool Knows(string adventurerId, string clueId) =>
            Clues.TryGetValue(clueId, out var who) && (who.Contains(Shown) || who.Contains(adventurerId));

        /// <summary>Clues the party shares (the count on the clue book).</summary>
        public int SharedClues => Clues.Count(kv => kv.Value.Contains(Shown));

        // ---- items ----

        public string? WhereIs(string itemId) => ItemAt.TryGetValue(itemId, out var w) ? w : null;
        public bool Holds(string adventurerId, string itemId) => WhereIs(itemId) == Pc(adventurerId);

        /// <summary>
        /// Checks a hand-over before narration (09_Spec 4): the giver must hold it and the receiver must be at the same place.
        /// Returns why not, or null.
        /// </summary>
        public string? CanGive(string giverId, string itemId, string receiverId, bool receiverIsAdventurer)
        {
            if (!Holds(giverId, itemId)) return "持っていない";
            if (!PartyAt.TryGetValue(giverId, out var here)) return "どこにいるか分からない";
            var there = receiverIsAdventurer
                ? (PartyAt.TryGetValue(receiverId, out var p) ? p : null)
                : Cast.Find(receiverId)?.Location;
            if (there == null) return "相手がいない";
            if (there != here) return "相手がその場にいない";
            return null;
        }

        /// <summary>Checks putting an item down at the adventurer's place. Returns why not, or null.</summary>
        public string? CanPlace(string adventurerId, string itemId, string place)
        {
            if (!Holds(adventurerId, itemId)) return "持っていない";
            if (!PartyAt.TryGetValue(adventurerId, out var here) || here != place) return "その場所にいない";
            return null;
        }

        /// <summary>Picking up: the item must lie where the adventurer stands.</summary>
        public string? CanTake(string adventurerId, string itemId)
        {
            var w = WhereIs(itemId);
            if (w == null || w == "gone") return "ない";
            if (!PartyAt.TryGetValue(adventurerId, out var here)) return "どこにいるか分からない";
            if (w != here) return w.StartsWith("npc:") ? "誰かが持っている" : w.StartsWith("pc:") ? "仲間が持っている" : "ここにはない";
            return null;
        }

        public void PutItem(string itemId, string where) => ItemAt[itemId] = where;

        // ---- conditions ----

        /// <summary>True when every condition holds (an empty list holds).</summary>
        public bool All(IEnumerable<Condition> conditions) => conditions.All(c => c.Holds(this));
    }

    public enum ConditionType { Person, Item, Place, Event, Clock, Conviction }

    /// <summary>
    /// One condition of a story sheet (09_Spec 9, six types; a list means "all of them").
    /// Person: Is = alive / down / asleep / retreated / dead / with_party / at / attitude (Value = place or attitude).
    /// Item: At = a place, or HeldBy = party / an adventurer id / an NPC id; or Kind + Count held by the party.
    /// Place: Target place, Who = party (all of them) / any.
    /// Event: Id. Clock: Id with Is = full / not_full / at_least (Count). Conviction: Id.
    /// </summary>
    public sealed class Condition
    {
        public ConditionType Type { get; set; }
        public string Id { get; set; } = "";
        public string Is { get; set; } = "";
        public string Value { get; set; } = "";
        public int Count { get; set; }
        public ItemKind? Kind { get; set; }
        public bool Not { get; set; }

        public bool Holds(World w) => Not ^ Test(w);

        bool Test(World w)
        {
            switch (Type)
            {
                case ConditionType.Event: return w.Events.Contains(Id);
                case ConditionType.Conviction: return w.Convictions.Contains(Id);
                case ConditionType.Clock:
                {
                    var c = w.Clocks.Find(Id);
                    if (c == null) return false;
                    return Is switch { "full" => c.Full, "not_full" => !c.Full, "at_least" => c.Filled >= Count, _ => c.Full };
                }
                case ConditionType.Place:
                {
                    var here = w.PartyAt.Values.ToList();
                    if (here.Count == 0) return false;
                    return Is == "any" ? here.Contains(Id) : here.All(p => p == Id);
                }
                case ConditionType.Item:
                {
                    if (Kind != null)
                    {
                        var held = w.ItemAt.Count(kv => kv.Value.StartsWith("pc:") && w.ItemKinds.TryGetValue(kv.Key, out var k) && k == Kind);
                        return held >= Math.Max(1, Count);
                    }
                    var where = w.WhereIs(Id);
                    if (where == null) return false;
                    return Is switch
                    {
                        "at" => where == Value,
                        "held_by" => Value == "party" ? where.StartsWith("pc:") : where == World.Pc(Value) || where == World.NpcHolder(Value),
                        "gone" => where == "gone",
                        _ => where == Value,
                    };
                }
                case ConditionType.Person:
                {
                    var p = w.Cast.Find(Id);
                    if (p == null) return Is == "dead";   // someone never met is not a living companion
                    return Is switch
                    {
                        "alive" => p.State != PersonState.Dead,
                        "down" => p.State == PersonState.Down,
                        "asleep" => p.State == PersonState.Asleep,
                        "retreated" => p.State == PersonState.Retreated,
                        "dead" => p.State == PersonState.Dead,
                        "with_party" => p.WithParty && p.State == PersonState.Alive,
                        "at" => p.Location == Value,
                        "attitude" => Enum.TryParse<Attitude>(Value, true, out var a) && p.Attitude >= a,
                        _ => false,
                    };
                }
            }
            return false;
        }

        public override string ToString() => $"{(Not ? "not " : "")}{Type}:{Id} {Is} {Value}{(Count > 0 ? " " + Count : "")}".Trim();
    }

    /// <summary>A mechanism on the sheet (仕掛け): a check at a place that raises an event; some also act in fights.</summary>
    public sealed class MechanismDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Place { get; set; } = "";
        public Ability Ability { get; set; }
        public Tier Tier { get; set; } = Tier.Normal;
        /// <summary>No check: it simply works (pulling a lever in peace).</summary>
        public bool NoCheck { get; set; }
        public string Event { get; set; } = "";
        /// <summary>In a fight: hold one enemy for a turn ("hold_one") or hurt every enemy each round ("damage_each_round").</summary>
        public string CombatEffect { get; set; } = "";
        public int CombatAmount { get; set; }
        /// <summary>How many times it works (0 = any number).</summary>
        public int Uses { get; set; } = 1;
    }

    public sealed class Mechanism
    {
        public MechanismDef Def { get; }
        public int Used { get; private set; }
        public Mechanism(MechanismDef def) { Def = def; }
        public bool Spent => Def.Uses > 0 && Used >= Def.Uses;

        /// <summary>Works it after a success: raises its event. False when spent or the event is empty.</summary>
        public bool Trigger(World w)
        {
            if (Spent) return false;
            Used++;
            if (Def.Event.Length > 0) w.Events.Add(Def.Event);
            return true;
        }

        public void Restore(int used) => Used = used;
    }
}
