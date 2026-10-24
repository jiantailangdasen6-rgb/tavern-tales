using System.Diagnostics;
using System.Text;
using Tavern.Core.Content;
using Tavern.Core.Game;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

// Play an evening against the AI game master without Unity (narration quality, effects, combat).
//   dotnet run --project Core/Tavern.Cli -- [--engine gemini|ollama] [--model M] [--highlight-model M|none] [--fallback-local]
//                                           [--script file.txt] [--seed N] [--save file.json] [--load file.json]
// Each script line (or stdin line) is one turn: declarations separated by " / ", each "名前: 行動".
// In an action, "@attack [>enemyUid]", "@defend", "@help >名前", "@revive >名前", "@flee", "@card id [>target]" pick
// a basic action or a card; any other text is a free action.
// Gemini reads GEMINI_API_KEY (never printed). The local model needs Tools/Ollama.ps1.

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var a = args.ToList();
string? Opt(string n) { int i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }

var root = FindRoot();
var contentDir = Path.Combine(root, "Unity", "Assets", "Resources", "Content");
if (a.Contains("--check-stories")) Environment.Exit(CheckStories(contentDir, Opt("--check-stories")));
if (a.Contains("--bench")) Environment.Exit(await Bench.RunAsync(contentDir, a, Opt));
if (a.Contains("--play")) Environment.Exit(await PlayBot.RunAsync(contentDir, a, Opt));
if (a.Contains("--simulate")) Environment.Exit(Simulate.Run(contentDir, a, Opt));
var db = ContentDb.LoadFolder(contentDir);
ScenarioDef scenario = ContentDb.LoadScenario(File.ReadAllText(Path.Combine(contentDir, "Scenarios", (Opt("--scenario") ?? "grey_shrine") + ".json")));   // --scenario castle_lights etc.

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var counters = new List<CountingLlm>();
ILlmClient Counted(ILlmClient c) { var w = new CountingLlm(c); counters.Add(w); return w; }

ILlmClient llm;
ILlmClient? highlight = null;
if ((Opt("--engine") ?? "gemini") == "gemini")
{
    var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
        ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY", EnvironmentVariableTarget.User)
        ?? throw new Exception("GEMINI_API_KEY is not set");
    // Everyday narration on the Flash-Lite free tier (500/day), big moments on Flash (20/day), local model last.
    var chain = new List<ILlmClient> { Counted(new GeminiClient(http, key, Opt("--model") ?? "gemini-3.5-flash-lite", Opt("--thinking") ?? "minimal")) };
    if (a.Contains("--fallback-local")) chain.Add(Counted(new OllamaClient(http)));
    llm = chain.Count == 1 ? chain[0] : new FallbackLlm(chain.ToArray());
    var hiModel = Opt("--highlight-model") ?? "gemini-3.8-flash";
    if (hiModel != "none") highlight = new FallbackLlm(Counted(new GeminiClient(http, key, hiModel, "low")), llm) { Patience = TimeSpan.FromSeconds(40) };
}
else llm = Counted(new OllamaClient(http, Opt("--model") ?? "qwen3:8b"));

if (a.Contains("--trace")) GeminiClient.Trace = m => Console.Error.WriteLine("  [trace] " + m);
var seed = ulong.TryParse(Opt("--seed"), out var s) ? s : (ulong)DateTime.Now.Ticks;
var story = StoryContent.LoadFolder(contentDir);
ScenarioPrompt.World = story.World.Describe();
var presetParty = new List<Character>
{
    TavernGame.CreateCharacter(db, "ガレス", "warrior", "没落した騎士の家の三男。家名を取り戻したい", "pc_warrior_m"),
    TavernGame.CreateCharacter(db, "エリス", "mage", "魔法学院を追い出された。理由は語りたがらない", "pc_mage_f"),
    TavernGame.CreateCharacter(db, "ロッコ", "thief", "港町の孤児。お調子者で、光る物に目がない", "pc_thief_m"),
};
if (Opt("--generate") is string templateId)
{
    var t = story.Template(templateId) ?? story.Templates[0];
    var sw0 = Stopwatch.StartNew();
    scenario = await ScenarioGenerator.GenerateAsync(a.Contains("--generate-strong") && highlight != null ? highlight : llm, story, db, t, presetParty, Opt("--request"), new SeededRandom(seed));
    Console.WriteLine($"[シナリオ生成 {sw0.Elapsed.TotalSeconds:0.0} 秒] {scenario.Title}（{scenario.Template}）\n導入: {scenario.Hook}\n真相: {scenario.Truth}\n鍵: {scenario.Weakness}");
    foreach (var l in scenario.Locations) Console.WriteLine($"  [{l.Act}] {l.Id} {l.Name}（{l.Art}）→ {string.Join(",", l.Exits)} {(l.Encounter.Count > 0 ? "敵: " + string.Join(",", l.Encounter) : "")}");
    foreach (var n in scenario.Npcs) Console.WriteLine($"  人物: {n.Name}（{n.Where}）{n.Role}");
    foreach (var c in scenario.Clues) Console.WriteLine($"  手がかり: {c.Text}（{c.Where}）");
    if (a.Contains("--generate-only")) return;
}
TavernGame game;
if (Opt("--load") is string loadPath)
    game = SaveData.FromJson(File.ReadAllText(loadPath)).Load(db, scenario, Storyteller.Find(SaveData.FromJson(File.ReadAllText(loadPath)).State.StorytellerId), llm, highlight, new SeededRandom(seed));
else
    game = TavernGame.New(db, scenario, Storyteller.Find(Opt("--teller")), presetParty, llm, highlight, new SeededRandom(seed));
game.SuggestLlm = llm;   // suggested actions from a call that only sees what the players know

game.Logged += e =>
{
    switch (e.Kind)
    {
        case LogKind.Narration: Console.WriteLine($"--- {e.Who}\n{e.Text}"); break;
        case LogKind.Declaration: Console.WriteLine($"  > {e.Who}: {e.Text}"); break;
        case LogKind.Roll: Console.WriteLine($"  [サイコロ] {e.Text}"); break;
        case LogKind.Warning: Console.WriteLine($"  [!] {e.Text}"); break;
        default: Console.WriteLine($"  [{(e.Who.Length > 0 ? e.Who : "ゲーム")}] {e.Text}"); break;
    }
};

// --auto N: a bot plays N turns (suggestions outside fights; cards, attacks and reviving inside), like Unity's -autoplay.
string AutoLine()
{
    var parts = new List<string>();
    var sug = game.State.Suggestions;
    for (int i = 0; i < game.Party.Count; i++)
    {
        var p = game.Party[i];
        if (!p.CanAct) continue;
        if (!game.InCombat) { parts.Add($"{p.Name}: {(sug.Count > 0 ? sug[i % sug.Count] : "辺りを調べる")}"); continue; }
        var downed = game.Party.FirstOrDefault(x => x.State == LifeState.Down);
        var target = game.State.Combat!.Enemies.Where(e => e.Alive).OrderBy(e => e.Hp).FirstOrDefault()?.Uid;
        var card = p.Cards.Select(id => db.Cards[id]).FirstOrDefault(c => c.Kind == "attack" && p.Focus >= c.Cost);
        var heal = p.Cards.Select(id => db.Cards[id]).FirstOrDefault(c => c.Kind == "heal" && p.Focus >= c.Cost);
        if (downed != null && heal != null) parts.Add($"{p.Name}: @card {heal.Id} >{downed.Name}");
        else if (downed != null && i == game.Party.Count - 1) parts.Add($"{p.Name}: @revive >{downed.Name}");
        else if (game.State.Combat.Enemies.Any(e => e.Boss && e.Alive) && !game.State.Combat.WeaknessUsed && (game.State.Clues.Contains("knight_name") || game.State.Clues.Contains("inscription")) && i == 2)
            parts.Add($"{p.Name}: 「バルドリック！」と騎士の生前の名を叫ぶ");
        else if (card != null && i % 2 == 0) parts.Add($"{p.Name}: @card {card.Id} >{target}");
        else parts.Add($"{p.Name}: @attack >{target}");
    }
    return string.Join(" / ", parts);
}

IEnumerable<string> Inputs()
{
    if (int.TryParse(Opt("--auto"), out var autoTurns)) { for (int i = 0; i < autoTurns && !game.Ended; i++) yield return AutoLine(); yield break; }
    var script = Opt("--script");
    if (script != null) { foreach (var line in File.ReadAllLines(script, Encoding.UTF8)) if (line.Trim().Length > 0 && !line.StartsWith("#")) yield return line; yield break; }
    while (true) { Console.Write("\n> "); var line = Console.ReadLine(); if (line == null || line == "quit") yield break; yield return line; }
}

Console.WriteLine($"[{llm.Name}{(highlight != null ? " + " + highlight.Name : "")}] seed {seed}");
var sw = Stopwatch.StartNew();
if (Opt("--load") == null) { var open = await game.OpenAsync(); PrintSuggestions(open); }
foreach (var line in Inputs())
{
    if (game.Ended) break;
    game.State.PlayMinutes += 6;   // a scripted turn stands for a few minutes of talking at the table
    var decls = line.Split(" / ").Select(Parse).Where(d => d != null).Select(d => d!).ToList();
    Console.WriteLine($"\n=== 手番 {game.State.Turn + 1}{(game.InCombat ? $"（戦闘 第{game.State.Combat!.Round}手番）" : "")}: {line}");
    var t = Stopwatch.StartNew();
    var report = await game.SubmitAsync(decls);
    PrintSuggestions(report);
    Console.WriteLine($"  （{report.Model}、{t.Elapsed.TotalSeconds:0.0} 秒）");
    foreach (var name in game.State.PendingGrowth.ToList()) game.Grow(name, db.Classes[game.Find(name)!.ClassId].AttackStat);
    if (Opt("--save") is string savePath) File.WriteAllText(savePath, SaveData.From(game).ToJson());
}
Console.WriteLine($"\n[状態] 場所 {game.Here.Name} / 第{game.State.Act}幕 / 手がかり {game.State.Clues.Count} / 結末 {game.State.Ending ?? "まだ"}");
foreach (var p in game.Party) Console.WriteLine($"  {p.Name} HP {p.Hp}/{p.MaxHp} 気力 {p.Focus}/{p.MaxFocus} {p.State} 持ち物 {string.Join("・", p.Items)}");
Console.WriteLine($"[AI の呼び出し] " + string.Join(" / ", counters.Select(c => $"{c.Name} {c.Calls} 回")) + $"（{sw.Elapsed.TotalSeconds:0} 秒）");

static void PrintSuggestions(TurnReport r) { if (r.Suggestions.Count > 0) Console.WriteLine("  [行動案] " + string.Join(" / ", r.Suggestions)); }

static Declaration? Parse(string part)
{
    var i = part.IndexOf(':');
    if (i < 0) return null;
    var d = new Declaration { Character = part[..i].Trim() };
    var body = part[(i + 1)..].Trim();
    if (body.StartsWith("@"))
    {
        var words = body[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var target = words.FirstOrDefault(w => w.StartsWith(">"));
        if (target != null) { d.Target = target[1..]; words.Remove(target); }
        if (words.Count > 0 && words[0] == "card") { d.CardId = words.ElementAtOrDefault(1); words.RemoveRange(0, Math.Min(2, words.Count)); }
        else if (words.Count > 0) { d.Basic = words[0]; words.RemoveAt(0); }
        d.Text = string.Join(" ", words);
    }
    else d.Text = body;
    return d;
}

// --check-stories [id]: runs the story sheet checker over Content/Stories (09_Spec 9). Exit code 1 when a sheet has errors.
static int CheckStories(string contentDir, string? only)
{
    var enemies = Tavern.Core.Play.Bestiary.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "enemies.json")));
    var art = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(contentDir, "art_catalog.json")), "\"id\"\\s*:\\s*\"([^\"]+)\"")
        .Select(m => m.Groups[1].Value));
    var regions = new HashSet<string>(((Newtonsoft.Json.Linq.JArray)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(contentDir, "world.json")))["regions"]!).Select(r => (string)r["id"]!));
    int errors = 0;
    foreach (var file in Directory.GetFiles(Path.Combine(contentDir, "Stories"), "*.json").OrderBy(f => f))
    {
        var id = Path.GetFileNameWithoutExtension(file);
        if (only != null && !only.StartsWith("--") && only != id) continue;
        try
        {
            var story = Tavern.Core.Story.StoryLoader.Load(File.ReadAllText(file));
            var problems = Tavern.Core.Story.StoryCheck.Run(story, enemies, art, regions);
            var e = problems.Count(p => p.Error);
            errors += e;
            Console.WriteLine($"{id}: {(e == 0 ? "OK" : $"{e} errors")}, {problems.Count - e} warnings");
            foreach (var p in problems) Console.WriteLine("  " + p);
        }
        catch (Exception ex) { errors++; Console.WriteLine($"{id}: cannot read: {ex.Message}"); }
    }
    return errors == 0 ? 0 : 1;
}

static string FindRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Unity", "Assets", "Resources", "Content"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

/// <summary>Counts backend calls (to estimate how much of a free quota one evening uses).</summary>
sealed class CountingLlm : ILlmClient
{
    readonly ILlmClient _inner;
    public int Calls { get; private set; }
    public CountingLlm(ILlmClient inner) { _inner = inner; }
    public string Name => _inner.Name;
    public Task<LlmReply> ChatAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDef> tools, string? forceTool = null, CancellationToken ct = default)
    {
        Calls++;
        return _inner.ChatAsync(messages, tools, forceTool, ct);
    }
}
