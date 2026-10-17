namespace Tavern.AudioGen;

/// <summary>
/// 音を合成するための基本部品。すべて 1 チャンネル(モノラル)の float 配列で扱い、最後に 16bit WAV にする。
/// 「チープに聞こえない」ために、物理モデル寄りの音源(撥弦の Karplus-Strong、息のノイズを混ぜた笛)と、
/// 残響(簡易リバーブ)を使う。
/// </summary>
public static class Synth
{
    public const int SampleRate = 44100;

    // ---- 基本波形 ----

    public static float[] Empty(double seconds) => new float[(int)(seconds * SampleRate)];

    /// <summary>ADSR 風の簡易エンベロープ(attack / decay を秒で指定)。</summary>
    public static double Env(double t, double dur, double attack, double release, double curve = 2.0)
    {
        if (t < 0 || t > dur) return 0;
        double a = attack <= 0 ? 1 : Math.Min(1, t / attack);
        double r = release <= 0 ? 1 : Math.Min(1, (dur - t) / release);
        return Math.Pow(a, 1.0) * Math.Pow(r, curve);
    }

    /// <summary>撥弦(Karplus-Strong)。ハープ・リュート向け。damping が小さいほど長く伸びる。</summary>
    public static void Pluck(float[] buf, int start, double freq, double dur, double gain, double damping = 0.996, double brightness = 0.5, Random? rng = null)
    {
        rng ??= new Random(1);
        int n = Math.Max(2, (int)(SampleRate / freq));
        var delay = new double[n];
        for (int i = 0; i < n; i++) delay[i] = rng.NextDouble() * 2 - 1;
        // 初期波形の高域を落として柔らかくする
        for (int pass = 0; pass < (int)((1 - brightness) * 6); pass++)
            for (int i = 1; i < n; i++) delay[i] = (delay[i] + delay[i - 1]) * 0.5;

        int len = (int)(dur * SampleRate);
        int idx = 0;
        double prev = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double cur = delay[idx];
            double next = (cur + prev) * 0.5 * damping;
            prev = cur;
            delay[idx] = next;
            idx = (idx + 1) % n;
            buf[p] += (float)(cur * gain * Env(i / (double)SampleRate, dur, 0.001, dur * 0.7, 1.4));
        }
    }

    /// <summary>笛(ティンホイッスル風)。正弦 + 奇数倍音 + 息のノイズ + ビブラート。</summary>
    public static void Flute(float[] buf, int start, double freq, double dur, double gain, Random? rng = null, double vibrato = 0.006)
    {
        rng ??= new Random(2);
        int len = (int)(dur * SampleRate);
        double phase = 0, phase3 = 0, phase2 = 0;
        double breath = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double vib = 1 + Math.Sin(2 * Math.PI * 5.2 * t) * vibrato * Math.Min(1, t / 0.25);
            double f = freq * vib;
            phase += 2 * Math.PI * f / SampleRate;
            phase2 += 2 * Math.PI * f * 2 / SampleRate;
            phase3 += 2 * Math.PI * f * 3 / SampleRate;
            double tone = Math.Sin(phase) + 0.18 * Math.Sin(phase2) + 0.10 * Math.Sin(phase3);
            // 息づかい(低域を通したノイズ)。立ち上がりで強く
            breath = breath * 0.93 + (rng.NextDouble() * 2 - 1) * 0.07;
            double air = breath * (0.5 + 1.6 * Math.Exp(-t * 14));
            double e = Env(t, dur, 0.05, 0.12, 1.2);
            buf[p] += (float)((tone * 0.55 + air) * gain * e);
        }
    }

    /// <summary>低いドローン(バグパイプ風。のこぎり波 + うなり)。</summary>
    public static void Drone(float[] buf, int start, double freq, double dur, double gain)
    {
        int len = (int)(dur * SampleRate);
        double p1 = 0, p2 = 0, lp = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            p1 += freq / SampleRate;
            p2 += freq * 1.004 / SampleRate;          // わずかにずらしてうなりを作る
            double saw = (p1 % 1.0) * 2 - 1 + ((p2 % 1.0) * 2 - 1);
            lp += (saw - lp) * 0.06;                   // ローパス
            double e = Env(t, dur, 0.8, 1.2, 1.0);
            buf[p] += (float)(lp * gain * e);
        }
    }

    /// <summary>太鼓(バウロン風)。ノイズ + 急降下する低音。</summary>
    public static void Drum(float[] buf, int start, double dur, double gain, double pitch = 90, Random? rng = null)
    {
        rng ??= new Random(3);
        int len = (int)(dur * SampleRate);
        double phase = 0, bp = 0, bp2 = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double f = pitch * (1 + 2.2 * Math.Exp(-t * 55));     // 打撃で音程が落ちる
            phase += 2 * Math.PI * f / SampleRate;
            double body = Math.Sin(phase) * Math.Exp(-t * 11);
            // 皮の質感(バンドパスしたノイズ)
            double noise = rng.NextDouble() * 2 - 1;
            bp += (noise - bp) * 0.35; bp2 += (bp - bp2) * 0.35;
            double skin = (bp - bp2) * Math.Exp(-t * 30);
            buf[p] += (float)((body * 0.9 + skin * 0.7) * gain);
        }
    }

    /// <summary>金属の打撃音(剣・盾)。非調和な倍音の束。</summary>
    public static void Metal(float[] buf, int start, double dur, double gain, double baseFreq, Random? rng = null)
    {
        rng ??= new Random(4);
        double[] ratios = { 1.0, 2.37, 3.41, 4.83, 6.11, 7.52 };
        int len = (int)(dur * SampleRate);
        var phases = new double[ratios.Length];
        double hp = 0, prev = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double v = 0;
            for (int k = 0; k < ratios.Length; k++)
            {
                phases[k] += 2 * Math.PI * baseFreq * ratios[k] / SampleRate;
                v += Math.Sin(phases[k]) * Math.Exp(-t * (5 + k * 3.5)) / (k + 1);
            }
            // 擦過音(ハイパスしたノイズ)
            double noise = rng.NextDouble() * 2 - 1;
            hp = 0.9 * (hp + noise - prev); prev = noise;
            v += hp * Math.Exp(-t * 45) * 0.5;
            buf[p] += (float)(v * gain);
        }
    }

    /// <summary>風切り音・魔法の気配(ノイズをバンドパスして周波数を動かす)。</summary>
    public static void Whoosh(float[] buf, int start, double dur, double gain, double fromHz, double toHz, Random? rng = null)
    {
        rng ??= new Random(5);
        int len = (int)(dur * SampleRate);
        double b1 = 0, b2 = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double k = t / dur;
            double f = fromHz + (toHz - fromHz) * k;
            double q = Math.Clamp(f / SampleRate * 6, 0.02, 0.45);
            double noise = rng.NextDouble() * 2 - 1;
            b1 += (noise - b1) * q;
            b2 += (b1 - b2) * q;
            double band = b1 - b2;
            buf[p] += (float)(band * gain * Env(t, dur, dur * 0.25, dur * 0.5, 1.5) * 4);
        }
    }

    /// <summary>鐘(倍音が非整数の金属。低めの damping で長く響く)。</summary>
    public static void Bell(float[] buf, int start, double freq, double dur, double gain)
    {
        double[] ratios = { 0.56, 0.92, 1.0, 1.71, 2.0, 2.74, 3.0, 3.76 };
        double[] decay = { 3.0, 3.6, 2.2, 4.5, 5.0, 6.5, 6.0, 8.0 };
        int len = (int)(dur * SampleRate);
        var phases = new double[ratios.Length];
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double v = 0;
            for (int k = 0; k < ratios.Length; k++)
            {
                phases[k] += 2 * Math.PI * freq * ratios[k] / SampleRate;
                v += Math.Sin(phases[k]) * Math.Exp(-t * decay[k]) / (1 + k * 0.6);
            }
            buf[p] += (float)(v * gain * Math.Min(1, t / 0.004));
        }
    }

    /// <summary>紙をめくる音(カードのドロー)。</summary>
    public static void Paper(float[] buf, int start, double dur, double gain, Random? rng = null)
    {
        rng ??= new Random(6);
        int len = (int)(dur * SampleRate);
        double hp = 0, prev = 0, env2 = 0;
        for (int i = 0; i < len; i++)
        {
            int p = start + i;
            if (p < 0 || p >= buf.Length) break;
            double t = i / (double)SampleRate;
            double noise = rng.NextDouble() * 2 - 1;
            hp = 0.88 * (hp + noise - prev); prev = noise;
            // ざらつきを時間で変える(こすれ)
            env2 = env2 * 0.98 + Math.Abs(Math.Sin(t * 30)) * 0.02;
            buf[p] += (float)(hp * gain * Env(t, dur, 0.006, dur * 0.7, 2.0) * (0.6 + env2 * 4));
        }
    }

    // ---- 加工 ----

    /// <summary>簡易リバーブ(複数のくし形遅延 + 全域通過)。石の広間のような残響。</summary>
    public static void Reverb(float[] buf, double mix = 0.25, double decay = 0.5)
    {
        int[] combs = { 1237, 1381, 1607, 1789 };
        var outBuf = new float[buf.Length];
        foreach (var d in combs)
        {
            var line = new float[d];
            int idx = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float v = line[idx];
                outBuf[i] += v * 0.25f;
                line[idx] = (float)(buf[i] + v * decay);
                idx = (idx + 1) % d;
            }
        }
        // 全域通過で拡散
        foreach (var d in new[] { 331, 227 })
        {
            var line = new float[d];
            int idx = 0;
            for (int i = 0; i < outBuf.Length; i++)
            {
                float v = line[idx];
                float x = outBuf[i];
                outBuf[i] = (float)(-x * 0.6 + v);
                line[idx] = (float)(x + v * 0.6);
                idx = (idx + 1) % d;
            }
        }
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * (1 - mix) + outBuf[i] * mix);
    }

    /// <summary>頭と尻のクリックノイズを消す。</summary>
    public static void FadeEdges(float[] buf, double seconds = 0.005)
    {
        int n = Math.Min(buf.Length / 2, (int)(seconds * SampleRate));
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)n;
            buf[i] *= k;
            buf[buf.Length - 1 - i] *= k;
        }
    }

    /// <summary>音量を正規化(peak を target に合わせる)。</summary>
    public static void Normalize(float[] buf, double target = 0.85)
    {
        double peak = 0;
        foreach (var v in buf) peak = Math.Max(peak, Math.Abs(v));
        if (peak < 1e-6) return;
        double g = target / peak;
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(buf[i] * g);
    }

    /// <summary>ループ用: 末尾を先頭にクロスフェードして継ぎ目を消す。</summary>
    public static float[] LoopCrossfade(float[] buf, double seconds)
    {
        int n = (int)(seconds * SampleRate);
        if (n <= 0 || n * 2 >= buf.Length) return buf;
        var outBuf = new float[buf.Length - n];
        Array.Copy(buf, outBuf, outBuf.Length);
        for (int i = 0; i < n; i++)
        {
            float k = i / (float)n;
            outBuf[i] = (float)(outBuf[i] * k + buf[outBuf.Length + i] * (1 - k));
        }
        return outBuf;
    }

    /// <summary>16bit モノラル WAV として書き出す。</summary>
    public static void WriteWav(string path, float[] buf, int sampleRate = SampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataBytes = buf.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)1);            // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (var v in buf)
            w.Write((short)(Math.Clamp(v, -1f, 1f) * 32767));
    }

    // ---- 音階 ----

    /// <summary>音名(例: "D4", "A#3")から周波数を求める。</summary>
    public static double Note(string name)
    {
        string letters = "C C#D D#E F F#G G#A A#B ";
        string l = name.Length > 1 && (name[1] == '#') ? name[..2] : name[..1];
        int octave = int.Parse(name[(l.Length)..]);
        int semitone = letters.IndexOf(l.Length == 1 ? l + " " : l, StringComparison.Ordinal) / 2;
        int midi = (octave + 1) * 12 + semitone;
        return 440.0 * Math.Pow(2, (midi - 69) / 12.0);
    }
}
