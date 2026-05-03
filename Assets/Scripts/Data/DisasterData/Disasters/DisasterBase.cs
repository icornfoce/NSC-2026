using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Character;

namespace Simulation.Mission
{
    /// <summary>
    /// Base class สำหรับ Disaster ทุกประเภท
    /// จัดการ lifecycle: Start → Execute (coroutine) → End
    /// </summary>
    public abstract class DisasterBase
    {
        protected DisasterData data;
        protected MonoBehaviour runner;           // ใช้สำหรับ StartCoroutine
        protected List<GameObject> spawnedVFX = new List<GameObject>();
        protected AudioSource audioSource;
        protected bool isRunning;

        public DisasterData Data => data;
        public bool IsRunning => isRunning;

        public DisasterBase(DisasterData data, MonoBehaviour runner)
        {
            this.data = data;
            this.runner = runner;
        }

        /// <summary>
        /// เริ่มภัยพิบัติ — spawn VFX, เล่นเสียง, และเริ่ม coroutine
        /// </summary>
        public void Start()
        {
            isRunning = true;

            // Spawn VFX
            if (data.vfxPrefab != null)
            {
                GameObject vfx = Object.Instantiate(data.vfxPrefab, data.centerOffset, Quaternion.identity);
                spawnedVFX.Add(vfx);
            }

            // Play SFX
            if (data.sfx != null)
            {
                GameObject sfxObj = new GameObject($"SFX_{data.disasterName}");
                audioSource = sfxObj.AddComponent<AudioSource>();
                audioSource.clip = data.sfx;
                audioSource.loop = true;
                audioSource.Play();
            }

            OnStart();
            runner.StartCoroutine(ExecuteCoroutine());
        }

        /// <summary>
        /// หยุดภัยพิบัติ — cleanup ทุกอย่าง
        /// </summary>
        public void Stop()
        {
            isRunning = false;
            OnStop();

            // Cleanup VFX
            foreach (var vfx in spawnedVFX)
            {
                if (vfx != null) Object.Destroy(vfx);
            }
            spawnedVFX.Clear();

            // Cleanup SFX
            if (audioSource != null)
            {
                audioSource.Stop();
                Object.Destroy(audioSource.gameObject);
                audioSource = null;
            }
        }

        private IEnumerator ExecuteCoroutine()
        {
            float elapsed = 0f;
            while (elapsed < data.duration && isRunning)
            {
                float dt = Time.deltaTime;
                OnUpdate(dt);
                elapsed += dt;
                yield return null;
            }

            Stop();
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// หา StructureUnit ทั้งหมดในฉาก
        /// </summary>
        protected StructureUnit[] GetAllStructures()
        {
            return Object.FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// หา StructureUnit ในรัศมีที่กำหนด (0 = ทั้งหมด)
        /// </summary>
        protected List<StructureUnit> GetStructuresInRadius(Vector3 center, float radius)
        {
            var all = GetAllStructures();
            var result = new List<StructureUnit>();
            foreach (var unit in all)
            {
                if (unit == null) continue;
                if (radius <= 0f || Vector3.Distance(unit.transform.position, center) <= radius)
                {
                    result.Add(unit);
                }
            }
            return result;
        }

        /// <summary>
        /// หา PersonAI ทั้งหมดในฉาก
        /// </summary>
        protected PersonAI[] GetAllPeople()
        {
            return Object.FindObjectsByType<PersonAI>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// ใส่ดาเมจให้โครงสร้างผ่าน StructuralStress
        /// </summary>
        protected void DamageStructure(StructureUnit unit, float damage)
        {
            if (unit == null) return;
            var stress = unit.GetComponent<Simulation.Physics.StructuralStress>();
            if (stress != null)
            {
                stress.ApplyExternalDamage(damage);
            }
        }

        /// <summary>
        /// ใส่ดาเมจให้คน
        /// </summary>
        protected void DamagePerson(PersonAI person, float damage)
        {
            if (person != null)
            {
                person.TakeDamage(damage);
            }
        }

        // ── Abstract methods ─────────────────────────────────────────────

        protected virtual void OnStart() { }
        protected abstract void OnUpdate(float dt);
        protected virtual void OnStop() { }
    }
}
