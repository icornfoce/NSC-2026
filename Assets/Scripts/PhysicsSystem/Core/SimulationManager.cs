using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    /// <summary>
    /// Manager สำหรับคุมเวลากดเริ่ม/หยุด การจำลองทางวิศวกรรมของตึก (Construction Simulation)
    ///
    /// ระบบ Snapshot/Restore:
    ///   - ก่อนจำลองจะบันทึกสถานะโครงสร้างตึกทุกชิ้น
    ///   - ResetSimulation() จะคืนทุกอย่างกลับสู่สถานะก่อนทดสอบสภาพตึก
    ///   - ใช้สำหรับการทดสอบความแข็งแรงของตึกหลังสร้างเสร็จ
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        private static SimulationManager _instance;
        public static SimulationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<SimulationManager>();

                    if (_instance == null)
                    {
                        var go = new GameObject("SimulationManager [Auto-Created]");
                        _instance = go.AddComponent<SimulationManager>();
                        Debug.LogWarning("[SimulationManager] ไม่พบใน Scene — สร้าง Instance อัตโนมัติแล้ว " +
                                         "แนะนำให้ลาก GameObject ที่ติด SimulationManager ใส่ Scene ให้เรียบร้อย");
                    }
                }
                return _instance;
            }
        }

        [Header("State")]
        [SerializeField] private bool isSimulating = false;

        // Load propagation system — หาอัตโนมัติจาก component บน GameObject เดียวกัน
        private LoadPropagationSystem _loadSystem;

        public bool IsSimulating => isSimulating;

        // ── Snapshot data ─────────────────────────────────────────────
        private struct StructureSnapshot
        {
            public StructureUnit unit;
            public Vector3 position;
            public Quaternion rotation;
            public float hp;
            /// <summary>connectedBody ของ Joint ตอนวาง — null = world anchor</summary>
            public Rigidbody jointConnectedBody;
        }

        private List<StructureSnapshot> _snapshots = new List<StructureSnapshot>();

        // ── Events ────────────────────────────────────────────────────
        /// <summary>Fired when simulation starts (after snapshots are taken).</summary>
        public event System.Action OnSimulationStarted;

        /// <summary>Fired when simulation stops (physics frozen).</summary>
        public event System.Action OnSimulationStopped;

        /// <summary>Fired after ResetSimulation() restores all structures.</summary>
        public event System.Action OnSimulationReset;

        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance == null) _instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // แช่แข็งฟิสิกส์เริ่มต้น
            FreezeAllStructures();

            // หา LoadPropagationSystem — ถ้าไม่มีให้เพิ่มอัตโนมัติ
            _loadSystem = GetComponent<LoadPropagationSystem>();
            if (_loadSystem == null)
                _loadSystem = gameObject.AddComponent<LoadPropagationSystem>();
        }

        private void FixedUpdate()
        {
            if (!isSimulating || _loadSystem == null) return;
            _loadSystem.PropagateLoads();
        }

        // ─────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// เริ่มการจำลองฟิสิกส์ — จะ snapshot ทุก structure ก่อนปลด Kinematic
        /// ใช้ลากใส่ปุ่ม OnClick() หรือเรียกผ่าน MissionSystem.BeginSimulation()
        /// </summary>
        public void StartSimulation()
        {
            if (isSimulating) return;
            isSimulating = true;

            // 1. ปิดโหมดสร้าง + ซ่อน Grid
            if (BuildingSystem.Instance != null)
            {
                BuildingSystem.Instance.ExitMode();
                BuildingSystem.Instance.HideGridVisual();
            }

            // 2. บันทึก snapshot ก่อนเริ่ม physics
            TakeSnapshots();

            // 3. ปิด Collision ระหว่างของที่ทับกันอยู่แต่แรก เพื่อไม่ให้เด้งระเบิดใส่กันตอนเปิดระบบฟิสิกส์
            IgnoreOverlappingCollisions();

            // 4. ปลด Kinematic ให้ physics ทำงาน — ใช้ StructureRegistry แทน FindObjectsByType
            var allUnits = StructureRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i] == null) continue;
                Rigidbody rb = allUnits[i].GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }
            }

            // 4. ของที่ลอยอยู่โดยไม่มีฐานรองรับ → ถอด Joint ให้ตกลงพื้น
            DetachFloatingStructures();

            OnSimulationStarted?.Invoke();
            Debug.Log("<color=green>▶ Start Simulation</color> — snapshots saved, physics running!");
        }

        /// <summary>Toggle เริ่ม/หยุด (ใช้ลากใส่ปุ่มเดียว)</summary>
        public void ToggleSimulation()
        {
            if (isSimulating) StopSimulation();
            else StartSimulation();
        }

        /// <summary>หยุดการจำลอง — แช่แข็ง Rigidbody ทุกตัว (ไม่ restore)</summary>
        public void StopSimulation()
        {
            if (!isSimulating) return;
            isSimulating = false;
            FreezeAllStructures();

            // แสดง Grid Visual กลับมา
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.ShowGridVisual();

            OnSimulationStopped?.Invoke();
            Debug.Log("<color=red>■ Stop Simulation</color> — physics frozen.");
        }

        /// <summary>
        /// คืน structures ทุกชิ้นกลับสู่สถานะก่อนจำลอง แล้วแช่แข็งใหม่
        /// ใช้จาก MissionSystem.ResetMission() หรือปุ่ม "Replay"
        /// </summary>
        public void ResetSimulation()
        {
            if (isSimulating) StopSimulation();

            RestoreSnapshots();
            _snapshots.Clear();

            // แสดง Grid Visual กลับมา
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.ShowGridVisual();

            OnSimulationReset?.Invoke();
            Debug.Log("<color=cyan>↺ Simulation Reset</color> — all structures restored to pre-sim state.");
        }

        // ─────────────────────────────────────────────────────────────
        // Snapshot / Restore
        // ─────────────────────────────────────────────────────────────

        private void TakeSnapshots()
        {
            _snapshots.Clear();
            // ใช้ StructureRegistry แทน FindObjectsByType — เร็วกว่าเมื่อมีวัตถุจำนวนมาก
            var allUnits = StructureRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null) continue;

                // เก็บ connectedBody ของ Joint เพื่อ restore ได้ถูกต้อง
                Rigidbody connectedBody = null;
                Joint existingJoint = unit.GetComponent<Joint>();
                if (existingJoint != null)
                    connectedBody = existingJoint.connectedBody;

                _snapshots.Add(new StructureSnapshot
                {
                    unit = unit,
                    position = unit.transform.position,
                    rotation = unit.transform.rotation,
                    hp = unit.CurrentHP,
                    jointConnectedBody = connectedBody,
                });
            }
            Debug.Log($"[SimulationManager] Snapshot taken for {_snapshots.Count} structures.");
        }

        private void RestoreSnapshots()
        {
            int restored = 0;
            foreach (var snap in _snapshots)
            {
                if (snap.unit == null) continue; // object was destroyed (e.g. after break + Destroy)

                // Re-enable object in case it was hidden
                snap.unit.gameObject.SetActive(true);

                // Restore transform
                snap.unit.transform.position = snap.position;
                snap.unit.transform.rotation = snap.rotation;

                // Freeze Rigidbody
                Rigidbody rb = snap.unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // Restore HP via StructuralStress (also resets visual colour)
                var stress = snap.unit.GetComponent<StructuralStress>();
                if (stress != null)
                    stress.InitializeStress(snap.hp); // re-init at saved HP (=max since snap is pre-sim)

                // Re-attach joint if it was destroyed during sim — ใช้ connectedBody จาก snapshot
                RestoreJoint(snap.unit, snap.jointConnectedBody);

                restored++;
            }
            Debug.Log($"[SimulationManager] Restored {restored}/{_snapshots.Count} structures.");
        }

        /// <summary>
        /// Joints are destroyed when a structure breaks. Re-add a FixedJoint connecting
        /// to the original connected body (from snapshot) so that the structural links
        /// are identical to what the player originally built.
        /// connectedBody = null means world anchor (ground placement).
        /// </summary>
        private void RestoreJoint(StructureUnit unit, Rigidbody connectedBody)
        {
            if (unit == null) return;

            // Remove any leftover broken joints
            foreach (var j in unit.GetComponents<Joint>())
                Destroy(j);

            // Re-add a FixedJoint with the original connected body
            FixedJoint fixedJoint = unit.gameObject.AddComponent<FixedJoint>();

            // connectedBody อาจ null (= world anchor) หรือชี้ไป Rigidbody ของ structure อื่น
            // ถ้า connectedBody ถูก destroy ไปแล้ว (เช่น structure ที่เชื่อมถูกลบ) จะเป็น null → world anchor
            if (connectedBody != null)
                fixedJoint.connectedBody = connectedBody;
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ถอด Collision คู่วัตถุที่วางทับซ้อนกันตั้งแต่เริ่มต้น
        /// เพื่อป้องกันปัญหาวัตถุเด้งระเบิดออกจากกันเมื่อเปิด physics
        /// </summary>
        private void IgnoreOverlappingCollisions()
        {
            var allUnits = StructureRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i] == null) continue;
                Collider[] myCols = allUnits[i].GetComponentsInChildren<Collider>();
                
                for (int j = i + 1; j < allUnits.Count; j++)
                {
                    if (allUnits[j] == null) continue;
                    Collider[] otherCols = allUnits[j].GetComponentsInChildren<Collider>();

                    // เช็คว่ามี Overlap กันไหม
                    bool isOverlapping = false;
                    foreach (var mc in myCols)
                    {
                        foreach (var oc in otherCols)
                        {
                            if (mc.bounds.Intersects(oc.bounds))
                            {
                                isOverlapping = true;
                                break;
                            }
                        }
                        if (isOverlapping) break;
                    }

                    // ถ้ามันทับกันอยู่ สั่ง Ignore กันเลย ไม่ต้องพยายามดันออก
                    if (isOverlapping)
                    {
                        foreach (var mc in myCols)
                        {
                            foreach (var oc in otherCols)
                            {
                                UnityEngine.Physics.IgnoreCollision(mc, oc, true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ตรวจจับชิ้นส่วนที่ลอยอยู่โดยไม่มีฐานรองรับ
        /// ถอด Joint ทั้งหมดออกเพื่อให้แรงโน้มถ่วงดึงลงพื้น
        /// </summary>
        private void DetachFloatingStructures()
        {
            float groundThreshold = 0.3f;
            var allUnits = StructureRegistry.All;
            int detached = 0;

            for (int i = 0; i < allUnits.Count; i++)
            {
                var unit = allUnits[i];
                if (unit == null) continue;

                // ชิ้นส่วนที่อยู่ใกล้พื้น (Y ต่ำ) ถือว่าอยู่บนพื้นแล้ว
                if (unit.transform.position.y <= groundThreshold) continue;

                // เช็คว่ามี Collider รองรับอยู่ข้างล่างจริงไหม
                // ใช้ OverlapBox ใต้วัตถุเพื่อหาว่ามีอะไรรองรับอยู่
                Collider[] cols = unit.GetComponentsInChildren<Collider>();
                bool hasPhysicalSupport = false;

                foreach (var col in cols)
                {
                    if (col == null) continue;
                    Bounds b = col.bounds;
                    // บริเวณใต้ฐานของวัตถุ
                    Vector3 checkCenter = new Vector3(b.center.x, b.min.y - 0.15f, b.center.z);
                    Vector3 checkExtents = new Vector3(b.extents.x * 0.5f, 0.1f, b.extents.z * 0.5f);

                    Collider[] hits = UnityEngine.Physics.OverlapBox(checkCenter, checkExtents);
                    foreach (var hit in hits)
                    {
                        // ข้ามตัวเอง
                        if (hit.transform.root == unit.transform.root) continue;
                        hasPhysicalSupport = true;
                        break;
                    }
                    if (hasPhysicalSupport) break;
                }

                if (!hasPhysicalSupport)
                {
                    // ถอด Joint ทั้งหมดออก → แรงโน้มถ่วงจะดึงลงพื้น
                    foreach (var j in unit.GetComponents<Joint>())
                        Destroy(j);
                    detached++;
                }
            }

            if (detached > 0)
                Debug.Log($"<color=orange>⚠ Detached {detached} floating structures</color>");
        }

        private void FreezeAllStructures()
        {
            // ใช้ StructureRegistry — ไม่ต้อง search ทั้ง Scene
            var allUnits = StructureRegistry.All;
            for (int i = 0; i < allUnits.Count; i++)
            {
                if (allUnits[i] == null) continue;
                Rigidbody rb = allUnits[i].GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}