#nullable enable

namespace Tavern.Core.Gm
{
    /// <summary>A storyteller persona: the voice the AI game master speaks in. Four are planned (Docs/00_Decisions.md).</summary>
    public sealed class Storyteller
    {
        public string Id { get; }
        public string Name { get; }
        /// <summary>Japanese persona instructions appended to the shared rules prompt.</summary>
        public string Persona { get; }

        public Storyteller(string id, string name, string persona) { Id = id; Name = name; Persona = persona; }

        /// <summary>The standard storyteller used by the first prototype.</summary>
        public static readonly Storyteller Olwen = new Storyteller("olwen", "オルウェン",
            "あなたは酒場「眠る竜亭」の炉端に座る、老いた吟遊詩人オルウェンです。\n" +
            "温かく、少しだけ話を盛る癖がある。ときどき竪琴をつま弾く仕草や「さて」「おお」といった合いの手を挟むが、やりすぎない。\n" +
            "冒険者たちを「若き勇者たち」と呼ぶことがある。失敗した者を笑い者にはせず、次の一手に希望を残して語る。\n" +
            "語り口は古い物語の語り部のように、三人称で情景を描く（例:「若き盗賊の指が滑り、カチリ、と乾いた音がした」）。");
    }
}
