using UnityEngine;
using UnityEngine.AI;
using BuildingSimulation.Data;
using BuildingSimulation.Building;

namespace BuildingSimulation.Physics
{
    /// <summary>
    /// Human NPC: supports custom model prefab or default Capsule.
    /// Random-walks on NavMesh to create dynamic load and vibrations.
    /// Supports ambient sound and persistent visual effects via NPCData.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HumanNPC : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float wanderRadius = 10f;
        [SerializeField] private float wanderInterval = 3f;
        [SerializeField] private float moveSpeed = 2f;

        [Header("Physics")]
        [SerializeField] private float mass = 50f;

        [Header("Data")]
        [SerializeField] private NPCData npcData;

        private NavMeshAgent _agent;
        private Rigidbody _rb;
        private float _wanderTimer;
        private bool _isActive;

        private AudioSource _ambientSource;
        private GameObject _persistentEffect;

        public NPCData Data => npcData;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _agent = GetComponent<NavMeshAgent>();

            // Configure rigidbody
            _rb.mass = mass;
            _rb.useGravity = true;
            _rb.isKinematic = false;

            // Configure agent if present
            if (_agent != null)
            {
                _agent.speed = moveSpeed;
                _agent.enabled = false;
            }

            _isActive = false;
        }

        /// <summary>
        /// Initialize from NPCData (called after CreateNPC).
        /// </summary>
        public void Initialize(NPCData data)
        {
            npcData = data;
            if (data == null) return;

            moveSpeed = data.moveSpeed;
            wanderRadius = data.wanderRadius;
            wanderInterval = data.wanderInterval;
            mass = data.mass;

            _rb.mass = mass;
            if (_agent != null) _agent.speed = moveSpeed;

            // Setup ambient sound
            if (data.ambientSound != null && SoundManager.Instance != null)
            {
                _ambientSource = SoundManager.Instance.PlayLoop(
                    data.ambientSound, transform, data.ambientVolume);
            }

            // Setup persistent effect
            if (data.persistentEffectPrefab != null && EffectManager.Instance != null)
            {
                _persistentEffect = EffectManager.Instance.SpawnPersistentEffect(
                    data.persistentEffectPrefab, transform);
            }
        }

        /// <summary>
        /// Call to begin NPC random-walking.
        /// </summary>
        public void Activate()
        {
            _isActive = true;
            if (_agent != null)
            {
                _agent.enabled = true;
                _wanderTimer = 0f;
                PickNewDestination();
            }
        }

        /// <summary>
        /// Stop NPC movement.
        /// </summary>
        public void Deactivate()
        {
            _isActive = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.enabled = false;
            }
        }

        private void Update()
        {
            if (!_isActive || _agent == null || !_agent.enabled) return;

            _wanderTimer += Time.deltaTime;
            if (_wanderTimer >= wanderInterval)
            {
                _wanderTimer = 0f;
                PickNewDestination();
            }
        }

        private void PickNewDestination()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            Vector3 randomDir = Random.insideUnitSphere * wanderRadius;
            randomDir += transform.position;

            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
            }
        }

        private void OnDestroy()
        {
            // Cleanup ambient sound
            if (_ambientSource != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.StopLoop(_ambientSource);
            }

            // Cleanup persistent effect
            if (_persistentEffect != null && EffectManager.Instance != null)
            {
                EffectManager.Instance.StopEffect(_persistentEffect);
            }
        }

        // ─── Factory Methods ─────────────────────────────────────────

        /// <summary>
        /// Creates a Human NPC from NPCData (custom prefab or default capsule).
        /// </summary>
        public static HumanNPC CreateNPC(Vector3 position, NPCData data)
        {
            GameObject npcObj;

            if (data != null && data.modelPrefab != null)
            {
                // Use custom model prefab
                npcObj = Instantiate(data.modelPrefab, position, Quaternion.identity);
            }
            else
            {
                // Default capsule
                npcObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                npcObj.transform.position = position;

                // Color it
                var renderer = npcObj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Color col = (data != null) ? data.modelColor : new Color(0.2f, 0.6f, 1f, 1f);
                    renderer.material.color = col;
                }
            }

            npcObj.name = (data != null) ? "NPC_" + data.npcName : "HumanNPC";

            // Add Rigidbody if not present
            var rb = npcObj.GetComponent<Rigidbody>();
            if (rb == null) rb = npcObj.AddComponent<Rigidbody>();
            rb.mass = (data != null) ? data.mass : 50f;
            rb.useGravity = true;

            // Add NavMeshAgent if not present
            var agent = npcObj.GetComponent<NavMeshAgent>();
            if (agent == null) agent = npcObj.AddComponent<NavMeshAgent>();
            agent.speed = (data != null) ? data.moveSpeed : 2f;
            agent.height = 2f;
            agent.radius = 0.5f;

            // Add HumanNPC component
            var npc = npcObj.GetComponent<HumanNPC>();
            if (npc == null) npc = npcObj.AddComponent<HumanNPC>();
            npc.Initialize(data);

            return npc;
        }

        /// <summary>
        /// Creates a default Human NPC (no NPCData, backward compatible).
        /// </summary>
        public static HumanNPC CreateNPC(Vector3 position)
        {
            return CreateNPC(position, null);
        }
    }
}
