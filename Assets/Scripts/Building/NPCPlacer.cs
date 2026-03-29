using System.Collections.Generic;
using UnityEngine;
using BuildingSimulation.Data;
using BuildingSimulation.Physics;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Handles manual placement of NPCs with ghost preview.
    /// Supports custom model prefabs, sounds, and visual effects.
    /// </summary>
    public class NPCPlacer : MonoBehaviour
    {
        public static NPCPlacer Instance { get; private set; }

        [Header("Placement Settings")]
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private float maxPlacementDistance = 100f;

        [Header("Ghost Preview")]
        [SerializeField] private Material ghostMaterial;

        [Header("References")]
        [SerializeField] private Transform npcParent;

        // State
        private NPCData _selectedNPCData;
        private GameObject _ghostObject;
        private bool _isPlacing;

        // Track all manually placed NPCs
        private readonly List<HumanNPC> _placedNPCs = new List<HumanNPC>();
        public IReadOnlyList<HumanNPC> PlacedNPCs => _placedNPCs;

        private UnityEngine.Camera _mainCam;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (npcParent == null)
            {
                npcParent = new GameObject("NPCs").transform;
            }
        }

        private void Start()
        {
            _mainCam = UnityEngine.Camera.main;
        }

        private void Update()
        {
            if (_isPlacing && _ghostObject != null)
            {
                UpdateGhostPosition();

                if (Input.GetMouseButtonDown(0))
                {
                    PlaceNPC();
                }
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelPlacement();
                }
            }
        }

        // ─── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Start placing an NPC of the given type. Called from UI.
        /// </summary>
        public void SelectNPC(NPCData data)
        {
            CancelPlacement();
            _selectedNPCData = data;
            _isPlacing = true;
            CreateGhost();
        }

        public void CancelPlacement()
        {
            _isPlacing = false;
            if (_ghostObject != null)
            {
                Destroy(_ghostObject);
                _ghostObject = null;
            }
        }

        /// <summary>
        /// Remove a specific NPC.
        /// </summary>
        public void RemoveNPC(HumanNPC npc)
        {
            if (npc == null) return;

            // Refund cost
            if (_selectedNPCData != null)
            {
                BudgetManager.Instance?.Refund(_selectedNPCData.cost);
            }

            _placedNPCs.Remove(npc);
            Destroy(npc.gameObject);
        }

        /// <summary>
        /// Remove all manually placed NPCs.
        /// </summary>
        public void ClearAllNPCs()
        {
            foreach (var npc in _placedNPCs)
            {
                if (npc != null) Destroy(npc.gameObject);
            }
            _placedNPCs.Clear();
        }

        // ─── Ghost ────────────────────────────────────────────────────

        private void CreateGhost()
        {
            if (_selectedNPCData == null) return;

            // Use custom prefab or default cylinder
            if (_selectedNPCData.modelPrefab != null)
            {
                _ghostObject = Instantiate(_selectedNPCData.modelPrefab);
            }
            else
            {
                _ghostObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }

            _ghostObject.name = "Ghost_NPC_" + _selectedNPCData.npcName;

            // Remove colliders from ghost
            foreach (var col in _ghostObject.GetComponentsInChildren<Collider>())
            {
                Destroy(col);
            }

            // Remove any scripts from ghost prefab
            foreach (var comp in _ghostObject.GetComponentsInChildren<MonoBehaviour>())
            {
                Destroy(comp);
            }

            // Remove Rigidbody from ghost
            foreach (var rb in _ghostObject.GetComponentsInChildren<Rigidbody>())
            {
                Destroy(rb);
            }

            // Apply ghost material / color
            foreach (var renderer in _ghostObject.GetComponentsInChildren<Renderer>())
            {
                if (ghostMaterial != null)
                {
                    renderer.material = new Material(ghostMaterial);
                    renderer.material.color = _selectedNPCData.previewColor;
                }
                else
                {
                    renderer.material.color = _selectedNPCData.previewColor;
                }
            }
        }

        private void UpdateGhostPosition()
        {
            Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);

            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance))
            {
                Vector3 pos = hit.point;

                // Offset up so NPC stands on the surface
                pos.y += 1f;

                // Snap to grid
                if (gridSize > 0)
                {
                    pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
                    pos.z = Mathf.Round(pos.z / gridSize) * gridSize;
                }

                _ghostObject.transform.position = pos;
            }
        }

        // ─── Placement ───────────────────────────────────────────────

        private void PlaceNPC()
        {
            if (_ghostObject == null || _selectedNPCData == null) return;

            // Check budget
            if (!BudgetManager.Instance.CanAfford(_selectedNPCData.cost))
            {
                Debug.LogWarning($"Cannot afford NPC {_selectedNPCData.npcName}! Cost: ${_selectedNPCData.cost:F0}");
                return;
            }

            Vector3 spawnPos = _ghostObject.transform.position;

            // Create the actual NPC
            HumanNPC npc = HumanNPC.CreateNPC(spawnPos, _selectedNPCData);
            if (npc != null)
            {
                npc.transform.SetParent(npcParent);

                // Play placement sound
                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlaySFX(_selectedNPCData.placementSound, spawnPos);
                }

                // Spawn placement effect
                if (EffectManager.Instance != null)
                {
                    EffectManager.Instance.SpawnEffect(_selectedNPCData.placementEffectPrefab, spawnPos);
                }

                // Deduct budget
                BudgetManager.Instance.Deduct(_selectedNPCData.cost);
                _placedNPCs.Add(npc);

                Debug.Log($"Placed NPC: {_selectedNPCData.npcName} | Cost: ${_selectedNPCData.cost:F0}");
            }
        }
    }
}
