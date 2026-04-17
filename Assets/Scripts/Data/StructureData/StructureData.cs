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
        
        public Vector3 size = Vector3.one;

        [Header("Grid System")]
        [Tooltip("Floor defines a building area. Wall/Object requires a Floor beneath them.")]
        public BuildType buildType = BuildType.Structure;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;
    }
}
