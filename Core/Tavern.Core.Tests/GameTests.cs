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
        public async Task Suggestions_DoNotGiveAwayWhatIsAhead()
        {
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "祠の前に着いた。古い扉がある。", suggestions = new[] { "現れたアンデッドと戦う準備をする", "扉を調べる", "罠を外して進む" } }),
                Plan(), Narr(new { text = "扉の下に細い糸が張られている。罠だ。", suggestions = new[] { "罠を外す", "骨の騎士に挑む" } }));
            var game = NewGame(llm, new FixedRandom(), "warrior");
            var first = await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "祠へ向かう" } });
            Assert.That(first.Suggestions, Is.EqualTo(new[] { "扉を調べる" }));
            var second = await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "扉を調べる" } });
            Assert.That(second.Suggestions, Is.EqualTo(new[] { "罠を外す" }));   // the trap was told of now; the knight still not
        }

        [Test]
        public async Task BlindSuggester_NeverSeesWhatThePlayersDoNotKnow()
        {
            var narrator = new ScriptedLlm(Plan(), Narr(new { text = "祠の前に着いた。古い扉がある。", move_to = "shrine_front" }),
                Plan(), Narr(new { text = "扉の奥は暗い。" }));
            var suggester = new ScriptedLlm(
                new LlmReply("", new[] { new ToolCall(Suggester.ToolName, JObject.FromObject(new { suggestions = new[] { "扉に耳を当てる", "碑文を読む", "骨の騎士に挑む" } })) }),
                new LlmReply("", new[] { new ToolCall(Suggester.ToolName, JObject.FromObject(new { suggestions = new[] { "松明をかざす", "引き返す", "奥へ進む" } })) }));
            var (db, s) = Load();
            var game = TavernGame.New(db, s, Storyteller.Olwen, new List<Character> { TavernGame.CreateCharacter(db, "ガレス", "warrior", "") }, narrator, null, new FixedRandom());
            game.SuggestLlm = suggester;

            var first = await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "祠へ向かう" } });
            Assert.That(first.Suggestions, Is.EqualTo(new[] { "扉に耳を当てる", "碑文を読む" }));   // the knight was never told of
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "扉を開ける" } });

            // the storyteller has no suggestions field any more; the suggester got only the public view, never a secret
            Assert.That(narrator.Forced, Has.None.EqualTo(Suggester.ToolName));
            var seen = string.Join("\n", suggester.Sent.SelectMany(m => m).Select(m => m.Content));
            var secrets = new[] { s.Truth, s.Weakness }
                .Concat(s.Locations.SelectMany(l => new[] { l.Secret, l.Trap }))
                .Concat(s.Npcs.Select(n => n.Role))
                .Concat(s.Clues.Where(c => !game.State.Clues.Contains(c.Id)).Select(c => c.Text))
                .Append(db.Enemies[s.Boss].Name)
                .Where(x => !string.IsNullOrWhiteSpace(x));
            foreach (var secret in secrets) Assert.That(seen, Does.Not.Contain(secret), secret);
            Assert.That(seen, Does.Contain("祠の前に着いた"));
        }

        [Test]
        public void EveryFoePlaceAndPersonHasArt()
        {
            var dir = ContentDir();
            var art = Path.Combine(Path.GetDirectoryName(dir)!, "Art");
            var db = ContentDb.LoadFolder(dir);
            var story = StoryContent.LoadFolder(dir);
            var ids = db.Enemies.Values.Select(e => e.Art).Concat(story.Catalog.Backgrounds.Select(b => b.Id)).Concat(story.Catalog.Portraits.Select(p => p.Id));
            foreach (var id in ids) Assert.That(File.Exists(Path.Combine(art, id + ".jpg")), id);
        }

        [Test]
        public void PreparedStories_AreConsistent()
        {
            var dir = ContentDir();
            var db = ContentDb.LoadFolder(dir);
            var story = StoryContent.LoadFolder(dir);
            var bgs = story.Catalog.Backgrounds.Select(b => b.Id).ToHashSet();
            var pts = story.Catalog.Portraits.Select(p => p.Id).ToHashSet();
            var files = Directory.GetFiles(Path.Combine(dir, "Scenarios"), "*.json");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(4));
            foreach (var file in files)
            {
                var s = ContentDb.LoadScenario(File.ReadAllText(file));
                var ids = s.Locations.Select(l => l.Id).ToHashSet();
                Assert.That(ids, Does.Contain(s.Start), s.Id);
                Assert.That(s.Acts.Select(a => a.N), Is.EqualTo(new[] { 1, 2, 3 }), s.Id);
                foreach (var l in s.Locations)
                {
                    Assert.That(bgs, Does.Contain(l.Art), $"{s.Id}/{l.Id} art");
                    foreach (var x in l.Exits)
                    {
                        Assert.That(ids, Does.Contain(x), $"{s.Id}/{l.Id} exit {x}");
                        Assert.That(s.Location(x).Exits, Does.Contain(l.Id), $"{s.Id}: {x} leads back to {l.Id}");
                    }
                    Assert.That(l.Encounter.All(db.Enemies.ContainsKey), $"{s.Id}/{l.Id} foes");
                }
                Assert.That(db.Enemies[s.Boss].Boss, s.Id);
                Assert.That(s.Locations.Any(l => l.Act == 3 && l.Encounter.Contains(s.Boss)), $"{s.Id}: the boss waits in act 3");
                foreach (var n in s.Npcs) { Assert.That(pts, Does.Contain(n.Art), $"{s.Id}/{n.Id}"); Assert.That(ids, Does.Contain(n.Where), $"{s.Id}/{n.Id}"); }
                foreach (var c in s.Clues) Assert.That(ids, Does.Contain(c.Where), $"{s.Id}/{c.Id}");
                // every place can be reached from the tavern
                var seen = new HashSet<string> { s.Start };
                var todo = new Queue<string>(seen);
                while (todo.Count > 0) foreach (var x in s.Location(todo.Dequeue()).Exits) if (seen.Add(x)) todo.Enqueue(x);
                Assert.That(seen, Is.EquivalentTo(ids), $"{s.Id}: all places reachable");
            }
        }

        [Test]
        public void MusicMap_IsValidJsonAndPointsAtFiles()
        {
            var resources = Path.GetDirectoryName(ContentDir())!;
            var map = JObject.Parse(File.ReadAllText(Path.Combine(resources, "Music", "music.json")));   // a broken file once blanked the title screen
            foreach (var path in ((JObject)map["music"]!).Properties().Concat(((JObject)map["sfx"]!).Properties()).Select(p => (string)p.Value!).Append((string)map["ambience"]!))
                Assert.That(Directory.GetFiles(Path.Combine(resources, Path.GetDirectoryName(path)!), Path.GetFileName(path) + ".*").Any(f => !f.EndsWith(".meta")), path);
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
                Plan(), Narr(new { text = "狼だ！", suggestions = new[] { "x" }, start_combat = new[] { "wolf", "wolf" } }),
                Narr(new { text = "斬った。", suggestions = new[] { "x" } }),
                Narr(new { text = "狼は倒れた。", suggestions = new[] { "x" } }));
            // alone, Gareth meets wolves of 3 HP (6 for a party of three, PartyScale 0.45)
            // round 1: Gareth hits the first (15+3 vs 11) for 3 and it falls; the other bites him and he fails to dodge (2+1 vs 12)
            // round 2: Gareth hits the second (14+3) for 3 -> it falls too
            var game = NewGame(llm, new FixedRandom(15, 2, 14), "warrior");

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "森を進む" } });
            Assert.That(game.InCombat);
            Assert.That(game.State.Combat!.Enemies.Select(e => e.Hp), Is.EqualTo(new[] { 3, 3 }));

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Basic = "attack", Target = "wolf1" } });
            Assert.That(game.State.Combat!.Enemies.Select(e => e.Hp), Is.EqualTo(new[] { 0, 3 }));
            Assert.That(game.Find("ガレス")!.Hp, Is.EqualTo(18 - 2));

            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Basic = "attack", Target = "wolf2" } });
            Assert.That(game.InCombat, Is.False);
            Assert.That(game.State.Log.Any(e => e.Text == "勝利"));
        }

        [Test]
        public async Task Fireball_HitsAllEnemies_AndCostsFocus()
        {
            var llm = new ScriptedLlm(
                Plan(), Narr(new { text = "", suggestions = new string[0], start_combat = new[] { "goblin", "goblin" } }),
                Narr(new { text = "炎！", suggestions = new string[0] }));
            var game = NewGame(llm, new FixedRandom(0, 19, 0, 19), "mage", "warrior", "thief");   // three, so full-strength goblins; each picks Gareth (0) and is dodged with 19
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
        public async Task NewCards_MultiHit_UndeadOnly_Taunt()
        {
            var narr = Narr(new { text = "…", suggestions = new string[0] });
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "", suggestions = new string[0], start_combat = new[] { "goblin", "skeleton" } }), narr, narr);
            // mage: arcane x3 at the goblin (15, 15, 2 -> two hits of 2+... ), warrior taunts; then the enemies both attack the warrior
            var game = NewGame(llm, new FixedRandom(15, 15, 2, 20, 20), "mage", "warrior", "thief");   // three: full-strength enemies
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "進む" } });
            var mage = game.Find("ガレス")!; var warrior = game.Find("エリス")!;
            mage.Cards.Add("arcane"); warrior.Cards.Add("taunt");

            await game.SubmitAsync(new[]
            {
                new Declaration { Character = "ガレス", CardId = "arcane", Target = "goblin1" },
                new Declaration { Character = "エリス", CardId = "taunt" },
            });

            Assert.That(game.State.Combat!.Enemies.First(e => e.DefId == "goblin").Hp, Is.EqualTo(5 - 4));
            // both enemy attacks went to the taunting warrior (two dodge rolls by エリス, both 20)
            Assert.That(game.State.Log.Count(e => e.Kind == LogKind.Roll && e.Who == "エリス"), Is.EqualTo(2));
            Assert.That(game.State.Log.Count(e => e.Kind == LogKind.Roll && e.Who == "ガレス" && e.Text.Contains("かわす")), Is.EqualTo(0));
        }

        [Test]
        public async Task Growth_CanLearnACardOfTheClass()
        {
            var llm = new ScriptedLlm(Plan(), Narr(new { text = "", suggestions = new[] { "a" }, next_act = true }));
            var game = NewGame(llm, new FixedRandom(), "priest");
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "進む" } });

            var options = game.GrowthCards("ガレス");
            Assert.That(options, Has.Count.EqualTo(2));
            Assert.That(options.All(c => c.ClassId == "priest" && !new[] { "heal", "holylight", "bless" }.Contains(c.Id)));
            Assert.That(game.Grow("ガレス", "card:" + options[0].Id), Is.True);
            Assert.That(game.Find("ガレス")!.Cards, Does.Contain(options[0].Id));
        }

        [Test]
        public async Task Ending_GoesToTheChronicle_AndCharactersCarryOver()
        {
            var llm = new ScriptedLlm(Plan(),
                Narr(new { text = "若者を連れ帰った。", suggestions = new string[0], items_gained = new[] { new { character = "ガレス", item = "金の燭台" } }, end_story = "success" }),
                Narr(new { text = "こうして、灰色の森に平和が戻った。", suggestions = new string[0] }));
            var game = NewGame(llm, new FixedRandom(), "warrior", "mage");
            game.Find("エリス")!.TakeDamage(99);
            game.Find("エリス")!.Restore(0, 0, 0, LifeState.Dead, 3, 0);
            await game.SubmitAsync(new[] { new Declaration { Character = "ガレス", Text = "村へ帰る" } });

            var entry = Chronicle.From(game, new System.DateTime(2026, 10, 18));
            Assert.That(entry.Ending, Is.EqualTo("success"));
            Assert.That(entry.Story, Is.EqualTo("こうして、灰色の森に平和が戻った。"));
            Assert.That(entry.Fallen, Is.EqualTo(new[] { "エリス" }));
            var chronicle = new Chronicle();
            Assert.That(chronicle.Add(entry), Is.True);
            Assert.That(chronicle.Add(entry), Is.False);

            var roster = new Roster();
            roster.Record(game, _ => true);
            roster.Record(game, _ => true);   // twice is the same as once
            var gareth = roster.Living.Single();
            Assert.That(gareth.Name, Is.EqualTo("ガレス"));
            Assert.That(gareth.Nights, Is.EqualTo(1));
            Assert.That(gareth.Keepsakes, Is.EqualTo(new[] { "金の燭台" }));
            Assert.That(gareth.Titles, Is.EqualTo(new[] { "灰色の森の祠の英雄" }));
            Assert.That(roster.Fallen.Single().Name, Is.EqualTo("エリス"));

            var (db, _) = Load();
            var again = gareth.ToCharacter(db);
            Assert.That(again.Hp, Is.EqualTo(18));
            Assert.That(again.Items, Is.EqualTo(new[] { "金の燭台" }));
            Assert.That(again.Background, Does.Contain("灰色の森の祠の英雄"));
        }

        [Test]
        public async Task GeneratedScenario_IsRepairedIntoAPlayableSheet()
        {
            var (db, _) = Load();
            var story = StoryContent.LoadFolder(ContentDir());
            var party = new List<Character> { TavernGame.CreateCharacter(db, "ガレス", "warrior", "没落騎士") };
            var s = await ScenarioGenerator.GenerateAsync(new DemoLlm(), story, db, story.Templates[0], party, "海賊", new FixedRandom(0, 0));

            Assert.That(s.Id, Does.StartWith("gen_"));
            Assert.That(s.Start, Is.EqualTo("tavern"));
            Assert.That(s.Locations[0].Id, Is.EqualTo("tavern"));
            var ids = s.Locations.Select(l => l.Id).ToHashSet();
            Assert.That(s.Locations.All(l => l.Exits.All(ids.Contains)));
            Assert.That(db.Enemies[s.Boss].Boss);
            Assert.That(s.Locations.Single(l => l.Encounter.Contains(s.Boss)).Act, Is.EqualTo(3));
            Assert.That(s.Acts.Select(a => a.N), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(s.Acts[2].Title, Is.EqualTo("井戸の底"));   // "第3幕：" is dropped (the screen adds it)

            // an evening on it keeps the sheet in its save, so continuing and friends use the same story
            var game = TavernGame.New(db, s, Storyteller.Olwen, party, new ScriptedLlm(), null, new FixedRandom());
            var loaded = SaveData.FromJson(SaveData.From(game).ToJson());
            Assert.That(Scenarios.For(loaded.State, Load().s).Title, Is.EqualTo(s.Title));
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
