using System.Collections.Generic;
using UnityEngine;
using BuildingSimulation.Data;
using BuildingSimulation.Physics;

namespace BuildingSimulation.Building
{
    /// <summary>
    /// Handles placement, ghost preview, rotation, scaling, and deletion of building parts.
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        public static BuildingSystem Instance { get; private set; }

        [Header("Placement Settings")]
        [SerializeField] private float gridSize = 1f;
        [SerializeField] private float rotationStep = 15f;
        [SerializeField] private float scaleStep = 0.1f;
        [SerializeField] private LayerMask placementLayerMask = ~0;
        [SerializeField] private float maxPlacementDistance = 100f;

        [Header("Ghost Preview")]
        [SerializeField] private Material ghostMaterial;

        [Header("References")]
        [SerializeField] private Transform buildingParent;

        // State
        private BuildingPartData _selectedPartData;
        private BuildingMaterialData _selectedMaterial;
        private GameObject _ghostObject;
        private Vector3 _currentGhostScale;
        private float _currentRotationY;
        private bool _isPlacing;
        private bool _isSelectionMode;
        private bool _isValidPosition;

        private BuildingPart _tempEditingPart; // To store part being moved
        private float _tempCostOriginal; // To handle budget refunds on move

        // Track all placed parts
        private readonly List<BuildingPart> _placedParts = new List<BuildingPart>();
        public IReadOnlyList<BuildingPart> PlacedParts => _placedParts;

        private UnityEngine.Camera _mainCam;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (buildingParent == null)
            {
                buildingParent = new GameObject("Building").transform;
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
                HandleRotationInput();
                HandleScaleInput();

                if (Input.GetMouseButtonDown(0))
                {
                    PlaceCurrentPart();
                }
                if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                {
                    CancelPlacement();
                }
            }
            else if (_isSelectionMode)
            {
                HandleSelectionMode();
            }
        }

        // ─── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Start placing a part type. Called from UI.
        /// </summary>
        public void SelectPart(BuildingPartData data)
        {
            CancelPlacement();
            _selectedPartData = data;
            _selectedMaterial = data.defaultMaterial;
            _currentGhostScale = data.defaultScale;
            _currentRotationY = 0f;
            _isPlacing = true;
            CreateGhost();
        }

        /// <summary>
        /// Override the material for the next placement.
        /// </summary>
        public void SetMaterial(BuildingMaterialData material)
        {
            _selectedMaterial = material;
        }

        /// <summary>
        /// Enter selection mode (Modify Mode).
        /// </summary>
        public void ToggleSelectMode()
        {
            bool wasSelectionMode = _isSelectionMode;
            CancelPlacement(); // This resets _isSelectionMode to false
            
            _isSelectionMode = !wasSelectionMode; // Restore and invert

            if (!_isSelectionMode) UI.ContextUI.Instance?.Hide();
        }

        public void CancelPlacement()
        {
            _isPlacing = false;
            _isSelectionMode = false;
            UI.ContextUI.Instance?.Hide();

            if (_tempEditingPart != null)
            {
                _tempEditingPart.gameObject.SetActive(true);
                _tempEditingPart = null;
            }

            if (_ghostObject != null)
            {
                Destroy(_ghostObject);
                _ghostObject = null;
            }
        }

        /// <summary>
        /// Start moving an existing part.
        /// </summary>
        public void StartMovingPart(BuildingPart part)
        {
            if (part == null) return;
            
            // Setup ghost from part data
            _selectedPartData = part.PartData;
            _selectedMaterial = part.MaterialData;
            _currentGhostScale = part.CurrentScale;
            _currentRotationY = part.transform.eulerAngles.y;
            _isPlacing = true;
            _isSelectionMode = false;
            
            _tempEditingPart = part;
            _tempEditingPart.gameObject.SetActive(false); // Hide original
            
            CreateGhost();
        }

        /// <summary>
        /// Apply the currently selected Material (from the UI) to a part.
        /// </summary>
        public void ApplySelectedMaterialToPart(BuildingPart part)
        {
            if (part == null || _selectedMaterial == null) return;

            float oldCost = part.GetCost();
            part.UpdateMaterial(_selectedMaterial);
            float newCost = part.GetCost();

            float diff = newCost - oldCost;
            if (diff > 0) BudgetManager.Instance?.Deduct(diff);
            else BudgetManager.Instance?.Refund(Mathf.Abs(diff));
            
            Debug.Log($"Updated Material: {part.MaterialData.materialName} | Budget Diff: ${diff:F0}");
        }

        /// <summary>
        /// Remove a specific part, refunding its cost.
        /// </summary>
        public void RemovePart(BuildingPart part)
        {
            if (part == null) return;

            float refund = part.GetCost();
            BudgetManager.Instance?.Refund(refund);
            _placedParts.Remove(part);
            Destroy(part.gameObject);
        }

        /// <summary>
        /// Destroy all placed parts and reset.
        /// </summary>
        public void ClearAll()
        {
            foreach (var part in _placedParts)
            {
                if (part != null) Destroy(part.gameObject);
            }
            _placedParts.Clear();
            BudgetManager.Instance?.ResetBudget();
        }

        // ─── Ghost ────────────────────────────────────────────────────

        private void CreateGhost()
        {
            if (_selectedPartData == null) return;

            _ghostObject = CreatePartVisual(_selectedPartData);
            _ghostObject.name = "Ghost_" + _selectedPartData.partName;
            _ghostObject.transform.localScale = _currentGhostScale;
 
            // Remove ALL colliders from ghost (including children) so it doesn't interfere
            foreach (var col in _ghostObject.GetComponentsInChildren<Collider>())
            {
                Destroy(col);
            }
 
            // Apply ghost material / color to all renderers
            var renderers = _ghostObject.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    if (ghostMaterial != null)
                    {
                        renderer.material = new Material(ghostMaterial);
                        renderer.material.color = _selectedPartData.previewColor;
                    }
                    else
                    {
                        renderer.material.color = _selectedPartData.previewColor;
                    }
                }
            }
        }

        private void UpdateGhostPosition()
        {
            Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);

            // Raycast against ALL colliders (not just placementLayerMask) so we can
            // detect existing BuildingParts and build on top of them.
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance))
            {
                // Calculate the offset from the hit surface:
                // Move the ghost along the hit normal by half of its size on that axis.
                Vector3 halfSize = _currentGhostScale * 0.5f;

                // The offset along the normal ensures the piece sits ON the surface, not inside it
                float normalOffset = Mathf.Abs(hit.normal.x) * halfSize.x
                                   + Mathf.Abs(hit.normal.y) * halfSize.y
                                   + Mathf.Abs(hit.normal.z) * halfSize.z;

                Vector3 pos = hit.point + hit.normal * normalOffset;

                // Snap to grid
                if (gridSize > 0)
                {
                    pos.x = Mathf.Round(pos.x / gridSize) * gridSize;
                    pos.y = Mathf.Round(pos.y / gridSize) * gridSize;
                    pos.z = Mathf.Round(pos.z / gridSize) * gridSize;
                }

                // For horizontal surfaces (floor/top of a block), ensure the piece
                // sits exactly on top by aligning to surface Y + half height
                if (Mathf.Abs(hit.normal.y) > 0.5f)
                {
                    // Snap the surface point to grid, then add half height
                    float surfaceY = gridSize > 0
                        ? Mathf.Round(hit.point.y / gridSize) * gridSize
                        : hit.point.y;
                    pos.y = surfaceY + _currentGhostScale.y * 0.5f;
                }

                _ghostObject.transform.position = pos;
                _ghostObject.transform.rotation = Quaternion.Euler(0f, _currentRotationY, 0f);
                _ghostObject.transform.localScale = _currentGhostScale;

                // Check for overlap with existing BuildingParts
                _isValidPosition = !CheckOverlap(pos, _ghostObject.transform.rotation, _currentGhostScale);

                // Update ghost color for feedback
                UpdateGhostMaterial(_isValidPosition);
            }
        }

        /// <summary>
        /// Returns true if there is a BuildingPart overlapping the given box.
        /// Uses slightly shrunken extents (90%) to avoid false positives from
        /// edge-to-edge touching neighbors.
        /// </summary>
        private bool CheckOverlap(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // halfExtents = half of the object size, shrunk by 10% to allow touching neighbors
            Vector3 halfExtents = scale * 0.5f * 0.9f;

            // Use ~0 (Everything) layer mask and include kinematic colliders
            Collider[] colliders = UnityEngine.Physics.OverlapBox(
                position, halfExtents, rotation, ~0, QueryTriggerInteraction.Ignore);

            foreach (var col in colliders)
            {
                // Skip the ghost object itself
                if (_ghostObject != null && col.transform.IsChildOf(_ghostObject.transform))
                    continue;

                // If we hit something that has a BuildingPart, it's an overlap
                if (col.GetComponent<BuildingPart>() != null || col.GetComponentInParent<BuildingPart>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdateGhostMaterial(bool isValid)
        {
            if (_ghostObject == null) return;
            var renderers = _ghostObject.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    if (!isValid)
                    {
                        // Tint red when position is occupied
                        renderer.material.color = new Color(1f, 0f, 0f, 0.5f);
                    }
                    else
                    {
                        // Reflect the SELECTED material color
                        Color baseColor = _selectedMaterial != null ? _selectedMaterial.materialColor : _selectedPartData.previewColor;
                        renderer.material.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.5f);
                    }
                }
            }
        }

        // ─── Input Handling ───────────────────────────────────────────

        private void HandleRotationInput()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                _currentRotationY += rotationStep;
            }
        }

        private void HandleScaleInput()
        {
            // Hold Shift + scroll to scale
            if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            float delta = scroll > 0 ? scaleStep : -scaleStep;

            // Scale depends on part type
            switch (_selectedPartData.partType)
            {
                case PartType.Pillar:
                    // Scale Y (height) and XZ (thickness) uniformly
                    if (Input.GetKey(KeyCode.LeftControl))
                        _currentGhostScale.y += delta; // Height only
                    else
                        _currentGhostScale += Vector3.one * delta; // Uniform
                    break;

                case PartType.Floor:
                    // Scale XZ (width/length)
                    _currentGhostScale.x += delta;
                    _currentGhostScale.z += delta;
                    break;

                case PartType.Wall:
                    // Scale X (width) and Y (height)
                    _currentGhostScale.x += delta;
                    _currentGhostScale.y += delta;
                    break;

                default:
                    _currentGhostScale += Vector3.one * delta;
                    break;
            }

            // Clamp
            _currentGhostScale = new Vector3(
                Mathf.Clamp(_currentGhostScale.x, _selectedPartData.minScale.x, _selectedPartData.maxScale.x),
                Mathf.Clamp(_currentGhostScale.y, _selectedPartData.minScale.y, _selectedPartData.maxScale.y),
                Mathf.Clamp(_currentGhostScale.z, _selectedPartData.minScale.z, _selectedPartData.maxScale.z)
            );
        }

        // ─── Placement ───────────────────────────────────────────────

        private void PlaceCurrentPart()
        {
            if (_ghostObject == null || _selectedPartData == null) return;

            // Calculate cost
            float volumeScale = _currentGhostScale.x * _currentGhostScale.y * _currentGhostScale.z;
            float matMul = _selectedMaterial != null ? _selectedMaterial.costMultiplier : 1f;
            float cost = _selectedPartData.baseCost * matMul * volumeScale;

            // Check budget
            if (!BudgetManager.Instance.CanAfford(cost))
            {
                Debug.LogWarning($"Cannot afford {_selectedPartData.partName}! Cost: ${cost:F0}, Budget: ${BudgetManager.Instance.CurrentBudget:F0}");
                return;
            }

            // Check validity
            if (!_isValidPosition)
            {
                Debug.LogWarning($"Cannot place {_selectedPartData.partName} here: Position is occupied!");
                return;
            }

            // --- IF MOVING: Finalize the move ---
            if (_tempEditingPart != null)
            {
                // Refund original part cost before placing the new one (avoids double charging if budget is low)
                float refund = _tempEditingPart.GetCost();
                BudgetManager.Instance?.Refund(refund);
                _placedParts.Remove(_tempEditingPart);
                Destroy(_tempEditingPart.gameObject);
                _tempEditingPart = null;
            }

            // Create real part
            GameObject partObj = CreatePartVisual(_selectedPartData);
            partObj.name = _selectedPartData.partName;
            partObj.transform.position = _ghostObject.transform.position;
            partObj.transform.rotation = _ghostObject.transform.rotation;
            partObj.transform.SetParent(buildingParent);
 
            // Ensure Rigidbody exists
            var rb = partObj.GetComponent<Rigidbody>();
            if (rb == null) rb = partObj.AddComponent<Rigidbody>();
 
            // Ensure Collider exists (add BoxCollider if none found in root or children)
            if (partObj.GetComponentInChildren<Collider>() == null)
            {
                partObj.AddComponent<BoxCollider>();
            }
 
            // Ensure BuildingPart component exists and initialize
            var buildingPart = partObj.GetComponent<BuildingPart>();
            if (buildingPart == null) buildingPart = partObj.AddComponent<BuildingPart>();
            
            buildingPart.Initialize(_selectedPartData, _selectedMaterial, _currentGhostScale);

            // Deduct budget
            BudgetManager.Instance.Deduct(cost);
            _placedParts.Add(buildingPart);

            // Create joints to adjacent parts
            if (StructuralJointManager.Instance != null)
            {
                StructuralJointManager.Instance.ConnectToNeighbors(buildingPart);
            }

            // Play placement sound
            if (SoundManager.Instance != null && _selectedPartData.placementSound != null)
            {
                SoundManager.Instance.PlaySFX(_selectedPartData.placementSound, partObj.transform.position);
            }

            // Spawn placement effect (one-shot)
            if (EffectManager.Instance != null && _selectedPartData.placementEffectPrefab != null)
            {
                EffectManager.Instance.SpawnEffect(_selectedPartData.placementEffectPrefab, partObj.transform.position);
            }

            Debug.Log($"Placed {_selectedPartData.partName} | Cost: ${cost:F0} | Budget: ${BudgetManager.Instance.CurrentBudget:F0}");
        }

        // ─── Selection Mode ────────────────────────────────────────────

        private void HandleSelectionMode()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            Ray ray = _mainCam.ScreenPointToRay(Input.mousePosition);
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, maxPlacementDistance))
            {
                var part = hit.collider.GetComponent<BuildingPart>() ?? hit.collider.GetComponentInParent<BuildingPart>();
                if (part != null)
                {
                    UI.ContextUI.Instance?.Show(part, hit.point);
                }
                else
                {
                    UI.ContextUI.Instance?.Hide();
                }
            }
        }

        // ─── Primitive Factory ────────────────────────────────────────
 
        private GameObject CreatePartVisual(BuildingPartData data)
        {
            if (data.modelPrefab != null)
            {
                return Instantiate(data.modelPrefab);
            }
            return CreatePrimitive(data.partType);
        }
 
        private GameObject CreatePrimitive(PartType type)
        {
            switch (type)
            {
                case PartType.Pillar:
                    return GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                case PartType.Wall:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);

                case PartType.Floor:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);

                case PartType.Stairs:
                    // Stairs approximated as a slanted cube
                    var stairs = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stairs.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
                    return stairs;

                case PartType.Door:
                    // Door = thin cube with distinctive scale
                    var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    return door;

                default:
                    return GameObject.CreatePrimitive(PrimitiveType.Cube);
            }
        }
    }
}
