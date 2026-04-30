using UnityEngine;

namespace Simulation.Character
{
    /// <summary>
    /// สคริปต์สำหรับกำหนดจุดเกิดของตัวละคร (Spawn Point)
    /// นำไปแปะไว้ที่ GameObject ในฉากที่เป็นจุดเริ่มต้น (เช่น ประตูทางเข้า)
    /// </summary>
    public class PersonSpawner : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
        }
    }
}
