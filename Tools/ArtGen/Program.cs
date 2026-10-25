using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

// Image generation for tavern-tales (based on the cardgame project's ArtGen).
//   dotnet run --project Tools/ArtGen -- <jobs.json> --engine fal|flux|sdxl --out DIR [--seeds 1,2] [--only id,..]
// jobs.json: [{ "id": "olwen", "kind": "portrait"|"scene"|"creature", "aspect": "16:9"|"2:3"|"1:1", "prompt": "subject..." }]
// - fal  : fal.ai FLUX.1 Kontext Pro text-to-image (key from env FAL_KEY, never printed or stored)
// - flux : local ComfyUI, FLUX.1 schnell fp8
// - sdxl : local ComfyUI, Juggernaut XL v9
// The shared style text lives in Style below; Docs/art/style-guide.md is the source of truth.

var a = args.ToList();
string? Opt(string n) { int i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }

// edit <src.png> <outDir> "<instruction>" [--seeds 1,2]: Kontext edits one picture (e.g. take out a second compass); writes <name>_edit_<seed>.png
if (a.Count > 0 && a[0] == "edit")
{
    Directory.CreateDirectory(a[2]);
    using var eh = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    foreach (var seed in (Opt("--seeds") ?? "1").Split(',').Select(long.Parse))
    {
        var outPath = Path.Combine(a[2], $"{Path.GetFileNameWithoutExtension(a[1])}_edit_{seed}.png");
        try { await File.WriteAllBytesAsync(outPath, await Fal.Edit(eh, a[1], a[3], seed)); Console.WriteLine($"{outPath} ... OK"); }
        catch (Exception ex) { Console.WriteLine($"{outPath} ... FAILED: {ex.Message}"); }
    }
    return 0;
}

// import <srcDir> <destDir> [--engine sdxl] [--pick id=2,id=1] [--only id,..] [--trim-bottom 0.08]: copy the chosen candidate of each id as a JPEG
// (backgrounds 1344 wide, the rest 512x768) into Unity/Assets/Resources/Art.
if (a.Count > 0 && a[0] == "import")
{
    var src = a[1]; var dest = a[2];
    var eng = Opt("--engine") ?? "sdxl";
    var picks = (Opt("--pick") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
    Directory.CreateDirectory(dest);
    var ids = Directory.GetFiles(src, $"*_{eng}_*.png").Select(f => Path.GetFileNameWithoutExtension(f))
        .Select(n => n[..n.LastIndexOf($"_{eng}_", StringComparison.Ordinal)]).Distinct().OrderBy(x => x).ToList();
    var chosen = Opt("--only")?.Split(",").ToHashSet();
    foreach (var id in ids.Where(i => chosen == null || chosen.Contains(i)))
    {
        var seed = picks.TryGetValue(id, out var sp) ? sp : "1";
        var file = Path.Combine(src, $"{id}_{eng}_{seed}.png");
        if (!File.Exists(file)) { Console.WriteLine($"{id}: missing {file}"); continue; }
        ImageTools.SaveJpeg(file, Path.Combine(dest, id + ".jpg"), id.StartsWith("bg_") ? 1344 : 512, 768, float.TryParse(Opt("--trim-bottom"), System.Globalization.CultureInfo.InvariantCulture, out var tb) ? tb : 0);
        Console.WriteLine($"{id} <- seed {seed}");
    }
    return 0;
}
var jobsPath = a.Count > 0 ? a[0] : throw new Exception("jobs.json is required");
var engine = Opt("--engine") ?? "fal";
var outDir = Opt("--out") ?? throw new Exception("--out DIR is required");
var seeds = (Opt("--seeds") ?? "1,2").Split(',').Select(long.Parse).ToArray();
var only = Opt("--only")?.Split(',').ToHashSet();
Directory.CreateDirectory(outDir);

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
int ok = 0, fail = 0;
foreach (var job in JsonNode.Parse(File.ReadAllText(jobsPath, Encoding.UTF8))!.AsArray())
{
    var id = job!["id"]!.GetValue<string>();
    if (only != null && !only.Contains(id)) continue;
    var kind = job["kind"]?.GetValue<string>() ?? "portrait";
    var aspect = job["aspect"]?.GetValue<string>() ?? "2:3";
    var prompt = Style.Build(kind, job["prompt"]!.GetValue<string>());
    foreach (var seed in seeds)
    {
        var outPath = Path.Combine(outDir, $"{id}_{engine}_{seed}.png");
        if (File.Exists(outPath)) { Console.WriteLine($"{id} {engine} {seed} ... exists"); continue; }
        try
        {
            var bytes = engine == "fal" ? await Fal.TextToImage(http, prompt, aspect, seed)
                : await Comfy.Generate(http, engine, prompt, Style.Negative, seed, aspect);
            await File.WriteAllBytesAsync(outPath, bytes);
            Console.WriteLine($"{id} {engine} {seed} ... OK"); ok++;
        }
        catch (Exception ex) { Console.WriteLine($"{id} {engine} {seed} ... FAILED: {ex.Message}"); fail++; }
    }
}
Console.WriteLine($"done: ok {ok} / failed {fail}");
return fail == 0 ? 0 : 1;

static class Style
{
    /// <summary>The look: heavy classical oil painting, like old fantasy-novel plates (owner's choice 2026-10-17).</summary>
    public const string Bible =
        "A classical oil painting on canvas in the manner of nineteenth-century romantic and academic painters and vintage fantasy book plates: " +
        "visible brushstrokes, canvas texture and rich glazes; a muted earthy palette of umber, ochre, deep olive, slate blue and dark crimson; " +
        "dramatic chiaroscuro with warm candle or firelight against deep shadows; atmospheric depth with softer, darker edges in the background. " +
        "Serious and grounded, painterly, not photorealistic, not anime, not cartoon, not smooth digital art.";

    /// <summary>
    /// Places: the same classical oil painting, but luminous. With the Bible's muted palette and dark edges every scene came out
    /// as a dim brown haze (2026-10-24), so scenes get their own light and colour.
    /// </summary>
    public const string SceneBible =
        "A detailed, luminous classical oil painting in the manner of nineteenth-century romantic landscape and interior painters and vintage fantasy book plates: " +
        "rich glowing colour, crisp detail, deep clear shadows and bright warm light from fire, lanterns, candles, moonlight or the sky, " +
        "strong contrast, the place and its main features clearly readable. Painterly, not photorealistic, not anime, not cartoon.";

    const string NoText ="No text, letters, numbers, logos, signatures, watermarks, borders or frames anywhere.";
    const string Anatomy = "Correct anatomy: each visible hand has exactly five fingers, and figures hold only the objects described.";

    public const string Negative =
        "text, letters, watermark, signature, frame, border, anime, cartoon, 3d render, photo, plastic, smooth digital art, oversaturated, extra fingers, deformed hands";

    /// <summary>Subject first (long style text first made models ignore the subject in the cardgame project).</summary>
    public static string Build(string kind, string subject) => kind switch
    {
        // scenes came out murky (dim, flat, a canvas haze over everything): they ask for a lit, readable place
        "scene" => $"Subject: {subject} An empty scene with no figures. Style: {SceneBible} {NoText}",
        // interface pieces (frames, panels, textures): flat front view on plain black, to be cut out and sliced
        "ui" => $"Subject: {subject} Style: a hand-painted interface element for a dark fantasy computer role-playing game, in the manner of classic painted RPG interfaces: " +
                "aged gold, bronze, dark leather and parchment, fine engraving, soft painted highlights, rich but restrained. Flat front view, perfectly symmetrical, " +
                $"centred, isolated on a plain pure black background, nothing else in the picture. {NoText}",
        // the region map came out as a tilted relief with tall cliffs (owner 2026-10-25: more like a fantasy map): flat, inked, from above
        // the first flat map came out pale cream (owner 2026-10-25: more parchment, a little brown, like the world maps of fantasy RPGs)
        "map" => $"Subject: {subject} Style: a hand-drawn world map in the tradition of classic fantasy role-playing games and the maps printed at the front of fantasy novels: " +
                 "drawn completely flat from directly above on old, deeply aged, tea-stained brown parchment with darker mottled stains and softly burnt, darkened edges, " +
                 "warm sepia and dark brown ink linework with soft hand-coloured washes of muted green, ochre and slate blue; " +
                 "mountains, hills, forests and towns drawn as small inked pictograms, the sea shown with gentle wave lines and hatching along the coastline, " +
                 $"land and sea lying on the same flat page with no relief, no cliffs, no perspective and no three-dimensional terrain. {NoText}",
        // the title screen's own picture (owner 2026-10-25): wide key art whose middle stays calm under the title and the menu
        "title" => $"Subject: {subject} Style: {Bible} A wide panoramic painting composed to sit behind a title: " +
                   $"the middle of the picture is calm, open and darker, the detail and the light gathered towards the left and right sides. {Anatomy} {NoText}",
        // the application icon: one bold emblem that still reads at 32 pixels
        "icon" => $"Subject: {subject} Style: {Bible} A single bold emblem for an application icon: one object, centred, filling most of the square, " +
                  $"a simple strong silhouette with clear warm highlights that still reads when very small, on a plain dark background with nothing else. {NoText}",
        "creature" => $"Subject: {subject} Style: {Bible} The creature is fully visible and centred, nothing important touches the edges. {Anatomy} {NoText}",
        "card" => $"Subject: {subject} Style: {Bible} The effect itself is the main subject, centred; any figure is secondary or seen from behind. {Anatomy} {NoText}",
        _ => $"Subject: {subject} Style: {Bible} The figure is centred with the face clearly lit. {Anatomy} {NoText}",
    };
}

static class Fal
{
    public static Task<byte[]> TextToImage(HttpClient http, string prompt, string aspect, long seed) =>
        Run(http, "https://fal.run/fal-ai/flux-pro/kontext/text-to-image", new JsonObject
        {
            ["prompt"] = prompt, ["aspect_ratio"] = aspect, ["num_images"] = 1, ["seed"] = seed,
            ["safety_tolerance"] = "5", ["output_format"] = "png",
        });

    /// <summary>Kontext's image editing: the picture is sent inline (a data URI) with an instruction like "remove the compass in the corner".</summary>
    public static Task<byte[]> Edit(HttpClient http, string imagePath, string instruction, long seed) =>
        Run(http, "https://fal.run/fal-ai/flux-pro/kontext", new JsonObject
        {
            ["prompt"] = instruction, ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(imagePath)),
            ["num_images"] = 1, ["seed"] = seed, ["safety_tolerance"] = "5", ["output_format"] = "png",
        });

    static async Task<byte[]> Run(HttpClient http, string endpoint, JsonObject body)
    {
        var key = Environment.GetEnvironmentVariable("FAL_KEY") ?? Environment.GetEnvironmentVariable("FAL_KEY", EnvironmentVariableTarget.User)
            ?? throw new Exception("FAL_KEY is not set");
        for (int attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Key", key);
            using var res = await http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if ((int)res.StatusCode == 429 && attempt < 20) { await Task.Delay(3000 + attempt * 1000); continue; }
            if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            var url = JsonNode.Parse(text)!["images"]?[0]?["url"]?.GetValue<string>() ?? throw new Exception("no image url");
            var bytes = url.StartsWith("data:") ? Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]) : await http.GetByteArrayAsync(url);
            if (bytes.Length < 20000) throw new Exception("suspiciously small image (blocked by the safety filter?)");
            return bytes;
        }
    }
}

static class Comfy
{
    const string BaseUrl = "http://127.0.0.1:8188";

    static (int w, int h) Size(string aspect) => aspect switch
    {
        "16:9" => (1344, 768), "1:1" => (1024, 1024), "3:4" => (896, 1152), _ => (832, 1216),
    };

    public static async Task<byte[]> Generate(HttpClient http, string engine, string prompt, string negative, long seed, string aspect)
    {
        var (w, h) = Size(aspect);
        bool flux = engine == "flux";
        var ckpt = flux ? "flux1-schnell-fp8.safetensors" : "Juggernaut-XL_v9_RunDiffusionPhoto_v2.safetensors";
        var wf = new JsonObject
        {
            ["4"] = Node("CheckpointLoaderSimple", new JsonObject { ["ckpt_name"] = ckpt }),
            ["6"] = Node("CLIPTextEncode", new JsonObject { ["text"] = prompt, ["clip"] = Link("4", 1) }),
            ["7"] = Node("CLIPTextEncode", new JsonObject { ["text"] = flux ? "" : negative, ["clip"] = Link("4", 1) }),
            ["5"] = Node("EmptyLatentImage", new JsonObject { ["width"] = w, ["height"] = h, ["batch_size"] = 1 }),
            ["3"] = Node("KSampler", new JsonObject
            {
                ["seed"] = seed, ["steps"] = flux ? 4 : 30, ["cfg"] = flux ? 1.0 : 4.5,
                ["sampler_name"] = flux ? "euler" : "dpmpp_2m", ["scheduler"] = flux ? "simple" : "karras", ["denoise"] = 1.0,
                ["model"] = Link("4", 0), ["positive"] = Link("6", 0), ["negative"] = Link("7", 0), ["latent_image"] = Link("5", 0),
            }),
            ["8"] = Node("VAEDecode", new JsonObject { ["samples"] = Link("3", 0), ["vae"] = Link("4", 2) }),
            ["9"] = Node("SaveImage", new JsonObject { ["filename_prefix"] = "tavern", ["images"] = Link("8", 0) }),
        };
        var body = new JsonObject { ["prompt"] = wf, ["client_id"] = "tavern-artgen" };
        using var res = await http.PostAsync(BaseUrl + "/prompt", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new Exception($"ComfyUI /prompt {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
        var promptId = JsonNode.Parse(text)!["prompt_id"]!.GetValue<string>();
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1500);
            var entry = JsonNode.Parse(await http.GetStringAsync($"{BaseUrl}/history/{promptId}"))![promptId];
            if (entry == null) continue;
            if (entry["status"]?["status_str"]?.GetValue<string>() == "error") throw new Exception("ComfyUI failed");
            var img = entry["outputs"]?["9"]?["images"]?[0];
            if (img == null) continue;
            var url = $"{BaseUrl}/view?filename={Uri.EscapeDataString(img["filename"]!.GetValue<string>())}&subfolder={Uri.EscapeDataString(img["subfolder"]?.GetValue<string>() ?? "")}&type={img["type"]!.GetValue<string>()}";
            return await http.GetByteArrayAsync(url);
        }
        throw new TimeoutException("ComfyUI timed out");
    }

    static JsonObject Node(string type, JsonObject inputs) => new() { ["class_type"] = type, ["inputs"] = inputs };
    static JsonArray Link(string node, int slot) => new(node, slot);
}
