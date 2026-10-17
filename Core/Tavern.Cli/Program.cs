using System.Diagnostics;
using System.Text;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

// Talk to the AI game master without Unity (checks the narration quality and tool use).
//   dotnet run --project Core/Tavern.Cli -- [--model qwen3:8b] [--script file.txt] [--seed N]
// Each line of the script (or of stdin) is one round of player declarations; "begin" opens the scene.
// Needs the local Ollama server: powershell -ExecutionPolicy Bypass -File Tools/Ollama.ps1

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var a = args.ToList();
string? Opt(string n) { int i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }

var party = new List<Character>
{
    new("ガレス", "warrior", might: 3, skill: 1, wits: 0, spirit: 1, maxHp: 18, maxFocus: 4, background: "没落した騎士の家の三男。家名を取り戻したい"),
    new("エリス", "mage", might: 0, skill: 1, wits: 3, spirit: 1, maxHp: 12, maxFocus: 6, background: "魔法学院を追い出された。理由は語りたがらない"),
    new("ロッコ", "thief", might: 0, skill: 3, wits: 1, spirit: 1, maxHp: 14, maxFocus: 4, background: "港町の孤児。お調子者で、光る物に目がない"),
};
const string scenario =
    "灰色の森グレイウッドの奥にある古い祠。三日前から村の若者が二人戻らない。\n" +
    "真相: 祠の地下には水没した神殿があり、骨の騎士（かつてこの地を守った騎士の亡骸）が目覚めて、近づく者を捕らえている。\n" +
    "若者たちは地下の牢で生きている。骨の騎士は、祠に刻まれた騎士の名を呼ばれると一瞬だけ動きを止める（手がかり: 祠の扉の碑文）。\n" +
    "祠の扉には罠（毒矢）がある。最初の場面: 夕暮れ、一行が祠の前に着いたところ。";

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var llm = new OllamaClient(http, Opt("--model") ?? "qwen3:8b");
var seed = ulong.TryParse(Opt("--seed"), out var s) ? s : (ulong)DateTime.Now.Ticks;
var gm = new GmSession(llm, Storyteller.Olwen, party, scenario, new SeededRandom(seed));

IEnumerable<string> Inputs()
{
    var script = Opt("--script");
    if (script != null) { foreach (var line in File.ReadAllLines(script, Encoding.UTF8)) if (line.Trim().Length > 0) yield return line; yield break; }
    yield return "begin";
    while (true) { Console.Write("\n> "); var line = Console.ReadLine(); if (line == null || line == "quit") yield break; yield return line; }
}

Console.WriteLine($"[{llm.Name}] seed {seed}");
foreach (var input in Inputs())
{
    var text = input == "begin" ? "物語を始めてください。最初の場面を語ってください。" : input.Replace(" / ", "\n");
    Console.WriteLine($"\n=== プレイヤー: {input}");
    var sw = Stopwatch.StartNew();
    var turn = await gm.PlayAsync(text);
    foreach (var c in turn.Checks) Console.WriteLine($"  [サイコロ] {c.Describe()}（{c.Request.Reason}）");
    foreach (var r in turn.Rejected) Console.WriteLine($"  [却下] {r}");
    Console.WriteLine($"--- {Storyteller.Olwen.Name}（{sw.Elapsed.TotalSeconds:0.0} 秒）\n{turn.Narration}");
    if (turn.Suggestions.Count > 0) Console.WriteLine("  [行動案] " + string.Join(" / ", turn.Suggestions));
}
