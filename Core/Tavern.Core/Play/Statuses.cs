#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>Conditions on an adventurer, an NPC or an enemy (09_Spec 7) 〔仮〕. The same one never stacks.</summary>
    public enum StatusKind
    {
        /// <summary>毒: 1 damage at the start of own turn; 3 turns.</summary>
        Poison,
        /// <summary>弱り（呪い）: every check one step harder; until cured or the evening ends.</summary>
        Weak,
        /// <summary>縛られ: cannot act (can talk); attacks on them count as critical.</summary>
        Bound,
        /// <summary>眠り: cannot act; attacks count as critical and wake them; 2 turns.</summary>
        Asleep,
        /// <summary>惑わし: the next action is chosen by whoever charmed them.</summary>
        Charmed,
        /// <summary>恐怖: cannot approach or attack its source; MND normal at turn start; 2 turns.</summary>
        Fear,
        /// <summary>目くらみ: attacks and looking one step harder; 1 turn.</summary>
        Dazzled,
        /// <summary>動けない（気絶）: does nothing for 1 turn.</summary>
        Stunned,
    }

    public sealed class Status
    {
        public StatusKind Kind { get; set; }
        /// <summary>Turns left; 0 or less = lasts until something removes it.</summary>
        public int Turns { get; set; }
        /// <summary>Who caused it (the charmer, the source of fear).</summary>
        public string Source { get; set; } = "";
    }

    public static class StatusInfo
    {
        public static string Label(this StatusKind k) => k switch
        {
            StatusKind.Poison => "毒", StatusKind.Weak => "弱り", StatusKind.Bound => "縛られ", StatusKind.Asleep => "眠り",
            StatusKind.Charmed => "惑わし", StatusKind.Fear => "恐怖", StatusKind.Dazzled => "目くらみ", StatusKind.Stunned => "動けない",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        /// <summary>How many turns a new status lasts (0 = until removed).</summary>
        public static int DefaultTurns(StatusKind k) => k switch
        {
            StatusKind.Poison => 3, StatusKind.Asleep => 2, StatusKind.Fear => 2, StatusKind.Charmed => 1,
            StatusKind.Dazzled => 1, StatusKind.Stunned => 1, _ => 0,
        };

        /// <summary>The 祝福 body trait keeps these away.</summary>
        public static bool BlessedImmune(StatusKind k) => k == StatusKind.Poison || k == StatusKind.Weak || k == StatusKind.Fear;

        /// <summary>Statuses that take the whole turn.</summary>
        public static bool Disables(StatusKind k) => k == StatusKind.Bound || k == StatusKind.Asleep || k == StatusKind.Stunned;

        /// <summary>Attacks against someone who cannot move count as critical (double).</summary>
        public static bool Helpless(StatusKind k) => k == StatusKind.Bound || k == StatusKind.Asleep;
    }

    public sealed class StatusSet
    {
        public List<Status> List { get; } = new List<Status>();

        public bool Has(StatusKind k) => List.Any(s => s.Kind == k);
        public Status? Get(StatusKind k) => List.FirstOrDefault(s => s.Kind == k);
        public bool CanAct => !List.Any(s => StatusInfo.Disables(s.Kind));
        public bool Helpless => List.Any(s => StatusInfo.Helpless(s.Kind));

        /// <summary>Adds a status, or refreshes the one already there (never two of the same). False when blessed armour keeps it off.</summary>
        public bool Add(StatusKind k, string source = "", bool blessed = false, int? turns = null)
        {
            if (blessed && StatusInfo.BlessedImmune(k)) return false;
            var t = turns ?? StatusInfo.DefaultTurns(k);
            var old = Get(k);
            if (old != null) { old.Turns = old.Turns <= 0 || t <= 0 ? 0 : Math.Max(old.Turns, t); old.Source = source; return true; }
            List.Add(new Status { Kind = k, Turns = t, Source = source });
            return true;
        }

        public bool Remove(StatusKind k) => List.RemoveAll(s => s.Kind == k) > 0;

        /// <summary>Poison damage due at the start of the owner's turn.</summary>
        public int PoisonDamage => Has(StatusKind.Poison) ? 1 : 0;

        /// <summary>Counts down at the end of the owner's turn; returns the statuses that ran out.</summary>
        public List<StatusKind> EndTurn()
        {
            var gone = new List<StatusKind>();
            foreach (var s in List.ToList())
            {
                if (s.Turns <= 0) continue;
                s.Turns--;
                if (s.Turns == 0) { List.Remove(s); gone.Add(s.Kind); }
            }
            return gone;
        }

        /// <summary>Being hit wakes a sleeper.</summary>
        public bool WakeOnHit() => Remove(StatusKind.Asleep);
    }
}
