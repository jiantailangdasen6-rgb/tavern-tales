#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>How a clock moves by itself (09_Spec 10).</summary>
    public enum ClockSpeed
    {
        /// <summary>N 手ごと: one segment every N exploration turns (more turns for small parties).</summary>
        EveryTurns,
        /// <summary>出来事でだけ: only reasons and events move it.</summary>
        EventsOnly,
        /// <summary>戦闘の手番ごと: one segment per combat round.</summary>
        CombatRounds,
    }

    /// <summary>Why a clock moves besides its speed.</summary>
    public enum ClockPush
    {
        /// <summary>休む +2</summary>
        Rest,
        /// <summary>時間をかける行動 +1</summary>
        SlowAction,
        /// <summary>騒ぎ・大失敗 +1</summary>
        Noise,
        /// <summary>忍ぶ・騙すに失敗して気付かれる +1</summary>
        Noticed,
        /// <summary>場所の決まりに触れた (the amount the place says)</summary>
        PlaceRule,
        /// <summary>失敗の代償「時計 +1」</summary>
        Cost,
        /// <summary>A story event says so.</summary>
        Event,
    }

    /// <summary>What happens when a clock fills (the story sheet picks from this list).</summary>
    public enum ClockFull
    {
        /// <summary>NPC が移る</summary>
        NpcMoves,
        /// <summary>NPC が死ぬ</summary>
        NpcDies,
        /// <summary>敵が現れる</summary>
        EnemiesAppear,
        /// <summary>場所が変わる</summary>
        PlaceChanges,
        /// <summary>黒幕が最後の場所を変える</summary>
        BossMoves,
        /// <summary>結末が 1 段苦くなる</summary>
        EndingWorse,
        /// <summary>結末になる (the story ends here)</summary>
        Ending,
    }

    public sealed class ClockRewind
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public int Amount { get; set; } = 2;
    }

    public sealed class ClockDef
    {
        public string Id { get; set; } = "";
        /// <summary>The name players see (表の名前).</summary>
        public string Name { get; set; } = "";
        /// <summary>The name only the AI knows (裏の名前).</summary>
        public string SecretName { get; set; } = "";
        public int Segments { get; set; } = 6;
        public ClockSpeed Speed { get; set; }
        public int Every { get; set; } = 5;
        /// <summary>Acts in which it runs (empty = all).</summary>
        public List<int> Acts { get; } = new List<int>();
        /// <summary>A good clock (dawn coming) is one the party wants filled.</summary>
        public bool Good { get; set; }
        public List<ClockFull> OnFull { get; } = new List<ClockFull>();
        /// <summary>Free text for the AI about what filling means here (who moves, which enemies...).</summary>
        public string FullText { get; set; } = "";
        public List<ClockRewind> Rewinds { get; } = new List<ClockRewind>();
    }

    public sealed class Clock
    {
        public ClockDef Def { get; }
        public int Filled { get; private set; }
        /// <summary>Shown on the bar once the party knows the danger.</summary>
        public bool Visible { get; set; }
        /// <summary>The fill was already acted on.</summary>
        public bool Fired { get; set; }
        /// <summary>Turns counted toward the next segment.</summary>
        public double Counted { get; set; }
        public HashSet<string> RewindsUsed { get; } = new HashSet<string>();
        /// <summary>Stopped (the break in a long story).</summary>
        public bool Paused { get; set; }

        public Clock(ClockDef def) { Def = def; }

        public string Id => Def.Id;
        public bool Full => Filled >= Def.Segments;
        public int Left => Math.Max(0, Def.Segments - Filled);
        public bool PastHalf => Filled * 2 > Def.Segments;
        public bool RunsIn(int act) => Def.Acts.Count == 0 || Def.Acts.Contains(act);

        /// <summary>Moves it; returns true when this filled it (only once).</summary>
        public bool Push(int segments)
        {
            if (segments <= 0 || Full) return false;
            Filled = Math.Min(Def.Segments, Filled + segments);
            return Full;
        }

        /// <summary>Back by n (never below 0); a filled clock that fired stays fired.</summary>
        public void Back(int n) => Filled = Math.Max(0, Filled - Math.Max(0, n));

        /// <summary>One step the good way: a bad clock back 1, a good clock forward 1 (the conviction reward).</summary>
        public void Better()
        {
            if (Def.Good) Push(1); else Back(1);
        }

        /// <summary>A rewind from the sheet (once each). Returns false when used or unknown.</summary>
        public bool Rewind(string rewindId)
        {
            var r = Def.Rewinds.FirstOrDefault(x => x.Id == rewindId);
            if (r == null || !RewindsUsed.Add(rewindId)) return false;
            if (Def.Good) Push(r.Amount); else Back(r.Amount);
            return true;
        }

        public void Restore(int filled, double counted) { Filled = Math.Max(0, Math.Min(Def.Segments, filled)); Counted = counted; }
    }

    /// <summary>The evening's clocks: counting turns, pushes, rewinds, and which ones just filled.</summary>
    public sealed class Clocks
    {
        public List<Clock> All { get; } = new List<Clock>();
        public Clock? Find(string id) => All.FirstOrDefault(c => c.Id == id);

        /// <summary>
        /// Small parties get more turns per segment: one adventurer x1.5, two x1.25 (since fewer act each turn).
        /// </summary>
        public static double PartyFactor(int partySize) => partySize <= 1 ? 1.5 : partySize == 2 ? 1.25 : 1.0;

        /// <summary>
        /// One exploration turn passed (a whole fight also counts as one). Returns the clocks that filled.
        /// </summary>
        public List<Clock> Turn(int act, int partySize)
        {
            var filled = new List<Clock>();
            foreach (var c in All.Where(c => c.Def.Speed == ClockSpeed.EveryTurns && c.RunsIn(act) && !c.Paused && !c.Full))
            {
                c.Counted += 1;
                var need = c.Def.Every * PartyFactor(partySize);
                while (c.Counted + 1e-9 >= need && !c.Full)
                {
                    c.Counted -= need;
                    if (c.Push(1)) filled.Add(c);
                }
            }
            return filled;
        }

        /// <summary>One combat round passed (clocks that move per round).</summary>
        public List<Clock> CombatRound(int act)
        {
            var filled = new List<Clock>();
            foreach (var c in All.Where(c => c.Def.Speed == ClockSpeed.CombatRounds && c.RunsIn(act) && !c.Paused))
                if (c.Push(1)) filled.Add(c);
            return filled;
        }

        /// <summary>Pushes every running clock (rest, noise, costs). Returns the clocks that filled.</summary>
        public List<Clock> PushAll(int act, int amount)
        {
            var filled = new List<Clock>();
            foreach (var c in All.Where(c => c.RunsIn(act) && !c.Paused && !c.Def.Good))
                if (c.Push(amount)) filled.Add(c);
            return filled;
        }

        public static int Amount(ClockPush why) => why == ClockPush.Rest ? 2 : 1;

        /// <summary>The clocks that filled and were not acted on yet (marks them fired).</summary>
        public List<Clock> TakeFired()
        {
            var list = All.Where(c => c.Full && !c.Fired).ToList();
            foreach (var c in list) c.Fired = true;
            return list;
        }

        /// <summary>A bad clock at one segment left (one of the tension triggers).</summary>
        public bool AnyAtLastSegment(int act) => All.Any(c => !c.Def.Good && c.RunsIn(act) && c.Left == 1);
    }

    /// <summary>
    /// Hints when the story stalls (09_Spec 10): turns with no new clue, place, act, fight or conviction are counted for the whole party.
    /// After the difficulty's numbers of turns the hint goes up a step (1 気配, 2 糸口, 3 the holder comes to them).
    /// </summary>
    public sealed class Hints
    {
        public int Stalled { get; private set; }
        public int Level { get; private set; }
        /// <summary>The level just reached, to be given this turn (0 = none).</summary>
        public int Due { get; private set; }

        /// <summary>Counts a turn. progress: something new happened (resets).</summary>
        public void Turn(bool progress, Difficulty d)
        {
            Due = 0;
            if (progress) { Stalled = 0; Level = 0; return; }
            Stalled++;
            var (first, second, third) = d.HintTurns();
            int next = Level switch { 0 => first, 1 => first + second, 2 => third > 0 ? first + second + third : int.MaxValue, _ => int.MaxValue };
            if (Stalled >= next) { Level++; Due = Level; }
        }

        /// <summary>「手がかりがほしい」: the next turn gets one level more (up to 3, or 2 when hard has no third step).</summary>
        public void Ask(Difficulty d)
        {
            var max = d.HintTurns().third > 0 ? 3 : 2;
            if (Level < max) { Level++; Due = Level; }
        }

        public void Restore(int stalled, int level) { Stalled = stalled; Level = level; Due = 0; }
    }

    public enum TensionTrigger { TensePlace, ClockLastSegment, AllyDown }

    /// <summary>緊迫: starts on a trigger, lasts three turns, at most twice per evening (per night in a long story).</summary>
    public sealed class Tension
    {
        public const int Turns = 3;
        public const int PerEvening = 2;
        public int Left { get; private set; }
        public int Used { get; private set; }
        public bool On => Left > 0;

        /// <summary>Starts it if allowed. Returns true when it started.</summary>
        public bool Trigger()
        {
            if (On || Used >= PerEvening) return false;
            Used++;
            Left = Turns;
            return true;
        }

        /// <summary>A turn passed (fights and climaxes take over the scene but still count).</summary>
        public void Turn() { if (Left > 0) Left--; }

        /// <summary>A new night of a long story starts the count again.</summary>
        public void NewNight() { Used = 0; Left = 0; }

        public void Restore(int left, int used) { Left = left; Used = used; }
    }
}
