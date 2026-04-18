using UnityEngine;

namespace Simulation.Physics
{
    /// <summary>
    /// ระบบคำนวณความเครียดของโครงสร้างตึก (Structural Stress System)
    ///
    /// รับค่าน้ำหนัก (Load) จาก LoadPropagationSystem ที่คำนวณแรงกดทับของอาคาร
    /// และตรวจสอบว่าส่วนประกอบของตึกยังรับน้ำหนักได้หรือไม่
    ///
    ///   - AccumulateLoad()   → รับน้ำหนักที่กดทับลงมาจากชั้นบน
    ///   - EvaluateBreak()    → ตรวจสอบสภาพโครงสร้างหลังคำนวณแรงเสร็จ
    ///
    /// ระบบนี้ช่วยจำลองเหตุการณ์ตึกถล่มหรือพื้นทรุดเมื่อรับน้ำหนักเกินกำหนด
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class StructuralStress : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────
        // Serialized Settings
        // ────────────────────────────────────────────────────────────────

        [Header("HP & Load Limit")]
        [Tooltip("HP สูงสุด — set อัตโนมัติจาก StructureData.baseHP")]
        [SerializeField] private float baseHP = 100f;

        [Tooltip("แรงสูงสุด (N) ที่รับได้ก่อนพัง — set อัตโนมัติจาก StructureData.maxStress")]
        [SerializeField] private float maxLoad = 500f;

        [Tooltip("HP ที่หายต่อวินาทีเมื่อรับ load เกิน maxLoad\n" +
                 "damage/s = (overload / maxLoad) * damageRate")]
        [SerializeField] private float damageRate = 50f;

        [Header("HP Recovery")]
        [Tooltip("HP ฟื้นต่อวินาทีเมื่อ load ต่ำกว่า maxLoad")]
        [SerializeField] private float recoveryRate = 10f;

        [Tooltip("วินาทีที่ต้องไม่โดน overload ก่อน recovery เริ่ม")]
        [SerializeField] private float recoveryCooldown = 0.5f;

        [Header("Visual Feedback")]
        [Tooltip("Renderer ที่จะเปลี่ยนสีตาม stress — ถ้าไม่ set จะหาเองจาก children")]
        [SerializeField] private Renderer stressRenderer;
        [SerializeField]
        private Gradient stressGradient = new Gradient()
        {
            colorKeys = new GradientColorKey[]
            {
                new GradientColorKey(Color.white,              0f),
                new GradientColorKey(Color.yellow,             0.33f),
                new GradientColorKey(new Color(1f, 0.5f, 0f), 0.66f),
                new GradientColorKey(Color.red,                1f)
            },
            alphaKeys = new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        };

        [Header("Collapse Shockwave")]
        [SerializeField] private float blastRadius = 3f;
        [SerializeField] private float blastForceMultiplier = 0.5f;
        [SerializeField] private float blastDamageMultiplier = 0.2f;

        [Header("Cascade Disintegration")]
        [SerializeField] private bool enableCascadeBreak = true;
        [SerializeField] private float cascadeRadius = 2.0f;
        [SerializeField] private float cascadeTriggerMultiplier = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        // ────────────────────────────────────────────────────────────────
        // Runtime State
        // ────────────────────────────────────────────────────────────────

        private float _currentHP;
        private Rigidbody _rb;
        private Collider[] _colliders;
        private bool _isBroken;
        private bool _hasBlasted;

        // Load accumulator — reset ทุก pass โดย LoadPropagationSystem
        private float _frameLoad;

        private float _lowStressTimer;
        private Color _cachedOriginalColor;
        private bool _hasStressRenderer;

        // ────────────────────────────────────────────────────────────────
        // Public Properties
        // ────────────────────────────────────────────────────────────────

        public float CurrentHP => _currentHP;
        public float BaseHP => baseHP;
        public float MaxLoad => maxLoad;
        public float HealthRatio => baseHP > 0f ? Mathf.Clamp01(_currentHP / baseHP) : 0f;
        public float LoadRatio => maxLoad > 0f ? Mathf.Clamp01(_frameLoad / maxLoad) : 0f;
        public bool IsBroken => _isBroken;
        public float CurrentFrameLoad => _frameLoad;

        public event System.Action<StructuralStress> OnBreak;
        public event System.Action<float, float> OnHealthChanged;

        // ────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _currentHP = baseHP;

            if (stressRenderer == null)
                stressRenderer = GetComponentInChildren<Renderer>();

            _hasStressRenderer = stressRenderer != null;
            if (_hasStressRenderer)
                _cachedOriginalColor = stressRenderer.material.color;
        }

        // ────────────────────────────────────────────────────────────────
        // Load Propagation API
        // ────────────────────────────────────────────────────────────────

        /// <summary>Reset load accumulator ก่อนเริ่ม pass ใหม่ทุก frame</summary>
        public void ResetFrameLoad()
        {
            _frameLoad = 0f;
        }

        /// <summary>บวก load (N) เข้า accumulator — เรียกได้หลายครั้งต่อเฟรม</summary>
        public void AccumulateLoad(float newtons)
        {
            if (_isBroken || newtons <= 0f) return;
            _frameLoad += newtons;
        }

        /// <summary>
        /// ประเมิน damage/recovery หลังจาก propagation pass เสร็จ
        /// เรียกโดย LoadPropagationSystem ตอนท้าย pass
        /// </summary>
        public void EvaluateBreak(float dt)
        {
            if (_isBroken) return;

            float overload = _frameLoad - maxLoad;

            if (overload > 0f)
            {
                float damage = (overload / maxLoad) * damageRate * dt;
                _currentHP -= damage;
                _lowStressTimer = 0f;

                if (showDebugLogs)
                    Debug.Log($"[LoadStress] {name}  load={_frameLoad:F0}N  max={maxLoad:F0}N  " +
                              $"overload={overload:F0}  dmg={damage:F2}  HP={_currentHP:F1}/{baseHP}", this);

                if (_currentHP <= 0f)
                {
                    _currentHP = 0f;
                    Break();
                    return;
                }
            }
            else
            {
                _lowStressTimer += dt;
                if (_lowStressTimer >= recoveryCooldown && _currentHP < baseHP)
                    _currentHP = Mathf.Min(_currentHP + recoveryRate * dt, baseHP);
            }

            UpdateVisualStress();
            OnHealthChanged?.Invoke(_currentHP, HealthRatio);
        }

        // ────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────

        public void ApplyExternalDamage(float amount)
        {
            if (_isBroken || amount <= 0f) return;
            _currentHP -= amount;
            _lowStressTimer = 0f;
            if (_currentHP <= 0f) { _currentHP = 0f; Break(); }
        }

        /// <summary>Legacy compat กับ SlabLoadDistributor เดิม</summary>
        public void AddDistributedLoad(float newtons) => AccumulateLoad(newtons);

        public void ForceBreak() => Break();

        public void InitializeStress(float hp)
            => InitializeStress(hp, -1f, 100f);

        public void InitializeStress(float hp, float newMaxLoad, float forceTransferPercent)
        {
            baseHP = hp;
            _currentHP = hp;
            _isBroken = false;
            _hasBlasted = false;
            _frameLoad = 0f;
            _lowStressTimer = 0f;

            if (newMaxLoad > 0f) maxLoad = newMaxLoad;

            _rb = GetComponent<Rigidbody>();
            if (_rb != null) _rb.isKinematic = true;

            if (_colliders != null)
                foreach (var col in _colliders)
                    if (col != null) col.enabled = true;

            if (_hasStressRenderer && stressRenderer != null)
                _cachedOriginalColor = stressRenderer.material.color;

            UpdateVisualStress();
        }

        public void RepairFull()
        {
            if (_isBroken) return;
            _currentHP = baseHP;
            UpdateVisualStress();
        }

        // ────────────────────────────────────────────────────────────────
        // Collision — เฉพาะ shockwave หลังพัง
        // (OnCollisionStay ถูกลบออกแล้ว ไม่จำเป็นอีกต่อไป)
        // ────────────────────────────────────────────────────────────────

        private void OnCollisionEnter(Collision collision)
        {
            // [Fix 4] ของทับกันไม่ทำดาเมจกันเอง — ถ้าชนกับโครงสร้างด้วยกันที่ยังไม่พัง ให้ข้ามไปเลย
            StructuralStress otherStress = collision.gameObject.GetComponentInParent<StructuralStress>();
            if (otherStress != null && !otherStress.IsBroken && !this._isBroken) 
                return;

            float impact = collision.impulse.magnitude / Time.fixedDeltaTime;

            // [Fix 1] ของตกถึงพื้นหรือกระแทกแรงๆ จอสั่น (ลดเกณฑ์เพื่อให้สั่นง่ายขึ้นเมื่อของตก)
            if (impact > 200f)
            {
                TriggerCameraShake(Mathf.Clamp(impact * 0.0005f, 0.05f, 0.5f), 0.15f);
            }

            if (!_isBroken)
            {
                // ถ้าแรงกระแทกเยอะกว่า MaxLoad มากๆ ถึงจะพังแบบร่วงหล่น
                return;
            }

            if (!_hasBlasted)
            {
                if (impact < maxLoad) return;
                _hasBlasted = true;
                FireShockwave(collision.contacts[0].point, impact);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Internal Helpers
        // ────────────────────────────────────────────────────────────────

        private void Break()
        {
            if (_isBroken) return;
            _isBroken = true;
            _currentHP = 0f;

            if (showDebugLogs)
                Debug.Log($"[LoadStress] *** BREAK *** {name}  finalLoad={_frameLoad:F0}N / maxLoad={maxLoad:F0}N", this);

            foreach (var j in GetComponents<Joint>())
                Destroy(j);

            if (_rb != null) { _rb.isKinematic = false; _rb.useGravity = true; }

            var unit = GetComponent<Building.StructureUnit>();
            if (unit?.CurrentMaterial != null)
            {
                if (unit.CurrentMaterial.breakSound != null)
                    AudioSource.PlayClipAtPoint(unit.CurrentMaterial.breakSound, transform.position);
                if (unit.CurrentMaterial.breakVFX != null)
                    Instantiate(unit.CurrentMaterial.breakVFX, transform.position, Quaternion.identity);
            }

            UpdateVisualStress();
            OnBreak?.Invoke(this);
        }

        private void FireShockwave(Vector3 origin, float impactForce)
        {
            TriggerCameraShake(Mathf.Clamp(impactForce * blastForceMultiplier * 0.0005f, 0.2f, 1f), 0.4f);

            float blastPower = impactForce * blastForceMultiplier;
            int mask = 1 << gameObject.layer;

            foreach (var hit in UnityEngine.Physics.OverlapSphere(origin, blastRadius, mask))
            {
                if (hit.transform.root == transform.root) continue;

                float dist = Vector3.Distance(origin, hit.transform.position);
                float falloff = 1f - Mathf.Clamp01(dist / blastRadius);

                var hitRb = hit.GetComponentInParent<Rigidbody>();
                var hitStress = hit.GetComponentInParent<StructuralStress>();

                if (hitRb != null && !hitRb.isKinematic)
                {
                    Vector3 dir = (hit.transform.position - origin).normalized;
                    dir.y = Mathf.Max(dir.y, 0.3f);
                    hitRb.AddForce(dir * blastPower * falloff, ForceMode.Impulse);
                }

                hitStress?.ApplyExternalDamage(blastPower * falloff * blastDamageMultiplier);
            }

            if (enableCascadeBreak && impactForce > maxLoad * cascadeTriggerMultiplier)
            {
                int broke = 0;
                foreach (var h in UnityEngine.Physics.OverlapSphere(origin, cascadeRadius, mask))
                {
                    if (h.transform.root == transform.root) continue;
                    var s = h.GetComponentInParent<StructuralStress>();
                    if (s != null && !s.IsBroken) { s.ForceBreak(); broke++; }
                }
                if (broke > 0 && showDebugLogs)
                    Debug.Log($"[Cascade] {name} caused {broke} structures to disintegrate!");
            }
        }

        private void UpdateVisualStress()
        {
            if (!_hasStressRenderer || stressRenderer == null) return;
            float t = 1f - HealthRatio;
            stressRenderer.material.color = Color.Lerp(_cachedOriginalColor, stressGradient.Evaluate(t), t);
        }

        private void TriggerCameraShake(float strength, float duration)
        {
            if (UnityEngine.Camera.main == null) return;
            UnityEngine.Camera.main
                .GetComponent<Simulation.Camera.CameraController>()
                ?.TriggerShake(strength, duration);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = Color.Lerp(Color.red, Color.green, HealthRatio);
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
#endif
    }
}