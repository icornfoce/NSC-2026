using UnityEngine;

namespace Simulation.Mission
{
    /// <summary>
    /// ประเภทภัยพิบัติที่รองรับ
    /// </summary>
    public enum DisasterType
    {
        Earthquake,     // แผ่นดินไหว
        Flood,          // น้ำท่วม
        StrongWind,     // ลมแรง
        UFOAbduction,   // UFO ดูดของ
        DragonFire,     // มังกรพ่นไฟ
        AcidRain,       // ฝนกรด
        Tornado         // พายุทอร์นาโด
    }

    /// <summary>
    /// ScriptableObject สำหรับกำหนดค่าภัยพิบัติแต่ละแบบ
    /// สร้างจาก Create > Simulation > Disaster Data
    /// </summary>
    [CreateAssetMenu(fileName = "New Disaster", menuName = "Simulation/Disaster Data")]
    public class DisasterData : ScriptableObject
    {
        [Header("General")]
        public string disasterName;
        public DisasterType disasterType;

        [Header("Timing")]
        [Tooltip("ระยะเวลาที่ภัยพิบัตินี้ทำงาน (วินาที)")]
        public float duration = 10f;

        [Header("Intensity")]
        [Tooltip("ความรุนแรง (ใช้ต่างกันตาม DisasterType เช่น แรงสั่น, ความเร็วลม, รัศมี)")]
        [Range(0.1f, 100f)]
        public float intensity = 10f;

        [Tooltip("รัศมีผลกระทบ (0 = ทั้งแผนที่)")]
        public float radius = 0f;

        [Tooltip("ตำแหน่งศูนย์กลาง Offset จาก Grid (0,0,0 = กลาง Grid)")]
        public Vector3 centerOffset = Vector3.zero;

        [Header("Damage")]
        [Tooltip("ดาเมจต่อวินาทีที่ใส่ให้โครงสร้าง")]
        public float damagePerSecond = 5f;

        [Tooltip("ดาเมจต่อวินาทีที่ใส่ให้คน")]
        public float peopleDamagePerSecond = 0f;

        [Header("Visual / Audio")]
        [Tooltip("Prefab ที่จะ Spawn เป็น VFX ของภัยพิบัติ (เช่น น้ำ, ไฟ, UFO)")]
        public GameObject vfxPrefab;

        [Tooltip("เสียงของภัยพิบัติ")]
        public AudioClip sfx;

        [Header("Type-Specific Settings")]
        [Tooltip("Earthquake: ความถี่ของการสั่น (ครั้ง/วินาที)")]
        public float shakeFrequency = 20f;

        [Tooltip("Flood: ความสูงน้ำสูงสุด (เมตร)")]
        public float floodMaxHeight = 3f;

        [Tooltip("StrongWind: ทิศทางลม (normalized)")]
        public Vector3 windDirection = Vector3.right;

        [Tooltip("UFO: จำนวนของที่จะดูดสูงสุด")]
        public int ufoMaxTargets = 3;

        [Tooltip("UFO: ความเร็วในการยกขึ้น")]
        public float ufoLiftSpeed = 5f;

        [Tooltip("DragonFire: Prefab มังกร (จะบินเข้ามาและพ่นไฟ)")]
        public GameObject dragonPrefab;

        [Tooltip("DragonFire: Prefab ลูกไฟ")]
        public GameObject fireballPrefab;

        [Tooltip("AcidRain: Prefab ฝนกรด (Particle System)")]
        public GameObject acidRainParticle;

        [Tooltip("Tornado: ความเร็วในการหมุน")]
        public float tornadoSpinSpeed = 10f;

        [Tooltip("Tornado: แรงดึงเข้าหาศูนย์กลาง")]
        public float tornadoPullForce = 15f;

        [Tooltip("Tornado: แรงยกขึ้น")]
        public float tornadoLiftForce = 8f;
    }
}
