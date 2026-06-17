using System;
using UnityEngine;

namespace HP55.Audio
{
    /// <summary>
    /// Bus statico di segnali audio.
    ///
    /// USO VIA SIGNAL (da qualsiasi punto del codice, senza riferimento al Manager):
    ///   AudioSignals.PlaySFX("coin");
    ///   AudioSignals.PlayMusic("theme_main");
    ///   AudioSignals.SetMusicVolume(0.5f);
    ///
    /// USO VIA SUBSCRIPTION (ascoltare eventi audio, es. per UI reattiva):
    ///   AudioSignals.OnPlaySFXRequested += id => Debug.Log($"SFX: {id}");
    ///
    /// NOTA: il bus non esegue l'audio — lo fa AudioManager che si iscrive
    ///       agli stessi eventi in OnEnable/OnDisable.
    /// </summary>
    public static class AudioSignals
    {
        // ── SFX ───────────────────────────────────────────────────────────────

        /// <summary>Richiede la riproduzione di un SFX tramite Id.</summary>
        public static event Action<string> OnPlaySFXRequested;

        /// <summary>Richiede la riproduzione di un SFX tramite AudioClip diretto.</summary>
        public static event Action<AudioClip, float> OnPlaySFXClipRequested;

        /// <summary>Richiede lo stop di un SFX attivo per Id.</summary>
        public static event Action<string> OnStopSFXRequested;

        // ── Music ─────────────────────────────────────────────────────────────

        /// <summary>Richiede la riproduzione di una traccia music per Id.</summary>
        public static event Action<string> OnPlayMusicRequested;

        /// <summary>Richiede lo stop della musica.</summary>
        public static event Action OnStopMusicRequested;

        /// <summary>Richiede la pausa della musica.</summary>
        public static event Action OnPauseMusicRequested;

        /// <summary>Richiede la ripresa della musica dopo pausa.</summary>
        public static event Action OnResumeMusicRequested;

        // ── Volume / Mute ─────────────────────────────────────────────────────

        /// <summary>Imposta il volume globale degli SFX (0–1).</summary>
        public static event Action<float> OnSFXVolumeChanged;

        /// <summary>Imposta il volume globale della musica (0–1).</summary>
        public static event Action<float> OnMusicVolumeChanged;

        /// <summary>Togglea il mute globale.</summary>
        public static event Action OnMuteToggled;

        // ── Emitters ──────────────────────────────────────────────────────────

        public static void PlaySFX(string id)
            => OnPlaySFXRequested?.Invoke(id);

        public static void PlaySFX(AudioClip clip, float volume = 1f)
            => OnPlaySFXClipRequested?.Invoke(clip, volume);

        public static void StopSFX(string id)
            => OnStopSFXRequested?.Invoke(id);

        public static void PlayMusic(string id)
            => OnPlayMusicRequested?.Invoke(id);

        public static void StopMusic()
            => OnStopMusicRequested?.Invoke();

        public static void PauseMusic()
            => OnPauseMusicRequested?.Invoke();

        public static void ResumeMusic()
            => OnResumeMusicRequested?.Invoke();

        public static void SetSFXVolume(float volume)
            => OnSFXVolumeChanged?.Invoke(Mathf.Clamp01(volume));

        public static void SetMusicVolume(float volume)
            => OnMusicVolumeChanged?.Invoke(Mathf.Clamp01(volume));

        public static void ToggleMute()
            => OnMuteToggled?.Invoke();
    }
}
