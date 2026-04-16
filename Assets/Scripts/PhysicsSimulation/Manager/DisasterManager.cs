using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Simulation.Data;
using Simulation.Building;

namespace Simulation.Physics
{
    /// <summary>
    /// Executes DisasterData assets during simulation.
    ///
    /// Per DisasterType:
    ///   Earthquake  → Random multi-directional impulses on all structure Rigidbodies
    ///   Windy       → Continuous lateral force (wind direction settable via Inspector)
    ///   Tornado     → Spiraling force (tangential + inward + upward)
    ///   HeavyLoad   → Direct HP damage scaled by intensity
    ///   Flood       → Buoyancy-style upward force + lateral push
    ///   Fire        → Continuous HP drain via ApplyExternalDamage
    ///   Default     → ApplyExternalDamage burst at intensity level
    /// </summary>
    public class DisasterManager : MonoBehaviour
    {
        public static DisasterManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Direction of wind-based disasters (XZ plane, will be normalised).")]
        [SerializeField] private Vector3 windDirection = new Vector3(1f, 0f, 0f);

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;

        // Active coroutines so we can cancel mid-way
        private List<Coroutine> _activeCoroutines = new List<Coroutine>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────

        /// <summary>Queue and execute all disasters from the mission list sequentially.</summary>
        public void ExecuteDisasters(List<DisasterData> disasters)
        {
            if (disasters == null || disasters.Count == 0) return;

            Coroutine c = StartCoroutine(RunDisastersSequentially(disasters));
            _activeCoroutines.Add(c);
        }

        /// <summary>Stop all running disaster coroutines immediately.</summary>
        public void CancelDisasters()
        {
            foreach (var c in _activeCoroutines)
            {
                if (c != null) StopCoroutine(c);
            }
            _activeCoroutines.Clear();
        }

        // ─────────────────────────────────────────────────────────────
        // Sequencer
        // ─────────────────────────────────────────────────────────────

        private IEnumerator RunDisastersSequentially(List<DisasterData> disasters)
        {
            // Small delay before the first disaster so the player can see structures settle
            yield return new WaitForSeconds(1.5f);

            foreach (var disaster in disasters)
            {
                if (disaster == null) continue;

                if (showDebugLogs)
                    Debug.Log($"[DisasterManager] <color=orange>⚠ Disaster: {disaster.disasterName}</color> ({disaster.type}, intensity={disaster.intensity}, duration={disaster.duration}s)");

                // Play sound if assigned
                if (disaster.disasterSound != null)
                    AudioSource.PlayClipAtPoint(disaster.disasterSound, Vector3.zero);

                // Spawn VFX if assigned
                if (disaster.disasterVFX != null)
                    Instantiate(disaster.disasterVFX, Vector3.zero, Quaternion.identity);

                // Execute the disaster and wait for it to finish
                yield return StartCoroutine(ApplyDisaster(disaster));

                // Gap between disasters
                yield return new WaitForSeconds(0.5f);
            }

            if (showDebugLogs)
                Debug.Log("[DisasterManager] All disasters finished.");
        }

        // ─────────────────────────────────────────────────────────────
        // Disaster Execution
        // ─────────────────────────────────────────────────────────────

        private IEnumerator ApplyDisaster(DisasterData disaster)
        {
            switch (disaster.type)
            {
                case DisasterType.Earthquake:
                    yield return StartCoroutine(ApplyEarthquake(disaster));
                    break;

                case DisasterType.Windy:
                    yield return StartCoroutine(ApplyWind(disaster, windDirection, false));
                    break;

                case DisasterType.Tornado:
                    yield return StartCoroutine(ApplyTornado(disaster));
                    break;

                case DisasterType.Flood:
                    yield return StartCoroutine(ApplyFlood(disaster));
                    break;

                case DisasterType.Fire:
                case DisasterType.ToxicRain:
                    yield return StartCoroutine(ApplyContinuousDamage(disaster));
                    break;

                case DisasterType.HeavyLoad:
                    yield return StartCoroutine(ApplyHeavyLoad(disaster));
                    break;

                case DisasterType.Tsunami:
                    // Mega-flood: high intensity, short duration in one direction
                    yield return StartCoroutine(ApplyWind(disaster, new Vector3(1f, 0.2f, 0.3f).normalized, true));
                    break;

                case DisasterType.Dragon:
                case DisasterType.UFO:
                case DisasterType.Volcanic:
                default:
                    // Generic: burst damage + quick shockwave
                    yield return StartCoroutine(ApplyGenericBurst(disaster));
                    break;
            }
        }

        // ── Earthquake ────────────────────────────────────────────────

        private IEnumerator ApplyEarthquake(DisasterData disaster)
        {
            float elapsed = 0f;
            float shakeInterval = 0.15f;
            float nextShake = 0f;

            while (elapsed < disaster.duration)
            {
                elapsed += Time.fixedDeltaTime;
                nextShake -= Time.fixedDeltaTime;

                if (nextShake <= 0f)
                {
                    nextShake = shakeInterval;
                    Vector3 impulse = new Vector3(
                        Random.Range(-1f, 1f),
                        Random.Range(0f, 0.3f),   // slight upward component
                        Random.Range(-1f, 1f)
                    ).normalized * disaster.intensity;

                    ApplyImpulseToAll(impulse);
                }

                yield return new WaitForFixedUpdate();
            }
        }

        // ── Wind ─────────────────────────────────────────────────────

        private IEnumerator ApplyWind(DisasterData disaster, Vector3 direction, bool includeUpward)
        {
            float elapsed = 0f;
            Vector3 dir = direction.normalized;
            if (!includeUpward) dir.y = 0f;

            while (elapsed < disaster.duration)
            {
                elapsed += Time.fixedDeltaTime;

                // Slight gust variation
                float gust = 1f + Mathf.Sin(elapsed * 3f) * 0.25f;
                ApplyForceToAll(dir * disaster.intensity * gust);

                yield return new WaitForFixedUpdate();
            }
        }

        // ── Tornado ───────────────────────────────────────────────────

        private IEnumerator ApplyTornado(DisasterData disaster)
        {
            float elapsed = 0f;
            Vector3 tornadoCenter = GetStructureCentroid();

            while (elapsed < disaster.duration)
            {
                elapsed += Time.fixedDeltaTime;

                StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
                foreach (var unit in units)
                {
                    if (unit == null) continue;
                    Rigidbody rb = unit.GetComponent<Rigidbody>();
                    if (rb == null || rb.isKinematic) continue;

                    Vector3 toCenter = tornadoCenter - unit.transform.position;
                    toCenter.y = 0f;

                    // Tangential (swirl), inward (pull), upward (lift)
                    Vector3 tangent = Vector3.Cross(Vector3.up, toCenter.normalized);
                    Vector3 force = (tangent * 1.5f + toCenter.normalized * 0.5f + Vector3.up * 0.3f) * disaster.intensity;
                    rb.AddForce(force, ForceMode.Force);
                }

                yield return new WaitForFixedUpdate();
            }
        }

        // ── Flood ─────────────────────────────────────────────────────

        private IEnumerator ApplyFlood(DisasterData disaster)
        {
            float elapsed = 0f;
            Vector3 floodDir = new Vector3(0.7f, 0.2f, 0.3f).normalized;

            while (elapsed < disaster.duration)
            {
                elapsed += Time.fixedDeltaTime;
                ApplyForceToAll(floodDir * disaster.intensity);
                yield return new WaitForFixedUpdate();
            }
        }

        // ── Continuous Damage (Fire / Toxic Rain) ─────────────────────

        private IEnumerator ApplyContinuousDamage(DisasterData disaster)
        {
            float elapsed = 0f;
            float damagePerSecond = disaster.intensity;
            float tickInterval = 0.5f;
            float nextTick = tickInterval;

            while (elapsed < disaster.duration)
            {
                elapsed += Time.deltaTime;
                nextTick -= Time.deltaTime;

                if (nextTick <= 0f)
                {
                    nextTick = tickInterval;
                    float tickDamage = damagePerSecond * tickInterval;
                    ApplyDamageToAll(tickDamage);
                }

                yield return null; // use Update timing for damage ticks
            }
        }

        // ── Heavy Load ────────────────────────────────────────────────

        private IEnumerator ApplyHeavyLoad(DisasterData disaster)
        {
            // Single burst of heavy downward force + damage
            ApplyImpulseToAll(Vector3.down * disaster.intensity * 2f);
            ApplyDamageToAll(disaster.intensity * 0.5f);
            yield return new WaitForSeconds(disaster.duration);
        }

        // ── Generic Burst ─────────────────────────────────────────────

        private IEnumerator ApplyGenericBurst(DisasterData disaster)
        {
            // 3 waves of random impulses
            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.3f; // bias horizontal
                ApplyImpulseToAll(dir * disaster.intensity);
                ApplyDamageToAll(disaster.intensity * 0.3f);
                yield return new WaitForSeconds(disaster.duration / 3f);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private void ApplyImpulseToAll(Vector3 impulse)
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                if (unit == null) continue;
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(impulse, ForceMode.Impulse);
            }
        }

        private void ApplyForceToAll(Vector3 force)
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                if (unit == null) continue;
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(force, ForceMode.Force);
            }
        }

        private void ApplyDamageToAll(float damage)
        {
            StructuralStress[] stresses = FindObjectsByType<StructuralStress>(FindObjectsSortMode.None);
            foreach (var s in stresses)
            {
                if (s != null && !s.IsBroken)
                    s.ApplyExternalDamage(damage);
            }
        }

        private Vector3 GetStructureCentroid()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            if (units.Length == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var u in units)
            {
                if (u != null) { sum += u.transform.position; count++; }
            }
            return count > 0 ? sum / count : Vector3.zero;
        }
    }
}
