#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Tavern.Core.Play
{
    /// <summary>
    /// Silver and shops (09_Spec 4): silver lives only for the evening. Shops are at the home tavern and at places marked as having one;
    /// they sell consumables and tools only (no fine items, no masterworks). A treasure sells for 10.
    /// </summary>
    public static class Shop
    {
        public const int TreasurePrice = 10;

        public sealed class Ware
        {
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public int Price { get; set; }
            public ItemKind Kind { get; set; }
            public string Use { get; set; } = "";
        }

        /// <summary>The price list 〔仮〕.</summary>
        public static readonly IReadOnlyList<Ware> Wares = new List<Ware>
        {
            new Ware { Key = "torch", Name = "松明", Price = 1, Kind = ItemKind.Tool },
            new Ware { Key = "rope", Name = "ロープ", Price = 2, Kind = ItemKind.Tool },
            new Ware { Key = "oil", Name = "油", Price = 2, Kind = ItemKind.Consumable, Use = "oil" },
            new Ware { Key = "antidote", Name = "解毒薬", Price = 5, Kind = ItemKind.Consumable, Use = "antidote" },
            new Ware { Key = "potion", Name = "薬（回復 小）", Price = 5, Kind = ItemKind.Consumable, Use = "heal:Small" },
            new Ware { Key = "potion_m", Name = "薬（回復 中）", Price = 10, Kind = ItemKind.Consumable, Use = "heal:Medium" },
            new Ware { Key = "scroll", Name = "巻物（呪文 1 回）", Price = 15, Kind = ItemKind.Consumable, Use = "spell:Medium" },
        };



        /// <summary>Buys one. Returns why not, or null (the item goes in the pack).</summary>
        public static string? Buy(Adventurer a, string key)
        {
            var w = Wares.FirstOrDefault(x => x.Key == key);
            if (w == null) return "売っていない";
            if (a.Silver < w.Price) return "銀貨が足りない";
            if (a.Gear.IsFull) return "持ち物がいっぱい";
            a.Silver -= w.Price;
            var n = 1; while (a.Gear.Has($"{w.Key}_{n}")) n++;
            a.Gear.Add(new Item { Id = $"{w.Key}_{n}", Name = w.Name, Kind = w.Kind, Use = w.Use });
            return null;
        }

        /// <summary>Sells a treasure for 10 silver. Returns why not, or null.</summary>
        public static string? Sell(Adventurer a, string itemId)
        {
            var item = a.Gear.Find(itemId);
            if (item == null) return "持っていない";
            if (item.Kind != ItemKind.Treasure) return "店が買うのは宝だけ";
            a.Gear.Remove(itemId);
            a.Silver += TreasurePrice;
            return null;
        }

        /// <summary>Hands silver to someone (a bribe, a fee, a gift). Returns why not, or null.</summary>
        public static string? Pay(Adventurer a, int amount)
        {
            if (amount <= 0) return "払う額がない";
            if (a.Silver < amount) return "銀貨が足りない";
            a.Silver -= amount;
            return null;
        }
    }

    /// <summary>Using a consumable from the pack (outside or inside a fight).</summary>
    public static class Consumables
    {
        /// <summary>
        /// Uses one: heals, cures poison, or reports the spell it holds (the caller resolves the spell). One use is spent
        /// (an item with several uses keeps going until 0). Returns the result line, or null when it is not usable.
        /// </summary>
        public static string? Use(Adventurer user, string itemId, Adventurer target, out Effect? spell)
        {
            spell = null;
            var item = user.Gear.Find(itemId);
            if (item == null || item.Kind != ItemKind.Consumable) return null;
            string line;
            if (item.Use.StartsWith("heal:") && System.Enum.TryParse<EffectSize>(item.Use.Substring(5), out var hs))
            {
                target.Heal(Effects.HealAmount(hs));
                line = $"{user.Name} の{item.Name}で {target.Name} の傷がふさがる（HP {target.Hp}/{target.MaxHp}）。";
            }
            else if (item.Use == "antidote")
            {
                target.Statuses.Remove(StatusKind.Poison);
                line = $"{target.Name} の毒が抜けた。";
            }
            else if (item.Use.StartsWith("spell:") && System.Enum.TryParse<EffectSize>(item.Use.Substring(6), out var ss))
            {
                spell = new Effect(EffectKind.Strike, ss);   // the kind is decided by the classifier; this carries the size
                line = $"{user.Name} は{item.Name}を広げた。";
            }
            else line = $"{user.Name} は{item.Name}を使った。";
            if (item.Uses is int n && n > 1) item.Uses = n - 1;
            else user.Gear.Remove(itemId);
            return line;
        }
    }
}
