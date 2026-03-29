using UnityEngine;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Singleton that manages playing sound effects.
    /// Handles one-shot SFX and looping ambient sounds.
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float defaultSFXVolume = 1f;
        [SerializeField] private int maxSimultaneousSFX = 10;

        private AudioSource _sfxSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Create a shared AudioSource for one-shot SFX
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f; // 2D fallback
        }

        /// <summary>
        /// Play a one-shot sound at a specific world position (3D).
        /// </summary>
        public void PlaySFX(AudioClip clip, Vector3 position, float volume = -1f)
        {
            if (clip == null) return;

            float vol = volume >= 0f ? volume : defaultSFXVolume;
            AudioSource.PlayClipAtPoint(clip, position, vol);
        }

        /// <summary>
        /// Play a one-shot sound (2D, no position).
        /// </summary>
        public void PlaySFX2D(AudioClip clip, float volume = -1f)
        {
            if (clip == null || _sfxSource == null) return;

            float vol = volume >= 0f ? volume : defaultSFXVolume;
            _sfxSource.PlayOneShot(clip, vol);
        }

        /// <summary>
        /// Attach a looping AudioSource to a target Transform.
        /// Returns the AudioSource so it can be stopped later.
        /// </summary>
        public AudioSource PlayLoop(AudioClip clip, Transform parent, float volume = 0.5f)
        {
            if (clip == null || parent == null) return null;

            // Create a child GameObject with AudioSource
            GameObject loopObj = new GameObject("AmbientSound_" + clip.name);
            loopObj.transform.SetParent(parent);
            loopObj.transform.localPosition = Vector3.zero;

            AudioSource source = loopObj.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = 1f; // 3D sound
            source.minDistance = 2f;
            source.maxDistance = 20f;
            source.Play();

            return source;
        }

        /// <summary>
        /// Stop and destroy a looping AudioSource.
        /// </summary>
        public void StopLoop(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            Destroy(source.gameObject);
        }
    }
}
