#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Tavern.Core.Play
{
    /// <summary>One sentence of a class text and the specialty (effect kind) behind it.</summary>
    public sealed class Specialty
    {
        public EffectKind Kind { get; set; }
        /// <summary>不死に強い: one size bigger against the undead.</summary>
        public bool UndeadBane { get; set; }
        /// <summary>Only on oneself (the warrior's second wind).</summary>
        public bool SelfOnly { get; set; }
        public string Text { get; set; } = "";
    }

    public sealed class GearSpec
    {
        public Item Item { get; set; } = new Item();
        /// <summary>main / off / body / trinket, or empty to carry it.</summary>
        public string Equip { get; set; } = "";
    }

    public sealed class ClassDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Dictionary<Ability, int> Stats { get; } = new Dictionary<Ability, int>();
        public int Hp { get; set; }
        public int Focus { get; set; }
        /// <summary>Can cast spells (mage, priest). Others reach the same results only with fitting tools or methods.</summary>
        public bool Spells { get; set; }
        /// <summary>The mage's magic missile: INT, 2 damage, no focus, whatever is in hand.</summary>
        public bool MagicMissile { get; set; }
        public string Intro { get; set; } = "";
        public string Outro { get; set; } = "";
        public List<Specialty> Specialties { get; } = new List<Specialty>();
        public List<Specialty> Growth { get; } = new List<Specialty>();
        public List<GearSpec> Gear { get; } = new List<GearSpec>();
    }

    /// <summary>The classes (Resources/Content/Rules/classes.json).</summary>
    public sealed class ClassBook
    {
        public Dictionary<string, ClassDef> Classes { get; } = new Dictionary<string, ClassDef>();
        public ClassDef this[string id] => Classes.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException("class " + id);

        public static ClassBook Load(string json)
        {
            var book = new ClassBook();
            foreach (var c in (JArray)JObject.Parse(json)["classes"]!)
            {
                var def = new ClassDef
                {
                    Id = (string)c["id"]!, Name = (string)c["name"]!, Hp = (int)c["hp"]!, Focus = (int)c["focus"]!,
                    Spells = (bool?)c["spells"] ?? false, MagicMissile = (bool?)c["magicMissile"] ?? false,
                    Intro = (string?)c["intro"] ?? "", Outro = (string?)c["outro"] ?? "",
                };
                foreach (var p in ((JObject)c["stats"]!).Properties())
                {
                    if (!AbilityNames.TryParse(p.Name, out var a)) throw new FormatException($"class {def.Id}: unknown ability {p.Name}");
                    def.Stats[a] = (int)p.Value;
                }
                foreach (Ability a in Enum.GetValues(typeof(Ability))) if (!def.Stats.ContainsKey(a)) def.Stats[a] = 0;
                foreach (var s in (JArray?)c["specialties"] ?? new JArray()) def.Specialties.Add(ReadSpecialty(s));
                foreach (var s in (JArray?)c["growth"] ?? new JArray()) def.Growth.Add(ReadSpecialty(s));
                foreach (var g in (JArray?)c["gear"] ?? new JArray())
                    def.Gear.Add(new GearSpec { Item = ReadItem(g), Equip = (string?)g["equip"] ?? "" });
                book.Classes[def.Id] = def;
            }
            return book;
        }

        static Specialty ReadSpecialty(JToken s) => new Specialty
        {
            Kind = Enum.Parse<EffectKind>((string)s["kind"]!),
            UndeadBane = (bool?)s["undeadBane"] ?? false,
            SelfOnly = (bool?)s["selfOnly"] ?? false,
            Text = (string?)s["text"] ?? "",
        };

        /// <summary>Reads an item (classes, stories): id, name, kind, grade, bonus, trait, weapon, ability, uses, use.</summary>
        public static Item ReadItem(JToken t)
        {
            var item = new Item
            {
                Id = (string)t["id"]!,
                Name = (string?)t["name"] ?? (string)t["id"]!,
                Kind = Enum.Parse<ItemKind>((string?)t["kind"] ?? "Tool"),
                Grade = Enum.Parse<Grade>((string?)t["grade"] ?? "Plain"),
                Trait = Enum.Parse<Trait>((string?)t["trait"] ?? "None"),
                Uses = (int?)t["uses"],
                Use = (string?)t["use"] ?? "",
            };
            item.Bonus = (int?)t["bonus"] ?? item.Grade switch { Grade.Masterwork => 2, Grade.Fine when item.Trait == Trait.None => 1, _ => 0 };
            if (t["weapon"] is JToken w) item.Weapon = Enum.Parse<WeaponType>((string)w!);
            if (t["ability"] is JToken ab && AbilityNames.TryParse((string?)ab, out var a)) item.Ability = a;
            return item;
        }
    }
}
