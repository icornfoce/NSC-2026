using UnityEngine;
using UnityEngine.AI;

namespace Simulation.Character
{
    /// <summary>
    /// AI สำหรับตัวละครให้เดินไปยังเป้าหมายด้วย NavMesh
    /// ถ้าเลือดหมดจะแสดง VFX และตาย
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PersonAI : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("ความเร็วในการเดิน (ควบคุมโดย NavMeshAgent ด้วย)")]
        public float moveSpeed = 2f;
        [Tooltip("ระยะที่ถือว่าถึงเป้าหมายแล้ว")]
        public float arrivalDistance = 0.1f;

        [Header("Health & Death")]
        public float maxHealth = 100f;
        public GameObject deathVFX;
        [Tooltip("ความแรงจากการชนขั้นต่ำที่จะทำให้ลดเลือด")]
        public float damageImpactThreshold = 3f;

        private float _currentHealth;
        private Transform _target;
        private Rigidbody _rb;
        private NavMeshAgent _agent;
        private bool _isDead = false;

        public bool IsDead => _isDead;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _currentHealth = maxHealth;
            
            // ล็อคการหมุนไม่ให้ล้ม
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public void InitializeAgent()
        {
            // เช็คว่าอยู่บน NavMesh หรือไม่ก่อน (ถ้าอยู่ให้ขยับให้ตรง)
            bool isOnNavMesh = UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas);
            if (isOnNavMesh)
            {
                transform.position = hit.position;
            }

            _agent = GetComponent<NavMeshAgent>();
            if (_agent == null && isOnNavMesh)
            {
                // เพิ่มเฉพาะตอนที่ชัวร์ว่ามี NavMesh รองรับแล้ว ป้องกัน Error
                _agent = gameObject.AddComponent<NavMeshAgent>();
            }

            // ตั้งค่า Agent
            if (_agent != null)
            {
                if (isOnNavMesh)
                {
                    _agent.enabled = true;
                    _agent.speed = moveSpeed;
                    _agent.stoppingDistance = arrivalDistance;
                    _agent.updateRotation = true;
                }
                else
                {
                    _agent.enabled = false;
                }
            }
        }

        public void SetTarget(Transform targetTransform)
        {
            _target = targetTransform;
            if (_agent != null && _target != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(_target.position);
            }
        }

        private void Update()
        {
            if (_isDead || _target == null) return;

            // สั่งให้เดินตามเป้าหมายด้วย NavMesh
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.SetDestination(_target.position);
                
                // เช็คว่าถึงเป้าหมายหรือยัง
                if (!_agent.pathPending && _agent.remainingDistance <= arrivalDistance)
                {
                    // ถึงแล้ว ซ่อนเป้าหมายทิ้ง
                    if (_target.gameObject.activeSelf)
                    {
                        _target.gameObject.SetActive(false);
                    }
                }
            }
        }

        public void TakeDamage(float amount)
        {
            if (_isDead) return;

            _currentHealth -= amount;
            if (_currentHealth <= 0)
            {
                Die();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // ถอดความเสียหายจากการโดนของหล่นทับ หรือตกจากที่สูง
            if (collision.relativeVelocity.magnitude > damageImpactThreshold)
            {
                TakeDamage(collision.relativeVelocity.magnitude * 5f);
            }
        }

        private void Die()
        {
            _isDead = true;
            
            if (_agent != null) _agent.enabled = false;
            
            // เล่น VFX
            if (deathVFX != null)
            {
                Instantiate(deathVFX, transform.position, Quaternion.identity);
            }
            
            // ลบตัวละครทิ้ง
            Destroy(gameObject);
        }
    }
}
