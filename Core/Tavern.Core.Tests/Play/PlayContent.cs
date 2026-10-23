using System.IO;
using NUnit.Framework;
using Tavern.Core.Play;

namespace Tavern.Core.Tests.Play
{
    /// <summary>The rule content the game ships (Unity/Assets/Resources/Content/Rules).</summary>
    static class PlayContent
    {
        static ClassBook? _classes;
        static Bestiary? _enemies;

        public static string Dir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity", "Assets", "Resources", "Content"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Unity", "Assets", "Resources", "Content");
        }

        public static ClassBook Classes => _classes ??= ClassBook.Load(File.ReadAllText(Path.Combine(Dir(), "Rules", "classes.json")));
        public static Bestiary Enemies => _enemies ??= Bestiary.Load(File.ReadAllText(Path.Combine(Dir(), "Rules", "enemies.json")));

        public static Adventurer Warrior(string name = "ガレス") => new Adventurer(Classes["warrior"], name);
        public static Adventurer Mage(string name = "エリス") => new Adventurer(Classes["mage"], name);
        public static Adventurer Thief(string name = "ロッコ") => new Adventurer(Classes["thief"], name);
        public static Adventurer Priest(string name = "ミーナ") => new Adventurer(Classes["priest"], name);

        public static Creature Enemy(string id, int? hp = null)
        {
            var d = Enemies[id];
            return Creature.FromEnemy(d, id, d.Name, hp ?? d.Hp);
        }
    }
}
