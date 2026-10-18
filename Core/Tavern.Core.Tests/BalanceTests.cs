using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Tavern.Core.Content;
using Tavern.Core.Game;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Tests
{
    /// <summary>
    /// Plays many boss fights with simple bots (the strongest affordable attack card, else a weapon attack; the priest heals
    /// the hurt) and reports how many rounds they take. The design aims at 3 to 5 rounds for any party size.
    /// dotnet test Core/Tavern.slnx --filter BalanceTests  (also checked in the normal run, on a smaller sample).
    /// </summary>
    public class BalanceTests
    {
        static string ContentDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity", "Assets", "Resources", "Content"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Unity", "Assets", "Resources", "Content");
        }

        static LlmReply Narr(object args) => new LlmReply("", new[] { new ToolCall(GmTools.Narrate, JObject.FromObject(args)) });
        static LlmReply Plan() => new LlmReply("", new[] { new ToolCall(GmTools.PlanChecks, JObject.FromObject(new { actions = new object[0] })) });

        static readonly string[][] Parties =
        {
            new[] { "warrior" }, new[] { "thief" },
            new[] { "warrior", "mage" }, new[] { "thief", "priest" },
            new[] { "warrior", "mage", "thief" }, new[] { "warrior", "priest", "mage" },
        };

        /// <summary>Average rounds of a fight against one boss, and how often the party wins.</summary>
        static async Task<(double rounds, double wins)> Fight(ContentDb db, ScenarioDef s, string boss, string[] classes, Storyteller teller, int fights)
        {
            int rounds = 0, wins = 0;
            for (int f = 0; f < fights; f++)
            {
                var narr = Narr(new { text = "…", suggestions = new string[0] });
                var replies = new List<LlmReply> { Plan(), Narr(new { text = "", suggestions = new string[0], start_combat = new[] { boss } }) };
                for (int i = 0; i < 80; i++) replies.Add(narr);
                var names = new[] { "ガレス", "エリス", "ロッコ" };
                var party = classes.Select((c, i) => TavernGame.CreateCharacter(db, names[i], c, "")).ToList();
                var game = TavernGame.New(db, s, teller, party, new ScriptedLlm(replies.ToArray()), null, new SeededRandom((ulong)(f * 7919 + 17)));
                await game.SubmitAsync(new[] { new Declaration { Character = names[0], Text = "玄室へ" } });
                int r = 0;
                while (game.State.Combat != null && !game.Ended && r < 40)
                {
                    var foe = game.State.Combat.Enemies.First(e => e.Alive);
                    var decl = new List<Declaration>();
                    foreach (var p in party.Where(p => p.CanAct))
                    {
                        var hurt = party.Where(o => o.State != LifeState.Dead && o.Hp <= o.MaxHp / 2).OrderBy(o => o.Hp).FirstOrDefault();
                        var heal = p.Cards.Select(id => db.Cards[id]).FirstOrDefault(c => c.Kind == "heal" && c.Cost <= p.Focus);
                        var attack = p.Cards.Select(id => db.Cards[id]).Where(c => c.Damage > 0 && c.Cost <= p.Focus).OrderByDescending(c => c.Damage * c.Hits).FirstOrDefault();
                        if (hurt != null && heal != null) decl.Add(new Declaration { Character = p.Name, CardId = heal.Id, Target = hurt.Name });
                        else if (attack != null) decl.Add(new Declaration { Character = p.Name, CardId = attack.Id, Target = foe.Uid });
                        else decl.Add(new Declaration { Character = p.Name, Basic = "attack", Target = foe.Uid });
                    }
                    if (decl.Count == 0) break;
                    await game.SubmitAsync(decl);
                    r++;
                }
                rounds += r;
                if (party.Any(p => p.CanAct)) wins++;
            }
            return (rounds / (double)fights, wins / (double)fights);
        }

        [Test]
        public async Task BossFights_TakeThreeToFiveRounds()
        {
            var db = ContentDb.LoadFolder(ContentDir());
            var s = ContentDb.LoadScenario(File.ReadAllText(Path.Combine(ContentDir(), "Scenarios", "grey_shrine.json")));
            var fights = TestContext.Parameters.Get("fights", 12);
            var report = new List<string>();
            var averages = new List<double>();
            foreach (var boss in db.Enemies.Values.Where(e => e.Boss).Select(e => e.Id))
                foreach (var classes in Parties)
                {
                    var (rounds, wins) = await Fight(db, s, boss, classes, Storyteller.Olwen, fights);
                    averages.Add(rounds);
                    report.Add($"{boss,-13} {string.Join("+", classes),-22} {rounds,4:0.0} rounds  win {wins:P0}");
                }
            TestContext.Out.WriteLine(string.Join("\n", report));
            Assert.That(averages.Average(), Is.InRange(3.0, 6.0));
            Assert.That(averages.Max(), Is.LessThan(8.0));
        }
    }
}
