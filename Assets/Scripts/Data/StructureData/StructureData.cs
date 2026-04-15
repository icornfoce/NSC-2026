using UnityEngine;

namespace Simulation.Data
{
    public enum BuildType { None, Floor, Wall, Object }

    [CreateAssetMenu(fileName = "New Structure Data", menuName = "Simulation/Structure Data")]
    public class StructureData : ScriptableObject
    {
        [Header("General Info")]
        public string structureName;
        public float basePrice;
        public float baseMass;
        public float baseHP;
        public Vector3 size = Vector3.one;

        [Header("Grid System")]
        [Tooltip("Floor defines a building area. Wall/Object requires a Floor beneath them.")]
        public BuildType buildType = BuildType.Object;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;
    }
}
