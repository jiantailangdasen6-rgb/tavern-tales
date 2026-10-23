#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Tavern.Core.Play
{
    public sealed class EnemyAttack
    {
        public string Name { get; set; } = "";
        /// <summary>The dodge target (AGI).</summary>
        public int Dc { get; set; }
        public int Damage { get; set; }
        /// <summary>A hit poisons.</summary>
        public bool Poison { get; set; }
    }

    /// <summary>A boss's big move, announced one turn before.</summary>
    public sealed class BigMove
    {
        public string Name { get; set; } = "";
        public int Every { get; set; }
        public int Dc { get; set; }
        public int Damage { get; set; }
        /// <summary>True: one adventurer only (the assassin's thrust). Otherwise everyone dodges.</summary>
        public bool Single { get; set; }
        /// <summary>Calls this many enemies of the id instead of striking (the captain's whistle).</summary>
        public string Summon { get; set; } = "";
        public int Count { get; set; }
        public string Warning { get; set; } = "";
    }

    public enum Targeting { Random, Weakest, Strongest }

    public sealed class EnemyDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Art { get; set; } = "";
        public int Hp { get; set; }
        public int Defense { get; set; }
        public bool Undead { get; set; }
        public bool Boss { get; set; }
        public int Speed { get; set; } = 10;
        public Targeting Pattern { get; set; }
        public List<EnemyAttack> Attacks { get; } = new List<EnemyAttack>();
        public BigMove? BigMove { get; set; }
        /// <summary>Seeing the party raises the story's alarm clock (guards).</summary>
        public bool Alarm { get; set; }
    }

    /// <summary>The enemies with art (Resources/Content/Rules/enemies.json).</summary>
    public sealed class Bestiary
    {
        public Dictionary<string, EnemyDef> Enemies { get; } = new Dictionary<string, EnemyDef>();
        public EnemyDef this[string id] => Enemies.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException("enemy " + id);
        public bool Has(string id) => Enemies.ContainsKey(id);

        public static Bestiary Load(string json)
        {
            var b = new Bestiary();
            foreach (var e in (JArray)JObject.Parse(json)["enemies"]!)
            {
                var d = new EnemyDef
                {
                    Id = (string)e["id"]!, Name = (string)e["name"]!, Art = (string?)e["art"] ?? "",
                    Hp = (int)e["hp"]!, Defense = (int)e["defense"]!, Undead = (bool?)e["undead"] ?? false, Boss = (bool?)e["boss"] ?? false,
                    Speed = (int?)e["speed"] ?? 10, Alarm = (bool?)e["alarm"] ?? false,
                    Pattern = ((string?)e["pattern"]) switch { "weakest" => Targeting.Weakest, "strongest" => Targeting.Strongest, _ => Targeting.Random },
                };
                foreach (var a in (JArray?)e["attacks"] ?? new JArray())
                    d.Attacks.Add(new EnemyAttack { Name = (string)a["name"]!, Dc = (int)a["dc"]!, Damage = (int)a["damage"]!, Poison = (bool?)a["poison"] ?? false });
                if (e["bigMove"] is JObject m)
                    d.BigMove = new BigMove
                    {
                        Name = (string)m["name"]!, Every = (int?)m["every"] ?? 3, Dc = (int?)m["dc"] ?? 0, Damage = (int?)m["damage"] ?? 0,
                        Single = (string?)m["target"] == "one", Summon = (string?)m["summon"] ?? "", Count = (int?)m["count"] ?? 0,
                        Warning = (string?)m["warning"] ?? "",
                    };
                b.Enemies[d.Id] = d;
            }
            return b;
        }
    }

    public enum Side { Party, Foe }

    /// <summary>A companion NPC's job in a fight (the game moves it by this).</summary>
    public enum CompanionRole { Attack, Defend, Heal }

    /// <summary>Fighting strength of an NPC or a companion: weak (5, 10, 1) / normal (9, 12, 2) / strong (14, 13, 3).</summary>
    public enum Build { Weak, Normal, Strong }

    /// <summary>Anything that fights and is not an adventurer: enemies, companion NPCs, story people turned hostile.</summary>
    public sealed class Creature
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Art { get; set; } = "";
        /// <summary>The enemy table id, or the story person id.</summary>
        public string Source { get; set; } = "";
        public Side Side { get; set; } = Side.Foe;
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int Defense { get; set; }
        public bool Undead { get; set; }
        public bool Boss { get; set; }
        public int Speed { get; set; } = 10;
        public Targeting Pattern { get; set; }
        public List<EnemyAttack> Attacks { get; } = new List<EnemyAttack>();
        public BigMove? BigMove { get; set; }
        public CompanionRole Role { get; set; }
        public bool Alarm { get; set; }
        public StatusSet Statuses { get; } = new StatusSet();
        /// <summary>Damage still to come at the start of its next turn (炎).</summary>
        public int Burning { get; set; }
        /// <summary>Gone from the fight without falling (fled, retreated, surrendered).</summary>
        public bool Left { get; set; }
        public bool Killed { get; set; }
        /// <summary>動きを止める (large) works on a boss once per fight.</summary>
        public bool HeldOnce { get; set; }

        public bool Down => Hp <= 0;
        public bool InFight => !Down && !Left;
        public bool CanAct => InFight && Statuses.CanAct;
        /// <summary>弱い敵: not a boss and 6 HP or less (small holds work on these).</summary>
        public bool Weak => !Boss && MaxHp <= 6;
        /// <summary>The dodge bonus when it is attacked by enemies (companions): defense - 10.</summary>
        public int DodgeModifier => Defense - 10;

        public void TakeDamage(int amount)
        {
            if (amount <= 0 || Down) return;
            Hp = Math.Max(0, Hp - amount);
            Statuses.WakeOnHit();
        }

        public void Heal(int amount) { if (!Killed && amount > 0) Hp = Math.Min(MaxHp, Hp + amount); }

        public static Creature FromEnemy(EnemyDef d, string id, string name, int hp)
        {
            var c = new Creature
            {
                Id = id, Name = name, Art = d.Art, Source = d.Id, Side = Side.Foe, MaxHp = hp, Hp = hp, Defense = d.Defense,
                Undead = d.Undead, Boss = d.Boss, Speed = d.Speed, Pattern = d.Pattern, BigMove = d.BigMove, Alarm = d.Alarm,
            };
            c.Attacks.AddRange(d.Attacks);
            return c;
        }

        /// <summary>A story person or companion with a build (09_Spec 8). Speed is normal.</summary>
        public static Creature FromBuild(Build build, string id, string name, Side side, CompanionRole role = CompanionRole.Attack, string art = "")
        {
            var (hp, def, atk) = Stats(build);
            var c = new Creature
            {
                Id = id, Name = name, Art = art, Source = id, Side = side, MaxHp = hp, Hp = hp, Defense = def, Speed = 10,
                Pattern = Targeting.Random, Role = role,
            };
            c.Attacks.Add(new EnemyAttack { Name = "攻撃", Dc = 10 + atk, Damage = atk });
            return c;
        }

        public static (int hp, int defense, int attack) Stats(Build b) => b switch
        {
            Build.Weak => (5, 10, 1), Build.Strong => (14, 13, 3), _ => (9, 12, 2),
        };
    }
}
