using UnityEngine;
using Simulation.Building;

namespace Simulation.Physics
{
    /// <summary>
    /// Manager สำหรับคุมเวลากดเริ่ม/หยุด การจำลองฟิสิกส์ (เหมือนปุ่ม Play ใน Poly Bridge)
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private GameObject gridObject;

        [Header("State")]
        [SerializeField] private bool isSimulating = false;

        public bool IsSimulating => isSimulating;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // แช่แข็งฟิสิกส์เริ่มต้น (ไม่ให้ชิ้นส่วนที่อยู่ในฉากแต่แรกร่วงลงมา)
            FreezeAllStructures();

            // พยายามหา Grid อัตโนมัติถ้าไม่ได้ใส่มา
            if (gridObject == null)
            {
                gridObject = GameObject.Find("Grid");
                if (gridObject == null) gridObject = GameObject.Find("GridObject");
                // ค้นหาจาก Tag ก็ได้ถ้าต้องการ
            }
        }

        /// <summary>
        /// ใช้ฟังก์ชันนี้ใส่ในปุ่ม OnClick() เพื่อเริ่มการจำลอง
        /// </summary>
        public void StartSimulation()
        {
            if (isSimulating) return;
            isSimulating = true;

            // 1. ยกเลิกโหมดสร้างของทั้งหมดก่อน
            if (BuildingSystem.Instance != null)
            {
                BuildingSystem.Instance.ExitMode();
            }

            // 1.1 ซ่อน Grid เมื่อเริ่มเล่น
            if (gridObject != null)
            {
                gridObject.SetActive(false);
            }

            // 2. ค้นหาชิ้นส่วนที่ถูกสร้างทั้งหมดในฉาก
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                // ตรวจสอบว่าชิ้นส่วนนั้นไม่ได้พังไปแล้ว
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // ปลดล็อค Kinematic เพื่อให้แรงโน้มถ่วงและฟิสิกส์เริ่มทำงาน
                    rb.isKinematic = false;
                    rb.WakeUp();
                }
            }

            Debug.Log("<color=green>▶ Start Simulation</color> - Physics started!");
        }

        /// <summary>
        /// ใช้ฟังก์ชันนี้ใส่ในปุ่ม OnClick() เพื่อสลับเริ่ม/หยุด
        /// </summary>
        public void ToggleSimulation()
        {
            if (isSimulating) StopSimulation();
            else StartSimulation();
        }

        public void StopSimulation()
        {
            if (!isSimulating) return;
            isSimulating = false;

            FreezeAllStructures();

            // แสดง Grid กลับมาเมื่อหยุด
            SetGridVisibility(true);

            Debug.Log("<color=red>■ Stop Simulation</color> - Physics frozen.");
        }

        /// <summary>
        /// สั่งเปิด/ปิด Grid ได้จากภายนอก (เช่น จาก UI)
        /// </summary>
        public void SetGridVisibility(bool visible)
        {
            if (gridObject != null)
            {
                gridObject.SetActive(visible);
            }
        }

        private void FreezeAllStructures()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // แช่แข็งโครงสร้าง
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
