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
        [SerializeField] private float forceThreshold = 100f;

        [Tooltip("Multiplier: damage = (forceMagnitude - threshold) * forceDamageMultiplier * dt")]
        [SerializeField] private float forceDamageMultiplier = 0.1f;

        [Header("Torque → Damage Conversion")]
        [Tooltip("Torques below this magnitude cause zero damage (N·m).")]
        [SerializeField] private float torqueThreshold = 100f;

        [Tooltip("Multiplier: damage = (torqueMagnitude - threshold) * torqueDamageMultiplier * dt")]
        [SerializeField] private float torqueDamageMultiplier = 0.05f;

        [Header("HP Recovery")]
        [Tooltip("HP recovered per second while total stress is below thresholds.")]
        [SerializeField] private float recoveryRate = 10f;

        [Tooltip("Time (seconds) of continuous low-stress before recovery begins.")]
        [SerializeField] private float recoveryCooldown = 0.5f;

        [Header("Physical Limits")]
        [SerializeField] private float maxCompression = 1000f;
        [SerializeField] private float maxTension = 1000f;
        [Tooltip("How many times its own weight a structure can support before stress damage begins. " +
                 "E.g. 10 means a floor can hold 10× its own mass worth of structures above it.")]
        [SerializeField] private float supportedLoadMultiplier = 10f;

        [Header("Visual Feedback")]
        [Tooltip("If assigned, material color lerps based on structural health.")]
        [SerializeField] private Renderer stressRenderer;
        
        [SerializeField] [Range(0f, 1f)] private float stressVisualIntensity = 1.0f;

        [Header("Global Settings")]
        public static bool ShowHPVisualsGlobal = true;

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

            // Auto-find renderer if not set manually
            if (stressRenderer == null)
            {
                stressRenderer = GetComponentInChildren<Renderer>();
            }

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
            if (_joint == null) _joint = GetComponent<Joint>();
            
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

            // ── Subtract expected structural load ────────────────────
            // Joint.currentForce includes the gravitational reaction force of
            // this piece AND everything it supports above (walls on floor, etc).
            // We subtract (own mass × gravity × multiplier) so that normal
            // building loads (up to N× own weight) cause zero damage.
            // Only forces that EXCEED this expected load cause stress.
            float restingWeight = _rb != null ? _rb.mass * UnityEngine.Physics.gravity.magnitude : 0f;
            float expectedLoad = restingWeight * supportedLoadMultiplier;
            forceMag = Mathf.Max(0f, forceMag - expectedLoad);

            LastForceMagnitude  = forceMag;
            LastTorqueMagnitude = torqueMag;

            // ── 2. Compute per-frame damage ───────────────────────────
            float dt = Time.fixedDeltaTime;

            float forceDamage  = 0f;
            float torqueDamage = 0f;

            // Use the lower of compression/tension as a base threshold for damage if needed,
            // or directly calculate damage based on reaching the limits.
            float currentLimit = maxTension; 
            // Simple heuristic for compression vs tension: 
            // If we have a joint, we could check axial force, but for now we use the magnitude
            // and compare against tension (common for most bridge materials).
            
            if (forceMag > forceThreshold)
            {
                forceDamage = (forceMag - forceThreshold) * forceDamageMultiplier * dt;
                
                // If force exceeds physical limits, take significant damage
                if (forceMag > currentLimit)
                {
                    forceDamage += (forceMag / currentLimit) * 10f * dt;
                }
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

        private void OnCollisionEnter(Collision collision)
        {
            // If the impact is strong enough, trigger a small camera shake
            if (collision.impulse.magnitude > 50f)
            {
                Building.BuildingSystem.Instance?.TriggerCameraShake(Mathf.Clamp(collision.impulse.magnitude * 0.005f, 0.2f, 1.5f));
            }
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

            // Trigger a strong camera shake when a structure breaks
            Building.BuildingSystem.Instance?.TriggerCameraShake(2.0f);

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
            if (unit != null)
            {
                if (unit.CurrentMaterial != null)
                {
                    if (unit.CurrentMaterial.breakSound != null)
                        AudioSource.PlayClipAtPoint(unit.CurrentMaterial.breakSound, transform.position);

                    if (unit.CurrentMaterial.breakVFX != null)
                        Instantiate(unit.CurrentMaterial.breakVFX, transform.position, Quaternion.identity);
                }

                if (unit.Data != null && unit.Data.breakVFX != null)
                {
                    Instantiate(unit.Data.breakVFX, transform.position, Quaternion.identity);
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

        public void RefreshVisual()
        {
            UpdateVisualStress();
        }

        private void UpdateVisualStress()
        {
            if (!_hasStressRenderer || stressRenderer == null) return;

            // ถ้าชิ้นส่วนนี้กำลังถูก Highlight (เลือกอยู่) ไม่ต้องอัปเดตสีทับ
            var unit = GetComponent<Simulation.Building.StructureUnit>();
            if (unit != null && unit.IsHighlighted) return;

            // ถ้าเปิดการแสดงผลสี ให้กลับไปใช้สีดั้งเดิม
            if (!ShowHPVisualsGlobal)
            {
                stressRenderer.material.color = _cachedOriginalColor;
                return;
            }

            // HealthRatio: 1.0 = เต็ม, 0.0 = พัง
            float health = HealthRatio;

            // 100% (1.0) -> ใช้สีวัสดุดั้งเดิม (ใส)
            // 50%  (0.5) -> สีส้ม
            // 0%   (0.0) -> สีแดง
            Color targetColor;

            if (health >= 0.5f)
            {
                // ช่วง 100% -> 50% (ไล่จากสีเดิม ไปหา ส้ม)
                float t = (1.0f - health) / 0.5f; 
                targetColor = Color.Lerp(_cachedOriginalColor, new Color(1f, 0.5f, 0f), t);
            }
            else
            {
                // ช่วง 50% -> 0% (ไล่จากส้ม ไปหา แดง)
                float t = (0.5f - health) / 0.5f; 
                targetColor = Color.Lerp(new Color(1f, 0.5f, 0f), Color.red, t);
            }

            // แสดงผลที่ Renderer ของชิ้นส่วนนั้นทันที
            stressRenderer.material.color = Color.Lerp(_cachedOriginalColor, targetColor, stressVisualIntensity);
        }

        /// <summary>
        /// คำสั่งสำหรับเปิด/ปิด การแสดงผลสี HP ทั้งหมด (ใช้ควบคุมจาก UI)
        /// </summary>
        public static void SetVisualStatus(bool isActive)
        {
            ShowHPVisualsGlobal = isActive;
            
            // อัปเดตสีของทุกชิ้นทันทีเพื่อให้เห็นผล
            StructuralStress[] allStress = FindObjectsByType<StructuralStress>(FindObjectsSortMode.None);
            foreach (var s in allStress) s.UpdateVisualStress();
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

        public void InitializeStress(float maxHP, float compression = 1000f, float tension = 1000f)
        {
            baseHP = maxHP;
            _currentHP = maxHP;
            maxCompression = compression;
            maxTension = tension;

            // บันทึกสีดั้งเดิมใหม่ทุกครั้งที่มีการ Initialize (เผื่อโดนเปลี่ยน Material ในโหมด Painting)
            if (stressRenderer == null) stressRenderer = GetComponentInChildren<Renderer>();
            if (stressRenderer != null)
            {
                _cachedOriginalColor = stressRenderer.material.color;
                _hasStressRenderer = true;
            }

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
