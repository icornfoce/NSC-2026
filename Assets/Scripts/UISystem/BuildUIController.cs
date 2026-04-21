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

        [Header("สำหรับเปลี่ยน Material")]
        [Tooltip("ลากไฟล์ MaterialData มาใส่ที่นี่ (ใช้กับ SelectMaterial)")]
        public MaterialData materialToSelect;

        /// <summary>
        /// เริ่มโหมดสร้าง — ต้องกำหนด structureToBuild ก่อน
        /// ใช้ลากใส่ OnClick() ของปุ่ม "สร้าง"
        /// </summary>
        public void StartBuilding()
        {
            if (BuildingSystem.Instance == null || structureToBuild == null) return;
            BuildingSystem.Instance.SelectStructure(structureToBuild);
        }

        /// <summary>
        /// เริ่มโหมดสร้าง — รับข้อมูลผ่าน Parameter (ใช้ในระบบ Inventory/Slot ได้)
        /// </summary>
        public void StartBuildingWithData(StructureData data)
        {
            if (BuildingSystem.Instance == null || data == null) return;
            BuildingSystem.Instance.SelectStructure(data);
        }

        /// <summary>
        /// เลือก Material ที่จะใช้สร้าง (ใช้ลากใส่ OnClick ของปุ่มเลือกวัสดุ)
        /// </summary>
        public void SelectMaterial()
        {
            if (BuildingSystem.Instance == null || materialToSelect == null) return;
            BuildingSystem.Instance.SelectMaterial(materialToSelect);
        }

        /// <summary>
        /// เลือก Material ที่จะใช้สร้าง — รับข้อมูลผ่าน Parameter
        /// </summary>
        public void SelectMaterialWithData(MaterialData data)
        {
            if (BuildingSystem.Instance == null || data == null) return;
            BuildingSystem.Instance.SelectMaterial(data);
        }

        /// <summary>
        /// เริ่มโหมดเลื่อนของ — คลิกที่ของในฉากเพื่อหยิบขึ้นมาย้าย (Toggle)
        /// </summary>
        public void StartMoving()
        {
            if (BuildingSystem.Instance == null) return;

            // Toggle: ถ้าอยู่ในโหมดนี้อยู่แล้ว ให้ยกเลิกกลับสู่ Idle
            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Moving)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterMoveMode();
        }

        /// <summary>
        /// เริ่มโหมดลบ/ขาย — คลิกที่ของในฉากเพื่อลบและได้เงินคืน (Toggle)
        /// </summary>
        public void StartDeleting()
        {
            if (BuildingSystem.Instance == null) return;

            // Toggle: ถ้าอยู่ในโหมดนี้อยู่แล้ว ให้ยกเลิกกลับสู่ Idle
            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Deleting)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterDeleteMode();
        }

        /// <summary>
        /// เริ่มโหมดระบายสี/เปลี่ยนวัสดุ — คลิกที่ของในฉากเพื่อเปลี่ยนวัสดุ (Toggle)
        /// </summary>
        public void StartPainting()
        {
            if (BuildingSystem.Instance == null) return;

            // Toggle
            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Painting)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterPaintMode();
        }

        /// <summary>
        /// ยกเลิกโหมดปัจจุบัน กลับสู่ Idle
        /// </summary>
        public void Cancel()
        {
            if (BuildingSystem.Instance == null) return;
            BuildingSystem.Instance.ExitMode();
        }

        // --------------------------------------------------------------------------------
        // Simulation Controls
        // --------------------------------------------------------------------------------

        /// <summary>
        /// เริ่มการจำลองฟิสิกส์ (Play)
        /// </summary>
        public void StartSimulation()
        {
            if (Simulation.Physics.SimulationManager.Instance != null)
                Simulation.Physics.SimulationManager.Instance.StartSimulation();
        }

        /// <summary>
        /// หยุดการจำลองฟิสิกส์ (Stop)
        /// </summary>
        public void StopSimulation()
        {
            if (Simulation.Physics.SimulationManager.Instance != null)
                Simulation.Physics.SimulationManager.Instance.StopSimulation();
        }

        /// <summary>
        /// เปิด/ปิด การแสดงผล Grid
        /// </summary>
        public void ToggleGrid(bool show)
        {
            if (Simulation.Physics.SimulationManager.Instance != null)
                Simulation.Physics.SimulationManager.Instance.SetGridVisibility(show);
        }

        /// <summary>
        /// เปิด/ปิด การแสดงผลสีเลือด (HP Stress) บนชิ้นส่วนโครงสร้าง
        /// </summary>
        public void ToggleStressVisuals(bool show)
        {
            Simulation.Physics.StructuralStress.SetVisualStatus(show);
        }

        // --- Convenience Wrappers for UI Buttons ---
        public void OpenGrid() => ToggleGrid(true);
        public void CloseGrid() => ToggleGrid(false);
        public void OpenStressVisuals() => ToggleStressVisuals(true);
        public void CloseStressVisuals() => ToggleStressVisuals(false);
    }
}
