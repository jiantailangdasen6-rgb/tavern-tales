#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>Item kinds (09_Spec 6). Name and look are free; the effect comes only from kind, grade and trait.</summary>
    public enum ItemKind { Weapon, Body, Shield, Trinket, Tool, Consumable, Treasure, Story }

    /// <summary>並 / 良い品 / 名品. Masterworks exist only where a story places them.</summary>
    public enum Grade { Plain, Fine, Masterwork }

    /// <summary>One trait per item at most.</summary>
    public enum Trait
    {
        None,
        // weapons
        /// <summary>退魔: double damage against the undead.</summary>
        Bane,
        /// <summary>会心: a natural 19 is also a critical.</summary>
        Keen,
        /// <summary>薙ぎ払い: hits two enemies.</summary>
        Cleave,
        /// <summary>投げ: reaches far.</summary>
        Thrown,
        /// <summary>炎: 1 more damage on the target's next turn.</summary>
        Flame,
        /// <summary>吸命: 1 HP back on a hit.</summary>
        Drain,
        // body
        /// <summary>重装: reduces 1 more, AGI checks one step harder.</summary>
        Heavy,
        /// <summary>祝福: immune to poison, curse and fear.</summary>
        Blessed,
        /// <summary>忍び: sneaking and hiding one step easier.</summary>
        Stealthy,
        // shield
        /// <summary>大盾: a covered ally takes no damage.</summary>
        Tower,
        /// <summary>返し: 1 damage back on a dodge.</summary>
        Riposte,
        /// <summary>魔除け: +2 to dodge big moves and magic.</summary>
        Warding,
    }

    public enum WeaponType { Dagger, Sword, Mace, Axe, Spear, Bow, Staff }

    public sealed class WeaponInfo
    {
        public WeaponType Type { get; }
        public string Label { get; }
        public Ability Ability { get; }
        public bool TwoHanded { get; }
        public int Damage { get; }
        WeaponInfo(WeaponType type, string label, Ability ability, bool twoHanded, int damage)
        { Type = type; Label = label; Ability = ability; TwoHanded = twoHanded; Damage = damage; }

        /// <summary>The weapon table (09_Spec 6) 〔仮〕.</summary>
        public static WeaponInfo Of(WeaponType t) => t switch
        {
            WeaponType.Dagger => new WeaponInfo(t, "短剣", Ability.Dex, false, 2),
            WeaponType.Sword => new WeaponInfo(t, "剣", Ability.Str, false, 3),
            WeaponType.Mace => new WeaponInfo(t, "鎚・鎚矛", Ability.Str, false, 2),
            WeaponType.Axe => new WeaponInfo(t, "斧・大剣", Ability.Str, true, 4),
            WeaponType.Spear => new WeaponInfo(t, "槍", Ability.Str, true, 3),
            WeaponType.Bow => new WeaponInfo(t, "弓", Ability.Dex, true, 2),
            WeaponType.Staff => new WeaponInfo(t, "杖", Ability.Int, true, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(t)),
        };

        /// <summary>Spear and bow strike once before the enemies at the start of a fight.</summary>
        public bool FirstStrike => Type == WeaponType.Spear || Type == WeaponType.Bow;
        /// <summary>Heavy weapons a strong adventurer (STR +3 or more) may hold in one hand.</summary>
        public bool OneHandable => Type == WeaponType.Axe || Type == WeaponType.Spear;
    }

    public sealed class Item
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ItemKind Kind { get; set; }
        public Grade Grade { get; set; }
        /// <summary>The number the grade adds (+1 fine, +2 masterwork; 0 for a fine item that has a trait instead).</summary>
        public int Bonus { get; set; }
        public Trait Trait { get; set; }
        public WeaponType? Weapon { get; set; }
        /// <summary>A trinket's ability (+1 to its checks, whatever the grade).</summary>
        public Ability? Ability { get; set; }
        /// <summary>Uses left for consumables with several uses (聖灯の油 3 回); null = one use, or not consumable.</summary>
        public int? Uses { get; set; }
        /// <summary>Taken in a secret action: only its owner sees it until it is used in front of others or shown.</summary>
        public bool Concealed { get; set; }
        /// <summary>What a consumable does when used (heal / antidote / spell), read by the turn flow.</summary>
        public string Use { get; set; } = "";
        /// <summary>The story item id this came from (for conditions), empty for gear made tonight.</summary>
        public string StoryId { get; set; } = "";

        public Item Clone() => (Item)MemberwiseClone();

        /// <summary>Whether grade, bonus and trait fit together (09_Spec 6).</summary>
        public bool IsValid(out string why)
        {
            why = "";
            if (Kind == ItemKind.Weapon && Weapon == null) { why = "武器に武器種がない"; return false; }
            if (Kind == ItemKind.Trinket && Ability == null) { why = "小物に能力値がない"; return false; }
            if (Trait != Trait.None && !TraitFits(Kind, Trait)) { why = $"{Kind} に付かない特性 {Trait}"; return false; }
            switch (Grade)
            {
                case Grade.Plain: if (Bonus != 0 || Trait != Trait.None) { why = "並の品に +格 や特性がある"; return false; } break;
                case Grade.Fine: if (!(Bonus == 1 && Trait == Trait.None) && !(Bonus == 0 && Trait != Trait.None)) { why = "良い品は +1 か特性 1 つ"; return false; } break;
                case Grade.Masterwork: if (Bonus != 2 || Trait == Trait.None) { why = "名品は +2 と特性 1 つ"; return false; } break;
            }
            return true;
        }

        public static bool TraitFits(ItemKind kind, Trait t) => kind switch
        {
            ItemKind.Weapon => t == Trait.Bane || t == Trait.Keen || t == Trait.Cleave || t == Trait.Thrown || t == Trait.Flame || t == Trait.Drain,
            ItemKind.Body => t == Trait.Heavy || t == Trait.Blessed || t == Trait.Stealthy,
            ItemKind.Shield => t == Trait.Tower || t == Trait.Riposte || t == Trait.Warding,
            _ => false,
        };

        /// <summary>Kinds that go in an equipment slot (the rest is carried).</summary>
        public bool Wearable => Kind == ItemKind.Weapon || Kind == ItemKind.Body || Kind == ItemKind.Shield || Kind == ItemKind.Trinket;
    }

    /// <summary>What an attack with the current hands looks like.</summary>
    public sealed class AttackProfile
    {
        public Ability Ability { get; set; }
        public int Damage { get; set; }
        /// <summary>Added to the to-hit roll (mace against heavy armour is added at roll time, see HitBonusAgainst).</summary>
        public int HitBonus { get; set; }
        public WeaponType? Type { get; set; }
        public bool DualWield { get; set; }
        public bool OneHandedHeavy { get; set; }
        public List<Trait> Traits { get; } = new List<Trait>();
        public bool FirstStrike => Type != null && WeaponInfo.Of(Type.Value).FirstStrike;

        /// <summary>Mace: +2 to hit an enemy whose defense is 14 or more.</summary>
        public int HitBonusAgainst(int defense) => HitBonus + (Type == WeaponType.Mace && defense >= 14 ? 2 : 0);
        public bool Has(Trait t) => Traits.Contains(t);
    }

    /// <summary>
    /// What an adventurer carries (10 things at most, worn ones included) and what is in the four slots:
    /// main hand, off hand (a shield or a second one-handed weapon), body, trinket.
    /// </summary>
    public sealed class Gear
    {
        public const int MaxItems = 10;
        public List<Item> Items { get; } = new List<Item>();
        public string MainHand { get; private set; } = "";
        public string OffHand { get; private set; } = "";
        public string Body { get; private set; } = "";
        public string Trinket { get; private set; } = "";

        public Item? Find(string id) => Items.FirstOrDefault(i => i.Id == id);
        public bool Has(string id) => Find(id) != null;
        public bool IsFull => Items.Count >= MaxItems;

        public Item? MainItem => Find(MainHand);
        public Item? OffItem => Find(OffHand);
        public Item? BodyItem => Find(Body);
        public Item? TrinketItem => Find(Trinket);
        public Item? Shield => OffItem?.Kind == ItemKind.Shield ? OffItem : null;

        /// <summary>Adds an item; false when the pack is full (10).</summary>
        public bool Add(Item item)
        {
            if (IsFull || Has(item.Id)) return false;
            Items.Add(item);
            return true;
        }

        /// <summary>Takes an item out (unequipping it). Returns it, or null when it is not here.</summary>
        public Item? Remove(string id)
        {
            var item = Find(id);
            if (item == null) return null;
            if (MainHand == id) MainHand = "";
            if (OffHand == id) OffHand = "";
            if (Body == id) Body = "";
            if (Trinket == id) Trinket = "";
            Items.Remove(item);
            return item;
        }

        /// <summary>
        /// Puts a carried item in its slot. Weapons go to the main hand unless offHand is set (a shield always goes to the off hand).
        /// Returns why not, or null when it worked. strength is the STR modifier (one-handed heavy weapons need +3).
        /// </summary>
        public string? Equip(string id, int strength, bool offHand = false)
        {
            var item = Find(id);
            if (item == null) return "持っていない";
            switch (item.Kind)
            {
                case ItemKind.Body: Body = id; return null;
                case ItemKind.Trinket: Trinket = id; return null;
                case ItemKind.Shield:
                    if (MainItem is Item m && TwoHandedNow(m, strength, true)) return "両手で持つ武器と盾は一緒に持てない";
                    if (MainHand == id) MainHand = "";
                    OffHand = id; return null;
                case ItemKind.Weapon:
                    var info = WeaponInfo.Of(item.Weapon!.Value);
                    if (offHand)
                    {
                        if (info.TwoHanded) return "両手の武器は逆の手に持てない";
                        if (MainItem is Item main && (main.Kind != ItemKind.Weapon || WeaponInfo.Of(main.Weapon!.Value).TwoHanded))
                            return "二刀流は片手の武器 2 本で";
                        if (MainHand == id) return "同じ武器を両手には持てない";
                        OffHand = id; return null;
                    }
                    if (OffHand == id) OffHand = "";
                    MainHand = id;
                    // A two-handed weapon frees the off hand, unless a strong arm holds it in one hand next to a shield.
                    if (info.TwoHanded && OffItem is Item off && !(off.Kind == ItemKind.Shield && CanHoldOneHanded(info, strength))) OffHand = "";
                    return null;
                default: return "身に着ける物ではない";
            }
        }

        /// <summary>Puts saved slots back (load); ids must be in the pack.</summary>
        public void Restore(string main, string off, string body, string trinket)
        {
            MainHand = Has(main) ? main : ""; OffHand = Has(off) ? off : ""; Body = Has(body) ? body : ""; Trinket = Has(trinket) ? trinket : "";
        }

        public void Unequip(string id)
        {
            if (MainHand == id) MainHand = "";
            if (OffHand == id) OffHand = "";
            if (Body == id) Body = "";
            if (Trinket == id) Trinket = "";
        }

        static bool CanHoldOneHanded(WeaponInfo info, int strength) => info.OneHandable && strength >= 3;

        /// <summary>Whether the main-hand item needs both hands now (with a shield wanted next to it).</summary>
        static bool TwoHandedNow(Item main, int strength, bool withShield)
        {
            if (main.Kind != ItemKind.Weapon) return false;
            var info = WeaponInfo.Of(main.Weapon!.Value);
            return info.TwoHanded && !(withShield && CanHoldOneHanded(info, strength));
        }

        /// <summary>The attack the hands make now. Mage's magic missile is a spell, not this (see Adventurer.MagicMissile).</summary>
        public AttackProfile Attack(int strength)
        {
            var p = new AttackProfile();
            var main = MainItem?.Kind == ItemKind.Weapon ? MainItem : null;
            if (main == null) { p.Ability = Ability.Str; p.Damage = 1; return p; }   // bare hands
            var info = WeaponInfo.Of(main.Weapon!.Value);
            p.Type = info.Type;
            p.Ability = info.Ability;
            p.Damage = info.Damage + main.Bonus;
            if (main.Trait != Trait.None) p.Traits.Add(main.Trait);
            if (OffItem is Item off && off.Kind == ItemKind.Weapon)
            {
                // Dual wield: one attack, +1 damage; grade and weapon perk from the main hand only, traits from both.
                p.DualWield = true;
                p.Damage += 1;
                if (off.Trait != Trait.None && !p.Traits.Contains(off.Trait)) p.Traits.Add(off.Trait);
            }
            if (info.TwoHanded && Shield != null) { p.OneHandedHeavy = true; p.HitBonus -= 3; }
            return p;
        }

        /// <summary>Damage taken off each hit by the body slot (heavy armour 1 more).</summary>
        public int ArmorReduction => BodyItem is Item b ? b.Bonus + (b.Trait == Trait.Heavy ? 1 : 0) : 0;
        public int ShieldDodgeBonus => Shield?.Bonus ?? 0;
        public bool Wears(Trait t) => BodyItem?.Trait == t || Shield?.Trait == t;
        /// <summary>+1 to checks of the trinket's ability.</summary>
        public int TrinketBonus(Ability a) => TrinketItem?.Ability == a ? 1 : 0;
        public bool HasTool(string id) => Find(id)?.Kind == ItemKind.Tool;
    }
}
