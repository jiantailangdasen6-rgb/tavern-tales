#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Tavern.Core.Content
{
    // Plain data read from Unity/Assets/Resources/Content/*.json (the single source; the CLI and tests read the same files).

    public sealed class ClassDef
    {
        public string Id = "", Name = "", AttackStat = "力", Weapon = "", Blurb = "";
        public int Might, Skill, Wits, Spirit, Hp, Focus, WeaponDamage;
        public List<string> Cards = new List<string>();
    }

    public sealed class CardDef
    {
        public string Id = "", ClassId = "", Name = "", Kind = "", Target = "none", Text = "";
        /// <summary>Stat used for the roll ("" when the card needs none).</summary>
        public string Stat = "";
        public int Cost, Damage, Amount, Bonus, UndeadBonus;
        public bool AutoHit;
    }

    public sealed class EnemyAttack
    {
        public string Name = "";
        /// <summary>The players' dodge roll must reach this.</summary>
        public int Dc, Damage;
    }

    public sealed class BigMove
    {
        public string Name = "", Warning = "";
        public int Every, Dc, Damage;
    }

    public sealed class EnemyDef
    {
        public string Id = "", Name = "", Art = "", Pattern = "random";
        public int Hp, Defense;
        public bool Undead, Boss;
        public List<EnemyAttack> Attacks = new List<EnemyAttack>();
        public BigMove? BigMove;
    }

    public sealed class ActDef
    {
        public int N, Minutes;
        public string Title = "", Goal = "";
    }

    public sealed class LocationDef
    {
        public string Id = "", Name = "", Art = "", Description = "";
        public string? Secret, Trap;
        public int Act;
        public List<string> Exits = new List<string>();
        public List<string> Encounter = new List<string>();
    }

    public sealed class NpcDef
    {
        public string Id = "", Name = "", Art = "", Where = "", Role = "";
    }

    public sealed class ClueDef
    {
        public string Id = "", Text = "", Where = "";
    }

    public sealed class Endings
    {
        public string Success = "", Bitter = "", Failure = "";
    }

    /// <summary>One evening's story sheet (filled template). The AI reads it; the game tracks progress through it.</summary>
    public sealed class ScenarioDef
    {
        public string Id = "", Title = "", Template = "", Hook = "", Truth = "", Weakness = "", Start = "", Boss = "";
        public List<ActDef> Acts = new List<ActDef>();
        public List<LocationDef> Locations = new List<LocationDef>();
        public List<NpcDef> Npcs = new List<NpcDef>();
        public List<ClueDef> Clues = new List<ClueDef>();
        public Endings Endings = new Endings();

        public LocationDef Location(string id) => Locations.First(l => l.Id == id);
    }

    /// <summary>All static content. Load once from the JSON texts.</summary>
    public sealed class ContentDb
    {
        public IReadOnlyDictionary<string, ClassDef> Classes { get; }
        public IReadOnlyDictionary<string, CardDef> Cards { get; }
        public IReadOnlyDictionary<string, EnemyDef> Enemies { get; }

        ContentDb(IEnumerable<ClassDef> classes, IEnumerable<CardDef> cards, IEnumerable<EnemyDef> enemies)
        {
            Classes = classes.ToDictionary(c => c.Id);
            Cards = cards.ToDictionary(c => c.Id);
            Enemies = enemies.ToDictionary(e => e.Id);
        }

        sealed class ClassFile { public List<ClassDef> Classes = new List<ClassDef>(); }
        sealed class CardFile { public List<CardDef> Cards = new List<CardDef>(); }
        sealed class EnemyFile { public List<EnemyDef> Enemies = new List<EnemyDef>(); }

        public static ContentDb Load(string classesJson, string cardsJson, string enemiesJson)
            => new ContentDb(
                JsonConvert.DeserializeObject<ClassFile>(classesJson)!.Classes,
                JsonConvert.DeserializeObject<CardFile>(cardsJson)!.Cards,
                JsonConvert.DeserializeObject<EnemyFile>(enemiesJson)!.Enemies);

        public static ScenarioDef LoadScenario(string json) => JsonConvert.DeserializeObject<ScenarioDef>(json)!;

        /// <summary>Reads everything from a Content folder on disk (CLI, tests).</summary>
        public static ContentDb LoadFolder(string folder)
            => Load(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "classes.json")),
                    System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "cards.json")),
                    System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "enemies.json")));
    }
}
