using UnityEngine;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Singleton that manages spawning visual effects (ParticleSystem prefabs).
    /// Handles one-shot effects and persistent (looping) effects.
    /// </summary>
    public class EffectManager : MonoBehaviour
    {
        public static EffectManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Default auto-destroy time for one-shot effects (seconds)")]
        [SerializeField] private float defaultEffectLifetime = 3f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Spawn a one-shot particle effect at a position. Auto-destroys after lifetime.
        /// </summary>
        public GameObject SpawnEffect(GameObject prefab, Vector3 position, Quaternion rotation = default)
        {
            if (prefab == null) return null;

            if (rotation == default)
                rotation = Quaternion.identity;

            GameObject effectObj = Instantiate(prefab, position, rotation);
            effectObj.name = "FX_" + prefab.name;

            // Try to get actual particle duration, otherwise use default
            float lifetime = defaultEffectLifetime;
            var ps = effectObj.GetComponent<ParticleSystem>();
            if (ps != null && !ps.main.loop)
            {
                lifetime = ps.main.duration + ps.main.startLifetime.constantMax;
            }

            Destroy(effectObj, lifetime);
            return effectObj;
        }

        /// <summary>
        /// Spawn a persistent (looping) particle effect as a child of a Transform.
        /// Returns the GameObject so it can be destroyed later.
        /// </summary>
        public GameObject SpawnPersistentEffect(GameObject prefab, Transform parent)
        {
            if (prefab == null || parent == null) return null;

            GameObject effectObj = Instantiate(prefab, parent.position, parent.rotation, parent);
            effectObj.name = "FX_Persistent_" + prefab.name;
            effectObj.transform.localPosition = Vector3.zero;

            return effectObj;
        }

        /// <summary>
        /// Stop and destroy a persistent effect.
        /// </summary>
        public void StopEffect(GameObject effectObj)
        {
            if (effectObj == null) return;

            // Stop all particle systems gracefully before destroying
            var particles = effectObj.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in particles)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // Destroy after a short delay to let particles fade out
            Destroy(effectObj, 2f);
        }
    }
}
