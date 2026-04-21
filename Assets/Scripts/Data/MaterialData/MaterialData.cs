using UnityEngine;

namespace Simulation.Data
{
    [CreateAssetMenu(fileName = "New Material Data", menuName = "Simulation/Material Data")]
    public class MaterialData : ScriptableObject
    {
        [Header("General Info")]
        public string materialName;
        public float priceModifier;
        public float massModifier;
        public float hpModifier;

        [Header("Assets")]
        public Material material;
        public AudioClip placeSound;
        public AudioClip breakSound;
        public GameObject placeVFX;
        public GameObject breakVFX;

        [Header("Limits")]
        public float maxCompression = 1000f;
        public float maxTension = 1000f;
    }
}
