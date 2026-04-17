using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        public bool IsSimulating { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void ToggleSimulation()
        {
            if (IsSimulating) StopSimulation();
            else StartSimulation();
        }

        [ContextMenu("Start Simulation")]
        public void StartSimulation()
        {
            if (IsSimulating) return;
            IsSimulating = true;

            StructureUnit[] allUnits = UnityEngine.Object.FindObjectsByType<StructureUnit>(UnityEngine.FindObjectsSortMode.None);
            
            ConnectStructures(allUnits);

            foreach (var unit in allUnits)
            {
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    // NEW: ลบการผูกมัดว่า Floor ต้องเป็นแสงสถิตย์ (kinematic)
                    // ทุกวัตถุจะต้องเป็นสิ่งของที่ตกนอิสระทั้งหมด (Dynamic) และต้องค้นหาฐานรองรับหรือพื้นโลกด้วยตัวเอง!
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    
                    rb.solverIterations = 50;
                    rb.solverVelocityIterations = 20;

                    rb.WakeUp();
                }
            }
        }

        [ContextMenu("Stop Simulation")]
        public void StopSimulation()
        {
            IsSimulating = false;
            
            StructureUnit[] allUnits = UnityEngine.Object.FindObjectsByType<StructureUnit>(UnityEngine.FindObjectsSortMode.None);
            foreach (var unit in allUnits)
            {
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                
                // Clear joints
                Joint[] joints = unit.GetComponents<Joint>();
                foreach (var j in joints)
                {
                    Destroy(j);
                }
            }
        }

        private Bounds GetTotalBounds(StructureUnit unit)
        {
            Collider[] colliders = unit.GetComponentsInChildren<Collider>();
            if (colliders.Length == 0) return new Bounds(unit.transform.position, Vector3.zero);
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
            {
                b.Encapsulate(colliders[i].bounds);
            }
            return b;
        }

        private void ConnectStructures(StructureUnit[] allUnits)
        {
            for (int i = 0; i < allUnits.Length; i++)
            {
                StructureUnit a = allUnits[i];
                Rigidbody rbA = a.GetComponent<Rigidbody>();
                if (rbA == null) continue;

                StructuralStress stressA = a.GetComponent<StructuralStress>();
                if (stressA == null)
                {
                    stressA = a.gameObject.AddComponent<StructuralStress>();
                    stressA.InitializeStress(a.CurrentHP, a.Data != null ? a.Data.maxStress : 1000f, a.Data != null ? a.Data.forceTransferPercent : 100f);
                }

                Bounds boundsA = GetTotalBounds(a);
                // ขยาย bounds เล็กน้อยเพื่อให้ครอบคลุมจุดเชื่อมต่อทั้งหมด 10cm ช่วยจับกล่องที่ชนกันพอดีได้แน่นขึ้น
                boundsA.Expand(0.1f); 

                // --- NEW: FOUNDATION ANCHOR SYSTEM ---
                // ตรวจสอบว่าชิ้นส่วนนี้แตะกับพื้นโลกหรือวัตถุฉากอื่นๆ (ที่ไม่ใช่ชิ้นส่วนก่อสร้างด้วยกัน) หรือไม่
                Collider[] overlaps = UnityEngine.Physics.OverlapBox(boundsA.center, boundsA.extents);
                bool touchingEnvironment = false;
                foreach (var hit in overlaps)
                {
                    // ถ้าโดนชนสิ่งที่ไม่ใช่ StructureUnit ถือว่าเป็นรากฐาน/พื้นดิน
                    if (hit.GetComponentInParent<StructureUnit>() == null && !hit.isTrigger)
                    {
                        touchingEnvironment = true;
                        break;
                    }
                }

                // กรณีที่แตะกับพื้นผิวโลก ให้สร้างจุดยึดเหนี่ยวกับดิน (Ground Anchor)
                if (touchingEnvironment)
                {
                    FixedJoint groundJoint = a.gameObject.AddComponent<FixedJoint>();
                    groundJoint.connectedBody = null; // World Anchor
                    if (stressA != null) stressA.AddJoint(groundJoint);
                }
                // -------------------------------------

                for (int j = i + 1; j < allUnits.Length; j++)
                {
                    StructureUnit b = allUnits[j];
                    Rigidbody rbB = b.GetComponent<Rigidbody>();
                    if (rbB == null) continue;

                    if (boundsA.Intersects(GetTotalBounds(b)))
                    {
                        CreateConnection(a, rbA, b, rbB, stressA);
                    }
                }
            }
        }

        private void CreateConnection(StructureUnit a, Rigidbody rbA, StructureUnit b, Rigidbody rbB, StructuralStress stressA)
        {
            FixedJoint joint = a.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = rbB;
            // ปิด collision ระหว่าง joint เพื่อป้องกันไม่ให้สั่นกระตุกจนพังเอง
            joint.enableCollision = false; 

            if (stressA != null) stressA.AddJoint(joint);

            StructuralStress stressB = b.GetComponent<StructuralStress>();
            if (stressB != null)
            {
                stressB.AddJoint(joint);
            }
            else
            {
                stressB = b.gameObject.AddComponent<StructuralStress>();
                stressB.InitializeStress(b.CurrentHP, b.Data != null ? b.Data.maxStress : 1000f, b.Data != null ? b.Data.forceTransferPercent : 100f);
                stressB.AddJoint(joint);
            }
        }
    }
}
