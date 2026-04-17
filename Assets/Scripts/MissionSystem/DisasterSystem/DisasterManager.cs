using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Simulation.Data;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// Manager สำหรับควบคุมการเกิดภัยพิบัติ (Disasters) ตามที่กำหนดใน MissionData
    /// </summary>
    public class DisasterManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool showDebug = true;

        private List<Coroutine> _activeDisasters = new List<Coroutine>();

        /// <summary>
        /// เริ่มต้นการทำงานของภัยพิบัติทั้งหมดที่ได้รับมา
        /// เรียกใช้โดย MissionSystem เมื่อเริ่มการจำลอง
        /// </summary>
        public void ExecuteDisasters(List<DisasterData> disasters)
        {
            if (disasters == null || disasters.Count == 0) return;

            foreach (var disaster in disasters)
            {
                if (disaster == null) continue;
                _activeDisasters.Add(StartCoroutine(ProcessDisaster(disaster)));
            }
        }

        /// <summary>
        /// ยกเลิกภัยพิบัติทั้งหมดที่กำลังทำงานอยู่
        /// เรียกใช้เมื่อ Reset ภารกิจ หรือจบการจำลอง
        /// </summary>
        public void CancelDisasters()
        {
            foreach (var coroutine in _activeDisasters)
            {
                if (coroutine != null) StopCoroutine(coroutine);
            }
            _activeDisasters.Clear();
            
            if (showDebug) Debug.Log("[DisasterManager] ยกเลิกภัยพิบัติทั้งหมดแล้ว");
        }

        private IEnumerator ProcessDisaster(DisasterData data)
        {
            if (showDebug) Debug.Log($"[DisasterManager] เริ่มภัยพิบัติ: <color=orange>{data.disasterName}</color> (ประเภท: {data.type})");

            // เล่นเสียง (ถ้ามี)
            if (data.disasterSound != null)
            {
                // เล่นเสียงที่ตำแหน่งกล้องเพื่อให้ได้ยินชัดเจน
                AudioSource.PlayClipAtPoint(data.disasterSound, UnityEngine.Camera.main.transform.position, 1.0f);
            }

            // แสดง VFX (ถ้ามี) - สามารถปรับแก้ให้ Spawn ตามจุดที่เหมาะสมได้
            GameObject vfxInstance = null;
            if (data.disasterVFX != null)
            {
                vfxInstance = Instantiate(data.disasterVFX, Vector3.zero, Quaternion.identity);
            }

            // แคชรายการสิ่งก่อสร้างในจังหวะที่เริ่ม (เพื่อลดภาระ CPU ในแต่ละเฟรม)
            StructureUnit[] cachedUnits = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);

            float elapsed = 0f;
            while (elapsed < data.duration)
            {
                ApplyDisasterEffect(data, cachedUnits);
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (vfxInstance != null) Destroy(vfxInstance);
            if (showDebug) Debug.Log($"[DisasterManager] ภัยพิบัติสิ้นสุดลง: {data.disasterName}");
        }

        private void ApplyDisasterEffect(DisasterData data, StructureUnit[] units)
        {
            foreach (var unit in units)
            {
                if (unit == null) continue; // ข้ามชิ้นส่วนที่พังไปแล้ว
                
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                bool hasPhysics = rb != null && !rb.isKinematic;

                switch (data.type)
                {
                    case DisasterType.Earthquake:
                        if (hasPhysics)
                        {
                            // แรงสั่นสะเทือนแบบสุ่มทั้ง 3 แกน
                            Vector3 shake = new Vector3(
                                Random.Range(-1f, 1f),
                                Random.Range(0f, 0.5f), // สั่นขึ้นบนเล็กน้อย
                                Random.Range(-1f, 1f)
                            ) * data.intensity;
                            rb.AddForce(shake, ForceMode.Acceleration);
                        }
                        break;

                    case DisasterType.Windy:
                        if (hasPhysics)
                        {
                            // แรงลมผลักไปในทิศทางเดียว (เช่น แกน X)
                            rb.AddForce(Vector3.right * data.intensity, ForceMode.Acceleration);
                        }
                        break;

                    case DisasterType.HeavyLoad:
                        if (hasPhysics)
                        {
                            // จำลองน้ำหนักที่เพิ่มขึ้นโดยใช้แรงกดลง
                            rb.AddForce(Vector3.down * data.intensity, ForceMode.Acceleration);
                        }
                        break;

                    case DisasterType.Fire:
                        // ลด HP ของโครงสร้างโดยตรง (ความเสียหายต่อวินาทีตามระดับ intensity)
                        unit.TakeDamage(data.intensity * Time.deltaTime);
                        break;

                    case DisasterType.Tsunami:
                    case DisasterType.Flood:
                        if (hasPhysics)
                        {
                            // แรงผลักแนวนอนในทิศทาง Z (หรือทิศทางที่คลื่นมา)
                            rb.AddForce(Vector3.forward * data.intensity, ForceMode.Acceleration);
                        }
                        break;

                    case DisasterType.Tornado:
                        if (hasPhysics)
                        {
                            // แรงหมุนวนและแรงดึงขึ้น
                            Vector3 center = Vector3.zero; // สมมติว่าพายุอยู่ตรงกลางแมพ
                            Vector3 toCenter = (center - unit.transform.position).normalized;
                            Vector3 lift = Vector3.up * 0.5f;
                            Vector3 spin = Vector3.Cross(toCenter, Vector3.up);
                            
                            rb.AddForce((spin + lift) * data.intensity, ForceMode.Acceleration);
                        }
                        break;

                    case DisasterType.ToxicRain:
                        // คล้ายกับไฟ แต่รุนแรงน้อยกว่าและส่งผลทุกชิ้นส่วน
                        unit.TakeDamage(data.intensity * 0.5f * Time.deltaTime);
                        break;

                    default:
                        // สำหรับประเภทอื่นๆ เช่น UFO, Dragon อาจจะต้องมีการเขียน Logic เฉพาะทางเพิ่มเติม
                        break;
                }
            }
        }
    }
}
