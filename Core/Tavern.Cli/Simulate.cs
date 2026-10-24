using Tavern.Core.Play;

/// <summary>
/// Fights a battle many times on the rules alone (no AI) to see how hard it is:
///   dotnet run --project Core/Tavern.Cli -- --simulate [boneknight,skeleton] [--runs 2000] [--party warrior,mage,thief]
/// Each adventurer plays a simple sensible tactic (the warrior covers the weakest before a big move, the mage holds a boss before
/// its big move, the priest heals, everyone else hits the weakest foe). Prints wins, wipes and deaths per difficulty.
/// </summary>
static class Simulate
{
    static double StartHp = 1.0;

    public static int Run(string contentDir, List<string> a, Func<string, string?> Opt)
    {
        var classes = ClassBook.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "classes.json")));
        var enemies = Bestiary.Load(File.ReadAllText(Path.Combine(contentDir, "Rules", "enemies.json")));
        var foeIds = (Opt("--simulate") is string f && !f.StartsWith("--") ? f : "boneknight,skeleton").Split(',');
        var runs = int.TryParse(Opt("--runs"), out var r) ? r : 2000;
        StartHp = double.TryParse(Opt("--start-hp"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sh) ? sh : 1.0;
        var parties = Opt("--party") is string p ? new[] { p.Split(',') } : new[]
        {
            new[] { "warrior", "mage", "thief" },
            new[] { "warrior", "mage", "thief", "priest" },
            new[] { "warrior", "priest" },
            new[] { "thief" },
        };
        Console.WriteLine($"simulate: {string.Join(" + ", foeIds)}, {runs} fights each");
        foreach (var party in parties)
            foreach (var d in new[] { Difficulty.Gentle, Difficulty.Standard, Difficulty.Harsh })
            {
                int wins = 0, wipes = 0, deaths = 0, rounds = 0;
                for (int i = 0; i < runs; i++)
                {
                    var (won, dead, n) = Fight(classes, enemies, party, foeIds, d, new SeededRandom((ulong)(i * 7919 + 13)));
                    if (won) wins++; else wipes++;
                    if (dead > 0) deaths++;
                    rounds += n;
                }
                Console.WriteLine($"  {string.Join("/", party),-28} {d.Label(),-4} win {100.0 * wins / runs,5:0.0}%  wipe {100.0 * wipes / runs,5:0.0}%  someone died {100.0 * deaths / runs,5:0.0}%  rounds {(double)rounds / runs:0.0}");
            }
        return 0;
    }

    static (bool won, int dead, int rounds) Fight(ClassBook classes, Bestiary enemies, string[] party, string[] foeIds, Difficulty d, IRandom rng)
    {
        var heroes = party.Where(c => !c.StartsWith("npc")).Select((c, i) => new Adventurer(classes[c], c + i, c + i)).ToList();
        // "npc" in the party: a companion of normal build (攻め), moved by the game
        var companions = party.Where(c => c.StartsWith("npc")).Select((c, i) => Creature.FromBuild(Build.Normal, "npc" + i, "仲間" + i, Side.Party, CompanionRole.Attack)).ToList();
        foreach (var h in heroes) { h.StartEvening(d); h.TakeDamage((int)(h.MaxHp * (1 - StartHp))); }
        var foes = foeIds.Select((id, i) => Creature.FromEnemy(enemies[id], id + i, id + i, Combat.ScaledHp(enemies[id].Hp, heroes.Count, d))).ToList();
        var init = heroes.ToDictionary(h => h.Id, _ => rng.D20());
        var c = Combat.Start(heroes, companions, foes, init, false, d, rng);
        int round = 0;
        while (c.State() == CombatEnd.Ongoing && round < 30)
        {
            round++;
            var boss = c.Foes.FirstOrDefault(x => x.InFight && x.Boss);
            var bigSoon = boss != null && c.BigMoveDue.Contains(boss.Id);
            // stances first
            foreach (var h in heroes.Where(h => h.CanAct))
                if (h.Class.Id == "warrior" && bigSoon && h.Focus >= 1)
                {
                    var weak = heroes.Where(x => x != h && x.IsUp).OrderBy(x => x.Hp).FirstOrDefault();
                    if (weak != null) c.Cover(h, weak.Id, EffectSize.Small);
                }
            Fighter? f;
            while ((f = c.NextTurn()) != null && c.State() == CombatEnd.Ongoing)
            {
                var lines = new List<string>();
                if (f.Adventurer is Adventurer h)
                {
                    if (h.Life == Life.Down) { c.DeathSaveTurn(h, rng.D20(), lines); continue; }
                    if (!c.BeginTurn(f, lines)) { c.EndTurn(f); continue; }
                    Act(c, h, heroes, rng);
                }
                else if (f.Creature is Creature e && e.Side == Side.Foe && c.BeginTurn(f, lines))
                {
                    var plan = c.PlanEnemy(e);
                    if (e.BigMove?.Summon.Length > 0 && plan.Blows.Count == 0 && plan.Lines.Any(l => l.Contains("」！"))) c.Summon(e, enemies[e.BigMove.Summon], heroes.Count);
                    foreach (var b in plan.Blows) c.Resolve(b, rng.D20());
                }
                else if (f.Creature is Creature m && m.Side == Side.Party && c.BeginTurn(f, lines))
                {
                    c.CompanionTurn(m);
                }
                c.EndTurn(f);
            }
            c.EndRound();
        }
        return (c.State() == CombatEnd.Won, heroes.Count(h => h.Life == Life.Dead), round);
    }

    static void Act(Combat c, Adventurer h, List<Adventurer> heroes, IRandom rng)
    {
        var foe = c.Foes.Where(x => x.InFight).OrderBy(x => x.Hp).FirstOrDefault();
        if (foe == null) return;
        var boss = c.Foes.FirstOrDefault(x => x.InFight && x.Boss);
        var down = heroes.FirstOrDefault(x => x.Life == Life.Down);
        switch (h.Class.Id)
        {
            case "priest":
                var hurt = heroes.Where(x => x.Life != Life.Dead && (x.Life == Life.Down || x.Hp * 2 < x.MaxHp)).OrderBy(x => x.Hp).FirstOrDefault();
                if (hurt != null && h.Focus >= 2) { c.HealEffect(h, hurt.Id, EffectSize.Medium); return; }
                if (foe.Undead && h.Focus >= 1) { c.Offense(h, new Effect(EffectKind.Strike, EffectSize.Small), Ability.Mnd, foe.Id, rng.D20()); return; }
                c.Attack(h, foe.Id, rng.D20());
                return;
            case "mage":
                if (boss != null && !boss.HeldOnce && c.BigMoveDue.Contains(boss.Id) && h.Focus >= 3) { c.Offense(h, new Effect(EffectKind.Hold, EffectSize.Large), Ability.Int, boss.Id, rng.D20()); return; }
                if (c.Foes.Count(x => x.InFight) > 1 && h.Focus >= 2) { c.Offense(h, new Effect(EffectKind.Sweep, EffectSize.Small), Ability.Int, "", rng.D20()); return; }
                c.Attack(h, foe.Id, rng.D20(), magicMissile: true);
                return;
            default:
                if (down != null && h.Class.Id == "warrior") { c.Revive(h, down.Id); return; }
                c.Attack(h, foe.Id, rng.D20());
                return;
        }
    }
}
