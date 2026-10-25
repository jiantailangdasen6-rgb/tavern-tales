#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tavern.Core.Play;

namespace Tavern.Core.Story
{
    /// <summary>
    /// Conditions as the sheet writes them: groups of conditions, every one in a group must hold, and any one group is enough.
    /// An empty set always holds. JSON: a list of groups ([[...], [...]]), or a single group ([...]), or one condition ({...}).
    /// </summary>
    public sealed class When
    {
        public List<List<Condition>> Groups { get; } = new List<List<Condition>>();
        public bool IsEmpty => Groups.Count == 0 || Groups.All(g => g.Count == 0);
        public bool Holds(World w, string self = "") => IsEmpty || Groups.Any(g => g.All(c => c.Holds(w, self)));
        public IEnumerable<Condition> All => Groups.SelectMany(g => g);
        public static When Always => new When();
    }

    /// <summary>A change the sheet makes happen (act starts, clocks filling, solved climaxes).</summary>
    public sealed class StoryAction
    {
        /// <summary>event / person_state / person_move / item_move / clock_push / clock_reveal / enemies / climax / text</summary>
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string Value { get; set; } = "";
        public int Amount { get; set; }
        public List<string> Enemies { get; } = new List<string>();
        public string Text { get; set; } = "";
    }

    public sealed class Loss
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public bool Heavy { get; set; }
        public When When { get; set; } = When.Always;
    }

    public sealed class Conclusion
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>The clock a conviction moves one step the good way.</summary>
        public string RewardClock { get; set; } = "";
        public List<string> Clues { get; } = new List<string>();
    }

    public enum ClueAccess { Free, Check, Request }

    public sealed class Clue
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string Conclusion { get; set; } = "";
        /// <summary>Where it is (a place id).</summary>
        public string Place { get; set; } = "";
        /// <summary>Who knows it (a person id), when a person holds it.</summary>
        public string Person { get; set; } = "";
        /// <summary>How it is found, in words (for the AI and the hint).</summary>
        public string How { get; set; } = "";
        public ClueAccess Access { get; set; }
        public Ability? Ability { get; set; }
        public Tier? Tier { get; set; }
        public RequestSize? Request { get; set; }
        /// <summary>Getting it is an important check (failure still gets it, with a cost).</summary>
        public bool Important { get; set; } = true;
        /// <summary>Extra conditions before it can be found (Elda tells it only when friendly).</summary>
        public When Requires { get; set; } = When.Always;
        /// <summary>The acts in which it can be found (empty = all; a song heard early can still be found).</summary>
        public List<int> Acts { get; } = new List<int>();
    }

    public sealed class PersonalGoal
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Checked at the end for the adventurer holding the handout ("self").</summary>
        public When When { get; set; } = When.Always;
    }

    public sealed class Link
    {
        public string To { get; set; } = "";
        /// <summary>道の条件: the way opens only when this holds.</summary>
        public When Open { get; set; } = When.Always;
        public string Text { get; set; } = "";
    }

    public sealed class PlaceRule
    {
        public string Text { get; set; } = "";
        public string Clock { get; set; } = "";
        public int Amount { get; set; } = 1;
    }

    public sealed class Trap
    {
        public string Text { get; set; } = "";
        public Ability Ability { get; set; } = Ability.Dex;
        public Tier Tier { get; set; } = Tier.Normal;
        public int Damage { get; set; } = 3;
        public StatusKind? Status { get; set; }
    }

    public sealed class Place
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Art { get; set; } = "";
        public List<int> Acts { get; } = new List<int>();
        /// <summary>見て分かる様子.</summary>
        public string Look { get; set; } = "";
        /// <summary>What only the AI knows here (調べて分かること that is not a clue).</summary>
        public string Secret { get; set; } = "";
        public List<Link> Links { get; } = new List<Link>();
        public List<PlaceRule> Rules { get; } = new List<PlaceRule>();
        public bool Tense { get; set; }
        public bool Shop { get; set; }
        /// <summary>Enemy table ids met here (the AI starts a fight from these).</summary>
        public List<string> Enemies { get; } = new List<string>();
        public List<Trap> Traps { get; } = new List<Trap>();
    }

    public sealed class StoryItem
    {
        public Item Item { get; set; } = new Item();
        /// <summary>A place id, "npc:{person}", or "enemy:{enemy id}" (dropped where that enemy falls).</summary>
        public string At { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public sealed class Person
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Portrait { get; set; } = "";
        public string At { get; set; } = "";
        public string Role { get; set; } = "";
        public string Voice { get; set; } = "";
        public string Wants { get; set; } = "";
        public string Fears { get; set; } = "";
        public string Secret { get; set; } = "";
        public string Lie { get; set; } = "";
        public List<string> Clues { get; } = new List<string>();
        public Attitude Attitude { get; set; } = Attitude.Neutral;
        public JoinWillingness Joins { get; set; } = JoinWillingness.Never;
        /// <summary>For conditional joining.</summary>
        public When JoinWhen { get; set; } = When.Always;
        public string JoinNote { get; set; } = "";
        /// <summary>Comes to the start place in the opening (the client in the tavern) and goes home to At once the party leaves it.</summary>
        public bool Opening { get; set; }
        /// <summary>Someone the party escorts (rescued people): does not fight and does not count as a companion.</summary>
        public bool Escort { get; set; }
        public Build Build { get; set; } = Build.Weak;
        public CompanionRole CombatRole { get; set; }
        /// <summary>An enemy table id when this person fights as that enemy (bosses).</summary>
        public string Enemy { get; set; } = "";
        public bool Mastermind { get; set; }
        /// <summary>The betrayal trigger.</summary>
        public When Betrays { get; set; } = When.Always;
        public bool CanBetray { get; set; }
        /// <summary>What passes on if they die (who knows their clues now, what drops).</summary>
        public string OnDeath { get; set; } = "";
    }

    public sealed class Act
    {
        public int N { get; set; }
        public string Title { get; set; } = "";
        public string Goal { get; set; } = "";
        /// <summary>When the act moves on (must include a place condition).</summary>
        public When Advance { get; set; } = When.Always;
        public List<StoryAction> OnStart { get; } = new List<StoryAction>();
    }

    public sealed class Solution
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public When When { get; set; } = When.Always;
        public List<StoryAction> Then { get; } = new List<StoryAction>();
    }

    public sealed class Climax
    {
        public string Text { get; set; } = "";
        public When Starts { get; set; } = When.Always;
        public List<StoryAction> OnStart { get; } = new List<StoryAction>();
        public List<Solution> Solutions { get; } = new List<Solution>();
    }

    public sealed class StoryClock
    {
        public ClockDef Def { get; set; } = new ClockDef();
        /// <summary>見える条件 (empty = from the start).</summary>
        public When Visible { get; set; } = When.Always;
        /// <summary>The conditions of each rewind (by rewind id).</summary>
        public Dictionary<string, When> RewindWhen { get; } = new Dictionary<string, When>();
        public List<StoryAction> OnFull { get; } = new List<StoryAction>();
    }

    public sealed class Hook
    {
        public string Place { get; set; } = "";
        public string Role { get; set; } = "";
    }

    /// <summary>A hand-written story sheet (09_Spec 9), read from Resources/Content/Stories/{id}.json.</summary>
    public sealed class StoryDef
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "";
        /// <summary>short (one evening, 3 acts) / long (two evenings, 5 acts).</summary>
        public bool Long { get; set; }
        /// <summary>The map spot (region id in world.json).</summary>
        public string Region { get; set; } = "";
        public string Darkness { get; set; } = "";
        public string Intro { get; set; } = "";
        /// <summary>前金: silver per adventurer at the start.</summary>
        public int Advance { get; set; }
        public string Start { get; set; } = "";
        public string Truth { get; set; } = "";
        public string GoalText { get; set; } = "";
        public When Goal { get; set; } = When.Always;
        public List<Loss> Losses { get; } = new List<Loss>();
        public List<StoryClock> Clocks { get; } = new List<StoryClock>();
        public List<Conclusion> Conclusions { get; } = new List<Conclusion>();
        public List<Clue> Clues { get; } = new List<Clue>();
        public List<string> Fragments { get; } = new List<string>();
        public List<PersonalGoal> PersonalGoals { get; } = new List<PersonalGoal>();
        public List<Place> Places { get; } = new List<Place>();
        public List<MechanismDef> Mechanisms { get; } = new List<MechanismDef>();
        public List<StoryItem> Items { get; } = new List<StoryItem>();
        public List<Person> People { get; } = new List<Person>();
        public List<Act> Acts { get; } = new List<Act>();
        public Climax Climax { get; set; } = new Climax();
        public string Choice { get; set; } = "";
        public List<Hook> Hooks { get; } = new List<Hook>();
        public string EndingSuccess { get; set; } = "";
        public string EndingBitter { get; set; } = "";
        public string EndingFailure { get; set; } = "";

        public Place? PlaceOf(string id) => Places.FirstOrDefault(p => p.Id == id);
        public Person? PersonOf(string id) => People.FirstOrDefault(p => p.Id == id);
        public Clue? ClueOf(string id) => Clues.FirstOrDefault(c => c.Id == id);
        public StoryItem? ItemOf(string id) => Items.FirstOrDefault(i => i.Item.Id == id);
        public StoryClock? ClockOf(string id) => Clocks.FirstOrDefault(c => c.Def.Id == id);
        public int ActCount => Long ? 5 : 3;
    }
}
