using Tavern.AudioGen;

// Sound effects, music and the fire ambience of tavern-tales, synthesized (no outside audio files).
//   dotnet run --project Tools/AudioGen -- [--out DIR] [--only se|bgm]
// Output: Unity/Assets/Resources/Audio/{se,bgm}/*.wav (gitignored; run this once after cloning).

var a = Environment.GetCommandLineArgs().Skip(1).ToList();
string? Opt(string n) { int i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }
var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
var outDir = Opt("--out") ?? Path.Combine(dir!.FullName, "Unity", "Assets", "Resources", "Audio");
var only = Opt("--only");
if (only != "bgm") Sounds.WriteAll(Path.Combine(outDir, "se"));
if (only != "se") Music.WriteAll(Path.Combine(outDir, "bgm"));
Console.WriteLine("done");
