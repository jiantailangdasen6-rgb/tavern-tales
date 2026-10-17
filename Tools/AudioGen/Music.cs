namespace Tavern.AudioGen;

/// <summary>
/// Looping music in an old-tavern, Celtic-medieval style (lute = plucked string, flute, frame drum, drones):
/// tavern (warm), explore (misty, sparse), battle (driving), boss (heavy), ending (resolving), plus a fire crackle ambience.
/// </summary>
public static class Music
{
    const int R = Synth.SampleRate;
    static int S(double seconds) => (int)(seconds * R);

    public static void WriteAll(string dir)
    {
        Directory.CreateDirectory(dir);
        Write(dir, "tavern", Tavern());
        Write(dir, "explore", Explore());
        Write(dir, "battle", Battle());
        Write(dir, "boss", Boss());
        Write(dir, "ending", Ending(), loop: false);
        Write(dir, "fire", Fire(), gain: 0.5);
    }

    static void Write(string dir, string name, float[] buf, bool loop = true, double gain = 0.62)
    {
        if (loop) buf = Synth.LoopCrossfade(buf, 1.5);
        Synth.FadeEdges(buf, 0.02);
        Synth.Normalize(buf, gain);
        Synth.WriteWav(Path.Combine(dir, name + ".wav"), buf);
        Console.WriteLine($"bgm/{name}.wav  {buf.Length / (double)R:0.0}s");
    }

    static void Arp(float[] b, Random rng, string[][] chords, double beat, int perBar, double gain, double startAt = 0, double dur = 1.6)
    {
        for (int bar = 0; bar < chords.Length; bar++)
            for (int n = 0; n < perBar; n++)
            {
                double t = startAt + bar * beat * 4 + n * beat * 4 / perBar;
                var note = chords[bar][n % chords[bar].Length];
                Synth.Pluck(b, S(t), Synth.Note(note), dur, n == 0 ? gain * 1.4 : gain, 0.9975, 0.45, rng);
            }
    }

    static void Melody(float[] b, Random rng, (string note, double beat, double len)[] notes, double beat, double gain, double startAt = 0)
    {
        foreach (var (note, at, len) in notes) Synth.Flute(b, S(startAt + at * beat), Synth.Note(note), len * beat * 0.95, gain, rng);
    }

    /// <summary>The tavern: a warm lute air in D dorian, a soft flute, the frame drum on the first beat.</summary>
    static float[] Tavern()
    {
        const double beat = 60.0 / 84;
        var chords = new[]
        {
            new[] { "D3", "A3", "D4", "F4" }, new[] { "C3", "G3", "C4", "E4" }, new[] { "F3", "C4", "F4", "A4" }, new[] { "G3", "D4", "G4", "B4" },
            new[] { "D3", "A3", "D4", "F4" }, new[] { "A2", "E3", "A3", "C4" }, new[] { "G3", "D4", "G4", "B4" }, new[] { "D3", "A3", "D4", "F4" },
        };
        double len = beat * 4 * chords.Length * 2 + 1.5;
        var b = Synth.Empty(len);
        var rng = new Random(101);
        Synth.Drone(b, 0, Synth.Note("D2"), len - 0.3, 0.05);
        Arp(b, rng, chords, beat, 8, 0.17);
        Arp(b, rng, chords, beat, 8, 0.15, startAt: beat * 4 * chords.Length);
        for (int bar = 0; bar < chords.Length * 2; bar++) Synth.Drum(b, S(bar * beat * 4), 0.4, 0.28, 85, rng);
        var tune = new (string, double, double)[]
        {
            ("A4", 0, 2), ("D5", 2, 1), ("C5", 3, 1), ("A4", 4, 2), ("G4", 6, 1), ("F4", 7, 1),
            ("E4", 8, 1.5), ("F4", 9.5, 0.5), ("G4", 10, 2), ("A4", 12, 3), ("D4", 15, 1),
            ("F4", 16, 2), ("A4", 18, 1), ("C5", 19, 1), ("D5", 20, 3), ("C5", 23, 1),
            ("A4", 24, 2), ("G4", 26, 1), ("E4", 27, 1), ("D4", 28, 4),
        };
        Melody(b, rng, tune, beat, 0.2, startAt: beat * 4 * chords.Length);
        Synth.Reverb(b, 0.32, 0.55);
        return b;
    }

    /// <summary>Exploring: a misty drone, sparse lute notes, a lonely flute (D aeolian).</summary>
    static float[] Explore()
    {
        const double beat = 60.0 / 60;
        double len = beat * 4 * 12 + 2;
        var b = Synth.Empty(len);
        var rng = new Random(202);
        Synth.Drone(b, 0, Synth.Note("D2"), len - 0.3, 0.09);
        Synth.Drone(b, 0, Synth.Note("A2"), len - 0.3, 0.05);
        string[] sparse = { "D4", "A3", "F4", "E4", "C4", "D4", "A3", "G3", "F3", "E3", "A3", "D4" };
        for (int i = 0; i < 24; i++) Synth.Pluck(b, S(i * beat * 2 + rng.NextDouble() * 0.1), Synth.Note(sparse[i % sparse.Length]), 2.4, 0.16, 0.998, 0.35, rng);
        var tune = new (string, double, double)[]
        {
            ("A4", 4, 3), ("G4", 7, 1), ("F4", 8, 4), ("E4", 14, 2), ("D4", 16, 4),
            ("F4", 24, 3), ("E4", 27, 1), ("C4", 28, 4), ("D4", 34, 6),
        };
        Melody(b, rng, tune, beat, 0.15);
        Synth.Reverb(b, 0.45, 0.7);
        return b;
    }

    /// <summary>Battle: a driving frame drum and a lute ostinato in A minor, flute stabs.</summary>
    static float[] Battle()
    {
        const double beat = 60.0 / 128;
        const int bars = 16;
        double len = beat * 4 * bars + 1.5;
        var b = Synth.Empty(len);
        var rng = new Random(303);
        Synth.Drone(b, 0, Synth.Note("A1"), len - 0.3, 0.07);
        for (int bar = 0; bar < bars; bar++)
            for (int n = 0; n < 8; n++)
            {
                double t = bar * beat * 4 + n * beat / 2;
                if (n % 4 == 0) Synth.Drum(b, S(t), 0.35, 0.7, 78, rng);
                else if (n % 2 == 0) Synth.Drum(b, S(t), 0.2, 0.35, 120, rng);
                else Synth.Drum(b, S(t), 0.1, 0.18, 220, rng);
            }
        string[] riff = { "A3", "C4", "E4", "A3", "D4", "E4", "C4", "B3" };
        string[] riff2 = { "F3", "A3", "C4", "F3", "G3", "B3", "D4", "E4" };
        for (int bar = 0; bar < bars; bar++)
        {
            var r = (bar / 2) % 2 == 0 ? riff : riff2;
            for (int n = 0; n < 8; n++) Synth.Pluck(b, S(bar * beat * 4 + n * beat / 2), Synth.Note(r[n]), 0.5, 0.2, 0.994, 0.6, rng);
        }
        for (int bar = 3; bar < bars; bar += 4)
        {
            Synth.Flute(b, S(bar * beat * 4), Synth.Note("E5"), beat * 1.5, 0.18, rng);
            Synth.Flute(b, S(bar * beat * 4 + beat * 2), Synth.Note("D5"), beat * 1.8, 0.18, rng);
        }
        Synth.Reverb(b, 0.22, 0.45);
        return b;
    }

    /// <summary>The boss: heavy slow drums, a low drone, metal strikes and a chromatic lute line.</summary>
    static float[] Boss()
    {
        const double beat = 60.0 / 100;
        const int bars = 12;
        double len = beat * 4 * bars + 2;
        var b = Synth.Empty(len);
        var rng = new Random(404);
        Synth.Drone(b, 0, Synth.Note("D1"), len - 0.3, 0.12);
        Synth.Drone(b, 0, Synth.Note("G#1"), len - 0.3, 0.06);
        for (int bar = 0; bar < bars; bar++)
        {
            double t = bar * beat * 4;
            Synth.Drum(b, S(t), 0.6, 0.85, 55, rng);
            Synth.Drum(b, S(t + beat * 1.5), 0.4, 0.5, 60, rng);
            Synth.Drum(b, S(t + beat * 2), 0.6, 0.75, 55, rng);
            if (bar % 2 == 0) Synth.Metal(b, S(t), 1.2, 0.25, 140, rng);
        }
        string[] line = { "D3", "D#3", "D3", "C#3", "D3", "F3", "E3", "D#3" };
        for (int bar = 0; bar < bars; bar++)
            for (int n = 0; n < 8; n++)
                Synth.Pluck(b, S(bar * beat * 4 + n * beat / 2), Synth.Note(line[n]), 0.45, 0.17, 0.993, 0.55, rng);
        Synth.Reverb(b, 0.3, 0.6);
        return b;
    }

    /// <summary>The ending: a slow lute in D major with a flute that comes home.</summary>
    static float[] Ending()
    {
        const double beat = 60.0 / 64;
        var chords = new[]
        {
            new[] { "D3", "A3", "D4", "F#4" }, new[] { "B2", "F#3", "B3", "D4" }, new[] { "G3", "D4", "G4", "B4" }, new[] { "A3", "E4", "A4", "C#5" },
            new[] { "D3", "A3", "D4", "F#4" }, new[] { "G3", "D4", "G4", "B4" }, new[] { "A3", "E4", "A4", "C#5" }, new[] { "D3", "A3", "D4", "F#4" },
        };
        double len = beat * 4 * chords.Length + 4;
        var b = Synth.Empty(len);
        var rng = new Random(505);
        Synth.Drone(b, 0, Synth.Note("D2"), len - 0.5, 0.07);
        Arp(b, rng, chords, beat, 6, 0.17, dur: 2.0);
        var tune = new (string, double, double)[]
        {
            ("F#5", 0, 3), ("E5", 3, 1), ("D5", 4, 2), ("B4", 6, 2), ("D5", 8, 2), ("E5", 10, 2), ("A4", 12, 4),
            ("F#5", 16, 2), ("G5", 18, 2), ("F#5", 20, 2), ("E5", 22, 2), ("D5", 24, 8),
        };
        Melody(b, rng, tune, beat, 0.2);
        Synth.Reverb(b, 0.4, 0.7);
        return b;
    }

    /// <summary>A hearth fire: soft rumble and random crackles (loops under the tavern music).</summary>
    static float[] Fire()
    {
        double len = 20;
        var b = Synth.Empty(len);
        var rng = new Random(606);
        for (int i = 0; i < b.Length; i++) b[i] = (float)((rng.NextDouble() * 2 - 1) * 0.03);
        // a low-pass for the rumble
        float prev = 0;
        for (int i = 0; i < b.Length; i++) { prev += (b[i] - prev) * 0.02f; b[i] = prev * 4; }
        for (int i = 0; i < 260; i++)
        {
            double t = rng.NextDouble() * (len - 0.2);
            Synth.Drum(b, S(t), 0.02 + rng.NextDouble() * 0.03, 0.05 + rng.NextDouble() * 0.15, 1500 + rng.Next(0, 2500), rng);
        }
        return b;
    }
}
