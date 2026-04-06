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
        private List<StructureUnit> _placedStructures = new List<StructureUnit>();
        private StructureUnit _movingUnit;
        private StructureUnit _hoveredUnit;

        // Placement Temp Data
        private Vector3 _currentHitPos;
        private Vector3 _currentHitNormal;
        private float _currentHeightOffset = 0f;
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
            HandleHeightAdjustment();

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

            if (mainCamera == null) return;

            // Combine masks to hit everything we can build on
            LayerMask combinedMask = groundLayer | structureLayer;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 500f, combinedMask))
            {
                // If we hit our own ghost, ignore it? (Ghost was already set to ignore colliders)
                _currentHitPos = hit.point;
                _currentHitNormal = hit.normal;
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
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, structureLayer))
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
        // HEIGHT ADJUSTMENT (Q / E)
        // --------------------------------------------------------------------------------

        private void HandleHeightAdjustment()
        {
            if (!IsPlacing && !IsMoving) return;

            // Finer height adjustment
            if (Input.GetKeyDown(KeyCode.E)) _currentHeightOffset += heightStep;
            if (Input.GetKeyDown(KeyCode.Q)) _currentHeightOffset -= heightStep;
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

                bool canAfford = _currentBudget >= _selectedData.basePrice;
                ghostBuilder.SetValid(canAfford);
            }

            if (Input.GetMouseButtonDown(0) && _hasValidTarget && ghostBuilder.IsValid)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                PlaceStructure(placePos, ghostBuilder.CurrentRotation);
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
                ghostBuilder.SetValid(true);
            }

            if (Input.GetMouseButtonDown(0) && _hasValidTarget)
            {
                Vector3 placePos = CalculatePlacementPosition(_currentHitPos);
                ConfirmMove(placePos, ghostBuilder.CurrentRotation);
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
            _currentHeightOffset = 0f;

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

        public void ExitMode()
        {
            if (_movingUnit != null) _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            _selectedData = null;
            _currentMode = BuildMode.Idle;
            _currentHeightOffset = 0f;
            ghostBuilder.DestroyGhost();
            ClearHover();
        }

        // --------------------------------------------------------------------------------
        // INTERNAL LOGIC
        // --------------------------------------------------------------------------------

        private void PlaceStructure(Vector3 position, float rotation)
        {
            _currentBudget -= _selectedData.basePrice;

            GameObject obj = Instantiate(_selectedData.prefab, position, Quaternion.Euler(0, rotation, 0));
            StructureUnit unit = obj.GetComponent<StructureUnit>() ?? obj.AddComponent<StructureUnit>();
            unit.Initialize(_selectedData, rotation);

            _placedStructures.Add(unit);

            if (_selectedData.placeSound != null) AudioSource.PlayClipAtPoint(_selectedData.placeSound, position);
            if (_selectedData.placeVFX != null) Instantiate(_selectedData.placeVFX, position, Quaternion.identity);
        }

        private void ConfirmMove(Vector3 position, float rotation)
        {
            _movingUnit.transform.position = position;
            _movingUnit.transform.rotation = Quaternion.Euler(0, rotation, 0);
            _movingUnit.SetRotation(rotation);
            _movingUnit.gameObject.SetActive(true);

            if (_movingUnit.Data.placeSound != null) AudioSource.PlayClipAtPoint(_movingUnit.Data.placeSound, position);

            _movingUnit = null;
            ghostBuilder.DestroyGhost();
            // Stay in Move mode for next pickup
        }

        private void CancelCurrentMove()
        {
            if (_movingUnit != null) _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            ghostBuilder.DestroyGhost();
        }

        private void TrySellStructure(StructureUnit unit)
        {
            _currentBudget += unit.Data.basePrice;
            _placedStructures.Remove(unit);
            if (unit.Data.breakSound != null) AudioSource.PlayClipAtPoint(unit.Data.breakSound, unit.transform.position);
            unit.DestroyStructure();
        }

        // --------------------------------------------------------------------------------
        // HELPER FUNCTIONS
        // --------------------------------------------------------------------------------

        private Vector3 CalculatePlacementPosition(Vector3 hitPoint)
        {
            float x = useGridSnap ? Mathf.Round(hitPoint.x / gridSize) * gridSize : hitPoint.x;
            float z = useGridSnap ? Mathf.Round(hitPoint.z / gridSize) * gridSize : hitPoint.z;

            // Y is bottom position + offset from pivot to model bottom
            float y = hitPoint.y + _pivotToBottomOffset + _currentHeightOffset;

            return new Vector3(x, y, z);
        }

        private float GetPivotToBottomOffset(GameObject prefab)
        {
            if (prefab == null) return 0f;

            // Calculate the distance from World position to the bottom of the bounds
            // This is relative to the pivot/center of the prefab
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;

            // We use local bounds to find the offset from pivot
            Bounds prefabBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInitialized = false;

            foreach (var r in renderers)
            {
                // Get bounds relative to parent root (assuming prefab root is at 0,0,0 locally)
                // This gives us the model's footprint relative to its pivot
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds localB = mf.sharedMesh.bounds;
                    // Move local mesh bounds by the child's local position
                    localB.center += r.transform.localPosition;
                    
                    if (!boundsInitialized) { prefabBounds = localB; boundsInitialized = true; }
                    else { prefabBounds.Encapsulate(localB); }
                }
            }

            // Pivot to Bottom offset = -min.y (distance from pivot 0 to floor)
            return -prefabBounds.min.y;
        }
    }
}
