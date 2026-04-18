using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Data;

namespace Simulation.Physics
{
    /// <summary>
    /// ระบบกระจายน้ำหนักตึกแบบบนลงล่าง (Architectural Load Distribution)
    ///
    /// ทำงานขณะจำลองการรับแรงของตึก (Simulation Run):
    ///   1. รวบรวมชิ้นส่วนโครงสร้างตึกทั้งหมด
    ///   2. เรียงจากชั้นบน (Y สูง) ลงไปฐานราก (Y ต่ำ)
    ///   3. คำนวณน้ำหนักตัวชิ้นส่วน + น้ำหนักที่กดทับมาจากชั้นบน
    ///   4. กระจายน้ำหนักลงสู่ชิ้นส่วนรองรับ (เสา/ผนัง/พื้น) ด้านล่าง
    ///   5. ตรวจสอบสภาพการแตกร้าวหรือการพังถล่มของตึก
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

            // 1. รวบรวม + Reset — ใช้ StructureRegistry แทน FindObjectsByType
            _sorted.Clear();
            var allUnits = StructureRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i] == null) continue;
                var s = allUnits[i].GetComponent<StructuralStress>();
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
            // ใช้ SupportQuery ที่แชร์กับ BuildingSystem — เกณฑ์เดียวกัน
            var myUnit = myStress.GetComponent<StructureUnit>();
            BuildType myType = (myUnit != null && myUnit.Data != null)
                ? myUnit.Data.buildType
                : BuildType.Structure;

            int layerMask = 1 << myStress.gameObject.layer;

            var supports = SupportQuery.FindSupports(
                myStress.transform.position,
                supportSearchRadius,
                verticalMargin,
                layerMask,
                myStress.transform.root,
                myType);

            if (supports.Count == 0) return;

            float totalFactor = 0f;
            foreach (var kvp in supports)
                totalFactor += kvp.Value;

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