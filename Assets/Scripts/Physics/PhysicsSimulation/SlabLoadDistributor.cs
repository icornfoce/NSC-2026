using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Data;

namespace Simulation.Physics
{
    /// <summary>
    /// ระบบกระจายน้ำหนักแบบ top-down (คล้าย Poly Bridge)
    ///
    /// ทำงานทุก FixedUpdate ขณะ simulation รัน:
    ///   1. รวบรวม structure ทุกชิ้นที่ยังไม่พัง
    ///   2. เรียงจาก Y สูง → ต่ำ  (บนลงล่าง)
    ///   3. แต่ละชิ้น: totalLoad = selfWeight + load ที่รับมาจาก AccumulateLoad()
    ///   4. กระจาย totalLoad ลงชิ้นที่รองรับอยู่ใต้ (proximity-weighted)
    ///   5. เรียก EvaluateBreak() ทุกชิ้นหลัง pass เสร็จ
    ///
    /// Attach script นี้ไว้บน SimulationManager GameObject เดียวกัน
    /// </summary>
    public class LoadPropagationSystem : MonoBehaviour
    {
        [Header("Search Settings")]
        [Tooltip("รัศมีค้นหา support ใต้แต่ละชิ้น (เมตร)")]
        [SerializeField] private float supportSearchRadius = 1.5f;

        [Tooltip("margin แนวตั้ง — support ต้องอยู่ต่ำกว่า Y ของชิ้นนี้อย่างน้อยเท่านี้")]
        [SerializeField] private float verticalMargin = 0.1f;

        [Header("Load Scale")]
        [Tooltip("ตัวคูณน้ำหนัก global — ลดลงถ้าโครงสร้างพังเร็วเกินไปในภาพรวม")]
        [SerializeField] private float gravityScale = 9.81f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = false;

        // cache per-frame — เพื่อไม่ต้อง alloc ใหม่ทุกเฟรม
        private readonly List<StructuralStress> _sorted = new List<StructuralStress>();

        // ────────────────────────────────────────────────────────────────
        // Main pass — เรียกจาก SimulationManager.FixedUpdate
        // ────────────────────────────────────────────────────────────────

        public void PropagateLoads()
        {
            float dt = Time.fixedDeltaTime;

            // 1. รวบรวม + Reset
            _sorted.Clear();
            StructuralStress[] all = FindObjectsByType<StructuralStress>(FindObjectsSortMode.None);
            foreach (var s in all)
            {
                if (s == null || s.IsBroken) continue;
                s.ResetFrameLoad();
                _sorted.Add(s);
            }

            // 2. เรียงจาก Y สูง → ต่ำ
            _sorted.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

            // 3–4. Propagate บนลงล่าง
            foreach (var stress in _sorted)
            {
                if (stress.IsBroken) continue;

                var unit = stress.GetComponent<StructureUnit>();
                if (unit == null || unit.Data == null) continue;

                // น้ำหนักตัวเอง
                float selfWeight = unit.Data.baseMass * gravityScale;

                // totalLoad = น้ำหนักตัวเอง + สิ่งที่กดลงมาจากชั้นบน (AccumulateLoad ถูกเรียกแล้ว)
                float totalLoad = selfWeight + stress.CurrentFrameLoad;

                // อัปเดต load ของตัวเองให้ครบก่อน evaluate
                // (ตอนนี้ CurrentFrameLoad ยังเป็นค่าที่รับมาจากบน ยังไม่รวม self)
                // เราต้อง set ใหม่ให้รวม self ด้วย
                stress.ResetFrameLoad();
                stress.AccumulateLoad(totalLoad);

                // หา support ใต้ตัวเอง
                DistributeToSupports(stress, totalLoad);
            }

            // 5. EvaluateBreak ทุกชิ้น
            foreach (var stress in _sorted)
            {
                if (!stress.IsBroken)
                    stress.EvaluateBreak(dt);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // ค้นหา support แล้วกระจาย load ลงไป
        // ────────────────────────────────────────────────────────────────

        private void DistributeToSupports(StructuralStress myStress, float totalLoad)
        {
            int layerMask = 1 << myStress.gameObject.layer;
            Collider[] hits = UnityEngine.Physics.OverlapSphere(
                myStress.transform.position, supportSearchRadius, layerMask);

            float totalFactor = 0f;
            var supports = new Dictionary<StructuralStress, float>(); // stress → weight factor

            foreach (var hit in hits)
            {
                if (hit.transform.root == myStress.transform.root) continue;

                var supportUnit = hit.GetComponentInParent<StructureUnit>();
                if (supportUnit == null) continue;

                // Floor ไม่รับน้ำหนักจาก Floor อื่น (กัน infinite loop ในชั้นเดียวกัน)
                // แต่ยอมให้ Floor รับจาก Structure และ Structure รับจาก Floor
                var myUnit = myStress.GetComponent<StructureUnit>();
                if (myUnit != null
                    && myUnit.Data.buildType == BuildType.Floor
                    && supportUnit.Data.buildType == BuildType.Floor) continue;

                var supportStress = supportUnit.GetComponent<StructuralStress>();
                if (supportStress == null || supportStress.IsBroken) continue;
                if (supports.ContainsKey(supportStress)) continue;

                // support ต้องอยู่ใต้เรา
                float supportY = supportUnit.transform.position.y;
                float myY = myStress.transform.position.y;
                if (supportY > myY - verticalMargin) continue;

                float dist = Vector3.Distance(myStress.transform.position, supportUnit.transform.position);
                float factor = 1f / (dist + 0.001f); // ยิ่งใกล้ยิ่งรับมาก

                supports[supportStress] = factor;
                totalFactor += factor;
            }

            if (totalFactor <= 0f) return;

            foreach (var kvp in supports)
            {
                float share = totalLoad * (kvp.Value / totalFactor);
                kvp.Key.AccumulateLoad(share);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying) return;
            foreach (var s in _sorted)
            {
                if (s == null) continue;
                float t = s.LoadRatio;
                Gizmos.color = Color.Lerp(Color.green, Color.red, t);
                Gizmos.DrawWireSphere(s.transform.position, 0.2f);
            }
        }
#endif
    }
}