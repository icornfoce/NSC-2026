using UnityEngine;
using Simulation.Data;
using Simulation.Building;
using Simulation.Mission;

namespace Simulation.UI
{
    /// <summary>
    /// UI Controller สำหรับระบบ Building
    /// ลากสคริปต์นี้ใส่ปุ่ม แล้วเลือกฟังก์ชันที่ต้องการใน OnClick()
    ///
    /// ฟังก์ชันที่ใช้ได้:
    ///   - StartBuilding()         → เริ่มโหมดสร้าง (ต้องใส่ StructureData ในช่อง structureToBuild)
    ///   - StartBuildingWithData() → เริ่มโหมดสร้างพร้อมส่ง Data โดยตรง
    ///   - StartMoving()           → เริ่มโหมดเลื่อนของ (Toggle)
    ///   - StartDeleting()         → เริ่มโหมดลบ/ขาย (Toggle)
    ///   - Cancel()                → ยกเลิกโหมดปัจจุบัน กลับ Idle
    ///   - SelectMaterial()        → เปลี่ยน Material
    ///
    /// หมายเหตุ: ทุกโหมดสร้างจะถูกบล็อกอัตโนมัติเมื่อ MissionSystem อยู่ในโหมดจำลอง
    /// </summary>
    public class BuildUIController : MonoBehaviour
    {
        [Header("สำหรับโหมดสร้างเท่านั้น")]
        [Tooltip("ลากไฟล์ StructureData มาใส่ที่นี่ (ใช้เฉพาะตอนกด StartBuilding)")]
        public StructureData structureToBuild;

        [Header("Material Settings")]
        [Tooltip("นำไฟล์ Wood Data มาใส่ที่นี่เพื่อเป็นค่าเริ่มต้น")]
        public MaterialData defaultWoodMaterial;

        private void Start()
        {
            // เซ็ต Wood เป็นค่าเริ่มต้นตอนเริ่มเกม
            if (defaultWoodMaterial != null && BuildingSystem.Instance != null)
            {
                BuildingSystem.Instance.SelectMaterial(defaultWoodMaterial);
            }
        }

        // ── Guard ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns true only when the player is in the Building phase (or no MissionSystem exists).
        /// Prevents interacting with structures during or after simulation.
        /// </summary>
        private bool IsBuildingAllowed()
        {
            if (MissionSystem.Instance == null) return true;
            return MissionSystem.Instance.Phase == MissionSystem.MissionPhase.Building;
        }
         
        // ── Actions ────────────────────────────────────────────────────

        /// <summary>เปลี่ยน Material ปัจจุบัน (ใช้ลากใส่ปุ่ม OnClick)</summary>
        public void SelectMaterial(MaterialData materialData)
        {
            if (!IsBuildingAllowed()) return;
            if (BuildingSystem.Instance != null && materialData != null)
                BuildingSystem.Instance.SelectMaterial(materialData);
        }

        /// <summary>เริ่มโหมดสร้าง — ใช้ structureToBuild ที่กำหนดใน Inspector</summary>
        public void StartBuilding()
        {
            if (!IsBuildingAllowed()) return;
            if (BuildingSystem.Instance == null || structureToBuild == null) return;
            BuildingSystem.Instance.SelectStructure(structureToBuild);
        }

        /// <summary>เริ่มโหมดสร้าง — รับ Data ผ่าน Parameter (ใช้กับ Inventory/Slot)</summary>
        public void StartBuildingWithData(StructureData data)
        {
            if (!IsBuildingAllowed()) return;
            if (BuildingSystem.Instance == null || data == null) return;
            BuildingSystem.Instance.SelectStructure(data);
        }

        /// <summary>เริ่มโหมดเลื่อนของ (Toggle)</summary>
        public void StartMoving()
        {
            if (!IsBuildingAllowed()) return;
            if (BuildingSystem.Instance == null) return;

            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Moving)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterMoveMode();
        }

        /// <summary>เริ่มโหมดลบ/ขาย (Toggle)</summary>
        public void StartDeleting()
        {
            if (!IsBuildingAllowed()) return;
            if (BuildingSystem.Instance == null) return;

            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Deleting)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterDeleteMode();
        }

        /// <summary>ยกเลิกโหมดปัจจุบัน กลับสู่ Idle</summary>
        public void Cancel()
        {
            if (BuildingSystem.Instance == null) return;
            BuildingSystem.Instance.ExitMode();
        }
    }
}
