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
        private float _wanderTimer;
        private Vector3 _wanderTarget;

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

            // เช็คว่ายังมีพื้นรองรับอยู่หรือไม่ (ป้องกันการเดินลอยบนอากาศหากพื้นพังไปแล้วแต่ NavMesh ยังไม่ถูกลบ)
            float rayDist = 0.8f; // ระยะประมาณก้าวขึ้นบันได (0.6) + เผื่อเหลือ (0.2)
            bool hasFloor = UnityEngine.Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, rayDist, UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            // สั่งให้เดินตามเป้าหมายด้วย NavMesh
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh && hasFloor)
            {
                if (HasReachedTarget)
                {
                    // พอถึงเป้าหมายแล้ว ให้เดินวนรอบๆ เป้าหมายแทนการยืนนิ่งๆ
                    _wanderTimer -= Time.deltaTime;
                    if (_wanderTimer <= 0f || (_agent.remainingDistance <= 0.5f && !_agent.pathPending))
                    {
                        Vector2 randomCircle = Random.insideUnitCircle * 3f;
                        Vector3 randomPos = _target.position + new Vector3(randomCircle.x, 0, randomCircle.y);
                        
                        if (UnityEngine.AI.NavMesh.SamplePosition(randomPos, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            _wanderTarget = hit.position;
                        }
                        else
                        {
                            _wanderTarget = _target.position;
                        }
                        
                        _agent.SetDestination(_wanderTarget);
                        _wanderTimer = Random.Range(2f, 5f); // สุ่มจุดเดินใหม่ทุกๆ 2-5 วินาที
                    }
                }
                else
                {
                    _agent.SetDestination(_target.position);
                    
                    // เช็คว่าถึงเป้าหมายหรือยัง
                    if (!_agent.pathPending && _agent.remainingDistance <= arrivalDistance)
                    {
                        HasReachedTarget = true;
                        _wanderTarget = _target.position; // เริ่มเดินวนจากจุดนี้
                    }
                }
            }
            else if (_agent != null && _agent.enabled && (!_agent.isOnNavMesh || !hasFloor))
            {
                // พื้น NavMesh หายไป หรือ ไม่มีพื้นรองรับด้านล่างแล้ว (พื้นถล่ม) 
                // ปิด Agent และเปิดฟิสิกส์ให้ร่วง
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
                // คิดน้ำหนักของสิ่งที่มาชนด้วย (มวลยิ่งเยอะ ยิ่งเจ็บ)
                float impact = otherRb.linearVelocity.magnitude;
                if (impact > damageImpactThreshold)
                {
                    float massFactor = Mathf.Clamp(otherRb.mass, 1f, 500f);
                    TakeDamage(impact * massFactor * 2f);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // รับความเสียหายเมื่อฟิสิกส์ทำงานปกติ (เช่น ตกจากที่สูงตอนพื้นพัง)
            if (collision.relativeVelocity.magnitude > damageImpactThreshold)
            {
                // ถ้าตกพื้นเอง คิดแค่มวลตัวเอง แต่ถ้ามีของหล่นมาทับต่อ ให้คิดมวลของชิ้นนั้น
                float massFactor = 1f;
                if (collision.rigidbody != null)
                {
                    massFactor = Mathf.Clamp(collision.rigidbody.mass, 1f, 500f);
                }
                TakeDamage(collision.relativeVelocity.magnitude * massFactor * 2f);
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
