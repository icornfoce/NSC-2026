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
        public bool HasReachedTarget { get; private set; } = false;

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

                    // ปรับแต่งค่า Agent ให้ลอดช่องแคบและขึ้นที่ชันได้ดีขึ้น
                    _agent.radius = 0.3f;     // เล็กลงเพื่อให้ลอดประตูได้
                    _agent.height = 1.8f;     // ความสูงมาตรฐาน
                    _agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance; // ลดการเบียดกันเองจนติด

                    // ปิดระบบฟิสิกส์ระหว่างเดินด้วย NavMesh เพื่อไม่ให้เกิดแรงมหาศาลไปผลักสิ่งก่อสร้างจนพัง และไม่ให้ติดขอบบันได
                    if (_rb != null) _rb.isKinematic = true;
                    var col = GetComponent<CapsuleCollider>();
                    if (col != null) col.isTrigger = true;
                }
                else
                {
                    _agent.enabled = false;
                    if (_rb != null) _rb.isKinematic = false;
                    var col = GetComponent<CapsuleCollider>();
                    if (col != null) col.isTrigger = false;
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
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.SetDestination(_target.position);
                
                // เช็คว่าถึงเป้าหมายหรือยัง
                if (!_agent.pathPending && _agent.remainingDistance <= arrivalDistance)
                {
                    HasReachedTarget = true;
                    // ถึงแล้ว ซ่อนเป้าหมายทิ้ง
                    if (_target.gameObject.activeSelf)
                    {
                        _target.gameObject.SetActive(false);
                    }
                }
            }
            else if (_agent != null && _agent.enabled && !_agent.isOnNavMesh)
            {
                // พื้น NavMesh หายไป (เช่น พื้นถล่ม) ปิด Agent และเปิดฟิสิกส์ให้ร่วง
                _agent.enabled = false;
                if (_rb != null) _rb.isKinematic = false;
                var col = GetComponent<CapsuleCollider>();
                if (col != null) col.isTrigger = false;
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

        private void OnTriggerEnter(Collider other)
        {
            if (_isDead) return;

            // ขณะที่กำลังเดิน (isTrigger = true) ให้รับความเสียหายจากสิ่งของที่ร่วงลงมาโดน
            Rigidbody otherRb = other.attachedRigidbody;
            if (otherRb != null)
            {
                float impact = otherRb.linearVelocity.magnitude;
                if (impact > damageImpactThreshold)
                {
                    TakeDamage(impact * 5f);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // รับความเสียหายเมื่อฟิสิกส์ทำงานปกติ (เช่น ตกจากที่สูงตอนพื้นพัง)
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
