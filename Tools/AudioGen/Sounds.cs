namespace Tavern.AudioGen;

/// <summary>Short sound effects: dice on a wooden table, blows, spells, the table's little noises.</summary>
public static class Sounds
{
    const int R = Synth.SampleRate;
    static int S(double seconds) => (int)(seconds * R);

    public static void WriteAll(string dir)
    {
        Directory.CreateDirectory(dir);
        Write(dir, "dice_roll", DiceRoll());
        Write(dir, "dice_land", DiceLand());
        Write(dir, "crit", Crit());
        Write(dir, "fumble", Fumble());
        Write(dir, "hit", Hit());
        Write(dir, "miss", Miss());
        Write(dir, "spell", Spell());
        Write(dir, "heal", Heal());
        Write(dir, "enemy", Enemy());
        Write(dir, "warn", Warn());
        Write(dir, "click", Click());
        Write(dir, "declare", Declare());
        Write(dir, "growth", Growth());
        Write(dir, "victory", Victory());
        Write(dir, "defeat", Defeat());
        Write(dir, "page", Page());
    }

    static void Write(string dir, string name, float[] buf)
    {
        Synth.FadeEdges(buf, 0.004);
        Synth.Normalize(buf, 0.8);
        Synth.WriteWav(Path.Combine(dir, name + ".wav"), buf);
        Console.WriteLine($"se/{name}.wav  {buf.Length / (double)R:0.00}s");
    }

    /// <summary>A d20 tumbling across a wooden table: quick bright clicks that slow down and soften.</summary>
    static float[] DiceRoll()
    {
        var b = Synth.Empty(1.3);
        var rng = new Random(7);
        double t = 0.02, gap = 0.045;
        for (int i = 0; i < 14 && t < 1.15; i++)
        {
            var g = 0.55 * (1 - t / 1.3);
            Synth.Drum(b, S(t), 0.06, g, 700 + rng.Next(0, 500), rng);
            Synth.Drum(b, S(t + 0.004), 0.08, g * 0.5, 180 + rng.Next(0, 60), rng);
            t += gap;
            gap *= 1.17;
        }
        Synth.Reverb(b, 0.12, 0.25);
        return b;
    }

    static float[] DiceLand()
    {
        var b = Synth.Empty(0.5);
        var rng = new Random(8);
        Synth.Drum(b, 0, 0.25, 0.9, 140, rng);
        Synth.Drum(b, S(0.005), 0.06, 0.4, 900, rng);
        Synth.Reverb(b, 0.15, 0.3);
        return b;
    }

    static float[] Crit()
    {
        var b = Synth.Empty(2.0);
        string[] notes = { "D5", "F#5", "A5", "D6" };
        for (int i = 0; i < notes.Length; i++) Synth.Bell(b, S(i * 0.07), Synth.Note(notes[i]), 1.6, 0.35);
        Synth.Reverb(b, 0.35, 0.6);
        return b;
    }

    static float[] Fumble()
    {
        var b = Synth.Empty(1.4);
        var rng = new Random(9);
        Synth.Drum(b, 0, 0.5, 0.8, 70, rng);
        Synth.Pluck(b, S(0.05), Synth.Note("A3"), 0.6, 0.35, 0.995, 0.3, rng);
        Synth.Pluck(b, S(0.25), Synth.Note("F3"), 0.6, 0.35, 0.995, 0.3, rng);
        Synth.Pluck(b, S(0.45), Synth.Note("C#3"), 0.9, 0.4, 0.995, 0.3, rng);
        Synth.Reverb(b, 0.25, 0.45);
        return b;
    }

    static float[] Hit()
    {
        var b = Synth.Empty(0.7);
        var rng = new Random(10);
        Synth.Drum(b, 0, 0.3, 0.8, 95, rng);
        Synth.Metal(b, 0, 0.5, 0.45, 520, rng);
        Synth.Reverb(b, 0.18, 0.35);
        return b;
    }

    static float[] Miss()
    {
        var b = Synth.Empty(0.5);
        Synth.Whoosh(b, 0, 0.4, 0.6, 900, 300, new Random(11));
        return b;
    }

    static float[] Spell()
    {
        var b = Synth.Empty(1.4);
        var rng = new Random(12);
        Synth.Whoosh(b, 0, 0.7, 0.5, 200, 1800, rng);
        Synth.Bell(b, S(0.3), Synth.Note("A5"), 1.0, 0.25);
        Synth.Bell(b, S(0.38), Synth.Note("E6"), 0.9, 0.2);
        Synth.Reverb(b, 0.4, 0.6);
        return b;
    }

    static float[] Heal()
    {
        var b = Synth.Empty(2.2);
        string[] chord = { "D5", "F#5", "A5" };
        foreach (var n in chord) Synth.Bell(b, 0, Synth.Note(n), 1.8, 0.22);
        Synth.Bell(b, S(0.25), Synth.Note("D6"), 1.6, 0.18);
        Synth.Reverb(b, 0.45, 0.65);
        return b;
    }

    static float[] Enemy()
    {
        var b = Synth.Empty(2.0);
        var rng = new Random(13);
        Synth.Drone(b, 0, Synth.Note("D2"), 1.8, 0.25);
        Synth.Drone(b, 0, Synth.Note("G#2"), 1.8, 0.18);
        Synth.Drum(b, 0, 0.6, 0.85, 60, rng);
        Synth.Drum(b, S(0.45), 0.6, 0.75, 55, rng);
        Synth.Reverb(b, 0.35, 0.6);
        return b;
    }

    static float[] Warn()
    {
        var b = Synth.Empty(1.6);
        var rng = new Random(14);
        Synth.Metal(b, 0, 1.2, 0.45, 180, rng);
        Synth.Drum(b, 0, 0.5, 0.7, 65, rng);
        Synth.Drone(b, 0, Synth.Note("C#2"), 1.4, 0.2);
        Synth.Reverb(b, 0.35, 0.55);
        return b;
    }

    static float[] Click()
    {
        var b = Synth.Empty(0.12);
        Synth.Paper(b, 0, 0.08, 0.5, new Random(15));
        Synth.Drum(b, 0, 0.05, 0.25, 1200, new Random(16));
        return b;
    }

    static float[] Declare()
    {
        var b = Synth.Empty(1.0);
        var rng = new Random(17);
        Synth.Pluck(b, 0, Synth.Note("D4"), 0.8, 0.4, 0.997, 0.5, rng);
        Synth.Pluck(b, S(0.11), Synth.Note("A4"), 0.8, 0.4, 0.997, 0.5, rng);
        Synth.Reverb(b, 0.25, 0.45);
        return b;
    }

    static float[] Growth()
    {
        var b = Synth.Empty(2.0);
        string[] notes = { "D5", "E5", "F#5", "A5", "D6" };
        for (int i = 0; i < notes.Length; i++) Synth.Bell(b, S(i * 0.11), Synth.Note(notes[i]), 1.4, 0.26);
        Synth.Reverb(b, 0.4, 0.6);
        return b;
    }

    static float[] Victory()
    {
        var b = Synth.Empty(3.0);
        var rng = new Random(18);
        string[] notes = { "D4", "F#4", "A4", "D5", "F#5", "A5" };
        for (int i = 0; i < notes.Length; i++) Synth.Pluck(b, S(i * 0.12), Synth.Note(notes[i]), 1.6, 0.35, 0.998, 0.5, rng);
        Synth.Bell(b, S(0.75), Synth.Note("D6"), 2.0, 0.25);
        Synth.Drone(b, 0, Synth.Note("D3"), 2.6, 0.12);
        Synth.Reverb(b, 0.4, 0.6);
        return b;
    }

    static float[] Defeat()
    {
        var b = Synth.Empty(3.5);
        var rng = new Random(19);
        Synth.Drone(b, 0, Synth.Note("D2"), 3.2, 0.22);
        string[] notes = { "A4", "F4", "D4", "A3" };
        for (int i = 0; i < notes.Length; i++) Synth.Pluck(b, S(i * 0.45), Synth.Note(notes[i]), 1.6, 0.35, 0.997, 0.35, rng);
        Synth.Reverb(b, 0.45, 0.7);
        return b;
    }

    static float[] Page()
    {
        var b = Synth.Empty(0.35);
        Synth.Paper(b, 0, 0.3, 0.45, new Random(20));
        return b;
    }
}
