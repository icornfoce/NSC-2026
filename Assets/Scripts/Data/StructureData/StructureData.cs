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
        public Vector3Int size = Vector3Int.one; // Grid size (e.g. 1x1x1, 2x3x1)

        [Header("Assets")]
        public GameObject prefab;
        public AudioClip placeSound;
        public AudioClip breakSound;
        public GameObject placeVFX;
        public GameObject breakVFX;
    }
}
