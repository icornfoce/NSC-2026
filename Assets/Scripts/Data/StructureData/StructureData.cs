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
        public Vector2Int size = Vector2Int.one; // Grid size (e.g. 1x1, 2x3)

        [Header("Assets")]
        public GameObject prefab;
        public AudioClip placeSound;
        public AudioClip breakSound;
        public GameObject placeVFX;
        public GameObject breakVFX;
    }
}
