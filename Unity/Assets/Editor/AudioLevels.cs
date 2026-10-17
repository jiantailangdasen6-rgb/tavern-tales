#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tavern.Unity.Editor
{
    /// <summary>
    /// Measures how loud each clip is (RMS and peak in dBFS), since nobody on the build side can listen:
    /// powershell -File Tools/RunUnity.ps1 -Method Tavern.Unity.Editor.AudioLevels.Report  -> Logs/audio-levels.txt.
    /// Each clip is decoded on load for the measurement and then put back to its import settings.
    /// </summary>
    public static class AudioLevels
    {
        public static void Report()
        {
            var folders = new[] { "Assets/Resources/Audio", "Assets/Resources/Music", "Assets/Resources/Sfx" }.Where(AssetDatabase.IsValidFolder).ToArray();
            var lines = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                var keep = ai.defaultSampleSettings;
                var decoded = keep;
                decoded.loadType = AudioClipLoadType.DecompressOnLoad;
                ai.defaultSampleSettings = decoded;
                ai.SaveAndReimport();
                try
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    var data = new float[clip.samples * clip.channels];
                    clip.GetData(data, 0);
                    double sum = 0; float peak = 0;
                    foreach (var v in data) { sum += v * v; peak = Math.Max(peak, Math.Abs(v)); }
                    var rms = Math.Sqrt(sum / Math.Max(1, data.Length));
                    lines.Add($"{Db(rms),7:0.0} rms {Db(peak),6:0.0} peak {clip.length,7:0.0}s  {path.Replace("Assets/Resources/", "")}");
                }
                finally
                {
                    ai.defaultSampleSettings = keep;
                    ai.SaveAndReimport();
                }
            }
            lines.Sort(StringComparer.Ordinal);
            var outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs/audio-levels.txt"));
            File.WriteAllLines(outPath, lines);
            Debug.Log($"[AudioLevels] {lines.Count} clips -> {outPath}");
        }

        static double Db(double v) => 20 * Math.Log10(Math.Max(v, 1e-6));
    }
}
