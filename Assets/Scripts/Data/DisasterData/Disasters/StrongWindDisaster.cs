using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// ลมแรง — ผลักโครงสร้างทั้งหมดในทิศทางที่กำหนด
    /// ใช้ windDirection เป็นทิศทาง, intensity เป็นแรงลม
    /// </summary>
    public class StrongWindDisaster : DisasterBase
    {
        public StrongWindDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnUpdate(float dt)
        {
            Vector3 force = data.windDirection.normalized * data.intensity;

            var structures = GetStructuresInRadius(data.centerOffset, data.radius);
            foreach (var unit in structures)
            {
                if (unit == null) continue;
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    rb.AddForce(force, ForceMode.Force);

                    // ลมกระแทกแรงเป็นช่วง (gust)
                    if (Random.value < 0.05f)
                    {
                        rb.AddForce(force * 3f, ForceMode.Impulse);
                    }
                }

                // ดาเมจต่อเนื่อง
                DamageStructure(unit, data.damagePerSecond * dt);
            }

            // คนก็โดนลมพัด
            var people = GetAllPeople();
            foreach (var person in people)
            {
                if (person == null) continue;
                Rigidbody prb = person.GetComponent<Rigidbody>();
                if (prb != null)
                {
                    prb.AddForce(force * 0.3f, ForceMode.Force);
                }
                if (data.peopleDamagePerSecond > 0f)
                {
                    DamagePerson(person, data.peopleDamagePerSecond * dt);
                }
            }
        }
    }
}
