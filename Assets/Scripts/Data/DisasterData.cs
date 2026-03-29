using UnityEngine;

namespace BuildingSimulation.Data
{
    public enum DisasterType
    {
        Earthquake,
        Wind
    }

    [CreateAssetMenu(fileName = "NewDisaster", menuName = "Building/Disaster Data")]
    public class DisasterData : ScriptableObject
    {
        [Header("General")]
        public string disasterName = "Unnamed Disaster";
        public DisasterType disasterType = DisasterType.Earthquake;
        public float duration = 5f;

        [Header("Earthquake Settings")]
        [Tooltip("Force magnitude applied as random horizontal impulse (Newtons)")]
        public float earthquakeMagnitude = 5000f;

        [Header("Wind Settings")]
        [Tooltip("Direction of wind force (will be normalized)")]
        public Vector3 windDirection = Vector3.right;
        [Tooltip("Constant force applied in wind direction (Newtons)")]
        public float windForce = 2000f;
    }
}
