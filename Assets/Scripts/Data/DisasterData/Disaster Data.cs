using UnityEngine;

namespace Simulation.Data
{
    public enum DisasterType
    {
        Dragon,
        Earthquake,
        Fire,
        Flood,
        Tornado,
        ToxicRain,
        Tsunami,
        UFO,
        Volcanic,
        Windy,
        HeavyLoad
    }

    [CreateAssetMenu(fileName = "New Disaster Data", menuName = "Simulation/Disaster Data")]
    public class DisasterData : ScriptableObject
    {
        [Header("General Info")]
        public string disasterName;
        public DisasterType type;
        [TextArea] public string description;

        [Header("Parameters")]
        [Tooltip("ความรุนแรงของภัยพิบัติ (เช่น แรงสั่นสะเทือน, แรงลม)")]
        public float intensity = 10f;
        
        [Tooltip("ระยะเวลาที่เกิด (วินาที)")]
        public float duration = 5f;

        [Header("Assets")]
        public AudioClip disasterSound;
        public GameObject disasterVFX;
    }
}
