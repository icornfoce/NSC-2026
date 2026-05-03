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

        [Header("Limits (Additive Modifier)")]
        [Tooltip("Bonus compression capacity added to the structure's base maxCompression (N).")]
        public float compressionModifier = 0f;
        [Tooltip("Bonus tension capacity added to the structure's base maxTension (N).")]
        public float tensionModifier = 0f;
    }
}
