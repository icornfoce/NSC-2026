using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// พายุทอร์นาโด — ดูดโครงสร้างเข้าหาจุดศูนย์กลางแล้วยกขึ้น + หมุน
    /// ใช้ tornadoSpinSpeed, tornadoPullForce, tornadoLiftForce
    /// VFX Prefab ควรเป็น Tornado effect (Particle System หรือ Model)
    /// </summary>
    public class TornadoDisaster : DisasterBase
    {
        private Vector3 _tornadoCenter;
        private float _moveAngle;

        public TornadoDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            _tornadoCenter = data.centerOffset;
            _moveAngle = 0f;
        }

        protected override void OnUpdate(float dt)
        {
            // Tornado เคลื่อนที่วนๆ
            _moveAngle += dt * 0.5f;
            float moveRadius = 5f;
            _tornadoCenter = data.centerOffset + new Vector3(
                Mathf.Cos(_moveAngle) * moveRadius,
                0f,
                Mathf.Sin(_moveAngle) * moveRadius
            );

            // ขยับ VFX ตาม
            if (spawnedVFX.Count > 0 && spawnedVFX[0] != null)
            {
                spawnedVFX[0].transform.position = _tornadoCenter;
                spawnedVFX[0].transform.Rotate(Vector3.up, data.tornadoSpinSpeed * 10f * dt);
            }

            float effectRadius = data.radius > 0f ? data.radius : 10f;

            // ดึงโครงสร้าง
            var structures = GetStructuresInRadius(_tornadoCenter, effectRadius);
            foreach (var unit in structures)
            {
                if (unit == null) continue;
                Rigidbody rb = unit.GetComponent<Rigidbody>();
                if (rb == null || rb.isKinematic) continue;

                Vector3 toCenter = _tornadoCenter - unit.transform.position;
                float dist = toCenter.magnitude;
                if (dist < 0.1f) continue;

                // แรงดึงเข้าศูนย์กลาง (ยิ่งใกล้ยิ่งแรง)
                float pullStrength = data.tornadoPullForce * (1f - (dist / effectRadius));
                rb.AddForce(toCenter.normalized * pullStrength, ForceMode.Force);

                // แรงยกขึ้น
                rb.AddForce(Vector3.up * data.tornadoLiftForce, ForceMode.Force);

                // แรงหมุน (tangential force — ตั้งฉากกับทิศทางเข้าศูนย์)
                Vector3 tangent = Vector3.Cross(Vector3.up, toCenter.normalized);
                rb.AddForce(tangent * data.tornadoSpinSpeed, ForceMode.Force);

                // Torque สุ่ม
                rb.AddTorque(Random.insideUnitSphere * data.tornadoSpinSpeed * 0.5f, ForceMode.Force);

                // ดาเมจ
                DamageStructure(unit, data.damagePerSecond * dt);
            }

            // ดึงคนด้วย
            var people = GetAllPeople();
            foreach (var person in people)
            {
                if (person == null) continue;
                float dist = Vector3.Distance(person.transform.position, _tornadoCenter);
                if (dist > effectRadius) continue;

                Rigidbody prb = person.GetComponent<Rigidbody>();
                if (prb != null)
                {
                    Vector3 toCenter = (_tornadoCenter - person.transform.position).normalized;
                    float pull = data.tornadoPullForce * 0.5f * (1f - (dist / effectRadius));
                    prb.AddForce(toCenter * pull + Vector3.up * data.tornadoLiftForce * 0.3f, ForceMode.Force);
                }

                if (data.peopleDamagePerSecond > 0f)
                {
                    DamagePerson(person, data.peopleDamagePerSecond * dt);
                }
            }

            // Camera shake
            BuildingSystem.Instance?.TriggerCameraShake(data.intensity * 0.1f);
        }
    }
}
