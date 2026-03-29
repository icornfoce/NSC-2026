using UnityEngine;

namespace BuildingSimulation.Data
{
    [CreateAssetMenu(fileName = "NewNPCData", menuName = "Building/NPC Data")]
    public class NPCData : ScriptableObject
    {
        [Header("Identity")]
        public string npcName = "Human";

        [Header("Model")]
        [Tooltip("Custom prefab to use. If null, a default Cylinder will be created.")]
        public GameObject modelPrefab;

        [Tooltip("Color tint (used only for default primitive model)")]
        public Color modelColor = new Color(0.2f, 0.6f, 1f, 1f);

        [Header("Sound")]
        [Tooltip("Sound played when NPC is placed")]
        public AudioClip placementSound;

        [Tooltip("Looping ambient sound while NPC exists (e.g. footsteps, breathing)")]
        public AudioClip ambientSound;

        [Tooltip("Volume for ambient sound")]
        [Range(0f, 1f)]
        public float ambientVolume = 0.5f;

        [Header("Visual Effect")]
        [Tooltip("Particle effect prefab spawned at placement position")]
        public GameObject placementEffectPrefab;

        [Tooltip("Looping particle effect attached to NPC")]
        public GameObject persistentEffectPrefab;

        [Header("Movement")]
        public float moveSpeed = 2f;
        public float wanderRadius = 10f;
        public float wanderInterval = 3f;

        [Header("Physics")]
        public float mass = 50f;

        [Header("Cost")]
        [Tooltip("Cost to place this NPC")]
        public float cost = 500f;

        [Header("Ghost Preview")]
        public Color previewColor = new Color(0.2f, 0.6f, 1f, 0.4f);
    }
}
