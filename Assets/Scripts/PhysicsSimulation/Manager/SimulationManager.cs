using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    /// <summary>
    /// Manager สำหรับคุมเวลากดเริ่ม/หยุด การจำลองฟิสิกส์ (เหมือนปุ่ม Play ใน Poly Bridge)
    ///
    /// เพิ่มระบบ Snapshot/Restore:
    ///   - ก่อนจำลองจะบันทึก transform + HP ของ structure ทุกชิ้น
    ///   - ResetSimulation() จะคืนทุกอย่างกลับสู่สถานะก่อนจำลอง
    ///   - ไม่ต้อง destroy/remake objects → เร็วกว่าและไม่มีการสูญเสีย references
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("State")]
        [SerializeField] private bool isSimulating = false;

        public bool IsSimulating => isSimulating;

        // ── Snapshot data ─────────────────────────────────────────────
        private struct StructureSnapshot
        {
            public StructureUnit unit;
            public Vector3 position;
            public Quaternion rotation;
            public float hp;
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
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // แช่แข็งฟิสิกส์เริ่มต้น
            FreezeAllStructures();
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

            // 1. ปิดโหมดสร้าง
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.ExitMode();

            // 2. บันทึก snapshot ก่อนเริ่ม physics
            TakeSnapshots();

            // 3. ปลด Kinematic ให้ physics ทำงาน
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.WakeUp();
                }
            }

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

            OnSimulationReset?.Invoke();
            Debug.Log("<color=cyan>↺ Simulation Reset</color> — all structures restored to pre-sim state.");
        }

        // ─────────────────────────────────────────────────────────────
        // Snapshot / Restore
        // ─────────────────────────────────────────────────────────────

        private void TakeSnapshots()
        {
            _snapshots.Clear();
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                if (unit == null) continue;
                _snapshots.Add(new StructureSnapshot
                {
                    unit     = unit,
                    position = unit.transform.position,
                    rotation = unit.transform.rotation,
                    hp       = unit.CurrentHP,
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
                    rb.linearVelocity  = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                // Restore HP via StructuralStress (also resets visual colour)
                var stress = snap.unit.GetComponent<StructuralStress>();
                if (stress != null)
                    stress.InitializeStress(snap.hp); // re-init at saved HP (=max since snap is pre-sim)

                // Re-attach joint if it was destroyed during sim
                RestoreJoint(snap.unit);

                restored++;
            }
            Debug.Log($"[SimulationManager] Restored {restored}/{_snapshots.Count} structures.");
        }

        /// <summary>
        /// Joints are destroyed when a structure breaks. Re-add a FixedJoint connecting
        /// to the world (null connected body = world anchor), which is enough to
        /// hold placed structures in Kinematic-off mode until re-simulation.
        /// </summary>
        private void RestoreJoint(StructureUnit unit)
        {
            if (unit == null) return;

            // Remove any leftover broken joints
            foreach (var j in unit.GetComponents<Joint>())
                Destroy(j);

            // Re-add a FixedJoint anchored to the world
            unit.gameObject.AddComponent<FixedJoint>();
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private void FreezeAllStructures()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            foreach (var unit in units)
            {
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic     = true;
                    rb.linearVelocity  = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }
    }
}
