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
        [Header("UI Audio")]
        [Tooltip("เสียงที่จะเล่นเมื่อกดปุ่ม (สามารถเอา AudioSource มาแปะใส่ปุ่มเองได้ แต่ใส่ตรงนี้สะดวกกว่า)")]
        [SerializeField] private AudioClip buttonClickSound;

        [Header("สำหรับโหมดสร้างเท่านั้น")]
        [Tooltip("ลากไฟล์ StructureData มาใส่ที่นี่ (ใช้เฉพาะตอนกด StartBuilding)")]
        public StructureData structureToBuild;

        [Header("สำหรับเปลี่ยน Material")]
        [Tooltip("ลากไฟล์ MaterialData มาใส่ที่นี่ (ใช้กับ SelectMaterial)")]
        public MaterialData materialToSelect;

        private void PlayClickSound()
        {
            if (buttonClickSound != null && UnityEngine.Camera.main != null)
            {
                AudioSource.PlayClipAtPoint(buttonClickSound, UnityEngine.Camera.main.transform.position);
            }
        }

        /// <summary>
        /// เริ่มโหมดสร้าง — ต้องกำหนด structureToBuild ก่อน
        /// ใช้ลากใส่ OnClick() ของปุ่ม "สร้าง"
        /// </summary>
        public void StartBuilding()
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null || structureToBuild == null) return;
            BuildingSystem.Instance.SelectStructure(structureToBuild);
        }

        /// <summary>
        /// เริ่มโหมดสร้าง — รับข้อมูลผ่าน Parameter (ใช้ในระบบ Inventory/Slot ได้)
        /// </summary>
        public void StartBuildingWithData(StructureData data)
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null || data == null) return;
            BuildingSystem.Instance.SelectStructure(data);
        }

        /// <summary>
        /// เลือก Material ที่จะใช้สร้าง (ใช้ลากใส่ OnClick ของปุ่มเลือกวัสดุ)
        /// เปลี่ยน Material ทันทีและจำค่าไว้ข้ามโหมด
        /// </summary>
        public void SelectMaterial()
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null || materialToSelect == null) return;
            BuildingSystem.Instance.SelectMaterial(materialToSelect);
        }

        /// <summary>
        /// เลือก Material ที่จะใช้สร้าง — รับข้อมูลผ่าน Parameter
        /// </summary>
        public void SelectMaterialWithData(MaterialData data)
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null || data == null) return;
            BuildingSystem.Instance.SelectMaterial(data);
        }

        /// <summary>
        /// ล้าง Material ที่เลือกไว้ กลับไปใช้ค่าเริ่มต้นของ StructureData
        /// </summary>
        public void ClearMaterial()
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null) return;
            BuildingSystem.Instance.ClearMaterial();
        }

        /// <summary>
        /// เริ่มโหมดเลื่อนของ — คลิกที่ของในฉากเพื่อหยิบขึ้นมาย้าย (Toggle)
        /// </summary>
        public void StartMoving()
        {
            PlayClickSound();
            if (BuildingSystem.Instance == null) return;

            // Toggle: ถ้าอยู่ในโหมดนี้อยู่แล้ว ให้ยกเลิกกลับสู่ Idle
            if (BuildingSystem.Instance.CurrentMode == BuildingSystem.BuildMode.Moving)
                BuildingSystem.Instance.ExitMode();
            else
                BuildingSystem.Instance.EnterMoveMode();
        }

        /// <summary>
        /// เริ่มโหมดลบ/ขาย — คลิกที่ของในฉากเพื่อลบและได้เงินคืน (Toggle)
        /// กดค้าง 2 วิ = ลบทั้งหมด (Delete All)
        /// 
        /// วิธีใช้ใน UI:
        ///   - ใส่ EventTrigger บนปุ่ม Delete
        ///   - PointerDown  → OnDeleteButtonDown()
        ///   - PointerUp    → OnDeleteButtonUp()
        /// </summary>

        [Header("Delete Hold Settings")]
        [Tooltip("ระยะเวลากดค้างเพื่อลบทั้งหมด (วินาที)")]
        [SerializeField] private float deleteHoldDuration = 2f;

        private bool _isHoldingDeleteButton = false;
        private float _deleteHoldTimer = 0f;
        private bool _deleteAllTriggered = false;

        private void Update()
        {
            // นับเวลากดค้างปุ่ม Delete
            if (_isHoldingDeleteButton && !_deleteAllTriggered)
            {
                _deleteHoldTimer += Time.deltaTime;

                if (_deleteHoldTimer >= deleteHoldDuration)
                {
                    _deleteAllTriggered = true;
                    PlayClickSound();

                    if (BuildingSystem.Instance != null)
                    {
                        BuildingSystem.Instance.DeleteAllStructures();
                    }

                    Debug.Log("<color=orange>🗑 Hold complete — Deleted ALL!</color>");
                }
            }
        }

        /// <summary>
        /// เรียกจาก EventTrigger → PointerDown บนปุ่ม Delete
        /// </summary>
        public void OnDeleteButtonDown()
        {
            _isHoldingDeleteButton = true;
            _deleteHoldTimer = 0f;
            _deleteAllTriggered = false;
        }

        /// <summary>
        /// เรียกจาก EventTrigger → PointerUp บนปุ่ม Delete
        /// </summary>
        public void OnDeleteButtonUp()
        {
            _isHoldingDeleteButton = false;

            // ถ้ากดสั้นๆ (ไม่ถึง 2 วิ) → Toggle โหมด Delete ตามปกติ
            if (!_deleteAllTriggered)
            {
                StartDeleting();
            }

            _deleteHoldTimer = 0f;
            _deleteAllTriggered = false;
        }

        /// <summary>
        /// Toggle โหมดลบ/ขาย (ใช้เมื่อกดสั้นๆ)
        /// </summary>
        public void StartDeleting()
        {
            PlayClickSound();
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
            PlayClickSound();
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
            PlayClickSound();
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
                
            ResetTimeSpeed();
        }

        /// <summary>
        /// หยุดการจำลองฟิสิกส์ (Stop)
        /// </summary>
        public void StopSimulation()
        {
            if (Simulation.Physics.SimulationManager.Instance != null)
                Simulation.Physics.SimulationManager.Instance.StopSimulation();
                
            ResetTimeSpeed();
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

        // --- Undo / Redo Wrappers ---
        public void UndoAction()
        {
            PlayClickSound();
            if (BuildingSystem.Instance != null) BuildingSystem.Instance.Undo();
        }

        public void RedoAction()
        {
            PlayClickSound();
            if (BuildingSystem.Instance != null) BuildingSystem.Instance.Redo();
        }

        // --------------------------------------------------------------------------------
        // Time Controls
        // --------------------------------------------------------------------------------

        private float[] _timeScaleSteps = { 0.5f, 0.75f, 1.0f, 1.5f, 2.0f };
        private int _currentTimeStepIndex = 2; // เริ่มต้นที่ 1.0x (Index 2)

        /// <summary>
        /// รีเซ็ตความเร็วกลับเป็น 1.0x
        /// </summary>
        public void ResetTimeSpeed()
        {
            _currentTimeStepIndex = 2;
            ApplyCurrentTimeScale();
        }

        /// <summary>
        /// เพิ่มความเร็วเวลา (เลื่อนขึ้นทีละสเต็ป)
        /// </summary>
        public void IncreaseTimeSpeed()
        {
            if (_currentTimeStepIndex < _timeScaleSteps.Length - 1)
            {
                _currentTimeStepIndex++;
                ApplyCurrentTimeScale();
            }
        }

        /// <summary>
        /// ลดความเร็วเวลา (เลื่อนลงทีละสเต็ป)
        /// </summary>
        public void DecreaseTimeSpeed()
        {
            if (_currentTimeStepIndex > 0)
            {
                _currentTimeStepIndex--;
                ApplyCurrentTimeScale();
            }
        }

        private void ApplyCurrentTimeScale()
        {
            float newScale = _timeScaleSteps[_currentTimeStepIndex];
            Time.timeScale = newScale;
            Debug.Log($"<color=cyan>⏱ Time Scale set to {newScale}x</color>");
        }
    }
}
