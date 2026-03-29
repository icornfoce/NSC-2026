using UnityEngine;

namespace BuildingSimulation.Data
{
    public enum MaterialType
    {
        Concrete,
        Steel,
        Wood
    }

    [CreateAssetMenu(fileName = "NewBuildingMaterial", menuName = "Building/Material Data")]
    public class BuildingMaterialData : ScriptableObject
    {
        [Header("General")]
        public string materialName = "Unnamed";
        public MaterialType materialType = MaterialType.Concrete;

        [Header("Physics")]
        [Tooltip("Density in kg/m³. Concrete ~2400, Steel ~7800, Wood ~600")]
        public float density = 2400f;

        [Tooltip("Force required to break a joint made of this material (Newtons)")]
        public float breakForce = 50000f;

        [Tooltip("Optional Unity PhysicMaterial for friction/bounciness")]
        public PhysicsMaterial physicsMaterial;

        [Header("Economy")]
        [Tooltip("Cost multiplier applied on top of base part cost")]
        public float costMultiplier = 1f;

        [Header("Visuals")]
        public Color materialColor = Color.gray;
    }
}
