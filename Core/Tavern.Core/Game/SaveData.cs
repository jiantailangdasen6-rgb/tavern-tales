#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tavern.Core.Content;
using Tavern.Core.Gm;
using Tavern.Core.Rules;

namespace Tavern.Core.Game
{
    public sealed class CharacterSave
    {
        public string Name = "", ClassId = "", Background = "", Portrait = "";
        public int Might, Skill, Wits, Spirit, MaxHp, MaxFocus, Hp, Focus, FateCoins, DeathSaveFailures, NextCheckBonus;
        public LifeState State;
        public List<string> Cards = new List<string>(), Items = new List<string>();

        public static CharacterSave From(Character c) => new CharacterSave
        {
            Name = c.Name, ClassId = c.ClassId, Background = c.Background, Portrait = c.Portrait,
            Might = c.Modifier(Stat.Might), Skill = c.Modifier(Stat.Skill), Wits = c.Modifier(Stat.Wits), Spirit = c.Modifier(Stat.Spirit),
            MaxHp = c.MaxHp, MaxFocus = c.MaxFocus, Hp = c.Hp, Focus = c.Focus, FateCoins = c.FateCoins,
            DeathSaveFailures = c.DeathSaveFailures, NextCheckBonus = c.NextCheckBonus, State = c.State,
            Cards = c.Cards.ToList(), Items = c.Items.ToList(),
        };

        public Character ToCharacter()
        {
            var c = new Character(Name, ClassId, Might, Skill, Wits, Spirit, MaxHp, MaxFocus, Background) { Portrait = Portrait };
            c.Restore(Hp, Focus, FateCoins, State, DeathSaveFailures, NextCheckBonus);
            c.Cards.AddRange(Cards); c.Items.AddRange(Items);
            return c;
        }
    }

    public sealed class MessageSave
    {
        public Role Role;
        public string Content = "";
        public string? ToolName;
        public List<string> CallNames = new List<string>();
        public List<JObject> CallArgs = new List<JObject>();
        public JToken? Raw;

        public static MessageSave From(ChatMessage m) => new MessageSave
        {
            Role = m.Role, Content = m.Content, ToolName = m.ToolName, Raw = m.Raw,
            CallNames = m.ToolCalls.Select(c => c.Name).ToList(), CallArgs = m.ToolCalls.Select(c => c.Arguments).ToList(),
        };

        public ChatMessage ToMessage() => new ChatMessage(Role, Content,
            CallNames.Select((n, i) => new ToolCall(n, i < CallArgs.Count ? CallArgs[i] : new JObject())).ToList(), ToolName, Raw);
    }

    /// <summary>An evening in progress, saved on the host after every turn ("continue another day").</summary>
    public sealed class SaveData
    {
        public int Version = 1;
        public GameState State = new GameState();
        public List<CharacterSave> Party = new List<CharacterSave>();
        public List<MessageSave> History = new List<MessageSave>();

        public static SaveData From(TavernGame game) => new SaveData
        {
            State = game.State,
            Party = game.Party.Select(CharacterSave.From).ToList(),
            History = game.Gm.History.Where(m => m.Role != Role.System).Select(MessageSave.From).ToList(),
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
        public static SaveData FromJson(string json) => JsonConvert.DeserializeObject<SaveData>(json)!;

        /// <summary>Rebuilds the game. The storyteller and scenario come from their ids in the state.</summary>
        public TavernGame Load(ContentDb db, ScenarioDef scenario, Storyteller teller, ILlmClient llm, ILlmClient? highlightLlm, IRandom rng)
            => TavernGame.Restore(db, scenario, teller, Party.Select(p => p.ToCharacter()).ToList(), State,
                History.Select(h => h.ToMessage()), llm, highlightLlm, rng);
    }
}
