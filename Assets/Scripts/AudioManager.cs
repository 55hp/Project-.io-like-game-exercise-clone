using System.Collections.Generic;
using UnityEngine;

namespace HP55.Audio
{
    /// <summary>
    /// Singleton che gestisce tutta la riproduzione audio.
    /// Si iscrive automaticamente ai segnali di AudioSignals
    /// ed espone un'API diretta per l'uso via codice.
    ///
    /// SETUP:
    ///   1. Crea un AudioConfig asset (Create → HP55 → Audio → AudioConfig)
    ///   2. Aggiungici i tuoi clip con Id univoci
    ///   3. Crea un GameObject "AudioManager" nella scena iniziale
    ///   4. Aggiungi questo script e assegna l'AudioConfig nel serialized field
    ///
    /// USO VIA CODICE:
    ///   AudioManager.Instance.PlaySFX("coin");
    ///   AudioManager.Instance.PlayMusic("theme_main");
    ///
    /// USO VIA SIGNAL (da qualsiasi script, senza riferimento al Manager):
    ///   AudioSignals.PlaySFX("coin");
    ///   AudioSignals.PlayMusic("theme_main");
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────

        public static AudioManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────

        [Header("Config")]
        [SerializeField] private AudioConfig _config;

        [Header("Default Volumes")]
        [SerializeField, Range(0f, 1f)] private float _sfxVolume   = 1f;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 1f;

        // ── Internal state ────────────────────────────────────────────────────

        private AudioSource        _musicSource;
        private List<AudioSource>  _sfxPool  = new();
        private int                _sfxIndex = 0;
        private bool               _isMuted  = false;

        // ── PlayerPrefs keys ──────────────────────────────────────────────────

        private const string K_SFX_VOL   = "hp55_audio_sfx";
        private const string K_MUSIC_VOL = "hp55_audio_music";
        private const string K_MUTED     = "hp55_audio_muted";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _config.Initialize();
            BuildPool();
            LoadPrefs();
        }

        private void OnEnable()  => Subscribe();
        private void OnDisable() => Unsubscribe();

        // ── Pool setup ────────────────────────────────────────────────────────

        private void BuildPool()
        {
            _musicSource            = gameObject.AddComponent<AudioSource>();
            _musicSource.loop       = true;
            _musicSource.playOnAwake= false;

            for (int i = 0; i < _config.SFXPoolSize; i++)
            {
                var src          = gameObject.AddComponent<AudioSource>();
                src.playOnAwake  = false;
                _sfxPool.Add(src);
            }
            AudioManager.Instance.PlayMusic("walk");
        }

        // ── Signal wiring ─────────────────────────────────────────────────────

        private void Subscribe()
        {
            AudioSignals.OnPlaySFXRequested     += PlaySFX;
            AudioSignals.OnPlaySFXClipRequested += PlaySFX;
            AudioSignals.OnStopSFXRequested     += StopSFX;
            AudioSignals.OnPlayMusicRequested   += PlayMusic;
            AudioSignals.OnStopMusicRequested   += StopMusic;
            AudioSignals.OnPauseMusicRequested  += PauseMusic;
            AudioSignals.OnResumeMusicRequested += ResumeMusic;
            AudioSignals.OnSFXVolumeChanged     += SetSFXVolume;
            AudioSignals.OnMusicVolumeChanged   += SetMusicVolume;
            AudioSignals.OnMuteToggled          += ToggleMute;
        }

        private void Unsubscribe()
        {
            AudioSignals.OnPlaySFXRequested     -= PlaySFX;
            AudioSignals.OnPlaySFXClipRequested -= PlaySFX;
            AudioSignals.OnStopSFXRequested     -= StopSFX;
            AudioSignals.OnPlayMusicRequested   -= PlayMusic;
            AudioSignals.OnStopMusicRequested   -= StopMusic;
            AudioSignals.OnPauseMusicRequested  -= PauseMusic;
            AudioSignals.OnResumeMusicRequested -= ResumeMusic;
            AudioSignals.OnSFXVolumeChanged     -= SetSFXVolume;
            AudioSignals.OnMusicVolumeChanged   -= SetMusicVolume;
            AudioSignals.OnMuteToggled          -= ToggleMute;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Riproduce un SFX per Id (definito in AudioConfig).</summary>
        public void PlaySFX(string id)
        {
            if (_isMuted) return;

            if (!_config.TryGetSFX(id, out var entry))
            {
                Debug.LogWarning($"[AudioManager] SFX '{id}' non trovato in AudioConfig.");
                return;
            }

            var src    = NextSFXSource();
            src.clip   = entry.Clip;
            src.volume = entry.Volume * _sfxVolume;
            src.Play();
        }

        /// <summary>Riproduce un AudioClip diretto (senza passare per il registry).</summary>
        public void PlaySFX(AudioClip clip, float volume = 1f)
        {
            if (_isMuted || clip == null) return;

            var src    = NextSFXSource();
            src.clip   = clip;
            src.volume = Mathf.Clamp01(volume) * _sfxVolume;
            src.Play();
        }

        /// <summary>Ferma tutti i SFX attivi che corrispondono all'Id.</summary>
        public void StopSFX(string id)
        {
            if (!_config.TryGetSFX(id, out var entry)) return;

            foreach (var src in _sfxPool)
                if (src.clip == entry.Clip && src.isPlaying)
                    src.Stop();
        }

        /// <summary>Avvia la musica per Id. Ignora se la traccia è già in riproduzione.</summary>
        public void PlayMusic(string id)
        {
            if (!_config.TryGetMusic(id, out var entry))
            {
                Debug.LogWarning($"[AudioManager] Music '{id}' non trovata in AudioConfig.");
                return;
            }

            if (_musicSource.clip == entry.Clip && _musicSource.isPlaying) return;

            _musicSource.clip   = entry.Clip;
            _musicSource.loop   = entry.Loop;
            _musicSource.volume = _isMuted ? 0f : entry.Volume * _musicVolume;
            _musicSource.Play();
        }

        public void StopMusic()   => _musicSource.Stop();
        public void PauseMusic()  => _musicSource.Pause();
        public void ResumeMusic() => _musicSource.UnPause();

        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            SavePrefs();
        }

        public void SetMusicVolume(float volume)
        {
            _musicVolume            = Mathf.Clamp01(volume);
            _musicSource.volume     = _isMuted ? 0f : _musicVolume;
            SavePrefs();
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            ApplyVolumes();
            SavePrefs();
        }

        // ── Getters ───────────────────────────────────────────────────────────

        public bool  IsMuted      => _isMuted;
        public float SFXVolume   => _sfxVolume;
        public float MusicVolume => _musicVolume;

        // ── Helpers ───────────────────────────────────────────────────────────

        private AudioSource NextSFXSource()
        {
            var src   = _sfxPool[_sfxIndex];
            _sfxIndex = (_sfxIndex + 1) % _sfxPool.Count;
            return src;
        }

        private void ApplyVolumes()
        {
            _musicSource.volume = _isMuted ? 0f : _musicVolume;
            // Il volume SFX viene applicato a ogni Play(), non serve aggiornare il pool
        }

        private void LoadPrefs()
        {
            _sfxVolume   = PlayerPrefs.GetFloat(K_SFX_VOL,   _sfxVolume);
            _musicVolume = PlayerPrefs.GetFloat(K_MUSIC_VOL, _musicVolume);
            _isMuted     = PlayerPrefs.GetInt(K_MUTED, 0) == 1;
            ApplyVolumes();
        }

        private void SavePrefs()
        {
            PlayerPrefs.SetFloat(K_SFX_VOL,   _sfxVolume);
            PlayerPrefs.SetFloat(K_MUSIC_VOL, _musicVolume);
            PlayerPrefs.SetInt(K_MUTED,       _isMuted ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
