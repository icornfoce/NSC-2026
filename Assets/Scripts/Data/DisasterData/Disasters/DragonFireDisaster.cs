using UnityEngine;
using Simulation.Building;

namespace Simulation.Mission
{
    /// <summary>
    /// มังกรพ่นไฟ — มังกรบินผ่านและพ่นลูกไฟใส่โครงสร้าง
    /// ใช้ dragonPrefab สำหรับโมเดลมังกร, fireballPrefab สำหรับลูกไฟ
    /// intensity เป็นดาเมจลูกไฟ
    /// </summary>
    public class DragonFireDisaster : DisasterBase
    {
        private GameObject _dragon;
        private float _fireTimer;
        private float _flyT; // 0-1 progress ของการบินผ่าน

        public DragonFireDisaster(DisasterData data, MonoBehaviour runner) : base(data, runner) { }

        protected override void OnStart()
        {
            _fireTimer = 0f;
            _flyT = 0f;

            // Spawn มังกร
            if (data.dragonPrefab != null)
            {
                Vector3 startPos = data.centerOffset + new Vector3(-30f, 15f, 0f);
                _dragon = Object.Instantiate(data.dragonPrefab, startPos, Quaternion.identity);
                spawnedVFX.Add(_dragon);
            }
        }

        protected override void OnUpdate(float dt)
        {
            // มังกรบินผ่าน
            _flyT += dt / data.duration;
            _flyT = Mathf.Clamp01(_flyT);

            if (_dragon != null)
            {
                // บินจากซ้ายไปขวา + วนขึ้นลง
                Vector3 startPos = data.centerOffset + new Vector3(-30f, 15f, 0f);
                Vector3 endPos = data.centerOffset + new Vector3(30f, 15f, 0f);
                Vector3 pos = Vector3.Lerp(startPos, endPos, _flyT);
                pos.y += Mathf.Sin(_flyT * Mathf.PI * 3f) * 3f; // บินขึ้นลง
                _dragon.transform.position = pos;

                // หันหน้าไปทิศที่บิน
                Vector3 dir = (endPos - startPos).normalized;
                if (dir != Vector3.zero)
                {
                    _dragon.transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            // ยิงลูกไฟ
            _fireTimer += dt;
            float fireInterval = 1.5f; // ยิงทุก 1.5 วินาที
            if (_fireTimer >= fireInterval)
            {
                _fireTimer -= fireInterval;
                ShootFireball();
            }

            // ดาเมจต่อเนื่องทั้ง field
            if (data.damagePerSecond > 0f)
            {
                var structures = GetStructuresInRadius(data.centerOffset, data.radius);
                foreach (var unit in structures)
                {
                    // ดาเมจเฉพาะตรงที่ลูกไฟลง (ลดลง 1/3 สำหรับ field damage)
                    DamageStructure(unit, data.damagePerSecond * dt * 0.3f);
                }
            }
        }

        private void ShootFireball()
        {
            if (_dragon == null) return;

            // สร้างลูกไฟ
            Vector3 firePos = _dragon.transform.position + Vector3.down * 2f;

            if (data.fireballPrefab != null)
            {
                GameObject fireball = Object.Instantiate(data.fireballPrefab, firePos, Quaternion.identity);
                Rigidbody rb = fireball.GetComponent<Rigidbody>();
                if (rb == null) rb = fireball.AddComponent<Rigidbody>();
                rb.useGravity = true;

                // ใส่แรงลงและไปข้างหน้าเล็กน้อย
                rb.AddForce(Vector3.down * 5f + _dragon.transform.forward * 3f, ForceMode.Impulse);

                // ทำลายลูกไฟหลัง 5 วินาที
                Object.Destroy(fireball, 5f);
            }

            // ใส่ดาเมจรัศมีรอบจุดที่มังกรอยู่
            var structures = GetStructuresInRadius(firePos, data.radius > 0f ? data.radius : 5f);
            foreach (var unit in structures)
            {
                DamageStructure(unit, data.intensity);
            }

            // Camera shake
            BuildingSystem.Instance?.TriggerCameraShake(data.intensity * 0.1f);
        }
    }
}
