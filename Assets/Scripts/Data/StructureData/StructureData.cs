using UnityEngine;

namespace Simulation.Data
{
    [CreateAssetMenu(fileName = "New Structure Data", menuName = "Simulation/Structure Data")]
    public class StructureData : ScriptableObject
    {
        [Header("General Info")]
        public string structureName;
        public float basePrice;
        public float baseMass;
        public float baseHP;
        public Vector3 size = Vector3.one;
        [Tooltip("If true, this structure can overlap with other structures (e.g. for walls)")]
        public bool allowOverlap = false;
        [Tooltip("If true, structures placed on top of this will sink to its base Y instead of stacking on top. Use for Floor/Foundation pieces so Walls share the same base level.")]
        public bool placementSinkThrough = false;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;
    }
}
