using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using Tavern.Core.Ai;
using Tavern.Core.Gm;
using Tavern.Core.Play;
using Tavern.Core.Story;

/// <summary>
/// Runs the mean script (bench/mean.json) through the classifier (and the narrator), scores the sorting against the expectations,
/// and measures calls and waiting time per turn (Docs/10_Plan.md stage 3).
///   dotnet run --project Core/Tavern.Cli -- --bench [file] [--engine gemini|ollama] [--model M] [--reduced] [--no-narrate]
///                                           [--record tape.jsonl] [--replay tape.jsonl] [--only id,id] [--verbose]
/// Gemini uses GEMINI_API_KEY_TEST (the test quota); --play-key uses GEMINI_API_KEY when the test quota is used up (never on a day with friends).
/// </summary>
static class Bench
{
    public static async Task<int> RunAsync(string contentDir, List<string> a, Func<string, string?> Opt)
    {
        var file = Opt("--bench") is string f && !f.StartsWith("--") ? f : Path.Combine(AppContext.BaseDirectory, "bench", "mean.json");
        if (!File.Exists(file)) file = Path.Combine(contentDir, "..", "..", "..", "..", "Core", "Tavern.Cli", "bench", "mean.json");
        var cases = ((JArray)JObject.Parse(File.ReadAllText(file))["cases"]!).OfType<JObject>().ToList();
        if (Opt("--only") is string only) { var keep = only.Split(','); cases = cases.Where(c => keep.Contains((string)c["id"]!)).ToList(); }

        var classes = ClassBook.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "classes.json")));
        var enemies = Bestiary.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "enemies.json")));
        var stories = new Dictionary<string, StoryDef>();
        StoryDef Story(string id) => stories.TryGetValue(id, out var s) ? s : stories[id] = StoryLoader.Load(File.ReadAllText(Path.Combine(contentDir, "Stories", id + ".json")));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        ILlmClient backend;
        var engine = Opt("--engine") ?? "gemini";
        if (Opt("--replay") is string tape) backend = new ReplayLlm(tape);
        else if (engine == "gemini")
        {
            var key = Environment.GetEnvironmentVariable(Keys.Env(a))
                ?? Environment.GetEnvironmentVariable(Keys.Env(a), EnvironmentVariableTarget.User)
                ?? throw new Exception(Keys.Env(a) + " is not set");
            backend = new GeminiClient(http, key, Opt("--model") ?? "gemini-3.5-flash-lite", Opt("--thinking") ?? "minimal");
        }
        else backend = new OllamaClient(http, Opt("--model") ?? "qwen3:8b");
        if (Opt("--record") is string rec) backend = new RecordingLlm(backend, rec);
        var meter = new CallMeter(backend, 500);
        var reduced = a.Contains("--reduced") || (engine == "ollama" && !a.Contains("--full"));
        var classifier = new Classifier(meter, reduced);
        var narrator = new Narrator(meter);
        var verbose = a.Contains("--verbose");
        if (a.Contains("--trace")) GeminiClient.Trace = m => Console.Error.WriteLine("  [trace] " + m);

        Console.WriteLine($"bench: {cases.Count} cases, {backend.Name}{(reduced ? " (reduced)" : "")}");
        int total = 0, passed = 0, turnCalls = 0, narrations = 0, retells = 0, problemsFound = 0;
        var fieldMiss = new Dictionary<string, int>();
        var turnSeconds = new List<double>();
        var failures = new List<string>();
        foreach (var c in cases)
        {
            var id = (string)c["id"]!;
            var e = Build(c, Story((string?)c["story"] ?? "grey_shrine"), classes);
            var decls = ((JArray)c["declare"]!).OfType<JObject>().Select(d => new Declaration
            {
                Who = (string)d["who"]!, Text = (string)d["text"]!, Secret = (bool?)d["secret"] ?? false, Cover = (string?)d["cover"] ?? "",
            }).ToList();
            var combat = (bool?)c["combat"] ?? false;
            var callsBefore = meter.Used;
            var sw = Stopwatch.StartNew();
            Classification cls;
            try { cls = await classifier.ClassifyAsync(e, decls, combat); }
            catch (Exception ex) { Console.WriteLine($"{id}: ERROR {ex.Message}"); failures.Add($"{id}: {ex.Message}"); total += ((JArray)c["expect"]!).Count; continue; }
            var classifySeconds = sw.Elapsed.TotalSeconds;

            // score
            var lines = new List<string>();
            foreach (JObject ex in (JArray)c["expect"]!)
            {
                total++;
                if (reduced && !ex.Properties().Any(p => ReducedFields.Contains(p.Name))) { total--; continue; }
                var miss = Check(ex, cls, reduced);
                if (miss.Count == 0) { passed++; continue; }
                foreach (var m in miss) fieldMiss[m.field] = fieldMiss.TryGetValue(m.field, out var n) ? n + 1 : 1;
                lines.Add($"    {ex["who"]}: " + string.Join(", ", miss.Select(m => $"{m.field} want {m.want} got {m.got}")));
            }

            // narrate the turn as if every step worked (calls and time of a whole turn; watcher findings)
            if (!a.Contains("--no-narrate") && !reduced)
            {
                var t = new TurnTelling { Scene = combat ? SceneKind.Combat : SceneKind.Explore };
                foreach (var d in cls.Declarations)
                {
                    var who = e.Find(d.Who)!;
                    var decl = decls.First(x => x.Who == d.Who);
                    t.Names.Add(who.Name);
                    if (decl.Secret)
                    {
                        t.Public[d.Who] = d.Cover.Length > 0 ? d.Cover : "辺りの様子をうかがう";
                        t.Secret[d.Who] = decl.Text + "（うまくいった。誰にも気付かれなかった）";
                    }
                    else t.Public[d.Who] = decl.Text;
                    foreach (var s in d.Steps.Where(_ => !decl.Secret))
                        t.Results.Add($"{who.Name}: {s.Text} → {(s.Sort == Sort.Impossible ? "できなかった（" + s.How + "）" : "うまくいった")}");
                }
                try
                {
                    var told = await narrator.TellAsync(e, t);
                    narrations++;
                    if (told.Retold) retells++;
                    if (told.Problems.Count > 0) problemsFound++;
                    if (verbose) Console.WriteLine($"    語り: {told.Text}" + (told.Problems.Count > 0 ? $"\n    （見張り: {string.Join(" / ", told.Problems)}）" : ""));
                }
                catch (Exception ex2) { lines.Add("    narrate ERROR " + ex2.Message); }
            }
            var calls = meter.Used - callsBefore;
            turnCalls += calls;
            turnSeconds.Add(sw.Elapsed.TotalSeconds);
            var ok = lines.Count == 0;
            Console.WriteLine($"{(ok ? "ok  " : "MISS")} {id} ({calls} calls, classify {classifySeconds:0.0}s, turn {sw.Elapsed.TotalSeconds:0.0}s){(cls.Retried ? " retried" : "")}");
            foreach (var l in lines) Console.WriteLine(l);
            if (verbose || !ok) foreach (var d in cls.Declarations) Console.WriteLine("    " + Describe(d));
            foreach (var dr in cls.Dropped) Console.WriteLine("    dropped: " + dr);
            if (!ok) failures.Add(id);
        }

        Console.WriteLine();
        Console.WriteLine($"sorting: {passed}/{total} = {(total == 0 ? 0 : 100.0 * passed / total):0.0}%  (goal 90%)");
        if (fieldMiss.Count > 0) Console.WriteLine("misses by field: " + string.Join(", ", fieldMiss.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        if (cases.Count > 0)
        {
            Console.WriteLine($"calls per turn: {(double)turnCalls / cases.Count:0.00}  (retold {retells} of {narrations} narrations; watcher found issues in {problemsFound})");
            var sorted = turnSeconds.OrderBy(x => x).ToList();
            Console.WriteLine($"turn time: mean {sorted.Average():0.0}s, median {sorted[sorted.Count / 2]:0.0}s, max {sorted.Last():0.0}s");
        }
        if (backend is ReplayLlm rp) Console.WriteLine($"replay: {rp.Hits} hits, {rp.Misses} misses");
        return total > 0 && passed * 10 >= total * 9 ? 0 : 2;
    }

    static readonly string[] Party = { "gareth:warrior:ガレス", "elis:mage:エリス", "rocco:thief:ロッコ", "mina:priest:ミーナ" };

    static Evening Build(JObject c, StoryDef story, ClassBook classes)
    {
        var party = Party.Select(p => p.Split(':')).Select(p => new Adventurer(classes[p[1]], p[2], p[0])).ToList();
        var e = Evening.Begin(story, party, Difficulty.Standard);
        e.World.Act = (int?)c["act"] ?? 1;
        var at = (string?)c["at"] ?? story.Start;
        foreach (var p in party) e.World.Move(p.Id, at);
        var setup = c["setup"] as JObject ?? new JObject();
        foreach (var ev in (setup["events"] as JArray ?? new JArray())) e.World.Events.Add((string)ev!);
        foreach (var p in (setup["attitudes"] as JObject ?? new JObject()).Properties())
            e.World.Cast.Find(p.Name)!.Attitude = Enum.Parse<Attitude>((string)p.Value!);
        foreach (var p in (setup["items"] as JObject ?? new JObject()).Properties())
        {
            e.World.PutItem(p.Name, (string)p.Value!);
            if (((string)p.Value!).StartsWith("pc:") && story.ItemOf(p.Name) is StoryItem si)
                e.Find(((string)p.Value!).Substring(3))!.Gear.Add(si.Item.Clone());
        }
        foreach (var cl in (setup["clues"] as JArray ?? new JArray())) e.World.Learn((string)cl!, "gareth", false);
        foreach (var p in (setup["statuses"] as JObject ?? new JObject()).Properties())
            e.Find(p.Name)!.Statuses.Add(Enum.Parse<StatusKind>((string)p.Value!));
        foreach (var p in (setup["techniques"] as JObject ?? new JObject()).Properties())
            e.Find(p.Name)!.AddTechnique((string)p.Value!, "柱を蹴って跳び、首を狙う", EffectKind.Strike, EffectSize.Medium, Ability.Dex);
        foreach (var p in (setup["rules"] as JObject ?? new JObject()).Properties())
            story.PlaceOf(p.Name)!.Rules.Add(new PlaceRule { Text = (string)p.Value!, Clock = "anger" });
        if ((bool?)setup["climax"] == true) e.ClimaxStarted = true;
        return e;
    }

    /// <summary>The fields of one expectation that did not match.</summary>
    /// <summary>What the reduced form (local models) answers; other expectations are not scored for it.</summary>
    static readonly HashSet<string> ReducedFields = new() { "sort", "ability", "tier", "steps", "checks", "why", "rule", "how" };

    static List<(string field, string want, string got)> Check(JObject ex, Classification cls, bool reduced = false)
    {
        var miss = new List<(string, string, string)>();
        var who = (string)ex["who"]!;
        var d = cls.For(who);
        if (d == null) { miss.Add(("who", who, "missing")); return miss; }
        if (d.Missing) miss.Add(("declaration", "classified", "missing"));
        // with "step" the fields are checked on that step; without it, on any step that matches them all
        var candidates = ex["step"] is JToken si ? d.Steps.Skip((int)si).Take(1).ToList() : d.Steps.ToList();
        var best = new List<(string, string, string)>();
        bool anyStep = false;
        foreach (var s in candidates.DefaultIfEmpty(new Step()))
        {
            var m = new List<(string, string, string)>();
            foreach (var p in ex.Properties())
            {
                if (p.Name is "who" or "step" or "steps" or "deferred" or "fight" or "cover" or "checks") continue;
                if (reduced && !ReducedFields.Contains(p.Name)) continue;
                var got = Field(s, p.Name);
                if (!Matches(p.Value, got)) m.Add((p.Name, p.Value.ToString(Newtonsoft.Json.Formatting.None), got));
            }
            if (m.Count == 0) { anyStep = true; break; }
            if (best.Count == 0 || m.Count < best.Count) best = m;
        }
        if (!anyStep) miss.AddRange(best);
        if (ex["steps"] is JToken st && d.Steps.Count != (int)st) miss.Add(("steps", st.ToString(), d.Steps.Count.ToString()));
        if (ex["checks"] is JToken ch && d.Steps.Count(x => x.Sort == Sort.Check) != (int)ch) miss.Add(("checks", ch.ToString(), d.Steps.Count(x => x.Sort == Sort.Check).ToString()));
        if (!reduced && (bool?)ex["deferred"] == true && d.Deferred.Count == 0) miss.Add(("deferred", "some", "none"));
        if (!reduced && (bool?)ex["cover"] == true && d.Cover.Trim().Length == 0) miss.Add(("cover", "some", "none"));
        if (!reduced && ex["fight"] is JToken fi && ((bool)fi) != (cls.Fight != null)) miss.Add(("fight", fi.ToString(), (cls.Fight != null).ToString()));
        return miss;
    }

    static bool Matches(JToken want, string got) => want switch
    {
        JArray arr => arr.Any(x => Same(x, got)),
        _ => Same(want, got),
    };

    static bool Same(JToken want, string got) => want.Type switch
    {
        JTokenType.Boolean when got.Length > 0 && got != "true" && got != "false" => (bool)want == (got.Length > 0),   // "how": true = some text
        JTokenType.Boolean => ((bool)want ? "true" : "false") == (got == "" ? "false" : got),
        JTokenType.Integer => ((int)want).ToString() == got,
        _ => string.Equals((string?)want ?? "", got, StringComparison.OrdinalIgnoreCase),
    };

    static string Field(Step s, string name) => name switch
    {
        "sort" => s.Sort switch { Sort.Check => "check", Sort.Impossible => "impossible", Sort.FateDie => "fate", _ => "auto" },
        "ability" => s.Check?.Ability.Code() ?? s.EffectAbility?.Code() ?? "",
        "tier" => s.Check?.Tier switch { Tier.Easy => "easy", Tier.Normal => "normal", Tier.Hard => "hard", Tier.Extreme => "extreme", _ => "" },
        "why" => s.Why switch { ImpossibleReason.WorldRule => "world_rule", ImpossibleReason.Lacking => "lacking", ImpossibleReason.Situation => "situation", _ => "" },
        "rule" => s.WorldRule.ToString(),
        "how" => s.How,
        "person" => s.Person,
        "request" => s.Request?.ToString().ToLowerInvariant() ?? "",
        "approach" => s.Approach?.ToString().ToLowerInvariant() ?? "",
        "item" => s.Item,
        "take" => s.Take ? "true" : "false",
        "give_to" => s.GiveTo,
        "place_at" => s.PlaceAt,
        "clue" => s.Clue,
        "move_to" => s.MoveTo,
        "against" => s.Against,
        "helps" => s.Helps,
        "mechanism" => s.Mechanism,
        "effect" => s.Effect?.Kind.ToString().ToLowerInvariant() ?? "",
        "conclusion" => s.Conclusion,
        "correct" => s.Correct ? "true" : "false",
        "unexpected" => s.Unexpected ? "true" : "false",
        "place_rule" => s.PlaceRule ? "true" : "false",
        "slow" => s.Slow ? "true" : "false",
        "rest" => s.Rest ? "true" : "false",
        "buy" => s.Buy,
        "pay" => s.Pay.ToString(),
        "target" => s.Target,
        "important" => s.Check?.Important == true ? "true" : "false",
        "technique" => s.Technique,
        "likelihood" => s.Likelihood.ToString().ToLowerInvariant(),
        _ => "?",
    };

    internal static string Describe(Classified d)
    {
        var b = new StringBuilder($"[{d.Who}]");
        foreach (var s in d.Steps)
        {
            b.Append($" «{s.Text}» {Field(s, "sort")}");
            if (s.Check != null) b.Append($" {s.Check.Ability.Code()}/{Field(s, "tier")}{(s.Check.Shifts.Count > 0 ? "/" + s.Check.Shifts[0] : "")}{(s.Check.Important ? "/important" : "")}");
            if (s.Why != null) b.Append($" {Field(s, "why")}{(s.WorldRule > 0 ? "#" + s.WorldRule : "")} how={s.How}");
            foreach (var f in new[] { "mechanism", "item", "give_to", "place_at", "person", "request", "approach", "clue", "move_to", "against", "helps", "target", "effect", "conclusion", "technique", "buy" })
                if (Field(s, f) is string v && v.Length > 0) b.Append($" {f}={v}");
            if (s.Conclusion.Length > 0) b.Append($" correct={s.Correct}");
            if (s.Unexpected) b.Append(" unexpected");
            if (s.PlaceRule) b.Append(" place_rule");
            if (s.Slow) b.Append(" slow");
            b.Append(';');
        }
        if (d.Deferred.Count > 0) b.Append(" deferred: " + string.Join(" / ", d.Deferred));
        if (d.Cover.Length > 0) b.Append(" cover: " + d.Cover);
        return b.ToString();
    }
}

static class Keys
{
    /// <summary>
    /// The key the test tools use: the test quota; --paid-key the paid test key (its own project with prepaid credit, no daily cap);
    /// --play-key the play key (only when the test quota is used up, never on friends' play days).
    /// </summary>
    public static string Env(List<string> a) => a.Contains("--paid-key") ? "GEMINI_API_KEY_PAID" : a.Contains("--play-key") ? "GEMINI_API_KEY" : "GEMINI_API_KEY_TEST";
}
