using UnityEngine;

namespace Simulation.Data
{
    /// <summary>
    /// Determines how a structure snaps to the grid.
    /// Normal = snap to cell center (supports multi-cell via size),
    /// Wall   = snap to grid edges (lines between cells),
    /// Door   = can only be placed on an existing Wall (replaces it).
    /// </summary>
    public enum StructureType
    {
        Normal,
        Wall,
        Door,
        Floor
    }

    [CreateAssetMenu(fileName = "New Structure Data", menuName = "Simulation/Structure Data")]
    public class StructureData : ScriptableObject
    {
        [Header("General Info")]
        public string structureName;
        [Tooltip("Normal = snaps to cell center, Wall = snaps to grid edges, Door = places on walls only")]
        public StructureType structureType = StructureType.Normal;
        public float basePrice;
        public float baseMass;
        public float baseHP;
        public Vector3 size = Vector3.one;
        [Tooltip("If true, the pivot of this prefab is at its geometric center. If false, it's assumed to be at the bottom.")]
        public bool pivotAtCenter = false;
        [Tooltip("If true, this structure can overlap with other structures (e.g. for walls)")]
        public bool allowOverlap = false;
        [Tooltip("If true, structures placed on top of this will sink to its base Y instead of stacking on top. Use for Floor/Foundation pieces so Walls share the same base level.")]
        public bool placementSinkThrough = false;
        [Tooltip("If true, this structure requires ground or another structure directly beneath it to be placed. Enable for Pillars to prevent floating placement.")]
        public bool requiresSupport = false;
        
        [Tooltip("If true, this structure can ONLY be placed on top of other structures, not on the ground.")]
        public bool placeOnStructureOnly = false;

        [Header("Physical Limits")]
        [Tooltip("Base maximum compression force this structure can withstand (N). Material modifier adds on top.")]
        public float baseMaxCompression = 1000f;
        [Tooltip("Base maximum tension force this structure can withstand (N). Material modifier adds on top.")]
        public float baseMaxTension = 1000f;

        [Header("Assets")]
        public GameObject prefab;
        public MaterialData defaultMaterial;
        public GameObject breakVFX;
        public AudioClip breakSFX;
        [Range(0f, 5f)] public float breakShakeIntensity = 2.0f;

        [Header("Door Settings")]
        [Tooltip("The prefab to replace the wall with when a door is placed. Only used when structureType = Door.")]
        public GameObject doorReplacementPrefab;
    }
}
