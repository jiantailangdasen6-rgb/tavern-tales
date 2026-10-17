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
        string _music = "";
        bool _aPlaying;
        float _fade = 1;

        /// <summary>0..3: off, quiet (default), normal, loud.</summary>
        public static int Level
        {
            get => PlayerPrefs.GetInt("volume_level", 1);
            set { PlayerPrefs.SetInt("volume_level", Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }
        static float LevelScale => new[] { 0f, 0.45f, 0.75f, 1f }[Mathf.Clamp(Level, 0, 3)];
        const float MusicBase = 0.3f, SfxBase = 0.45f, AmbienceBase = 0.25f;

        public static AudioDirector Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioDirector>();
            a._musicA = a.NewSource(true); a._musicB = a.NewSource(true); a._ambience = a.NewSource(true);
            for (int i = 0; i < 8; i++) a._sfx.Add(a.NewSource(false));
            Instance = a;
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
        static JObject Map => _map ??= Resources.Load<TextAsset>("Music/music") is TextAsset t ? JObject.Parse(t.text) : new JObject();

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
