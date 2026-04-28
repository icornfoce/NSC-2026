using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    /// <summary>
    /// Manager สำหรับคุมเวลากดเริ่ม/หยุด การจำลองฟิสิกส์ (เหมือนปุ่ม Play ใน Poly Bridge)
    /// เมื่อกด Stop จะย้อนกลับไปสถานะก่อน Start ทั้งหมด (Snapshot/Rewind)
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private GameObject gridObject;

        [Header("State")]
        [SerializeField] private bool isSimulating = false;

        public bool IsSimulating => isSimulating;

        // ────────────────────────────────────────────────────────────────
        // Snapshot System
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// เก็บสถานะก่อน Simulate ของแต่ละชิ้นส่วน
        /// </summary>
        private struct StructureSnapshot
        {
            public StructureUnit unit;
            public Vector3 position;
            public Quaternion rotation;
            public Rigidbody connectedBody; // Joint เชื่อมกับ Rigidbody ตัวไหน (null = world/ground)
            public bool wasActive;
        }

        private List<StructureSnapshot> _snapshots = new List<StructureSnapshot>();

        // ────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ────────────────────────────────────────────────────────────────

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

        // ────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────

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

            // 2. บันทึก Snapshot ก่อนเริ่ม Simulate
            SaveSnapshots();

            // 3. ปลดล็อค Kinematic เพื่อให้ฟิสิกส์ทำงาน
            foreach (var snap in _snapshots)
            {
                if (snap.unit == null) continue;
                Rigidbody rb = snap.unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
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

            // ย้อนกลับไปสถานะก่อน Start (Rewind)
            RestoreSnapshots();

            // แสดง Grid กลับมาเมื่อหยุด
            SetGridVisibility(true);

            Debug.Log("<color=red>■ Stop Simulation</color> - Rewound to pre-simulation state.");
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

        // ────────────────────────────────────────────────────────────────
        // Snapshot Save / Restore
        // ────────────────────────────────────────────────────────────────

        private void SaveSnapshots()
        {
            _snapshots.Clear();

            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                var snap = new StructureSnapshot
                {
                    unit = unit,
                    position = unit.transform.position,
                    rotation = unit.transform.rotation,
                    wasActive = unit.gameObject.activeSelf,
                    connectedBody = null
                };

                // บันทึก Joint connection (เชื่อมกับใคร)
                var joint = unit.GetComponent<Joint>();
                if (joint != null)
                {
                    snap.connectedBody = joint.connectedBody; // null = connected to world
                }

                _snapshots.Add(snap);
            }
        }

        private void RestoreSnapshots()
        {
            foreach (var snap in _snapshots)
            {
                if (snap.unit == null) continue; // ถูก Destroy จริงๆ (ไม่น่าเกิด)

                // 1. เปิด GameObject กลับมา (อาจถูกปิดจาก Break)
                snap.unit.gameObject.SetActive(snap.wasActive);

                // 2. คืนตำแหน่งและการหมุน
                snap.unit.transform.position = snap.position;
                snap.unit.transform.rotation = snap.rotation;

                // 3. รีเซ็ต Rigidbody → kinematic, หยุดนิ่ง
                Rigidbody rb = snap.unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // 4. รีเซ็ต StructuralStress (HP, isBroken, colliders)
                var stress = snap.unit.GetComponent<StructuralStress>();
                if (stress != null)
                {
                    stress.ResetFull();
                }

                // 5. ลบ Joint เก่า แล้วสร้างใหม่ตาม Snapshot
                Joint[] existingJoints = snap.unit.GetComponents<Joint>();
                foreach (var j in existingJoints) Destroy(j);

                FixedJoint newJoint = snap.unit.gameObject.AddComponent<FixedJoint>();
                newJoint.connectedBody = snap.connectedBody;
            }

            _snapshots.Clear();
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

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
