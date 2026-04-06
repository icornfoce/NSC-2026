using UnityEngine;
using Simulation.Data;
using Simulation.Building;

namespace Simulation.UI
{
    /// <summary>
    /// UI Controller สำหรับระบบ Building
    /// ลากสคริปต์นี้ใส่ปุ่ม แล้วเลือกฟังก์ชันที่ต้องการใน OnClick()
    /// 
    /// ฟังก์ชันที่ใช้ได้:
    ///   - StartBuilding()  → เริ่มโหมดสร้าง (ต้องใส่ StructureData ในช่อง structureToBuild)
    ///   - StartMoving()    → เริ่มโหมดเลื่อนของ (คลิกของในฉากเพื่อหยิบ)
    ///   - StartDeleting()  → เริ่มโหมดลบ/ขาย (คลิกของในฉากเพื่อลบ)
    ///   - Cancel()         → ยกเลิกโหมดปัจจุบัน กลับ Idle
    /// </summary>
    public class BuildUIController : MonoBehaviour
    {
        [Header("สำหรับโหมดสร้างเท่านั้น")]
        [Tooltip("ลากไฟล์ StructureData มาใส่ที่นี่ (ใช้เฉพาะตอนกด StartBuilding)")]
        public StructureData structureToBuild;

        /// <summary>
        /// เริ่มโหมดสร้าง — ต้องกำหนด structureToBuild ก่อน
        /// ใช้ลากใส่ OnClick() ของปุ่ม "สร้าง"
        /// </summary>
        public void StartBuilding()
        {
            if (BuildingSystem.Instance == null)
            {
                Debug.LogError("BuildUIController: ไม่พบ BuildingSystem ในฉาก!");
                return;
            }

            if (structureToBuild == null)
            {
                Debug.LogWarning("BuildUIController: ยังไม่ได้กำหนด StructureData ให้กับปุ่มนี้!");
                return;
            }

            BuildingSystem.Instance.SelectStructure(structureToBuild);
        }

        /// <summary>
        /// เริ่มโหมดเลื่อนของ — คลิกที่ของในฉากเพื่อหยิบขึ้นมาย้าย
        /// ใช้ลากใส่ OnClick() ของปุ่ม "เลื่อน/ย้าย"
        /// </summary>
        public void StartMoving()
        {
            if (BuildingSystem.Instance == null)
            {
                Debug.LogError("BuildUIController: ไม่พบ BuildingSystem ในฉาก!");
                return;
            }

            BuildingSystem.Instance.EnterMoveMode();
        }

        /// <summary>
        /// เริ่มโหมดลบ/ขาย — คลิกที่ของในฉากเพื่อลบและได้เงินคืน
        /// ใช้ลากใส่ OnClick() ของปุ่ม "ลบ/ขาย"
        /// </summary>
        public void StartDeleting()
        {
            if (BuildingSystem.Instance == null)
            {
                Debug.LogError("BuildUIController: ไม่พบ BuildingSystem ในฉาก!");
                return;
            }

            BuildingSystem.Instance.EnterDeleteMode();
        }

        /// <summary>
        /// ยกเลิกโหมดปัจจุบัน กลับสู่ Idle
        /// ใช้ลากใส่ OnClick() ของปุ่ม "ยกเลิก"
        /// </summary>
        public void Cancel()
        {
            if (BuildingSystem.Instance == null)
            {
                Debug.LogError("BuildUIController: ไม่พบ BuildingSystem ในฉาก!");
                return;
            }

            BuildingSystem.Instance.ExitMode();
        }
    }
}
