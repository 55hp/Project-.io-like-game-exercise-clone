using System;
using System.Collections.Generic;
using UnityEngine;

namespace HP55.Audio
{
    [Serializable]
    public class AudioEntry
    {
        public string          Id;
        public AudioClip       Clip;
        [Range(0f, 1f)] public float Volume = 1f;
        public bool            Loop   = false;
    }

    /// <summary>
    /// ScriptableObject di configurazione audio.
    /// Crea un'istanza via: Assets → Create → HP55 → Audio → AudioConfig
    /// </summary>
    [CreateAssetMenu(fileName = "AudioConfig", menuName = "HP55/Audio/AudioConfig")]
    public class AudioConfig : ScriptableObject
    {
        [Header("SFX")]
        public List<AudioEntry> SFXEntries = new();

        [Header("Music")]
        public List<AudioEntry> MusicEntries = new();

        [Header("Pool")]
        [Range(1, 16)] public int SFXPoolSize = 8;

        // ── Lookup tables (build a runtime) ───────────────────────────────────

        private Dictionary<string, AudioEntry> _sfxMap;
        private Dictionary<string, AudioEntry> _musicMap;

        public void Initialize()
        {
            _sfxMap = new Dictionary<string, AudioEntry>(SFXEntries.Count);
            foreach (var e in SFXEntries)
            {
                if (string.IsNullOrEmpty(e.Id))
                {
                    Debug.LogWarning("[AudioConfig] SFX entry senza Id — ignorata.");
                    continue;
                }
                _sfxMap[e.Id] = e;
            }

            _musicMap = new Dictionary<string, AudioEntry>(MusicEntries.Count);
            foreach (var e in MusicEntries)
            {
                if (string.IsNullOrEmpty(e.Id))
                {
                    Debug.LogWarning("[AudioConfig] Music entry senza Id — ignorata.");
                    continue;
                }
                _musicMap[e.Id] = e;
            }
        }

        public bool TryGetSFX(string id, out AudioEntry entry)
            => _sfxMap.TryGetValue(id, out entry);

        public bool TryGetMusic(string id, out AudioEntry entry)
            => _musicMap.TryGetValue(id, out entry);
    }
}
