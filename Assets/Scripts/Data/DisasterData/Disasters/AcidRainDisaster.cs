using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// ฝนกรด — ดาเมจต่อเนื่องให้กับโครงสร้างทั้งหมดจากด้านบน
    /// ชิ้นส่วนที่อยู่ชั้นบนจะโดนดาเมจมากกว่าชิ้นที่อยู่ด้านล่าง (มีหลังคาบัง)
    /// VFX ควรเป็น Particle System ฝนตก
    /// </summary>
    public class AcidRainDisaster : DisasterBase
    {
        public AcidRainDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            // Spawn Particle System ฝนกรด
            if (data.acidRainParticle != null)
            {
                Vector3 rainPos = data.centerOffset + Vector3.up * 25f;
                GameObject rain = Object.Instantiate(data.acidRainParticle, rainPos, Quaternion.identity);
                spawnedVFX.Add(rain);
            }
        }

        protected override void OnUpdate(float dt)
        {
            var structures = GetStructuresInRadius(data.centerOffset, data.radius);
            
            // หา Y สูงสุดเพื่อคำนวณว่าชิ้นไหนโดนฝนเต็มๆ
            float maxY = 0f;
            foreach (var unit in structures)
            {
                if (unit == null) continue;
                if (unit.transform.position.y > maxY) maxY = unit.transform.position.y;
            }

            float safeMaxY = maxY > 0f ? maxY : 1f;

            foreach (var unit in structures)
            {
                if (unit == null) continue;

                // ชิ้นที่อยู่สูงสุดโดนดาเมจเต็ม, ชิ้นที่อยู่ล่างโดนน้อยลง (มีหลังคาบัง)
                float exposureRatio = Mathf.Clamp01(unit.transform.position.y / safeMaxY);
                float exposure = Mathf.Lerp(0.2f, 1.0f, exposureRatio);

                DamageStructure(unit, data.damagePerSecond * exposure * dt);
            }

            // คนโดนฝนกรด
            if (data.peopleDamagePerSecond > 0f)
            {
                var people = GetAllPeople();
                foreach (var person in people)
                {
                    if (person == null) continue;

                    // เช็คว่ามีหลังคาบังไหม (Raycast ขึ้นไป)
                    bool hasCover = UnityEngine.Physics.Raycast(
                        person.transform.position,
                        Vector3.up,
                        20f,
                        LayerMask.GetMask("Structure") // สมมุติ Layer ชื่อ Structure
                    );

                    if (!hasCover)
                    {
                        DamagePerson(person, data.peopleDamagePerSecond * dt);
                    }
                }
            }
        }
    }
}
