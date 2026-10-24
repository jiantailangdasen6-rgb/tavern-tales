using System.Text;

// Tools for the story sheets and the AI without Unity:
//   dotnet run --project Core/Tavern.Cli -- --check-stories [id]        the sheet checker (09_Spec 9)
//   dotnet run --project Core/Tavern.Cli -- --play <id> --walk FILE ...  the play bot (PlayBot.cs)
//   dotnet run --project Core/Tavern.Cli -- --bench ...                  the classifier and narrator bench (Bench.cs)
// Gemini reads GEMINI_API_KEY (or GEMINI_API_KEY_TEST; never printed).

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var a = args.ToList();
string? Opt(string n) { int i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }

var contentDir = Path.Combine(FindRoot(), "Unity", "Assets", "Resources", "Content");
if (a.Contains("--check-stories")) return CheckStories(contentDir, Opt("--check-stories"));
if (a.Contains("--bench")) return await Bench.RunAsync(contentDir, a, Opt);
if (a.Contains("--play")) return await PlayBot.RunAsync(contentDir, a, Opt);
Console.WriteLine("usage: --check-stories [id] | --play <id> --walk FILE [--play-key] [--difficulty gentle] [--record F] | --bench ...");
return 2;

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
