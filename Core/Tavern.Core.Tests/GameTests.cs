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
    public class GameTests
    {
        static string ContentDir()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity", "Assets", "Resources", "Content"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Unity", "Assets", "Resources", "Content");
        }

        static (ContentDb db, ScenarioDef s) Load()
        {
            var dir = ContentDir();
            return (ContentDb.LoadFolder(dir), ContentDb.LoadScenario(File.ReadAllText(Path.Combine(dir, "Scenarios", "grey_shrine.json"))));
        }

        static LlmReply Narr(object args) => new LlmReply("", new[] { new ToolCall(GmTools.Narrate, JObject.FromObject(args)) });
        static LlmReply Plan(params object[] actions) => new LlmReply("", new[] { new ToolCall(GmTools.PlanChecks, JObject.FromObject(new { actions })) });

        static TavernGame NewGame(ScriptedLlm llm, IRandom rng, params string[] classes)
        {
            var (db, s) = Load();
            var names = new[] { "ガレス", "エリス", "ロッコ", "セラ" };
            var party = classes.Select((c, i) => TavernGame.CreateCharacter(db, names[i], c, "")).ToList();
            return TavernGame.New(db, s, Storyteller.Olwen, party, llm, null, rng);
        }

        [Test]
        public void Content_LoadsAndScenarioReferencesExist()
        {
            var (db, s) = Load();
            Assert.That(db.Classes.Keys, Is.EquivalentTo(new[] { "warrior", "mage", "thief", "priest" }));
            foreach (var c in db.Classes.Values) Assert.That(c.Cards.All(db.Cards.ContainsKey), c.Id);
            foreach (var l in s.Locations)
            {
                Assert.That(l.Exits.All(x => s.Locations.Any(o => o.Id == x)), l.Id);
                Assert.That(l.Encounter.All(db.Enemies.ContainsKey), l.Id);
            }
            Assert.That(db.Enemies.ContainsKey(s.Boss));
            Assert.That(s.Locations.Any(l => l.Id == s.Start));
        }

        [Test]
        public async Task Explore_NarrationEffectsChangeTheState()
        {
            var llm = new ScriptedLlm(
                Plan(new { character = "ロッコ", needs_check = true, stat = "技", difficulty = "普通", reason = "罠" }),
                Narr(new
                {
                    text = "毒矢がかすめた。", suggestions = new[] { "a", "b", "c" },
                    damage = new[] { new { character = "ロッコ", size = "中", cause = "毒矢" } },
                    items_gained = new[] { new { character = "ロッコ", item = "銀貨" } },
                    clues_found = new[] { "knight_tomb", "nope" },
                    move_to = "village",
                }));
            var game = NewGame(llm, new FixedRandom(5), "warrior", "mage", "thief");

            var report = await game.SubmitAsync(new[] { new Declaration { Character = "ロッコ", Text = "罠を外す" } });

            var rocco = game.Find("ロッコ")!;
            Assert.That(rocco.Hp, Is.EqualTo(14 - 4));
            Assert.That(rocco.Items, Is.EqualTo(new[] { "銀貨" }));
            Assert.That(game.State.Clues, Is.EqualTo(new[] { "knight_tomb" }));
            Assert.That(game.State.Location, Is.EqualTo("village"));
            Assert.That(report.Entries.Any(e => e.Kind == LogKind.Roll && e.Natural == 5));
            Assert.That(report.Suggestions, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task Combat_AttackThenEnemyAttacks_ThenWin()
        {
            var llm = new ScriptedLlm(
                Plan(), Narr(new { text = "狼だ！", suggestions = new[] { "x" }, start_combat = new[] { "wolf" } }),
                Narr(new { text = "斬った。", suggestions = new[] { "x" } }),
                Narr(new { text = "狼は倒れた。", suggestions = new[] { "x" } }));
            // round 1: Gareth hits (15+3 vs 11) for 3; the wolf bites the weakest (Gareth alone) and he fails to dodge (2+1 vs 12)
            // round 2: Gareth hits again (14+3) for 3 -> the wolf (6 HP) falls
            var game = NewGame(llm, new FixedRandom(15, 2, 14), "warrior");

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "森を進む" } });
            Assert.That(game.InCombat);
            Assert.That(game.State.Combat!.Enemies.Single().Name, Is.EqualTo("灰色狼"));

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Basic = "attack" } });
            Assert.That(game.State.Combat!.Enemies.Single().Hp, Is.EqualTo(3));
            Assert.That(game.Find("ガレス")!.Hp, Is.EqualTo(18 - 2));

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Basic = "attack" } });
            Assert.That(game.InCombat, Is.False);
            Assert.That(game.State.Log.Any(e => e.Text == "勝利"));
        }

        [Test]
        public async Task Fireball_HitsAllEnemies_AndCostsFocus()
        {
            var llm = new ScriptedLlm(
                Plan(), Narr(new { text = "", suggestions = new string[0], start_combat = new[] { "goblin", "goblin" } }),
                Narr(new { text = "炎！", suggestions = new string[0] }));
            var game = NewGame(llm, new FixedRandom(19, 19), "mage");   // the two goblins attack after: both dodged with 19
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "進む" } });

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", CardId = "fireball" } });

            Assert.That(game.State.Combat!.Enemies.Select(e => e.Hp), Is.EqualTo(new[] { 1, 1 }), string.Join(" | ", game.State.Log.Select(e => e.Text)));
            Assert.That(game.Find("ガレス")!.Focus, Is.EqualTo(6 - 3));
            Assert.That(game.State.Combat.Enemies.Select(e => e.Name), Is.EqualTo(new[] { "沼のゴブリンA", "沼のゴブリンB" }));
        }

        [Test]
        public async Task Boss_WarnsTheRoundBefore_ThenHitsEveryone()
        {
            var narr = Narr(new { text = "…", suggestions = new string[0] });
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "", suggestions = new string[0], start_combat = new[] { "boneknight" } }),
                narr, narr, narr, narr);
            // the entrance narration, then rounds: everyone defends; dodges: 20 (r1), 20 (r2, warning given), 1 1 (r3 big move, both fail badly)
            var game = NewGame(llm, new FixedRandom(20, 20, 1, 1), "warrior", "priest");
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "玄室へ" } });

            var defend = new[] { new Declaration { Character = "ガレス", Basic = "defend" }, new Declaration { Character = "セラ".Replace("セラ", "エリス"), Basic = "defend" } };
            await game.SubmitAsync(defend);
            var r2 = await game.SubmitAsync(defend);
            Assert.That(r2.Entries.Any(e => e.Kind == LogKind.Warning));
            var r3 = await game.SubmitAsync(defend);
            Assert.That(r3.Entries.Count(e => e.Kind == LogKind.Roll), Is.EqualTo(2));
            Assert.That(game.Find("ガレス")!.Hp, Is.EqualTo(18 - 8));   // fumbled dodge doubles the 4
        }

        [Test]
        public async Task FateCoin_RerollsAFailedCheck_BeforeTheNarration()
        {
            var llm = new ScriptedLlm(
                Plan(new { character = "ガレス", needs_check = true, stat = "力", difficulty = "難しい", reason = "扉を破る" }),
                Narr(new { text = "扉が砕けた。", suggestions = new[] { "a" } }));
            var game = NewGame(llm, new FixedRandom(3, 17), "warrior");
            game.ChooseRerolls = failed => Task.FromResult<IReadOnlyList<LogEntry>>(failed.ToList());

            var report = await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "扉を蹴破る" } });

            Assert.That(report.Entries.Count(e => e.Kind == LogKind.Roll), Is.EqualTo(2));
            Assert.That(game.Find("ガレス")!.FateCoins, Is.EqualTo(0));
            // the AI only hears the reroll
            var answer = game.Gm.History.Last(m => m.Role == Role.Tool && m.ToolName == GmTools.PlanChecks).Content;
            Assert.That(answer, Does.Contain("17+3").And.Contain("振り直し"));
            Assert.That(answer, Does.Not.Contain("3+3"));
        }

        [Test]
        public async Task FateCoin_InAFight_RerollsAFailedDodge()
        {
            var llm = new ScriptedLlm(
                Plan(), Narr(new { text = "狼だ！", suggestions = new[] { "x" }, start_combat = new[] { "wolf" } }),
                Narr(new { text = "…", suggestions = new[] { "x" } }));
            // Gareth defends (no roll); the wolf bites: dodge 2+1+3 fails, the coin rerolls it to 19 -> no damage
            var game = NewGame(llm, new FixedRandom(2, 19), "warrior");
            game.ChooseRerolls = failed => Task.FromResult<IReadOnlyList<LogEntry>>(failed.ToList());
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "進む" } });

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Basic = "defend" } });

            Assert.That(game.Find("ガレス")!.Hp, Is.EqualTo(18));
            Assert.That(game.Find("ガレス")!.FateCoins, Is.EqualTo(0));
        }

        [Test]
        public async Task NextAct_GivesEveryoneAGrowthChoice()
        {
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "祠に着いた。", suggestions = new[] { "a" }, next_act = true, move_to = "shrine_front" }));
            var game = NewGame(llm, new FixedRandom(), "warrior", "mage");
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "祠へ" } });

            Assert.That(game.State.Act, Is.EqualTo(2));
            Assert.That(game.State.PendingGrowth, Is.EquivalentTo(new[] { "ガレス", "エリス" }));
            Assert.That(game.Grow("ガレス", "hp"), Is.True);
            Assert.That(game.Find("ガレス")!.MaxHp, Is.EqualTo(21));
            Assert.That(game.Grow("エリス", "知"), Is.True);
            Assert.That(game.Find("エリス")!.Modifier(Stat.Wits), Is.EqualTo(4));
            Assert.That(game.Grow("エリス", "知"), Is.False);
            Assert.That(game.State.PendingGrowth, Is.Empty);
        }

        [Test]
        public async Task TurnMessage_MirrorsTheHostGame()
        {
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "狼だ！", suggestions = new[] { "a", "b" }, start_combat = new[] { "wolf", "wolf" } }));
            var game = NewGame(llm, new FixedRandom(), "warrior", "mage");
            game.State.Owners["ガレス"] = "host"; game.State.Owners["エリス"] = "friend";
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "進む" } });

            var json = NetJson.Json(TurnMsg.From(game, new[] { "エリス" }, busy: false));
            var msg = NetJson.Read<TurnMsg>(json);
            var (db, s) = Load();
            var mirror = Mirror.Build(msg, db, s);

            Assert.That(mirror.InCombat);
            Assert.That(mirror.State.Combat!.Enemies.Select(e => e.Name), Is.EqualTo(new[] { "灰色狼A", "灰色狼B" }));
            Assert.That(mirror.State.Suggestions, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(mirror.State.Owners["エリス"], Is.EqualTo("friend"));
            Assert.That(mirror.State.Log.Last().Seq, Is.EqualTo(game.State.Log.Count));
            Assert.That(msg.Declared, Is.EqualTo(new[] { "エリス" }));
            Assert.That(mirror.Party.Select(p => p.Name), Is.EqualTo(new[] { "ガレス", "エリス" }));
        }

        [Test]
        public async Task SaveAndLoad_KeepsStateAndHistory()
        {
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "村に着いた。", suggestions = new[] { "a" }, move_to = "village" }));
            var game = NewGame(llm, new FixedRandom(), "thief");
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "村へ行く" } });
            game.Find("ガレス")!.TakeDamage(3);

            var json = SaveData.From(game).ToJson();
            var (db, s) = Load();
            var loaded = SaveData.FromJson(json).Load(db, s, Storyteller.Olwen, new ScriptedLlm(), null, new FixedRandom());

            Assert.That(loaded.State.Location, Is.EqualTo("village"));
            Assert.That(loaded.Find("ガレス")!.Hp, Is.EqualTo(11));
            Assert.That(loaded.Find("ガレス")!.Cards, Is.EqualTo(new[] { "backstab", "lockpick", "smoke" }));
            Assert.That(loaded.Gm.History.Count, Is.EqualTo(game.Gm.History.Count));
            Assert.That(loaded.Gm.History[0].Role, Is.EqualTo(Role.System));
            Assert.That(loaded.State.Log.Last().Text, Is.EqualTo(game.State.Log.Last().Text));
        }
    }
}
