using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// แผ่นดินไหว — สั่นโครงสร้างทั้งหมดด้วยแรง impulse สุ่ม
    /// ใช้ intensity เป็นแรงสั่น, shakeFrequency เป็นความถี่
    /// </summary>
    public class EarthquakeDisaster : DisasterBase
    {
        private float _shakeTimer;

        public EarthquakeDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            // Camera shake
            BuildingSystem.Instance?.TriggerCameraShake(data.intensity * 0.5f);
        }

        protected override void OnUpdate(float dt)
        {
            _shakeTimer += dt;
            float interval = data.shakeFrequency > 0f ? 1f / data.shakeFrequency : 0.05f;

            if (_shakeTimer >= interval)
            {
                _shakeTimer -= interval;

                var structures = GetStructuresInRadius(data.centerOffset, data.radius);
                foreach (var unit in structures)
                {
                    if (unit == null) continue;
                    Rigidbody rb = unit.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        // สั่นในทุกทิศทาง
                        Vector3 shakeForce = Random.insideUnitSphere * data.intensity;
                        rb.AddForce(shakeForce, ForceMode.Impulse);
                    }

                    // ใส่ดาเมจ
                    DamageStructure(unit, data.damagePerSecond * interval);
                }

                // Camera shake ต่อเนื่อง
                BuildingSystem.Instance?.TriggerCameraShake(data.intensity * 0.2f);
            }

            // ใส่ดาเมจคน
            if (data.peopleDamagePerSecond > 0f)
            {
                var people = GetAllPeople();
                foreach (var person in people)
                {
                    DamagePerson(person, data.peopleDamagePerSecond * dt);
                }
            }
        }
    }
}
