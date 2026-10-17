#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Tavern.Core.Content
{
    public sealed class WorldDef
    {
        public string Name = "", Summary = "";
        public List<string> Places = new List<string>(), Gods = new List<string>(), History = new List<string>(), Factions = new List<string>();

        /// <summary>The world as prompt text (every scenario happens in this one world).</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"世界: {Name}。{Summary}");
            sb.AppendLine("土地: " + string.Join(" / ", Places));
            sb.AppendLine("神々: " + string.Join(" / ", Gods));
            sb.AppendLine("歴史: " + string.Join(" / ", History));
            sb.AppendLine("勢力: " + string.Join(" / ", Factions));
            return sb.ToString();
        }
    }

    public sealed class ArtEntry { public string Id = "", About = ""; }

    public sealed class ArtCatalog
    {
        public List<ArtEntry> Backgrounds = new List<ArtEntry>(), Portraits = new List<ArtEntry>();
    }

    /// <summary>A story skeleton (one of the eight types); the AI fills it into an evening's scenario sheet.</summary>
    public sealed class TemplateDef
    {
        public string Id = "", Name = "", Premise = "";
        public List<string> Acts = new List<string>(), Roles = new List<string>(), Bosses = new List<string>(), Twists = new List<string>();
    }

    public sealed class StoryContent
    {
        public WorldDef World = new WorldDef();
        public ArtCatalog Catalog = new ArtCatalog();
        public List<TemplateDef> Templates = new List<TemplateDef>();

        sealed class TemplateFile { public List<TemplateDef> Templates = new List<TemplateDef>(); }

        public static StoryContent Load(string worldJson, string catalogJson, string templatesJson) => new StoryContent
        {
            World = JsonConvert.DeserializeObject<WorldDef>(worldJson)!,
            Catalog = JsonConvert.DeserializeObject<ArtCatalog>(catalogJson)!,
            Templates = JsonConvert.DeserializeObject<TemplateFile>(templatesJson)!.Templates,
        };

        public static StoryContent LoadFolder(string folder)
            => Load(System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "world.json")),
                    System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "art_catalog.json")),
                    System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "templates.json")));

        public TemplateDef? Template(string? id) => Templates.FirstOrDefault(t => t.Id == id);
    }
}
