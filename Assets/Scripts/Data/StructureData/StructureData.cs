using UnityEngine;

namespace Simulation.Data
{
    public enum BuildType { None, Floor, Structure }

    [CreateAssetMenu(fileName = "New Structure Data", menuName = "Simulation/Structure Data")]
    public class StructureData : ScriptableObject
    {
        [Header("General Info")]
        public string structureName;
        public float basePrice;
        public float baseMass;
        public float baseHP;
        
        [Header("Physics & Stress")]
        [Tooltip("ค่าความเครียดสูงสุดก่อนชิ้นส่วนจะพัง")]
        public float maxStress = 1000f;
        [Tooltip("อัตราการถ่ายเทแรง %")]
        [Range(0.1f, 100f)]
        public float forceTransferPercent = 100f;
        
        [Tooltip("ขนาด fallback — ถ้าเปิด autoCalculateSize จะคำนวณจาก Prefab แทน")]
        public Vector3 size = Vector3.one;

        [Header("Grid System")]
        [Tooltip("Floor defines a building area. Wall/Object requires a Floor beneath them.")]
        public BuildType buildType = BuildType.Structure;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;

        // ────────────────────────────────────────────────────────────────
        // Auto Size
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// คำนวณขนาดจริงจาก Prefab bounds โดยอัตโนมัติ
        /// ใช้แทน size ที่ตั้งมือ — ทำให้ 1 ชั้น = ความสูงเสาจริง 1 ต้น
        /// </summary>
        public Vector3 GetActualSize()
        {
            if (prefab == null) return size;

            Bounds combined = new Bounds();
            bool init = false;

            // วัดขนาดจาก Collider ทั้งหมดใน Prefab
            foreach (var col in prefab.GetComponentsInChildren<Collider>(true))
            {
                Bounds localB;
                if (col is BoxCollider bc)
                    localB = new Bounds(bc.center, bc.size);
                else if (col is CapsuleCollider cc)
                {
                    float h = cc.height, r = cc.radius;
                    Vector3 s = new Vector3(r * 2, r * 2, r * 2);
                    if (cc.direction == 0) s.x = h;
                    else if (cc.direction == 1) s.y = h;
                    else s.z = h;
                    localB = new Bounds(cc.center, s);
                }
                else if (col is MeshCollider mc && mc.sharedMesh != null)
                    localB = mc.sharedMesh.bounds;
                else continue;

                // แปลงมุมกล่อง 8 จุดเข้า local space ของ Prefab root
                Vector3 min = localB.min, max = localB.max;
                Vector3[] corners = {
                    new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                    new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
                };
                foreach (var c in corners)
                {
                    Vector3 world = col.transform.TransformPoint(c);
                    Vector3 local = prefab.transform.InverseTransformPoint(world);
                    if (!init) { combined = new Bounds(local, Vector3.zero); init = true; }
                    else combined.Encapsulate(local);
                }
            }

            if (!init) return size;
            return Vector3.Scale(combined.size, prefab.transform.localScale);
        }
    }
}
