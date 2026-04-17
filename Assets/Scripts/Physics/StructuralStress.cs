using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    [RequireComponent(typeof(Rigidbody))]
    public class StructuralStress : MonoBehaviour
    {
        [Header("Stress Parameters")]
        [Tooltip("ค่าความเครียดสูงสุดที่รับได้ก่อนจะพัง")]
        public float maxStress = 1000f;
        [Tooltip("อัตราการถ่ายเทแรงสูงสุด (เป็นเปอร์เซ็นต์) ยิ่งค่าน้อยจุดเชื่อมต่อยิ่งเปราะบาง")]
        [Range(0.1f, 100f)]
        public float forceTransferPercent = 100f;
        
        [Header("Stress Colors")]
        public Color lowStressColor = Color.green;
        public Color midStressColor = Color.yellow;
        public Color highStressColor = Color.red;
        
        public float CurrentStress { get; private set; }

        private Rigidbody rb;
        private List<Joint> joints = new List<Joint>();
        private StructureUnit unit;
        private List<Renderer> renderers = new List<Renderer>();

        public void InitializeStress(float hp, float defaultMaxStress = 1000f, float transferPercent = 100f)
        {
            unit = GetComponent<StructureUnit>();
            rb = GetComponent<Rigidbody>();
            
            // If data maxStress is provided, use it. Otherwise derive from HP.
            maxStress = defaultMaxStress > 0 ? defaultMaxStress : hp * 10f; 
            forceTransferPercent = transferPercent;

            renderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                renderers.Add(r);
            }
        }

        public void AddJoint(Joint j)
        {
            if (j != null && !joints.Contains(j))
            {
                joints.Add(j);
                
                // กำหนดลิมิตการถ่ายเทแรงสูงสุดของชิ้นส่วน (max force transfer percent)
                // ยิ่ง%น้อย joint ก็ยิ่งพังง่ายเมื่อได้รับแรงสะสม
                float yieldForce = maxStress * (forceTransferPercent / 100f);
                j.breakForce = yieldForce;
                j.breakTorque = yieldForce;
            }
        }

        private void Update()
        {
            if (rb == null || joints == null) return;

            joints.RemoveAll(j => j == null); // Remove unexpectedly broken joints (snapped)

            CalculateStress();
            UpdateColor();
        }

        private void CalculateStress()
        {
            float totalForce = 0f;
            foreach (var j in joints)
            {
                if (j != null)
                {
                    totalForce += j.currentForce.magnitude;
                    totalForce += j.currentTorque.magnitude * 0.5f; 
                }
            }
            
            CurrentStress = totalForce;

            if (CurrentStress >= maxStress && maxStress > 0)
            {
                BreakStructure();
            }
        }

        private void UpdateColor()
        {
            if (maxStress <= 0 || renderers.Count == 0) return;

            float ratio = Mathf.Clamp01(CurrentStress / maxStress);
            Color targetColor;

            if (ratio < 0.5f)
            {
                // ดึงสัดส่วนขึ้นให้สัดส่วนน้อยๆ (0.1) เริ่มเปลี่ยนสีให้เห็นชัดเจนขึ้น
                float t = ratio * 2f;
                // ใส่ curve ให้สีเหลืองมาไวขึ้นเพื่อการรับรู้แรงที่ไวขึ้น
                t = Mathf.Pow(t, 0.7f); 
                targetColor = Color.Lerp(lowStressColor, midStressColor, t);
            }
            else
            {
                targetColor = Color.Lerp(midStressColor, highStressColor, (ratio - 0.5f) * 2f);
            }

            foreach (var r in renderers)
            {
                if (r != null && r.material != null)
                {
                    // เปลี่ยนสีแบบ smooth (fade)
                    r.material.color = Color.Lerp(r.material.color, targetColor, Time.deltaTime * 5f);
                }
            }
        }

        private void BreakStructure()
        {
            if (unit != null)
            {
                unit.DestroyStructure();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnJointBreak(float breakForce)
        {
            // Joint will automatically break and be removed from list next frame
            // We can add effects here if needed
        }
    }
}
