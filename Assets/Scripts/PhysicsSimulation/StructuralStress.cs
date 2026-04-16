using UnityEngine;
using DG.Tweening;

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
        [SerializeField] private Gradient stressGradient = new Gradient()
        {
            colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.yellow, 0.33f),
                new GradientColorKey(new Color(1f, 0.5f, 0f), 0.66f),
                new GradientColorKey(Color.red, 1f)
            },
            alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        };

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        [Header("Collapse Shockwave")]
        [Tooltip("Radius of the shockwave blast when a broken structure impacts something.")]
        [SerializeField] private float blastRadius = 3f;

        [Tooltip("Multiplier for the impact force applied to nearby structures.")]
        [SerializeField] private float blastForceMultiplier = 0.5f;

        [Tooltip("Multiplier for direct HP damage applied to nearby structures.")]
        [SerializeField] private float blastDamageMultiplier = 0.2f;

        [Header("Cascade Disintegration")]
        [Tooltip("If true, impacting the ground while broken will force nearby joints to break instantly.")]
        [SerializeField] private bool enableCascadeBreak = true;

        [Tooltip("Radius to search for joints to force-break on impact.")]
        [SerializeField] private float cascadeRadius = 2.0f;

        [Tooltip("Force threshold to trigger the cascade break (relative to forceThreshold).")]
        [SerializeField] private float cascadeTriggerMultiplier = 1.5f;

        // ────────────────────────────────────────────────────────────────
        // Runtime State
        // ────────────────────────────────────────────────────────────────

        private float _currentHP;
        private Joint _joint;
        private Rigidbody _rb;
        private Collider[] _colliders;
        private bool _isBroken;
        private bool _hasBlasted; // Ensures shockwave only fires once per collapse

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

        /// <summary>
        /// Total collision force accumulated against this structure this FixedUpdate frame.
        /// Read by SlabLoadDistributor to include resting-object weight in load calculations.
        /// </summary>
        public float LastCollisionForce { get; private set; }

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

            // พยายามดึง Renderer ในตัวเอง หรือตัวลูกมาใช้อัตโนมัติ หากยังไม่ได้ระบุ
            if (stressRenderer == null)
            {
                stressRenderer = GetComponentInChildren<Renderer>();
            }

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

            // Include collision forces (e.g. weight of stacked objects)
            forceMag += _currentCollisionForceSum;

            LastForceMagnitude  = forceMag;
            LastTorqueMagnitude = torqueMag;
            LastCollisionForce  = _currentCollisionForceSum;

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

        private void OnCollisionEnter(Collision collision)
        {
            // ── Normal impact (ยังไม่พัง) — กล้องสั่น ──
            if (!_isBroken)
            {
                float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
                
                if (impactForce > forceThreshold * 2f) 
                {
                    if (UnityEngine.Camera.main != null)
                    {
                        float shakeStrength = Mathf.Clamp(impactForce * 0.001f, 0.1f, 0.6f);
                        var camCtrl = UnityEngine.Camera.main.GetComponent<Simulation.Camera.CameraController>();
                        if (camCtrl != null)
                        {
                            camCtrl.TriggerShake(shakeStrength, 0.2f);
                        }
                    }
                }
                return;
            }

            // ── Collapse Shockwave (ของพังแล้วตกกระแทก) ──
            // ยิง blast ครั้งเดียว กระจายแรงไปทุกทิศ แล้วหายไป
            if (_isBroken && !_hasBlasted)
            {
                float impactForce = collision.impulse.magnitude / Time.fixedDeltaTime;
                if (impactForce < forceThreshold) return; // แรงน้อยเกินไม่ต้อง blast

                _hasBlasted = true; // ป้องกัน blast ซ้ำจากการเด้ง

                Vector3 blastOrigin = collision.contacts[0].point;
                float blastPower = impactForce * blastForceMultiplier;

                // กล้องสั่นรุนแรง
                if (UnityEngine.Camera.main != null)
                {
                    float shakeStr = Mathf.Clamp(blastPower * 0.0005f, 0.2f, 1.0f);
                    var camCtrl = UnityEngine.Camera.main.GetComponent<Simulation.Camera.CameraController>();
                    if (camCtrl != null)
                        camCtrl.TriggerShake(shakeStr, 0.4f);
                }

                // หาทุก structure ใน blast radius
                int structureLayerMask = 1 << gameObject.layer;
                Collider[] hits = UnityEngine.Physics.OverlapSphere(blastOrigin, blastRadius, structureLayerMask);

                foreach (var hit in hits)
                {
                    if (hit.transform.root == transform.root) continue; // ข้ามตัวเอง

                    Rigidbody hitRb = hit.GetComponentInParent<Rigidbody>();
                    StructuralStress hitStress = hit.GetComponentInParent<StructuralStress>();

                    float dist = Vector3.Distance(blastOrigin, hit.transform.position);
                    float falloff = 1f - Mathf.Clamp01(dist / blastRadius); // 1.0 ที่ศูนย์กลาง → 0.0 ที่ขอบ

                    // 1. Physics impulse — ผลักออกจากจุดระเบิด + ยกขึ้นเล็กน้อย
                    if (hitRb != null && !hitRb.isKinematic)
                    {
                        Vector3 dir = (hit.transform.position - blastOrigin).normalized;
                        dir.y = Mathf.Max(dir.y, 0.3f); // bias ยกขึ้น
                        hitRb.AddForce(dir * blastPower * falloff, ForceMode.Impulse);
                    }

                    // 2. Direct HP damage — แรงยิ่งใกล้ยิ่งเจ็บ
                    if (hitStress != null && !hitStress.IsBroken)
                    {
                        hitStress.ApplyExternalDamage(blastPower * falloff * blastDamageMultiplier);
                    }
                }

                if (showDebugLogs)
                {
                    Debug.Log($"[Shockwave] <color=orange>💥 BLAST</color> from '{name}' " +
                              $"at {blastOrigin} power={blastPower:F0}N radius={blastRadius}m", this);
                }

                // ── Cascade Disintegration (โซ่พังทะลวงสวน) ──
                if (enableCascadeBreak && impactForce > forceThreshold * cascadeTriggerMultiplier)
                {
                    // ค้นหารอบๆ เพื่อสั่งให้หลุด (ไม่จำกัดเฉพาะ Layer ตัวเอง เพราะอาจโดนชิ้นส่วนอื่น)
                    Collider[] cascadeHits = UnityEngine.Physics.OverlapSphere(blastOrigin, cascadeRadius, structureLayerMask);
                    int brokeCount = 0;
                    foreach (var h in cascadeHits)
                    {
                        if (h.transform.root == transform.root) continue;
                        var s = h.GetComponentInParent<StructuralStress>();
                        if (s != null && !s.IsBroken)
                        {
                            s.ForceBreak();
                            brokeCount++;
                        }
                    }
                    if (brokeCount > 0 && showDebugLogs)
                        Debug.Log($"[Cascade] Impact caused {brokeCount} nearby structures to disintegrate!");
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Breaking Logic
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Manually force this structure to break its joints and fall.
        /// Used by the Cascade Disintegration system when a nearby part hits the ground.
        /// </summary>
        public void ForceBreak()
        {
            Break();
        }

        /// <summary>
        /// Immediately breaks this structural element:
        ///  1. Destroys the Joint (disconnects from the structure)
        ///  2. We keep colliders enabled so it can fall and hit things
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

            // 2. We keep colliders enabled so the broken piece can fall
            // and impact other objects/ground to trigger the Collapse Shockwave.
            // (Previously we disabled them here)
            /*
            foreach (var col in _colliders)
            {
                if (col != null) col.enabled = false;
            }
            */

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

            // NOTE: We do NOT Destroy(gameObject) here.
            // Keeping the broken piece in-scene allows SimulationManager.ResetSimulation()
            // to restore it to its pre-simulation state. The Rigidbody free-falls
            // naturally since isKinematic=false + useGravity=true was set above.
            // If you want debris to disappear, call Destroy() from the result screen
            // after the player confirms they do NOT want to reset.
        }

        // ────────────────────────────────────────────────────────────────
        // Visual Stress Indicator
        // ────────────────────────────────────────────────────────────────

        private void UpdateVisualStress()
        {
            if (!_hasStressRenderer || stressRenderer == null) return;

            float t = 1f - HealthRatio;
            
            // เพื่อไม่ให้ไปทับสี Material ตั้งต้น (เช่น ไม้/เหล็ก)
            // ถ้า t=0 จะใช้สีออริจินัล 100% และค่อยๆ เรืองสีตาม Gradient เมื่อมีแรงกด
            Color stressC = stressGradient.Evaluate(t);
            stressRenderer.material.color = Color.Lerp(_cachedOriginalColor, stressC, t);
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

        /// <summary>
        /// Add a distributed load (Newtons) into this frame's collision force accumulator.
        /// Called by SlabLoadDistributor to pass floor/slab weight down to supporting walls and pillars.
        /// The load is processed in the next FixedUpdate alongside physical contact forces.
        /// </summary>
        public void AddDistributedLoad(float newtons)
        {
            if (_isBroken || newtons <= 0f) return;
            _currentCollisionForceSum += newtons;
        }

        public void InitializeStress(float maxHP)
        {
            baseHP = maxHP;
            _currentHP = maxHP;
            _isBroken = false;
            _hasBlasted = false;
            
            // Re-fetch Rigidbody in case it was destroyed or needs re-init
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = true; // Freeze for building phase
            }

            // Re-enable all colliders (essential after a reset/replay)
            if (_colliders != null)
            {
                foreach (var col in _colliders)
                {
                    if (col != null) col.enabled = true;
                }
            }

            // อัปเดตสีตั้งต้นใหม่ (เผื่อว่าเพิ่งโดนเปลี่ยน Material เป็นอันอื่นมา)
            if (_hasStressRenderer && stressRenderer != null)
            {
                _cachedOriginalColor = stressRenderer.material.color;
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
