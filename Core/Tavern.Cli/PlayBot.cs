using System.Diagnostics;
using Tavern.Core.Ai;
using Tavern.Core.Flow;
using Tavern.Core.Gm;
using Tavern.Core.Play;
using Tavern.Core.Story;

/// <summary>
/// A bot plays a whole evening of a story sheet on the new flow (Docs/10_Plan.md stage 4):
///   dotnet run --project Core/Tavern.Cli -- --play [story] --walk bench/shrine_success.txt [--engine gemini|ollama] [--seed N]
///                                           [--record tape] [--replay tape] [--difficulty gentle|standard|harsh] [--party 3|4] [--max-turns N]
/// The walkthrough has one exploration turn per line ("gareth: … / rocco: …"); lines starting with "その後:" are the afterword.
/// Fights are declared by the bot (attack the weakest foe, the priest heals the fallen). Gemini uses GEMINI_API_KEY_TEST.
/// </summary>
static class PlayBot
{
    static readonly string[] PartyIds = { "gareth:warrior:ガレス", "elis:mage:エリス", "rocco:thief:ロッコ", "mina:priest:ミーナ" };

    public static async Task<int> RunAsync(string contentDir, List<string> a, Func<string, string?> Opt)
    {
        var storyId = Opt("--play") is string s && !s.StartsWith("--") ? s : "grey_shrine";
        var story = StoryLoader.Load(File.ReadAllText(Path.Combine(contentDir, "Stories", storyId + ".json")));
        var classes = ClassBook.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "classes.json")));
        var enemies = Bestiary.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "enemies.json")));
        var walkFile = Opt("--walk") ?? Path.Combine(contentDir, "..", "..", "..", "..", "Core", "Tavern.Cli", "bench", "shrine_success.txt");
        var walk = File.ReadAllLines(walkFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        ILlmClient backend;
        var engine = Opt("--engine") ?? "gemini";
        if (Opt("--replay") is string tape) backend = new ReplayLlm(tape);
        else if (engine == "gemini")
        {
            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY_TEST") ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY_TEST", EnvironmentVariableTarget.User)
                ?? throw new Exception("GEMINI_API_KEY_TEST is not set");
            backend = new GeminiClient(http, key, Opt("--model") ?? "gemini-3.5-flash-lite", "minimal");
        }
        else backend = new OllamaClient(http, Opt("--model") ?? "qwen3:8b");
        if (Opt("--record") is string rec) backend = new RecordingLlm(backend, rec);
        var meter = new CallMeter(backend, 500);
        var reduced = engine == "ollama" && Opt("--replay") == null && !a.Contains("--full");

        var seed = ulong.TryParse(Opt("--seed"), out var sd) ? sd : 7UL;
        var rng = new SeededRandom(seed);
        var size = int.TryParse(Opt("--party"), out var ps) ? Math.Clamp(ps, 1, 4) : 3;
        var party = PartyIds.Take(size).Select(p => p.Split(':')).Select(p => new Adventurer(classes[p[1]], p[2], p[0])).ToList();
        var difficulty = (Opt("--difficulty") ?? "standard") switch { "gentle" => Difficulty.Gentle, "harsh" => Difficulty.Harsh, _ => Difficulty.Standard };
        var e = Evening.Begin(story, party, difficulty);
        e.FirstEvening = true;
        var bot = new BotPlayers(rng, e.Find) { Redeclare = (who, why) => "辺りを見回す" };
        var table = new Table(e, new Classifier(meter, reduced), new Narrator(meter), bot, rng, enemies);
        string? lastSave = null;
        table.Save = t => lastSave = SaveGame.From(t).ToJson();
        if (a.Contains("--trace")) GeminiClient.Trace = m => Console.Error.WriteLine("  [trace] " + m);

        Console.WriteLine($"play: {story.Title}, {size} adventurers, {difficulty.Label()}, seed {seed}, {backend.Name}{(reduced ? " (reduced)" : "")}");
        var sw = Stopwatch.StartNew();
        var bookends = new Bookends(meter);
        var hands = await table.DealHandoutsAsync(bookends);
        foreach (var h in hands) Console.WriteLine($"  handout {e.Find(h.Who)?.Name}: {h.Bond} / 目的: {story.PersonalGoals.FirstOrDefault(g => g.Id == h.GoalId)?.Text} / 断片: {h.Fragment}");

        var maxTurns = int.TryParse(Opt("--max-turns"), out var mt) ? mt : 80;
        int line = 0, turns = 0;
        var turnSeconds = new List<double>();
        while (table.Phase != Phase.Over && turns++ < maxTurns)
        {
            List<Declaration> decls;
            string label;
            if (table.Phase == Phase.Combat) { decls = FightDeclarations(e); label = $"戦闘 {e.Combat!.Round}"; }
            else if (table.Phase == Phase.Afterword)
            {
                var after = walk.Skip(line).FirstOrDefault(l => l.StartsWith("その後:"));
                decls = Parse((after ?? "その後: " + string.Join(" / ", e.Party.Where(x => x.IsUp).Select(x => $"{x.Id}: 若者たちを連れて村へ帰る"))).Substring(4), e);
                label = "その後";
            }
            else
            {
                while (line < walk.Count && walk[line].StartsWith("その後:")) line++;
                if (line >= walk.Count) { Console.WriteLine("  (the walkthrough ran out)"); break; }
                decls = Parse(walk[line++], e);
                label = $"手 {e.Turn + 1}（第 {e.World.Act} 幕）";
            }
            var t0 = sw.Elapsed.TotalSeconds;
            var calls0 = meter.Used;
            var r = await table.ExploreAsync(decls);
            turnSeconds.Add(sw.Elapsed.TotalSeconds - t0);
            Console.WriteLine($"\n== {label}  [{meter.Used - calls0} calls, {sw.Elapsed.TotalSeconds - t0:0.0}s]");
            foreach (var d in decls) Console.WriteLine($"  > {e.Find(d.Who)?.Name}{(d.Secret ? "（内緒）" : "")}: {d.Text}");
            foreach (var l in r.Lines) Console.WriteLine("  ・" + l);
            foreach (var kv in r.PrivateLines) foreach (var l in kv.Value) Console.WriteLine($"  （{e.Find(kv.Key)?.Name}だけ）{l}");
            foreach (var n in r.Notes) Console.WriteLine("  ! " + n);
            Console.WriteLine("  語り: " + r.Narration);
            foreach (var kv in r.PrivateNarration) Console.WriteLine($"  （{e.Find(kv.Key)?.Name}だけ）{kv.Value}");
            Console.WriteLine($"  状態: " + string.Join(" / ", e.Party.Select(x => $"{x.Name} HP{x.Hp}/{x.MaxHp}{(x.Life != Life.Up ? "(" + x.Life + ")" : "")} @{e.PlaceOf(x)}")) +
                $" | 時計 " + string.Join(" ", e.World.Clocks.All.Select(c => $"{c.Def.Name}{c.Filled}/{c.Def.Segments}")) + $" | 手がかり {e.World.Clues.Count}/{story.Clues.Count}");
        }

        Console.WriteLine();
        if (table.Ending is EndingResult end)
        {
            Console.WriteLine($"ending: {EndingResult.Label(end.Card)} ({end.Trigger}{(end.SolvedBy.Length > 0 ? ", " + end.SolvedBy : "")})");
            foreach (var l in end.Lost) Console.WriteLine("  lost: " + l);
            foreach (var kv in end.Personal) Console.WriteLine($"  {e.Find(kv.Key)?.Name}: {kv.Value.goal} → {(kv.Value.done ? "done" : "not done")}");
            var c = await table.WrapUpAsync(bookends);
            Console.WriteLine($"chronicle「{c.Title}」: {c.Story}");
            foreach (var x in c.Rumors) Console.WriteLine("  rumor: " + x);
            foreach (var x in c.Bonds) Console.WriteLine($"  bond: {x.Kind} {x.Text}");
            foreach (var x in c.Marks) Console.WriteLine($"  mark: {e.Find(x.Key)?.Name} 「{x.Value}」");
        }
        else Console.WriteLine("ending: none (stopped)");
        var sorted = turnSeconds.OrderBy(x => x).ToList();
        Console.WriteLine($"turns {turnSeconds.Count}, calls {meter.Used} ({(double)meter.Used / Math.Max(1, turnSeconds.Count):0.00}/turn), time {sw.Elapsed.TotalMinutes:0.0} min" +
            (sorted.Count > 0 ? $", turn median {sorted[sorted.Count / 2]:0.0}s max {sorted.Last():0.0}s" : ""));
        if (lastSave != null && Opt("--save") is string savePath) File.WriteAllText(savePath, lastSave);
        if (backend is ReplayLlm rp) Console.WriteLine($"replay: {rp.Hits} hits, {rp.Misses} misses");
        return table.Ending != null ? 0 : 3;
    }

    /// <summary>"gareth: … / rocco: …" (an id or a name; "(内緒)" before the text makes it secret).</summary>
    static List<Declaration> Parse(string line, Evening e)
    {
        var list = new List<Declaration>();
        foreach (var part in line.Split(" / "))
        {
            var i = part.IndexOf(':');
            if (i < 0) continue;
            var who = part[..i].Trim();
            var a = e.Find(who) ?? e.Party.FirstOrDefault(x => x.Name == who);
            if (a == null || !a.IsUp) continue;
            var text = part[(i + 1)..].Trim();
            var secret = text.StartsWith("(内緒)");
            list.Add(new Declaration { Who = a.Id, Text = secret ? text[4..].Trim() : text, Secret = secret });
        }
        return list;
    }

    /// <summary>The bot's fight: the priest raises the fallen, everyone else hits the weakest foe (the mage with spells, the thief with daggers).</summary>
    static List<Declaration> FightDeclarations(Evening e)
    {
        var c = e.Combat!;
        var foe = c.Foes.Where(f => f.InFight).OrderBy(f => f.Hp).FirstOrDefault();
        var name = foe?.Name ?? "敵";
        var list = new List<Declaration>();
        foreach (var a in e.Party.Where(x => x.IsUp))
        {
            var down = e.Party.FirstOrDefault(x => x.Life == Life.Down);
            var text = a.Class.Id switch
            {
                "priest" when down != null => $"祈りを捧げて、倒れた{down.Name}の傷をふさぐ",
                "priest" => $"聖なる光で{name}を撃つ",
                "mage" when a.Focus >= 2 && c.Foes.Count(f => f.InFight) > 1 => "炎の渦を呼んで、敵をまとめて焼き払う",
                "mage" => $"魔力の矢を{name}に放つ",
                "thief" => $"短剣で{name}の隙を突く",
                _ when down != null && a.Class.Id == "warrior" => $"倒れた{down.Name}を助け起こす",
                _ => $"剣で{name}に斬りかかる",
            };
            list.Add(new Declaration { Who = a.Id, Text = text });
        }
        return list;
    }
}
