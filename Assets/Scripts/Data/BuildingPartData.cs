using UnityEngine;

namespace BuildingSimulation.Data
{
    public enum PartType
    {
        Pillar,
        Wall,
        Floor,
        Stairs,
        Door
    }

    [CreateAssetMenu(fileName = "NewBuildingPart", menuName = "Building/Part Data")]
    public class BuildingPartData : ScriptableObject
    {
        [Header("Identity")]
        public string partName = "Unnamed Part";
        public PartType partType = PartType.Pillar;

        [Header("Cost")]
        [Tooltip("Base cost in dollars before material multiplier")]
        public float baseCost = 100f;

        [Header("Scale Limits")]
        public Vector3 defaultScale = Vector3.one;
        public Vector3 minScale = new Vector3(0.2f, 0.2f, 0.2f);
        public Vector3 maxScale = new Vector3(5f, 5f, 5f);

        [Header("Default Material")]
        public BuildingMaterialData defaultMaterial;

        [Header("Sound")]
        [Tooltip("Sound played when this part is placed")]
        public AudioClip placementSound;

        [Tooltip("Looping ambient sound while this part exists")]
        public AudioClip ambientSound;

        [Tooltip("Volume for ambient sound")]
        [Range(0f, 1f)]
        public float ambientVolume = 0.3f;

        [Header("Visual Effects")]
        [Tooltip("Particle effect prefab spawned at placement position")]
        public GameObject placementEffectPrefab;

        [Tooltip("Looping particle effect attached to this part")]
        public GameObject persistentEffectPrefab;

        [Header("Visuals")]
        [Tooltip("Optional custom 3D model (prefab). If null, a primitive is used.")]
        public GameObject modelPrefab;

        [Tooltip("Color tint for the ghost preview")]
        public Color previewColor = new Color(0f, 1f, 0f, 0.4f);
    }
}
