#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Music (crossfaded), the hearth ambience and sound effects. Resources/Music/music.json says which file plays for each
    /// scene, place and sound (free tracks, Docs/credits.md); anything it leaves out uses the clips synthesized by Tools/AudioGen
    /// into Resources/Audio. Default volume is low; automated runs are silent (AudioListener.volume = 0 with -autoquit / -mute).
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public static AudioDirector? Instance { get; private set; }

        AudioSource _musicA = null!, _musicB = null!, _ambience = null!;
        readonly List<AudioSource> _sfx = new List<AudioSource>();
        readonly Dictionary<string, AudioClip?> _clips = new Dictionary<string, AudioClip?>();
        readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        string _music = "";
        bool _aPlaying;
        float _fade = 1;

        /// <summary>0..3: off, quiet (default), normal, loud.</summary>
        public static int Level
        {
            get => int.TryParse(AppMain.Arg("-volume"), out var v) ? v : PlayerPrefs.GetInt("volume_level", 1);   // -volume: checks, not saved
            set { PlayerPrefs.SetInt("volume_level", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }
        static float LevelScale => new[] { 0f, 0.4f, 0.7f, 1f }[Mathf.Clamp(Level, 0, 3)];
        // Music measured at about -19 dBFS RMS (music.json gains): "小" plays it near -31 dBFS, "大" near -23 (the old "小" was about -45, too quiet to hear).
        const float MusicBase = 0.6f, SfxBase = 0.7f, AmbienceBase = 0.5f;

        public static AudioDirector Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioDirector>();
            // Without a listener Unity plays nothing at all (the scene is built from code and had none: the game was silent).
            if (FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            a._musicA = a.NewSource(true); a._musicB = a.NewSource(true); a._ambience = a.NewSource(true);
            for (int i = 0; i < 8; i++) a._sfx.Add(a.NewSource(false));
            Instance = a;
            if (AppMain.Flag("-audiocheck")) a.StartCoroutine(a.CheckOutput());
            return a;
        }

        AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0;
            return s;
        }

        AudioClip? Clip(string resourcePath)
        {
            if (_clips.TryGetValue(resourcePath, out var c)) return c;
            _clips[resourcePath] = c = Resources.Load<AudioClip>(resourcePath);
            if (c != null && Map["gain"]?[resourcePath] is JToken g) _gains[c] = (float)g;
            return c;
        }

        /// <summary>Per-file loudness correction from music.json "gain" (measured with Editor/AudioLevels).</summary>
        readonly Dictionary<AudioClip, float> _gains = new Dictionary<AudioClip, float>();
        float Gain(AudioClip? c) => c != null && _gains.TryGetValue(c, out var g) ? g : 1f;

        static JObject? _map;
        static JObject Map => _map ??= LoadMap();

        /// <summary>music.json; a broken file only means the synthesized audio plays (it must never stop the game).</summary>
        static JObject LoadMap()
        {
            if (!(Resources.Load<TextAsset>("Music/music") is TextAsset t)) return new JObject();
            try { return JObject.Parse(t.text); }
            catch (Newtonsoft.Json.JsonException e) { Debug.LogWarning("[Audio] music.json: " + e.Message); return new JObject(); }
        }

        /// <summary>The music scene of a place, by its background art ("cave", "crypt", "ritual"; "explore" for the rest).</summary>
        public static string Zone(string art) => (string?)Map["zones"]?[art] ?? "explore";

        /// <summary>The chosen file for a music scene, else the synthesized one (new scenes fall back to exploring, endings to "ending").</summary>
        AudioClip? MusicClip(string name)
        {
            if ((string?)Map["music"]?[name] is string path && Clip(path) is AudioClip chosen) return chosen;
            return Clip("Audio/bgm/" + name)
                ?? Clip("Audio/bgm/" + (name.StartsWith("ending") ? "ending" : "explore"));
        }

        AudioClip? SfxClip(string name) =>
            ((string?)Map["sfx"]?[name] is string path ? Clip(path) : null) ?? Clip("Audio/se/" + name);

        /// <summary>Plays a sound effect (se/name).</summary>
        public static void Play(string name, float volume = 1)
        {
            var a = Instance;
            if (a == null) return;
            var clip = a.SfxClip(name);
            if (clip == null) return;
            // a jingle (a 20, a won fight) is called from both the dice and the log line: let it play once
            if (clip.length > 1f && a._lastPlayed.TryGetValue(name, out var at) && Time.unscaledTime - at < clip.length) return;
            a._lastPlayed[name] = Time.unscaledTime;
            var src = a._sfx.Find(s => !s.isPlaying) ?? a._sfx[0];
            src.pitch = 1 + Random.Range(-0.03f, 0.03f);
            src.PlayOneShot(clip, volume * SfxBase * LevelScale * a.Gain(clip));
        }

        /// <summary>Switches the music (a scene of music.json) with a crossfade; the hearth crackles under the tavern music.</summary>
        public static void Music(string name, bool loop = true)
        {
            var a = Instance;
            if (a == null || a._music == name) return;
            a._music = name;
            var clip = a.MusicClip(name);
            Debug.Log($"[Audio] {name} -> {(clip != null ? clip.name : "none")}");
            var next = a._aPlaying ? a._musicB : a._musicA;
            next.clip = clip; next.loop = loop; next.volume = 0;
            if (clip != null) next.Play();
            a._aPlaying = !a._aPlaying;
            a._fade = 0;
            var fire = ((string?)Map["ambience"] is string amb ? a.Clip(amb) : null) ?? a.Clip("Audio/bgm/fire");
            if (name == "tavern" && fire != null) { if (!a._ambience.isPlaying) { a._ambience.clip = fire; a._ambience.Play(); } }
            else a._ambience.Stop();
        }

        /// <summary>
        /// -audiocheck: proves sound reaches the output without anyone listening (OutputMeter measures the final mix
        /// and silences it) and logs its level.
        /// </summary>
        System.Collections.IEnumerator CheckOutput()
        {
            // The meter clears the mix after measuring it, so the listener can stay at full volume without a sound.
            var meter = gameObject.AddComponent<OutputMeter>();
            for (int i = 0; i < 3; i++) { AudioListener.volume = 1; yield return new WaitForSecondsRealtime(1.5f); }
            var (rms, peak) = meter.Read();
            foreach (var s in new[] { _musicA, _musicB, _ambience })
                Debug.Log($"[AudioCheck] source clip={(s.clip != null ? s.clip.name + " " + s.clip.loadState + " " + s.clip.loadType : "none")} playing={s.isPlaying} time={s.time:0.00} volume={s.volume:0.000} mute={s.mute}");
            var cfg = AudioSettings.GetConfiguration();
            Debug.Log($"[AudioCheck] listener volume={AudioListener.volume} pause={AudioListener.pause} rate={AudioSettings.outputSampleRate} speaker={cfg.speakerMode} dsp={cfg.dspBufferSize} driver={AudioSettings.driverCapabilities} listeners={FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}");
            Debug.Log($"[AudioCheck] music \"{_music}\" level {Level}: output rms {rms:0.0} dBFS, peak {peak:0.0} dBFS");
        }

        void Update()
        {
            _fade = Mathf.MoveTowards(_fade, 1, Time.unscaledDeltaTime / 1.5f);
            var on = _aPlaying ? _musicA : _musicB;
            var off = _aPlaying ? _musicB : _musicA;
            on.volume = MusicBase * LevelScale * Gain(on.clip) * _fade;
            off.volume = MusicBase * LevelScale * Gain(off.clip) * (1 - _fade);
            if (_fade >= 1 && off.isPlaying) off.Stop();
            _ambience.volume = AmbienceBase * LevelScale * Gain(_ambience.clip);
        }
    }
}
