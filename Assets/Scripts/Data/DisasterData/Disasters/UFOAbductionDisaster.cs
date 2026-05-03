using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// UFO ดูดของ — เลือกโครงสร้างสุ่มแล้วยกขึ้นฟ้า
    /// ใช้ ufoMaxTargets เป็นจำนวนเป้าหมาย, ufoLiftSpeed เป็นความเร็ว
    /// VFX Prefab ควรเป็น UFO Model ที่บินอยู่เหนือตึก
    /// </summary>
    public class UFOAbductionDisaster : DisasterBase
    {
        private List<Rigidbody> _targets = new List<Rigidbody>();
        private float _pickTimer;

        public UFOAbductionDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            _pickTimer = 0f;
            PickTargets();
        }

        private void PickTargets()
        {
            _targets.Clear();
            var structures = GetStructuresInRadius(data.centerOffset, data.radius);
            
            // สุ่มเลือก
            int count = Mathf.Min(data.ufoMaxTargets, structures.Count);
            var shuffled = new List<StructureUnit>(structures);
            
            // Fisher-Yates shuffle
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var temp = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = temp;
            }

            for (int i = 0; i < count; i++)
            {
                if (shuffled[i] == null) continue;
                Rigidbody rb = shuffled[i].GetComponent<Rigidbody>();
                if (rb != null)
                {
                    _targets.Add(rb);
                }
            }
        }

        protected override void OnUpdate(float dt)
        {
            // เลือกเป้าใหม่ทุก 3 วินาที
            _pickTimer += dt;
            if (_pickTimer >= 3f)
            {
                _pickTimer = 0f;
                PickTargets();
            }

            // ดูดขึ้น
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                if (_targets[i] == null)
                {
                    _targets.RemoveAt(i);
                    continue;
                }

                Rigidbody rb = _targets[i];
                if (rb.isKinematic) continue;

                // แรงยก + แรงดูดเข้าหาจุดศูนย์กลาง
                rb.AddForce(Vector3.up * data.ufoLiftSpeed * rb.mass, ForceMode.Force);

                // ใส่ดาเมจ
                var unit = rb.GetComponent<StructureUnit>();
                if (unit != null)
                {
                    DamageStructure(unit, data.damagePerSecond * dt);
                }
            }

            // UFO VFX ลอยอยู่เหนือตึก
            if (spawnedVFX.Count > 0 && spawnedVFX[0] != null)
            {
                Vector3 ufoPos = spawnedVFX[0].transform.position;
                ufoPos.y = 20f; // ลอยสูง
                
                // หมุน UFO ช้าๆ
                spawnedVFX[0].transform.position = ufoPos;
                spawnedVFX[0].transform.Rotate(Vector3.up, 30f * dt);
            }
        }

        protected override void OnStop()
        {
            _targets.Clear();
        }
    }
}
