using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Data;

namespace Simulation.Physics
{
    /// <summary>
    /// Shared utility สำหรับค้นหา "ฐานราก" หรือ "ส่วนรองรับ" ใต้อาคาร
    /// ใช้ร่วมกันทั้ง BuildingSystem (ตอนเช็คการวางโครงสร้างตึก) 
    /// และ LoadPropagationSystem (ตอนกระจายน้ำหนักตึกลงสู่ด้านล่าง)
    /// เพื่อให้มั่นใจว่าโครงสร้างที่สร้างขึ้นมาสามารถถ่ายเทน้ำหนักลงสู่ฐานได้จริง
    /// </summary>
    public static class SupportQuery
    {
        /// <summary>
        /// ค้นหา StructuralStress ที่รองรับอยู่ใต้ตำแหน่งที่กำหนด
        /// พร้อมคืน weight factor (ยิ่งใกล้ยิ่งมาก) สำหรับ load distribution
        /// </summary>
        /// <param name="origin">ตำแหน่ง world ของวัตถุที่ต้องการหา support</param>
        /// <param name="searchRadius">รัศมีค้นหา (เมตร)</param>
        /// <param name="verticalMargin">support ต้องอยู่ต่ำกว่า Y อย่างน้อยเท่านี้</param>
        /// <param name="layerMask">layer ที่จะค้นหา</param>
        /// <param name="selfRoot">root transform ของตัวเอง เพื่อข้ามไม่นับ</param>
        /// <param name="selfBuildType">BuildType ของตัวเอง — ใช้กรอง Floor-Floor loop</param>
        /// <returns>Dictionary ของ StructuralStress → weight factor</returns>
        public static Dictionary<StructuralStress, float> FindSupports(
            Vector3 origin,
            float searchRadius,
            float verticalMargin,
            int layerMask,
            Transform selfRoot,
            BuildType selfBuildType)
        {
            var supports = new Dictionary<StructuralStress, float>();
            Collider[] hits = UnityEngine.Physics.OverlapSphere(origin, searchRadius, layerMask);

            foreach (var hit in hits)
            {
                if (hit.transform.root == selfRoot) continue;

                var supportUnit = hit.GetComponentInParent<StructureUnit>();
                if (supportUnit == null) continue;

                // Floor ไม่รับน้ำหนักจาก Floor อื่น (กัน infinite loop ในชั้นเดียวกัน)
                if (selfBuildType == BuildType.Floor
                    && supportUnit.Data != null
                    && supportUnit.Data.buildType == BuildType.Floor)
                    continue;

                var supportStress = supportUnit.GetComponent<StructuralStress>();
                if (supportStress == null || supportStress.IsBroken) continue;
                if (supports.ContainsKey(supportStress)) continue;

                // support ต้องอยู่ใต้เรา
                float supportY = supportUnit.transform.position.y;
                if (supportY > origin.y - verticalMargin) continue;

                float dist = Vector3.Distance(origin, supportUnit.transform.position);
                float factor = 1f / (dist + 0.001f); // ยิ่งใกล้ยิ่งรับมาก

                supports[supportStress] = factor;
            }

            return supports;
        }

        /// <summary>
        /// เวอร์ชันเรียบง่ายสำหรับ BuildingSystem — แค่เช็คว่า "มีอะไรรองรับไหม"
        /// ไม่สนใจ StructuralStress เพราะตอนสร้างยังไม่ได้จำลอง
        /// </summary>
        public static bool HasAnySupportBelow(
            Vector3 origin,
            Vector3 halfExtents,
            int layerMask,
            Transform selfRoot)
        {
            // ใช้ OverlapBox เหมือนเดิมเพื่อ backward compat กับ BuildingSystem
            Collider[] supports = UnityEngine.Physics.OverlapBox(origin, halfExtents, Quaternion.identity, layerMask);

            foreach (var sup in supports)
            {
                if (selfRoot != null && sup.transform.IsChildOf(selfRoot)) continue;

                StructureUnit hitUnit = sup.GetComponentInParent<StructureUnit>();
                if (hitUnit != null) return true;

                // เช็คพื้นดิน — ถ้าอยู่ใน groundLayer ก็ถือว่ามี support
                // Note: caller ต้อง include groundLayer ใน layerMask
                if (hitUnit == null) return true; // ground collider
            }

            return false;
        }
    }
}
