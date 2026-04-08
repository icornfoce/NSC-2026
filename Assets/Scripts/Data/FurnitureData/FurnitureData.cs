using UnityEngine;

namespace Simulation.Data
{
    [CreateAssetMenu(fileName = "New Furniture Data", menuName = "Simulation/Furniture Data")]
    public class FurnitureData : ScriptableObject
    {
        [Header("General Info")]
        public string furnitureName;
        public float Price;
        public float Mass;
        public float HP;
        public Vector3Int size = Vector3Int.one;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;
    }
}
