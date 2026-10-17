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
var db = ContentDb.LoadFolder(contentDir);
var scenario = ContentDb.LoadScenario(File.ReadAllText(Path.Combine(contentDir, "Scenarios", "grey_shrine.json")));

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
    if (hiModel != "none") highlight = new FallbackLlm(Counted(new GeminiClient(http, key, hiModel, "low")), llm);
}
else llm = Counted(new OllamaClient(http, Opt("--model") ?? "qwen3:8b"));

var seed = ulong.TryParse(Opt("--seed"), out var s) ? s : (ulong)DateTime.Now.Ticks;
TavernGame game;
if (Opt("--load") is string loadPath)
    game = SaveData.FromJson(File.ReadAllText(loadPath)).Load(db, scenario, Storyteller.Olwen, llm, highlight, new SeededRandom(seed));
else
    game = TavernGame.New(db, scenario, Storyteller.Olwen, new List<Character>
    {
        TavernGame.CreateCharacter(db, "ガレス", "warrior", "没落した騎士の家の三男。家名を取り戻したい", "pc_warrior_m"),
        TavernGame.CreateCharacter(db, "エリス", "mage", "魔法学院を追い出された。理由は語りたがらない", "pc_mage_f"),
        TavernGame.CreateCharacter(db, "ロッコ", "thief", "港町の孤児。お調子者で、光る物に目がない", "pc_thief_m"),
    }, llm, highlight, new SeededRandom(seed));

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

IEnumerable<string> Inputs()
{
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
