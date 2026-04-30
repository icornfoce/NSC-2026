using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Character;
using Unity.AI.Navigation;
using UnityEngine.AI;

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

        [Header("Character System")]
        [Tooltip("Prefab ของคนตัวจริง (ที่มี PersonAI) ที่จะเกิดมาตอนกด Start")]
        [SerializeField] private GameObject personAIPrefab;
        private List<GameObject> _spawnedCharacters = new List<GameObject>();

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
            public List<Rigidbody> connectedBodies; // เก็บ Joint ทุกตัวที่ต่ออยู่ (Main + Side joints)
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

            // ปิด Agent ทั้งหมดชั่วคราวก่อนอบ เพื่อไม่ให้เกิด Error ว่าหา NavMesh ไม่เจอ
            NavMeshAgent[] existingAgents = FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None);
            foreach (var agent in existingAgents)
            {
                agent.enabled = false;
            }

            // 2.2 Bake NavMesh สดๆ ก่อนให้คนเดิน
            NavMeshSurface surface = GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = gameObject.AddComponent<NavMeshSurface>();
            }
            
            // ตั้งค่าพื้นผิวให้ใช้อบ
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; // ใช้ Collider เป็นตัวคำนวณพื้นเดิน

            surface.BuildNavMesh();

            // 2.5 เรียกคนออกมาเดิน
            SpawnCharacters();

            // 3. ปลดล็อค Kinematic เพื่อให้ฟิสิกส์ทำงาน และปลุก AI ที่วางไว้
            foreach (var snap in _snapshots)
            {
                if (snap.unit == null) continue;
                
                // ถ้าเป็นตัวละครที่เคยวางไว้ ให้เปิด AI
                PersonAI ai = snap.unit.GetComponent<PersonAI>();
                if (ai != null)
                {
                    ai.InitializeAgent();
                }

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

            // ลบตัวละครจริงทิ้ง
            ClearCharacters();

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
                    connectedBodies = new List<Rigidbody>()
                };

                // บันทึก Joint connection ทุกตัว (ทั้ง Main และ Side)
                var joints = unit.GetComponents<Joint>();
                foreach (var joint in joints)
                {
                    snap.connectedBodies.Add(joint.connectedBody); // null = connected to world
                }

                _snapshots.Add(snap);
            }
        }

        private void RestoreSnapshots()
        {
            // ── Phase 1: คืนตำแหน่ง, ลบ Joint เก่า, รีเซ็ตสถานะ ──
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
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                    }
                }

                // 4. รีเซ็ต StructuralStress (HP, isBroken, colliders)
                var stress = snap.unit.GetComponent<StructuralStress>();
                if (stress != null)
                {
                    stress.ResetFull();
                }

                // 5. ลบ Joint เก่าทิ้งให้หมด (ต้องใช้ DestroyImmediate เพื่อให้หายไปทันที ไม่ค้างในเฟรม)
                Joint[] existingJoints = snap.unit.GetComponents<Joint>();
                foreach (var j in existingJoints) DestroyImmediate(j);
            }

            // ── บังคับอัปเดตฟิสิกส์ ──
            // สำคัญมาก! หลังจากย้ายตำแหน่งกลับมาแล้ว ต้องสั่งให้ Unity อัปเดตตำแหน่ง Collider ใหม่ทันที
            // ไม่งั้นตอนสร้าง Joint ใหม่ มันจะใช้ตำแหน่งเก่าที่เพิ่งพังร่วงลงไป ทำให้เกิดบั๊กระเบิดกระจาย
            UnityEngine.Physics.SyncTransforms();

            // ── Phase 2: สร้าง Joint ใหม่ตามตำแหน่งที่ถูกต้อง ──
            foreach (var snap in _snapshots)
            {
                if (snap.unit == null) continue;

                if (snap.connectedBodies != null)
                {
                    foreach (var connectedBody in snap.connectedBodies)
                    {
                        FixedJoint newJoint = snap.unit.gameObject.AddComponent<FixedJoint>();
                        newJoint.connectedBody = connectedBody;
                    }
                }
            }

            _snapshots.Clear();

            // คืนค่า IgnoreCollision ให้กับทุกชิ้นส่วน (ป้องกัน Bug ของระเบิดกระจายตอนกด Start รอบสอง)
            // เพราะตอนที่ของพังไปแล้ว ระบบจะเปิดให้ชนกันใหม่ (RestorePhysicsCollisions) 
            // เมื่อย้อนเวลากลับมา (Rewind) จึงต้องสั่งให้มันเลิกชนกันอีกรอบ เหมือนตอนเพิ่งวางเสร็จ
            StructureUnit[] allUnits = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            for (int i = 0; i < allUnits.Length; i++)
            {
                if (allUnits[i] == null) continue;
                Collider[] cols1 = allUnits[i].GetComponentsInChildren<Collider>(true);
                
                for (int j = i + 1; j < allUnits.Length; j++)
                {
                    if (allUnits[j] == null) continue;
                    Collider[] cols2 = allUnits[j].GetComponentsInChildren<Collider>(true);
                    
                    foreach (var c1 in cols1)
                    {
                        foreach (var c2 in cols2)
                        {
                            if (c1 != null && c2 != null)
                                UnityEngine.Physics.IgnoreCollision(c1, c2, true);
                        }
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Character System
        // ────────────────────────────────────────────────────────────────

        private void SpawnCharacters()
        {
            ClearCharacters();

            if (personAIPrefab == null) return;

            PersonSpawner[] spawners = FindObjectsByType<PersonSpawner>(FindObjectsSortMode.None);
            PersonTarget[] targets = FindObjectsByType<PersonTarget>(FindObjectsSortMode.None);

            if (spawners.Length > 0 && targets.Length > 0)
            {
                // สำหรับแต่ละ Target ให้เกิดคนที่ Spawner อันแรกสุดแล้วเดินไปหา
                foreach (var target in targets)
                {
                    PersonSpawner spawner = spawners[0]; // หรือถ้ามีหลายทางเข้าก็ Random.Range ได้
                    
                    // ป้องกันการเกิดนอก NavMesh (เช่น จุด Spawn จมดิน หรือลอย)
                    Vector3 spawnPos = spawner.transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(spawnPos, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        spawnPos = hit.position; // ใช้จุดที่อยู่บนพื้น NavMesh จริงๆ
                    }
                    else
                    {
                        // ถ้าหาไม่เจอจริงๆ ลองขยับขึ้นนิดนึง
                        spawnPos += Vector3.up * 0.5f; 
                    }

                    GameObject charObj = Instantiate(personAIPrefab, spawnPos, Quaternion.identity);
                    _spawnedCharacters.Add(charObj);

                    PersonAI ai = charObj.GetComponent<PersonAI>();
                    if (ai != null)
                    {
                        ai.InitializeAgent(); // เพิ่มบรรทัดนี้เพื่อเปิด Agent อย่างปลอดภัย
                        ai.SetTarget(target.transform);
                    }
                }
            }
        }

        private void ClearCharacters()
        {
            foreach (var charObj in _spawnedCharacters)
            {
                if (charObj != null) Destroy(charObj);
            }
            _spawnedCharacters.Clear();
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
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                    }
                }
            }
        }
    }
}
