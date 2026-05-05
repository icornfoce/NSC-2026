using UnityEngine;
using System.Collections.Generic;

namespace Simulation.Camera
{
    /// <summary>
    /// กล้องมุมคงที่ (Fixed Angle) — ล็อคมุมมองไว้กลาง map
    /// หมุนด้วย WASD, Zoom ด้วย Scroll Wheel
    /// 
    /// Occlusion Transparency:
    ///   ตรวจ Structure ที่บังเส้น Camera→Pivot แล้วทำโปร่งใสชั่วคราว
    ///   ใช้ SphereCastAll เพื่อจับของที่บังได้กว้างกว่า Raycast ปกติ
    ///   รองรับทั้ง Standard Shader และ URP Lit Shader
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Camera Rotation")]
        [SerializeField] private float mouseRotateSensitivity = 2f;
        [SerializeField] private float keyboardRotateSpeed = 60f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Zoom (Scroll Wheel)")]
        [SerializeField] private float zoomSensitivity = 2f;
        [SerializeField] private float minDistance = 5f;
        [SerializeField] private float maxDistance = 100f;
        [SerializeField] private float zoomSmoothing = 10f;

        [Header("Camera Angle (Initial)")]
        [Tooltip("มุมก้ม — 90 = มองตรงลงจากบน, 45 = เฉียง")]
        [SerializeField] private float pitch = 60f;
        [Tooltip("มุมหมุนรอบแกน Y")]
        [SerializeField] private float yaw = 45f;

        [Header("Initial")]
        [SerializeField] private Vector3 pivotPoint = Vector3.zero;
        [SerializeField] private float initialDistance = 25f;

        [Header("Camera Shake")]
        [SerializeField] private float shakeDecayRate = 5f;
        [SerializeField] private float maxShakeIntensity = 2f;
        private float _currentShakeIntensity = 0f;

        [Header("Occlusion Transparency")]
        [Tooltip("Layer ที่จะตรวจหาของบัง (ตั้งให้ตรงกับ Structure Layer)")]
        [SerializeField] private LayerMask occlusionLayer;
        [Tooltip("ค่า alpha ของของที่บัง (0 = หายไปหมด, 1 = ไม่โปร่งใส)")]
        [SerializeField] [Range(0f, 1f)] private float occludedAlpha = 0.05f;
        [Tooltip("รัศมีของ SphereCast สำหรับตรวจจับของบัง (ยิ่งกว้าง จับได้มาก)")]
        [SerializeField] private float occlusionRadius = 1.5f;
        [Tooltip("ความเร็ว fade เข้า/ออก")]
        [SerializeField] private float fadeSmoothSpeed = 10f;

        private float _currentDistance;
        private float _targetDistance;

        // ── Occlusion tracking ──
        private class OccludedEntry
        {
            public Renderer rend;
            public Material[] originalMaterials;   // sharedMaterials ก่อนเปลี่ยน
            public Material[] fadedMaterials;       // material instance โปร่งใส
            public float currentAlpha;
            public bool  isOccluding;               // true = ยังบังอยู่
        }

        private readonly Dictionary<Renderer, OccludedEntry> _occluded = new();
        private readonly HashSet<Collider> _occludedColliders = new();

        /// <summary>Collider ที่กำลังถูกทำโปร่งใสเพราะบังกล้อง — BuildingSystem อ่านเพื่อข้าม</summary>
        public HashSet<Collider> OccludedColliders => _occludedColliders;

        // ─────────────────────────────────────────────

        private void Start()
        {
            _currentDistance = initialDistance;
            _targetDistance  = initialDistance;
            UpdateCameraPosition();

            if (occlusionLayer.value == 0)
            {
                Debug.LogWarning("[CameraController] Occlusion Layer ยังไม่ได้ตั้ง! " +
                    "ให้ตั้งเป็น Structure Layer ใน Inspector เพื่อให้ระบบมองทะลุทำงาน", this);
            }
        }

        private void LateUpdate()
        {
            HandleRotation();
            HandleZoom();
            UpdateCameraPosition();
            HandleOcclusion();
        }

        // ─────────────────────────────────────────────
        // Input
        // ─────────────────────────────────────────────

        private void HandleRotation()
        {
            // Right Click Drag to rotate
            if (Input.GetMouseButton(1))
            {
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");

                yaw += mouseX * mouseRotateSensitivity * 100f * Time.deltaTime;
                pitch -= mouseY * mouseRotateSensitivity * 100f * Time.deltaTime;
            }

            // Still support WASD but as an alternative or just remove it? 
            // The user said "Change from wasd to click right", so I will make WASD secondary or remove.
            // Let's keep WASD as an option but prioritize mouse.
            float h = Input.GetAxis("Horizontal"); 
            float v = Input.GetAxis("Vertical");

            yaw += h * keyboardRotateSpeed * Time.deltaTime;
            pitch -= v * keyboardRotateSpeed * Time.deltaTime;

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            _targetDistance -= scroll * zoomSensitivity * _currentDistance * 0.3f;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }

        private void UpdateCameraPosition()
        {
            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, zoomSmoothing * Time.deltaTime);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);

            Vector3 shakeOffset = Vector3.zero;
            if (_currentShakeIntensity > 0.01f)
            {
                shakeOffset = Random.insideUnitSphere * _currentShakeIntensity;
                _currentShakeIntensity = Mathf.Lerp(_currentShakeIntensity, 0f, shakeDecayRate * Time.deltaTime);
            }

            transform.position = pivotPoint + offset + shakeOffset;
            transform.LookAt(pivotPoint); // Look at the stable pivot point while shaking
        }

        // ─────────────────────────────────────────────
        // Occlusion Transparency
        // ─────────────────────────────────────────────

        private void HandleOcclusion()
        {
            if (occlusionLayer.value == 0) return;

            // ── 1. SphereCastAll จาก Camera → Pivot ─────────────────────
            // ใช้ SphereCast แทน Raycast เพื่อจับของบังที่ไม่อยู่ตรงกลางจอได้ดีขึ้น
            Vector3 camPos = transform.position;
            Vector3 dir    = pivotPoint - camPos;
            float   dist   = dir.magnitude;

            if (dist < 0.5f) return;

            // ยิงแค่ 70% ของระยะ → ของที่อยู่ตรง/ใกล้ pivot ไม่ถือว่า "บัง"
            float castDist = dist * 0.7f;

            RaycastHit[] hits = UnityEngine.Physics.SphereCastAll(
                camPos, occlusionRadius, dir.normalized, castDist, occlusionLayer);

            // ── 2. รวม Renderer ที่บังอยู่ตอนนี้ ────────────────────────
            HashSet<Renderer> hitRenderers = new();
            _occludedColliders.Clear();

            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger) continue;

                // เก็บ collider สำหรับ BuildingSystem ใช้ข้าม
                foreach (var col in hit.collider.GetComponentsInChildren<Collider>())
                    _occludedColliders.Add(col);

                // หา Renderer ทั้งหมดบน root GameObject
                Transform root = hit.transform.root;
                foreach (var rend in root.GetComponentsInChildren<Renderer>())
                {
                    hitRenderers.Add(rend);

                    if (!_occluded.ContainsKey(rend))
                    {
                        // เจอใหม่ → สร้าง entry + clone material เป็น transparent
                        var entry = new OccludedEntry
                        {
                            rend              = rend,
                            originalMaterials = rend.sharedMaterials, // เก็บ reference เดิม
                            currentAlpha      = 1f,
                            isOccluding       = true
                        };

                        // Clone materials เพื่อทำ transparent (ไม่ยุ่งกับของเดิม)
                        Material[] cloned = new Material[entry.originalMaterials.Length];
                        for (int i = 0; i < cloned.Length; i++)
                        {
                            cloned[i] = new Material(entry.originalMaterials[i]);
                            SetMaterialTransparent(cloned[i]);
                        }
                        entry.fadedMaterials = cloned;
                        rend.materials = cloned; // ใส่ material โปร่งใสแทน

                        _occluded[rend] = entry;
                    }

                    _occluded[rend].isOccluding = true;
                }
            }

            // ── 3. Renderer ที่ไม่บังแล้ว → mark ให้ fade กลับ ─────────
            foreach (var kvp in _occluded)
            {
                if (!hitRenderers.Contains(kvp.Key))
                    kvp.Value.isOccluding = false;
            }

            // ── 4. Lerp alpha ทุก entry + cleanup ที่ restore เสร็จ ───
            List<Renderer> toRemove = new();
            float dt = Time.deltaTime * fadeSmoothSpeed;

            foreach (var kvp in _occluded)
            {
                OccludedEntry e = kvp.Value;
                if (e.rend == null) { toRemove.Add(kvp.Key); continue; }

                float target = e.isOccluding ? occludedAlpha : 1f;
                e.currentAlpha = Mathf.Lerp(e.currentAlpha, target, dt);

                // อัพเดท alpha ของทุก material
                foreach (var mat in e.fadedMaterials)
                {
                    if (mat == null) continue;
                    SetMaterialAlpha(mat, e.currentAlpha);
                }

                // Restore เสร็จแล้ว? → คืน material เดิม + ลบ entry
                if (!e.isOccluding && Mathf.Abs(e.currentAlpha - 1f) < 0.02f)
                {
                    e.rend.sharedMaterials = e.originalMaterials;

                    // ทำลาย material clone ที่สร้างขึ้น
                    foreach (var mat in e.fadedMaterials)
                        if (mat != null) Destroy(mat);

                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var r in toRemove) _occluded.Remove(r);
        }

        // ─────────────────────────────────────────────
        // Material Helpers — รองรับทั้ง Standard Shader + URP Lit
        // ─────────────────────────────────────────────

        private static void SetMaterialTransparent(Material mat)
        {
            if (mat == null) return;

            // ── URP Lit Shader ──
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // 0=Opaque, 1=Transparent
                mat.SetFloat("_Blend", 0f);   // 0=Alpha
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                return;
            }

            // ── Standard Shader ──
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 3); // Transparent

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        private static void SetMaterialAlpha(Material mat, float alpha)
        {
            if (mat == null) return;

            // URP ใช้ _BaseColor, Standard ใช้ _Color
            if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = alpha;
                mat.SetColor("_BaseColor", c);
            }
            
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.color;
                c.a = alpha;
                mat.color = c;
            }
        }

        // ─────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────

        /// <summary>
        /// Current floor index being viewed (read-only from outside).
        /// </summary>
        public int CurrentViewFloor { get; private set; } = 1;

        public void FocusOn(Vector3 point)
        {
            pivotPoint = point;
        }

        /// <summary>
        /// Lock the camera to view a specific floor level.
        /// Called by BuildingSystem when Q/E is pressed.
        /// </summary>
        /// <param name="floorIndex">Floor number (0 = ground, 1 = first floor, etc.)</param>
        /// <param name="floorWorldY">World Y position of that floor.</param>
        public void SetFloorView(int floorIndex, float floorWorldY)
        {
            CurrentViewFloor = floorIndex;
            // Move the pivot point's Y to the floor level, keep X/Z the same
            pivotPoint = new Vector3(pivotPoint.x, floorWorldY, pivotPoint.z);
        }

        /// <summary>
        /// Triggers a screen shake effect with the given intensity.
        /// </summary>
        public void TriggerShake(float intensity)
        {
            float cappedIntensity = Mathf.Min(intensity, maxShakeIntensity);
            if (cappedIntensity > _currentShakeIntensity)
            {
                _currentShakeIntensity = cappedIntensity;
            }
        }

        private void OnDestroy()
        {
            // คืน material เดิมทั้งหมดเมื่อ script ถูกทำลาย
            foreach (var kvp in _occluded)
            {
                if (kvp.Value.rend != null)
                    kvp.Value.rend.sharedMaterials = kvp.Value.originalMaterials;

                foreach (var mat in kvp.Value.fadedMaterials)
                    if (mat != null) Destroy(mat);
            }
            _occluded.Clear();
        }
    }
}
