// File: Assets/Scripts/Audio/MusicManager.cs
// Persistent music manager that plays looping tracks for menu and game scenes.

using UnityEngine;
using UnityEngine.SceneManagement;

namespace TheWaningBorder.Audio
{
    /// <summary>
    /// Plays looping background music, crossfading between menu and game tracks.
    /// Persists across scene loads via DontDestroyOnLoad.
    ///
    /// Place audio files in Resources/Audio/:
    ///   - MenuTheme.ogg   (or .mp3 / .wav)
    ///   - GameTheme.ogg   (or .mp3 / .wav)
    /// </summary>
    public class MusicManager : MonoBehaviour
    {
        private static MusicManager _instance;

        private AudioSource _sourceA;
        private AudioSource _sourceB;

        private AudioClip _menuClip;
        private AudioClip _gameClip;

        private float _masterVolume = 0.5f;
        private const float FadeDuration = 1.5f;

        // Crossfade state
        private bool _isFading;
        private float _fadeTimer;
        private AudioSource _fadeIn;
        private AudioSource _fadeOut;

        // ═══════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;

            var go = new GameObject("MusicManager");
            _instance = go.AddComponent<MusicManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            // Singleton guard
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Create two AudioSources for crossfading
            _sourceA = gameObject.AddComponent<AudioSource>();
            _sourceB = gameObject.AddComponent<AudioSource>();
            ConfigureSource(_sourceA);
            ConfigureSource(_sourceB);

            // Load clips from Resources/Audio/
            _menuClip = Resources.Load<AudioClip>("Audio/MenuTheme");
            _gameClip = Resources.Load<AudioClip>("Audio/GameTheme");

            if (_menuClip == null) Debug.LogWarning("[MusicManager] Missing Resources/Audio/MenuTheme audio clip");
            if (_gameClip == null) Debug.LogWarning("[MusicManager] Missing Resources/Audio/GameTheme audio clip");

            // Listen for scene changes
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Play the right track for the current scene
            PlayForScene(SceneManager.GetActiveScene().name);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (!_isFading) return;

            _fadeTimer += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_fadeTimer / FadeDuration);

            if (_fadeIn != null) _fadeIn.volume = t * _masterVolume;
            if (_fadeOut != null) _fadeOut.volume = (1f - t) * _masterVolume;

            if (t >= 1f)
            {
                _isFading = false;
                if (_fadeOut != null)
                {
                    _fadeOut.Stop();
                    _fadeOut.volume = 0f;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SCENE HANDLING
        // ═══════════════════════════════════════════════════════════════

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PlayForScene(scene.name);
        }

        private void PlayForScene(string sceneName)
        {
            // Earlier missing braces meant CrossfadeTo(_menuClip) ran
            // unconditionally — in the Game scene the second call immediately
            // replaced the first, so menu music played in-game and game music
            // never played. The Menu scene worked by accident (only the second
            // line fired). (task-059 F-2 / MB-30)
            if (string.Equals(sceneName, "Game"))
                CrossfadeTo(_gameClip);
            else
                CrossfadeTo(_menuClip);
        }

        // ═══════════════════════════════════════════════════════════════
        // PLAYBACK
        // ═══════════════════════════════════════════════════════════════

        private void CrossfadeTo(AudioClip clip)
        {
            if (clip == null) return;

            // Determine which source is currently playing
            AudioSource current = _sourceA.isPlaying ? _sourceA : (_sourceB.isPlaying ? _sourceB : null);
            AudioSource next = (current == _sourceA) ? _sourceB : _sourceA;

            // Already playing this clip — skip
            if (current != null && current.clip == clip && current.isPlaying)
                return;

            // Set up the incoming source
            next.clip = clip;
            next.volume = 0f;
            next.Play();

            // If nothing was playing, snap to full volume immediately
            if (current == null || !current.isPlaying)
            {
                next.volume = _masterVolume;
                return;
            }

            // Start crossfade
            _fadeIn = next;
            _fadeOut = current;
            _fadeTimer = 0f;
            _isFading = true;
        }

        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        private static void ConfigureSource(AudioSource src)
        {
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;  // 2D audio
            src.volume = 0f;
        }

        /// <summary>
        /// Set master music volume (0–1). Can be called from a settings UI.
        /// </summary>
        public static void SetVolume(float volume)
        {
            if (_instance == null) return;
            _instance._masterVolume = Mathf.Clamp01(volume);

            // Update currently playing source
            if (_instance._sourceA.isPlaying && !_instance._isFading)
                _instance._sourceA.volume = _instance._masterVolume;
            if (_instance._sourceB.isPlaying && !_instance._isFading)
                _instance._sourceB.volume = _instance._masterVolume;
        }

        /// <summary>
        /// Get current master music volume.
        /// </summary>
        public static float GetVolume()
        {
            return _instance != null ? _instance._masterVolume : 0.5f;
        }
    }
}
