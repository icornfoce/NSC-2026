using System.Collections;
using UnityEngine;
using BuildingSimulation.Building;
using BuildingSimulation.Data;

namespace BuildingSimulation.Disaster
{
    /// <summary>
    /// Triggers natural disasters using DisasterData ScriptableObjects.
    /// Earthquake = random horizontal impulse. Wind = constant directional force.
    /// </summary>
    public class DisasterManager : MonoBehaviour
    {
        public static DisasterManager Instance { get; private set; }

        [Header("Disaster Presets (ScriptableObject)")]
        [Tooltip("Assign DisasterData assets here for quick-trigger from UI")]
        [SerializeField] private DisasterData[] disasterPresets;

        private Coroutine _earthquakeCoroutine;
        private Coroutine _windCoroutine;

        public bool IsEarthquakeActive { get; private set; }
        public bool IsWindActive { get; private set; }

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
        /// All configured disaster presets.
        /// </summary>
        public DisasterData[] Presets => disasterPresets;

        // ─── Generic Trigger ─────────────────────────────────────────

        /// <summary>
        /// Trigger a disaster from a DisasterData asset.
        /// </summary>
        public void TriggerDisaster(DisasterData data)
        {
            if (data == null) return;

            switch (data.disasterType)
            {
                case DisasterType.Earthquake:
                    TriggerEarthquake(data.duration, data.earthquakeMagnitude);
                    break;
                case DisasterType.Wind:
                    TriggerWind(data.windDirection, data.windForce, data.duration);
                    break;
            }
        }

        /// <summary>
        /// Trigger a preset by index (for UI buttons).
        /// </summary>
        public void TriggerPreset(int index)
        {
            if (disasterPresets == null || index < 0 || index >= disasterPresets.Length) return;
            TriggerDisaster(disasterPresets[index]);
        }

        // ─── Earthquake ──────────────────────────────────────────────

        /// <summary>
        /// Trigger the first Earthquake preset, or use defaults.
        /// </summary>
        public void TriggerEarthquake()
        {
            var preset = FindPreset(DisasterType.Earthquake);
            if (preset != null)
                TriggerDisaster(preset);
            else
                TriggerEarthquake(5f, 5000f);
        }

        public void TriggerEarthquake(float duration, float magnitude)
        {
            if (IsEarthquakeActive) return;
            _earthquakeCoroutine = StartCoroutine(EarthquakeCoroutine(duration, magnitude));
        }

        private IEnumerator EarthquakeCoroutine(float duration, float magnitude)
        {
            IsEarthquakeActive = true;
            Debug.Log($"EARTHQUAKE! Duration: {duration}s, Magnitude: {magnitude}N");

            float elapsed = 0f;

            while (elapsed < duration)
            {
                var parts = BuildingSystem.Instance?.PlacedParts;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        if (part == null) continue;
                        var rb = part.GetComponent<Rigidbody>();
                        if (rb == null || rb.isKinematic) continue;

                        Vector3 force = new Vector3(
                            Random.Range(-1f, 1f),
                            0f,
                            Random.Range(-1f, 1f)
                        ).normalized * magnitude * Random.Range(0.5f, 1.5f);

                        rb.AddForce(force, ForceMode.Impulse);
                    }
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            IsEarthquakeActive = false;
            Debug.Log("Earthquake ended.");
        }

        public void StopEarthquake()
        {
            if (_earthquakeCoroutine != null)
            {
                StopCoroutine(_earthquakeCoroutine);
                _earthquakeCoroutine = null;
            }
            IsEarthquakeActive = false;
        }

        // ─── Wind ─────────────────────────────────────────────────────

        /// <summary>
        /// Trigger the first Wind preset, or use defaults.
        /// </summary>
        public void TriggerWind()
        {
            var preset = FindPreset(DisasterType.Wind);
            if (preset != null)
                TriggerDisaster(preset);
            else
                TriggerWind(Vector3.right, 2000f, 10f);
        }

        public void TriggerWind(Vector3 direction, float force, float duration)
        {
            if (IsWindActive) return;
            _windCoroutine = StartCoroutine(WindCoroutine(direction.normalized, force, duration));
        }

        private IEnumerator WindCoroutine(Vector3 direction, float force, float duration)
        {
            IsWindActive = true;
            Debug.Log($"WIND! Direction: {direction}, Force: {force}N, Duration: {duration}s");

            float elapsed = 0f;

            while (elapsed < duration)
            {
                var parts = BuildingSystem.Instance?.PlacedParts;
                if (parts != null)
                {
                    foreach (var part in parts)
                    {
                        if (part == null) continue;
                        var rb = part.GetComponent<Rigidbody>();
                        if (rb == null || rb.isKinematic) continue;

                        rb.AddForce(direction * force, ForceMode.Force);
                    }
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            IsWindActive = false;
            Debug.Log("Wind ended.");
        }

        public void StopWind()
        {
            if (_windCoroutine != null)
            {
                StopCoroutine(_windCoroutine);
                _windCoroutine = null;
            }
            IsWindActive = false;
        }

        public void StopAll()
        {
            StopEarthquake();
            StopWind();
        }

        // ─── Helpers ──────────────────────────────────────────────────

        private DisasterData FindPreset(DisasterType type)
        {
            if (disasterPresets == null) return null;
            foreach (var d in disasterPresets)
            {
                if (d != null && d.disasterType == type) return d;
            }
            return null;
        }
    }
}
