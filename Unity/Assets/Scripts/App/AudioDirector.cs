#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Tavern.Unity.App
{
    /// <summary>
    /// Music (crossfaded), the hearth ambience and sound effects. Clips are synthesized by Tools/AudioGen into
    /// Resources/Audio. Default volume is low; automated runs are silent (AudioListener.volume = 0 with -autoquit / -mute).
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

        AudioClip? Clip(string path)
        {
            if (!_clips.TryGetValue(path, out var c)) _clips[path] = c = Resources.Load<AudioClip>("Audio/" + path);
            return c;
        }

        /// <summary>Plays a sound effect (se/name).</summary>
        public static void Play(string name, float volume = 1)
        {
            var a = Instance;
            if (a == null) return;
            var clip = a.Clip("se/" + name);
            if (clip == null) return;
            var src = a._sfx.Find(s => !s.isPlaying) ?? a._sfx[0];
            src.pitch = 1 + Random.Range(-0.03f, 0.03f);
            src.PlayOneShot(clip, volume * SfxBase * LevelScale);
        }

        /// <summary>Switches the music (bgm/name) with a crossfade; the hearth crackles under the tavern music.</summary>
        public static void Music(string name, bool loop = true)
        {
            var a = Instance;
            if (a == null || a._music == name) return;
            a._music = name;
            var clip = a.Clip("bgm/" + name);
            var next = a._aPlaying ? a._musicB : a._musicA;
            next.clip = clip; next.loop = loop; next.volume = 0;
            if (clip != null) next.Play();
            a._aPlaying = !a._aPlaying;
            a._fade = 0;
            var fire = a.Clip("bgm/fire");
            if (name == "tavern" && fire != null) { if (!a._ambience.isPlaying) { a._ambience.clip = fire; a._ambience.Play(); } }
            else a._ambience.Stop();
        }

        void Update()
        {
            _fade = Mathf.MoveTowards(_fade, 1, Time.unscaledDeltaTime / 1.5f);
            var on = _aPlaying ? _musicA : _musicB;
            var off = _aPlaying ? _musicB : _musicA;
            on.volume = MusicBase * LevelScale * _fade;
            off.volume = MusicBase * LevelScale * (1 - _fade);
            if (_fade >= 1 && off.isPlaying) off.Stop();
            _ambience.volume = AmbienceBase * LevelScale;
        }
    }
}
