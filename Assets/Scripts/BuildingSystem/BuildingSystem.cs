using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    /// <summary>
    /// 3D Building System (Poly Bridge-style stacking).
    /// - Place on ground or on top of existing structures
    /// - Stacks upward infinitely
    /// - Size comes from Prefab bounds (Pivot-aware)
    /// - Ghost follows mouse smoothly
    /// Modes: Idle, Placing, Moving, Deleting
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        public enum BuildMode { Idle, Placing, Moving, Deleting }

        public static BuildingSystem Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private bool useGridSnap = true;
        [SerializeField] private float gridSize = 1f;

        [Header("Layer Masks")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask structureLayer;

        [Header("Height Settings")]
        [SerializeField] private float heightStep = 0.5f;

        [Header("Budget")]
        [SerializeField] private float initialBudget = 1000f;
        private float _currentBudget;

        [Header("References")]
        [SerializeField] private UnityEngine.Camera mainCamera;
        [SerializeField] private GhostBuilder ghostBuilder;

        // State
        private BuildMode _currentMode = BuildMode.Idle;
        private StructureData _selectedData;
        private MaterialData _selectedMaterial;
        private List<StructureUnit> _placedStructures = new List<StructureUnit>();
        private StructureUnit _movingUnit;
        private StructureUnit _hoveredUnit;

        // Placement Temp Data
        private Vector3 _currentHitPos;
        private Vector3 _currentHitNormal;
        private Collider _currentHitCollider;
        private float _pivotToBottomOffset = 0f;
        private bool _hasValidTarget;

        public float CurrentBudget => _currentBudget;
        public BuildMode CurrentMode => _currentMode;
        public bool IsPlacing => _currentMode == BuildMode.Placing;
        public bool IsMoving => _currentMode == BuildMode.Moving;
        public bool IsDeleting => _currentMode == BuildMode.Deleting;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        private void Start()
        {
            _currentBudget = initialBudget;
            if (mainCamera == null) mainCamera = UnityEngine.Camera.main;
            if (ghostBuilder == null) ghostBuilder = GetComponent<GhostBuilder>();
        }

        private void Update()
        {
            UpdateRaycast();
            HandleHoverHighlight();

            switch (_currentMode)
            {
                case BuildMode.Placing:
                    HandlePlacementMode();
                    break;
                case BuildMode.Moving:
                    if (_movingUnit != null)
                        HandleMovingMode();
                    else
                        HandlePickupMode();
                    break;
                case BuildMode.Deleting:
                    HandleDeleteMode();
                    break;
                default:
                    break;
            }
        }

        // --------------------------------------------------------------------------------
        // RAYCAST - hits BOTH ground and structures
        // --------------------------------------------------------------------------------

        private void UpdateRaycast()
        {
            _hasValidTarget = false;
            _currentHitCollider = null;

            if (mainCamera == null) return;

            // Combine masks to hit everything we can build on
            LayerMask combinedMask = groundLayer | structureLayer;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 500f, combinedMask))
            {
                // If we hit our own ghost, ignore it? (Ghost was already set to ignore colliders)
                _currentHitPos = hit.point;
                _currentHitNormal = hit.normal;
                _currentHitCollider = hit.collider;
                _hasValidTarget = true;
            }
        }

        // --------------------------------------------------------------------------------
        // HOVER HIGHLIGHT (Select items to Move/Delete)
        // --------------------------------------------------------------------------------

        private void HandleHoverHighlight()
        {
            // Only highlight in Move or Delete modes when NOT currently holding an object
            if ((_currentMode != BuildMode.Moving && _currentMode != BuildMode.Deleting) || (_currentMode == BuildMode.Moving && _movingUnit != null))
            {
                ClearHover();
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 500f, structureLayer))
            {
                // Find StructureUnit on this object or any parent
                StructureUnit unit = hit.collider.GetComponentInParent<StructureUnit>();
                if (unit != _hoveredUnit)
                {
                    ClearHover();
                    _hoveredUnit = unit;
                    if (_hoveredUnit != null) _hoveredUnit.SetHighlight(true);
                }
            }
            else
            {
                ClearHover();
            }
        }

        private void ClearHover()
        {
            if (_hoveredUnit != null)
            {
                _hoveredUnit.SetHighlight(false);
                _hoveredUnit = null;
            }
        }

        // --------------------------------------------------------------------------------
        // PLACEMENT MODE
        // --------------------------------------------------------------------------------

        private void HandlePlacementMode()
        {
            if (Input.GetMouseButtonDown(1)) { ExitMode(); return; }
            if (Input.GetKeyDown(KeyCode.R)) ghostBuilder.Rotate();

            if (_hasValidTarget && ghostBuilder.HasGhost)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                ghostBuilder.UpdatePosition(placePos);

                MaterialData mat = _selectedMaterial != null ? _selectedMaterial : _selectedData.defaultMaterial;
                float materialPrice = mat != null ? mat.priceModifier : 0f;
                bool canAfford = _currentBudget >= (_selectedData.basePrice + materialPrice);
                bool isClear = IsAreaClear(placePos, ghostBuilder.CurrentRotation, _selectedData);
                ghostBuilder.SetValid(canAfford && isClear);
            }

            if (Input.GetMouseButtonDown(0) && _hasValidTarget && ghostBuilder.IsValid)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                PlaceStructure(placePos, ghostBuilder.CurrentRotation, _currentHitCollider);
            }
        }

        // --------------------------------------------------------------------------------
        // MOVE MODE - Pickup Phase
        // --------------------------------------------------------------------------------

        private void HandlePickupMode()
        {
            if (Input.GetMouseButtonDown(1)) { ExitMode(); return; }

            if (Input.GetMouseButtonDown(0) && _hoveredUnit != null)
            {
                EnterMovingSubmode(_hoveredUnit);
            }
        }

        private void EnterMovingSubmode(StructureUnit unit)
        {
            ClearHover();
            _movingUnit = unit;
            _movingUnit.gameObject.SetActive(false);

            // Initialize offset for the pickup
            _pivotToBottomOffset = GetPivotToBottomOffset(_movingUnit.Data.prefab);
            ghostBuilder.CreateGhost(_movingUnit.Data.prefab);
            ghostBuilder.SetRotation(_movingUnit.Rotation); // Match original rotation
        }

        // --------------------------------------------------------------------------------
        // MOVE MODE - Placement Phase
        // --------------------------------------------------------------------------------

        private void HandleMovingMode()
        {
            if (Input.GetMouseButtonDown(1)) { CancelCurrentMove(); return; }
            if (Input.GetKeyDown(KeyCode.R)) ghostBuilder.Rotate();

            if (_hasValidTarget && ghostBuilder.HasGhost)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                ghostBuilder.UpdatePosition(placePos);
                
                bool isClear = IsAreaClear(placePos, ghostBuilder.CurrentRotation, _movingUnit.Data);
                ghostBuilder.SetValid(isClear);
            }

            if (Input.GetMouseButtonDown(0) && _hasValidTarget)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                ConfirmMove(placePos, ghostBuilder.CurrentRotation, _currentHitCollider);
            }
        }

        // --------------------------------------------------------------------------------
        // DELETE MODE
        // --------------------------------------------------------------------------------

        private void HandleDeleteMode()
        {
            if (Input.GetMouseButtonDown(1)) { ExitMode(); return; }

            if (Input.GetMouseButtonDown(0) && _hoveredUnit != null)
            {
                TrySellStructure(_hoveredUnit);
            }
        }

        // --------------------------------------------------------------------------------
        // PUBLIC INTERFACE (for UI)
        // --------------------------------------------------------------------------------

        public void SelectStructure(StructureData data)
        {
            ExitMode();
            _selectedData = data;
            _currentMode = BuildMode.Placing;

            if (data != null && data.prefab != null)
            {
                _pivotToBottomOffset = GetPivotToBottomOffset(data.prefab);
                ghostBuilder.CreateGhost(data.prefab);
            }
        }

        public void EnterMoveMode()
        {
            ExitMode();
            _currentMode = BuildMode.Moving;
        }

        public void EnterDeleteMode()
        {
            ExitMode();
            _currentMode = BuildMode.Deleting;
        }

        public void SelectMaterial(MaterialData material)
        {
            _selectedMaterial = material;
            if (_currentMode == BuildMode.Placing && ghostBuilder.HasGhost)
            {
                // Optionally update ghost appearance?
            }
        }

        public void ExitMode()
        {
            if (_movingUnit != null) _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            _selectedData = null;
            _selectedMaterial = null;
            _currentMode = BuildMode.Idle;
            ghostBuilder.DestroyGhost();
            ClearHover();
        }

        // --------------------------------------------------------------------------------
        // INTERNAL LOGIC
        // --------------------------------------------------------------------------------

        private void PlaceStructure(Vector3 position, float rotation, Collider targetCollider = null)
        {
            MaterialData mat = _selectedMaterial != null ? _selectedMaterial : _selectedData.defaultMaterial;
            float materialPrice = mat != null ? mat.priceModifier : 0f;
            
            _currentBudget -= (_selectedData.basePrice + materialPrice);

            GameObject obj = Instantiate(_selectedData.prefab, position, Quaternion.Euler(0, rotation, 0));
            SetLayerRecursively(obj, structureLayer);
            
            // Rename to include grid position
            obj.name = $"{_selectedData.prefab.name} {GetGridPositionString(position)}";

            StructureUnit unit = obj.GetComponent<StructureUnit>() ?? obj.AddComponent<StructureUnit>();
            unit.Initialize(_selectedData, mat, rotation);

            AttachJoint(obj, targetCollider);

            _placedStructures.Add(unit);

            if (mat != null)
            {
                if (mat.placeSound != null) AudioSource.PlayClipAtPoint(mat.placeSound, position);
                if (mat.placeVFX != null) Instantiate(mat.placeVFX, position, Quaternion.identity);
            }
        }

        private void ConfirmMove(Vector3 position, float rotation, Collider targetCollider = null)
        {
            _movingUnit.transform.position = position;
            _movingUnit.transform.rotation = Quaternion.Euler(0, rotation, 0);
            _movingUnit.SetRotation(rotation);
            
            // Rename to include new grid position
            _movingUnit.name = $"{_movingUnit.Data.prefab.name} {GetGridPositionString(position)}";

            _movingUnit.gameObject.SetActive(true);

            AttachJoint(_movingUnit.gameObject, targetCollider);

            if (_movingUnit.CurrentMaterial != null && _movingUnit.CurrentMaterial.placeSound != null) 
                AudioSource.PlayClipAtPoint(_movingUnit.CurrentMaterial.placeSound, position);

            _movingUnit = null;
            ghostBuilder.DestroyGhost();
            // Stay in Move mode for next pickup
        }

        private void AttachJoint(GameObject structureObj, Collider targetCollider)
        {
            Rigidbody newRb = structureObj.GetComponent<Rigidbody>();
            if (newRb == null) return;

            // Remove existing joints just to be safe
            Joint[] existingJoints = structureObj.GetComponents<Joint>();
            foreach (var j in existingJoints) Destroy(j);

            FixedJoint fixedJoint = structureObj.AddComponent<FixedJoint>();

            if (targetCollider != null)
            {
                Rigidbody targetRb = targetCollider.GetComponentInParent<Rigidbody>();
                // if targetRb is null, it connects to the world (static) which is usually what we want for ground.
                fixedJoint.connectedBody = targetRb;
            }
        }

        private void CancelCurrentMove()
        {
            if (_movingUnit != null) _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            ghostBuilder.DestroyGhost();
        }

        private void TrySellStructure(StructureUnit unit)
        {
            float materialPrice = unit.CurrentMaterial != null ? unit.CurrentMaterial.priceModifier : 0f;
            _currentBudget += (unit.Data.basePrice + materialPrice);
            _placedStructures.Remove(unit);
            unit.DestroyStructure();
        }

        // --------------------------------------------------------------------------------
        // HELPER FUNCTIONS
        // --------------------------------------------------------------------------------

        private Vector3 CalculatePlacementPosition(Vector3 hitPoint)
        {
            float x = useGridSnap ? Mathf.Round(hitPoint.x / gridSize) * gridSize : hitPoint.x;
            float z = useGridSnap ? Mathf.Round(hitPoint.z / gridSize) * gridSize : hitPoint.z;

            float y = hitPoint.y;

            if (_currentHitCollider != null)
            {
                StructureUnit hitUnit = _currentHitCollider.GetComponentInParent<StructureUnit>();
                if (hitUnit != null && hitUnit.Data != null)
                {
                    // Size is from Data. Y stacking is exactly based on the unit's declared height!
                    float occupiedHeight = hitUnit.Data.size.y * heightStep;
                    float bottomY = hitUnit.transform.position.y - GetPivotToBottomOffset(hitUnit.Data.prefab);
                    float topY = bottomY + occupiedHeight;

                    y = topY;
                }
            }

            // Pivot to Bottom offset
            y += _pivotToBottomOffset;

            return new Vector3(x, y, z);
        }

        private void SetLayerRecursively(GameObject obj, LayerMask layerMask)
        {
            int layerIndex = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((layerMask.value & (1 << i)) != 0)
                {
                    layerIndex = i;
                    break;
                }
            }

            SetLayer(obj, layerIndex);
        }

        private void SetLayer(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }

        private float GetPivotToBottomOffset(GameObject prefab)
        {
            (Vector3 center, Vector3 size) = GetPrefabBounds(prefab);
            // Pivot to Bottom offset = - (center.y - size.y * 0.5) = size.y * 0.5 - center.y
            // Wait, previous logic was -prefabBounds.min.y.
            // min.y is center.y - size.y * 0.5f.
            // So -min.y = -(center.y - size.y * 0.5f) = size.y * 0.5f - center.y.
            return (size.y * 0.5f) - center.y;
        }

        private (Vector3 center, Vector3 size) GetPrefabBounds(GameObject prefab)
        {
            if (prefab == null) return (Vector3.zero, Vector3.one);

            // Use BoxCollider if it exists for perfectly consistent size.
            BoxCollider bc = prefab.GetComponentInChildren<BoxCollider>(true);
            if (bc != null)
            {
                Vector3 center = bc.center;
                // Scale center if the collider is on a scaled child object
                if (bc.transform != prefab.transform)
                {
                    center = Vector3.Scale(center, bc.transform.localScale) + bc.transform.localPosition;
                }
                Vector3 size = Vector3.Scale(bc.size, bc.transform.localScale);
                return (center, size);
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return (Vector3.zero, Vector3.one);

            Bounds prefabBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInitialized = false;

            foreach (var r in renderers)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds localB = mf.sharedMesh.bounds;
                    // Approximate its root-relative position
                    localB.size = Vector3.Scale(localB.size, r.transform.localScale);
                    localB.center = Vector3.Scale(localB.center, r.transform.localScale);
                    localB.center += r.transform.localPosition;
                    
                    if (!boundsInitialized) { prefabBounds = localB; boundsInitialized = true; }
                    else { prefabBounds.Encapsulate(localB); }
                }
            }
            return (prefabBounds.center, prefabBounds.size);
        }

        private string GetGridPositionString(Vector3 position)
        {
            int gridX = Mathf.RoundToInt(position.x / (gridSize > 0 ? gridSize : 1f));
            int gridY = Mathf.RoundToInt(position.y / (heightStep > 0 ? heightStep : 1f));
            int gridZ = Mathf.RoundToInt(position.z / (gridSize > 0 ? gridSize : 1f));
            return $"({gridX}, {gridY}, {gridZ})";
        }

        private Bounds GetGridBounds(Vector3 position, float rotation, StructureData data)
        {
            if (data == null) return new Bounds(position, Vector3.zero);

            float w = data.size.x * gridSize;
            float h = data.size.y * heightStep;
            float d = data.size.z * gridSize;

            Vector3 extents = new Vector3(w * 0.5f, h * 0.5f, d * 0.5f);

            // Swap X and Z for 90 or 270 degree rotations
            if (Mathf.Abs(rotation % 180f) > 45f)
            {
                extents = new Vector3(extents.z, extents.y, extents.x);
            }

            // Correctly derive bottom and map to the exact world space bounds center
            float bottomY = position.y - GetPivotToBottomOffset(data.prefab);
            Vector3 worldCenter = position;
            worldCenter.y = bottomY + extents.y;

            // Shrink by 5% so adjacent surfaces don't trigger overlap
            return new Bounds(worldCenter, extents * 1.9f); 
        }

        private bool IsAreaClear(Vector3 position, float rotation, StructureData structureData)
        {
            if (structureData == null) return true;

            Bounds boundsA = GetGridBounds(position, rotation, structureData);

            foreach (var unit in _placedStructures)
            {
                if (unit == _movingUnit || unit == null) continue;

                Bounds boundsB = GetGridBounds(unit.transform.position, unit.Rotation, unit.Data);

                if (boundsA.Intersects(boundsB))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
