using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// น้ำท่วม — น้ำค่อยๆ สูงขึ้น ใส่ดาเมจโครงสร้างที่อยู่ใต้น้ำ
    /// ใช้ floodMaxHeight เป็นระดับน้ำสูงสุด, intensity เป็นความเร็วที่น้ำขึ้น
    /// VFX Prefab ควรเป็น Water Plane ที่ scale ได้
    /// </summary>
    public class FloodDisaster : DisasterBase
    {
        private float _currentWaterLevel;
        private Transform _waterTransform;

        public FloodDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            _currentWaterLevel = 0f;

            // หา Water VFX ที่ spawn มาแล้ว (จาก base class)
            if (spawnedVFX.Count > 0 && spawnedVFX[0] != null)
            {
                _waterTransform = spawnedVFX[0].transform;
                _waterTransform.position = new Vector3(data.centerOffset.x, 0f, data.centerOffset.z);
            }
        }

        protected override void OnUpdate(float dt)
        {
            // น้ำค่อยๆ สูงขึ้น
            float riseSpeed = data.floodMaxHeight / data.duration;
            _currentWaterLevel += riseSpeed * dt;
            _currentWaterLevel = Mathf.Min(_currentWaterLevel, data.floodMaxHeight);

            // ขยับ Water VFX ขึ้น
            if (_waterTransform != null)
            {
                Vector3 pos = _waterTransform.position;
                pos.y = _currentWaterLevel;
                _waterTransform.position = pos;
            }

            // ใส่ดาเมจโครงสร้างที่อยู่ใต้ระดับน้ำ
            var structures = GetStructuresInRadius(data.centerOffset, data.radius);
            foreach (var unit in structures)
            {
                if (unit == null) continue;
                if (unit.transform.position.y < _currentWaterLevel)
                {
                    DamageStructure(unit, data.damagePerSecond * dt);

                    // แรงดันน้ำผลักโครงสร้าง
                    Rigidbody rb = unit.GetComponent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        float submergedDepth = _currentWaterLevel - unit.transform.position.y;
                        float buoyancy = Mathf.Clamp01(submergedDepth / data.floodMaxHeight);
                        rb.AddForce(Vector3.up * buoyancy * data.intensity, ForceMode.Force);
                    }
                }
            }

            // ใส่ดาเมจคนที่อยู่ใต้น้ำ
            var people = GetAllPeople();
            foreach (var person in people)
            {
                if (person != null && person.transform.position.y < _currentWaterLevel)
                {
                    DamagePerson(person, data.peopleDamagePerSecond * dt);
                }
            }
        }
    }
}
