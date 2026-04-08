using UnityEngine;

namespace Simulation.Physics
{
    /// <summary>
    /// Physics-based structural stress system.
    /// 
    /// Reads currentForce and currentTorque from a Joint each FixedUpdate,
    /// converts them into a combined stress value, deducts HP proportionally,
    /// recovers HP when stress is relieved, and breaks the structural connection
    /// (destroying the Joint + disabling all Colliders) when HP reaches zero.
    /// 
    /// Attach this component to any GameObject that has:
    ///   - A Rigidbody (isKinematic = false, useGravity = true)
    ///   - A Joint (FixedJoint or ConfigurableJoint) connecting it to the grid/neighbour
    ///   - One or more Colliders
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class StructuralStress : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────
        // Serialized Settings
        // ────────────────────────────────────────────────────────────────

        [Header("HP Settings")]
        [Tooltip("Maximum structural hit points. Pulled from StructureData.baseHP if a StructureUnit is present.")]
        [SerializeField] private float baseHP = 100f;

        [Header("Force → Damage Conversion")]
        [Tooltip("Forces below this magnitude cause zero damage (Newtons).")]
        [SerializeField] private float forceThreshold = 50f;

        [Tooltip("Multiplier: damage = (forceMagnitude - threshold) * forceDamageMultiplier * dt")]
        [SerializeField] private float forceDamageMultiplier = 0.1f;

        [Header("Torque → Damage Conversion")]
        [Tooltip("Torques below this magnitude cause zero damage (N·m).")]
        [SerializeField] private float torqueThreshold = 30f;

        [Tooltip("Multiplier: damage = (torqueMagnitude - threshold) * torqueDamageMultiplier * dt")]
        [SerializeField] private float torqueDamageMultiplier = 0.05f;

        [Header("HP Recovery")]
        [Tooltip("HP recovered per second while total stress is below thresholds.")]
        [SerializeField] private float recoveryRate = 10f;

        [Tooltip("Time (seconds) of continuous low-stress before recovery begins.")]
        [SerializeField] private float recoveryCooldown = 0.5f;

        [Header("Visual Feedback (optional)")]
        [Tooltip("If assigned, material color lerps from normalColor → stressColor as HP decreases.")]
        [SerializeField] private Renderer stressRenderer;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color stressColor = Color.red;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        // ────────────────────────────────────────────────────────────────
        // Runtime State
        // ────────────────────────────────────────────────────────────────

        private float _currentHP;
        private Joint _joint;
        private Rigidbody _rb;
        private Collider[] _colliders;
        private bool _isBroken;

        // Tracks collision forces (e.g. weight of objects above)
        private float _currentCollisionForceSum;

        // Recovery cooldown timer — counts how long stress has been below thresholds
        private float _lowStressTimer;

        // Cached original material color (if stressRenderer is set)
        private Color _cachedOriginalColor;
        private bool _hasStressRenderer;

        // ────────────────────────────────────────────────────────────────
        // Public Read-Only Properties
        // ────────────────────────────────────────────────────────────────

        /// <summary>Current structural HP (0 = broken).</summary>
        public float CurrentHP => _currentHP;

        /// <summary>Maximum HP this part started with.</summary>
        public float BaseHP => baseHP;

        /// <summary>Normalised health ratio [0 … 1].</summary>
        public float HealthRatio => baseHP > 0f ? Mathf.Clamp01(_currentHP / baseHP) : 0f;

        /// <summary>True after the joint has been destroyed and colliders disabled.</summary>
        public bool IsBroken => _isBroken;

        /// <summary>Last computed combined force magnitude on the joint (N).</summary>
        public float LastForceMagnitude { get; private set; }

        /// <summary>Last computed combined torque magnitude on the joint (N·m).</summary>
        public float LastTorqueMagnitude { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // Events
        // ────────────────────────────────────────────────────────────────

        /// <summary>Fired the instant the structure breaks. Passes this component.</summary>
        public event System.Action<StructuralStress> OnBreak;

        /// <summary>Fired every FixedUpdate with (currentHP, healthRatio).</summary>
        public event System.Action<float, float> OnHealthChanged;

        // ────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _joint = GetComponent<Joint>();

            // Base HP will be set by StructureUnit's InitializeStress later
            _currentHP = baseHP;

            // Cache stress renderer
            _hasStressRenderer = stressRenderer != null;
            if (_hasStressRenderer)
            {
                _cachedOriginalColor = stressRenderer.material.color;
            }
        }

        private void Start()
        {
            if (_joint == null)
            {
                Debug.LogWarning($"[StructuralStress] No Joint found on '{name}'. " +
                                 "Attach a FixedJoint or ConfigurableJoint for stress simulation.", this);
            }
        }

        /// <summary>
        /// All physics stress logic runs in FixedUpdate to stay synchronised with
        /// Unity's physics solver (where joint forces are computed).
        /// </summary>
        private void FixedUpdate()
        {
            if (_isBroken) return;
            if (_joint == null)
            {
                _joint = GetComponent<Joint>();
                if (_joint == null)
                {
                    // Joint was destroyed externally or never added — treat as break
                    Break();
                    return;
                }
            }

            // ── 1. Read forces from the joint ─────────────────────────
            float forceMag = 0f;
            float torqueMag = 0f;

            if (_joint != null)
            {
                forceMag = _joint.currentForce.magnitude;
                torqueMag = _joint.currentTorque.magnitude;
            }

            // Include collision forces (e.g. weight of stacked objects)
            forceMag += _currentCollisionForceSum;

            LastForceMagnitude  = forceMag;
            LastTorqueMagnitude = torqueMag;

            // Reset collision force for the next fixed update frame
            _currentCollisionForceSum = 0f;

            // ── 2. Compute per-frame damage ───────────────────────────
            float dt = Time.fixedDeltaTime;

            float forceDamage  = 0f;
            float torqueDamage = 0f;

            if (forceMag > forceThreshold)
            {
                forceDamage = (forceMag - forceThreshold) * forceDamageMultiplier * dt;
            }

            if (torqueMag > torqueThreshold)
            {
                torqueDamage = (torqueMag - torqueThreshold) * torqueDamageMultiplier * dt;
            }

            float totalDamage = forceDamage + torqueDamage;

            // ── 3. Apply damage or recovery ───────────────────────────
            if (totalDamage > 0f)
            {
                _currentHP -= totalDamage;
                _lowStressTimer = 0f; // reset recovery cooldown

                if (showDebugLogs)
                {
                    Debug.Log($"[Stress] {name}  F={forceMag:F1}N  T={torqueMag:F1}N·m  " +
                              $"dmg={totalDamage:F2}  HP={_currentHP:F1}/{baseHP}", this);
                }

                // ── 4. Break check ────────────────────────────────────
                if (_currentHP <= 0f)
                {
                    _currentHP = 0f;
                    Break();
                    return;
                }
            }
            else
            {
                // No damage this tick — count towards recovery
                _lowStressTimer += dt;

                if (_lowStressTimer >= recoveryCooldown && _currentHP < baseHP)
                {
                    _currentHP = Mathf.Min(_currentHP + recoveryRate * dt, baseHP);
                }
            }

            // ── 5. Visual feedback ────────────────────────────────────
            UpdateVisualStress();

            // ── 6. Notify listeners ───────────────────────────────────
            OnHealthChanged?.Invoke(_currentHP, HealthRatio);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (_isBroken) return;
            // Accumulate impulses for this FixedUpdate step.
            // collision.impulse is the total impulse applied per contact pair. Force = impulse / dt.
            _currentCollisionForceSum += (collision.impulse.magnitude / Time.fixedDeltaTime);
        }

        // ────────────────────────────────────────────────────────────────
        // Breaking Logic
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Immediately breaks this structural element:
        ///  1. Destroys the Joint (disconnects from the structure)
        ///  2. Disables every Collider (no more collision — free fall)
        ///  3. Fires the OnBreak event
        ///  4. Optionally plays break VFX / SFX via StructureUnit
        /// </summary>
        private void Break()
        {
            if (_isBroken) return;
            _isBroken = true;
            _currentHP = 0f;

            if (showDebugLogs)
            {
                Debug.Log($"[StructuralStress] *** BREAK *** {name}", this);
            }

            // 1. Destroy the joint
            if (_joint != null)
            {
                Destroy(_joint);
                _joint = null;
            }

            // 2. Disable all colliders so the broken piece cannot interact
            foreach (var col in _colliders)
            {
                if (col != null)
                {
                    col.enabled = false;
                }
            }

            // 3. Ensure the Rigidbody is non-kinematic so it falls freely
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }

            // 4. Play break effects via StructureUnit if available
            var unit = GetComponent<Building.StructureUnit>();
            if (unit != null && unit.CurrentMaterial != null)
            {
                if (unit.CurrentMaterial.breakSound != null)
                {
                    AudioSource.PlayClipAtPoint(unit.CurrentMaterial.breakSound, transform.position);
                }

                if (unit.CurrentMaterial.breakVFX != null)
                {
                    Instantiate(unit.CurrentMaterial.breakVFX, transform.position, Quaternion.identity);
                }
            }

            // 5. Fire event
            OnBreak?.Invoke(this);

            // 6. (Optional) Auto-destroy after a few seconds so debris doesn't pile up
            Destroy(gameObject, 5f);
        }

        // ────────────────────────────────────────────────────────────────
        // Visual Stress Indicator
        // ────────────────────────────────────────────────────────────────

        private void UpdateVisualStress()
        {
            if (!_hasStressRenderer || stressRenderer == null) return;

            // Lerp from normalColor (full HP) → stressColor (0 HP)
            float t = 1f - HealthRatio;
            stressRenderer.material.color = Color.Lerp(normalColor, stressColor, t);
        }

        // ────────────────────────────────────────────────────────────────
        // Public API — External Damage / Reset
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply direct damage from an external source (e.g. explosions, disasters).
        /// </summary>
        public void ApplyExternalDamage(float amount)
        {
            if (_isBroken || amount <= 0f) return;

            _currentHP -= amount;
            _lowStressTimer = 0f;

            if (_currentHP <= 0f)
            {
                _currentHP = 0f;
                Break();
            }
        }

        public void InitializeStress(float maxHP)
        {
            baseHP = maxHP;
            _currentHP = maxHP;
            UpdateVisualStress();
        }

        /// <summary>
        /// Fully reset HP to baseHP (e.g. after a repair action).
        /// Does nothing if already broken.
        /// </summary>
        public void RepairFull()
        {
            if (_isBroken) return;
            _currentHP = baseHP;
            UpdateVisualStress();
        }

#if UNITY_EDITOR
        // ────────────────────────────────────────────────────────────────
        // Editor Gizmos — visualise stress state in Scene view
        // ────────────────────────────────────────────────────────────────

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // Draw a small sphere coloured by health
            Gizmos.color = Color.Lerp(Color.red, Color.green, HealthRatio);
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            // Draw force vector
            if (_joint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(transform.position, _joint.currentForce.normalized * 0.5f);
            }
        }
#endif
    }
}
