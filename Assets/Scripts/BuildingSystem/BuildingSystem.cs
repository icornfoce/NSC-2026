using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    /// <summary>
    /// Building System with UI-driven modes.
    /// Modes: Idle, Placing, Moving, Deleting
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        public enum BuildMode { Idle, Placing, Moving, Deleting }

        public static BuildingSystem Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private float gridSize = 1f;
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
        private Vector3 _currentSnappedPos;
        private float _currentHeight = 0f;
        private bool _hasValidTarget;

        // Public access
        public float CurrentBudget => _currentBudget;
        public StructureData SelectedData => _selectedData;
        public BuildMode CurrentMode => _currentMode;
        public bool IsPlacing => _currentMode == BuildMode.Placing;
        public bool IsMoving => _currentMode == BuildMode.Moving && _movingUnit != null;
        public bool IsDeleting => _currentMode == BuildMode.Deleting;
        public bool IsIdle => _currentMode == BuildMode.Idle;

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

        // ─────────────────────────────────────────────
        // RAYCAST
        // ─────────────────────────────────────────────

        private void UpdateRaycast()
        {
            _hasValidTarget = false;

            if (mainCamera == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, groundLayer))
            {
                _currentSnappedPos = SnapToGrid(hit.point);
                _hasValidTarget = true;
            }
        }

        // ─────────────────────────────────────────────
        // HOVER HIGHLIGHT
        // ─────────────────────────────────────────────

        private void HandleHoverHighlight()
        {
            // Highlight structures when in Move or Delete mode
            if (_currentMode != BuildMode.Moving && _currentMode != BuildMode.Deleting)
            {
                ClearHover();
                return;
            }

            // Don't highlight when already carrying an object
            if (_currentMode == BuildMode.Moving && _movingUnit != null)
            {
                ClearHover();
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, structureLayer))
            {
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

        // ─────────────────────────────────────────────
        // HEIGHT ADJUSTMENT (Q / E)
        // ─────────────────────────────────────────────

        private void HandleHeightAdjustment()
        {
            if (_currentMode != BuildMode.Placing && !(_currentMode == BuildMode.Moving && _movingUnit != null))
                return;

            if (Input.GetKeyDown(KeyCode.E))
            {
                _currentHeight += heightStep;
                Debug.Log($"Height: {_currentHeight}");
            }
            if (Input.GetKeyDown(KeyCode.Q))
            {
                _currentHeight -= heightStep;
                if (_currentHeight < 0f) _currentHeight = 0f;
                Debug.Log($"Height: {_currentHeight}");
            }
        }

        // ─────────────────────────────────────────────
        // PLACEMENT MODE
        // ─────────────────────────────────────────────

        private void HandlePlacementMode()
        {
            // Right-click → cancel
            if (Input.GetMouseButtonDown(1))
            {
                ExitMode();
                return;
            }

            // R → rotate
            if (Input.GetKeyDown(KeyCode.R))
            {
                ghostBuilder.Rotate();
            }

            // Update ghost position & validity
            if (_hasValidTarget && ghostBuilder.HasGhost)
            {
                Vector3 posWithHeight = _currentSnappedPos + Vector3.up * _currentHeight;
                ghostBuilder.UpdatePosition(posWithHeight);
                bool canPlace = _currentBudget >= _selectedData.basePrice;
                ghostBuilder.SetValid(canPlace);
            }

            // Left-click → place
            if (Input.GetMouseButtonDown(0) && _hasValidTarget)
            {
                if (ghostBuilder.IsValid)
                {
                    Vector3 posWithHeight = _currentSnappedPos + Vector3.up * _currentHeight;
                    PlaceStructure(posWithHeight, ghostBuilder.CurrentRotation);
                }
            }
        }

        // ─────────────────────────────────────────────
        // MOVE MODE — pickup phase (click to pick up)
        // ─────────────────────────────────────────────

        private void HandlePickupMode()
        {
            // Right-click → cancel move mode
            if (Input.GetMouseButtonDown(1))
            {
                ExitMode();
                return;
            }

            // Click on hovered structure → pick it up
            if (Input.GetMouseButtonDown(0) && _hoveredUnit != null)
            {
                PickUpStructure(_hoveredUnit);
            }
        }

        // ─────────────────────────────────────────────
        // MOVE MODE — carrying phase (place it down)
        // ─────────────────────────────────────────────

        private void HandleMovingMode()
        {
            // Right-click → cancel move, put it back
            if (Input.GetMouseButtonDown(1))
            {
                CancelMove();
                return;
            }

            // R → rotate
            if (Input.GetKeyDown(KeyCode.R))
            {
                ghostBuilder.Rotate();
            }

            // Update ghost
            if (_hasValidTarget && ghostBuilder.HasGhost)
            {
                Vector3 posWithHeight = _currentSnappedPos + Vector3.up * _currentHeight;
                ghostBuilder.UpdatePosition(posWithHeight);
                ghostBuilder.SetValid(true); // Always valid (stacking allowed)
            }

            // Left-click → confirm new position
            if (Input.GetMouseButtonDown(0) && _hasValidTarget)
            {
                Vector3 posWithHeight = _currentSnappedPos + Vector3.up * _currentHeight;
                ConfirmMove(posWithHeight, ghostBuilder.CurrentRotation);
            }
        }

        // ─────────────────────────────────────────────
        // DELETE MODE
        // ─────────────────────────────────────────────

        private void HandleDeleteMode()
        {
            // Right-click → cancel delete mode
            if (Input.GetMouseButtonDown(1))
            {
                ExitMode();
                return;
            }

            // Click on hovered structure → sell/delete it
            if (Input.GetMouseButtonDown(0) && _hoveredUnit != null)
            {
                TrySellStructure(_hoveredUnit);
            }
        }

        // ─────────────────────────────────────────────
        // PUBLIC ACTIONS (for UI buttons)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Select a structure for placement (called from UI).
        /// </summary>
        public void SelectStructure(StructureData data)
        {
            ExitMode();

            _selectedData = data;
            _currentHeight = 0f;
            _currentMode = BuildMode.Placing;

            if (data != null && data.prefab != null)
            {
                ghostBuilder.CreateGhost(data.prefab);
            }

            Debug.Log($"Mode: Placing ({data?.structureName})");
        }

        /// <summary>
        /// Enter Move mode — click on a structure to pick it up (called from UI).
        /// </summary>
        public void EnterMoveMode()
        {
            ExitMode();
            _currentMode = BuildMode.Moving;
            _currentHeight = 0f;
            Debug.Log("Mode: Moving — Click on a structure to pick it up");
        }

        /// <summary>
        /// Enter Delete mode — click on a structure to sell/remove it (called from UI).
        /// </summary>
        public void EnterDeleteMode()
        {
            ExitMode();
            _currentMode = BuildMode.Deleting;
            Debug.Log("Mode: Deleting — Click on a structure to sell it");
        }

        /// <summary>
        /// Exit current mode and return to Idle.
        /// </summary>
        public void ExitMode()
        {
            // Cancel any active move
            if (_movingUnit != null)
            {
                _movingUnit.gameObject.SetActive(true);
                _movingUnit = null;
            }

            _selectedData = null;
            _currentHeight = 0f;
            _currentMode = BuildMode.Idle;
            ghostBuilder.DestroyGhost();
            ClearHover();
        }

        // ─────────────────────────────────────────────
        // INTERNAL ACTIONS
        // ─────────────────────────────────────────────

        private void PlaceStructure(Vector3 position, float rotation)
        {
            if (_selectedData == null || _selectedData.prefab == null) return;

            // Deduct budget
            _currentBudget -= _selectedData.basePrice;

            // Instantiate
            Quaternion rot = Quaternion.Euler(0f, rotation, 0f);
            GameObject obj = Instantiate(_selectedData.prefab, position, rot);

            StructureUnit unit = obj.GetComponent<StructureUnit>();
            if (unit == null) unit = obj.AddComponent<StructureUnit>();
            unit.Initialize(_selectedData, rotation);

            _placedStructures.Add(unit);

            // Audio feedback
            if (_selectedData.placeSound != null)
                AudioSource.PlayClipAtPoint(_selectedData.placeSound, position);

            // VFX feedback
            if (_selectedData.placeVFX != null)
                Instantiate(_selectedData.placeVFX, position, Quaternion.identity);

            Debug.Log($"Placed {_selectedData.structureName} | Budget: {_currentBudget}");
        }

        private void PickUpStructure(StructureUnit unit)
        {
            ClearHover();
            _movingUnit = unit;
            _movingUnit.gameObject.SetActive(false);

            ghostBuilder.CreateGhost(_movingUnit.Data.prefab);
        }

        private void ConfirmMove(Vector3 newPosition, float newRotation)
        {
            if (_movingUnit == null) return;

            _movingUnit.transform.position = newPosition;
            _movingUnit.transform.rotation = Quaternion.Euler(0f, newRotation, 0f);
            _movingUnit.SetRotation(newRotation);
            _movingUnit.gameObject.SetActive(true);

            // Audio
            if (_movingUnit.Data.placeSound != null)
                AudioSource.PlayClipAtPoint(_movingUnit.Data.placeSound, newPosition);

            _movingUnit = null;
            ghostBuilder.DestroyGhost();

            // Stay in Move mode for picking up another
            Debug.Log("Move confirmed — still in Move mode");
        }

        private void CancelMove()
        {
            if (_movingUnit == null) return;

            _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            ghostBuilder.DestroyGhost();
            // Stay in Move mode
        }

        private void TrySellStructure(StructureUnit unit)
        {
            if (unit == null) return;

            ClearHover();

            // Refund
            _currentBudget += unit.Data.basePrice;
            _placedStructures.Remove(unit);

            Debug.Log($"Sold {unit.Data.structureName} | Refund: {unit.Data.basePrice} | Budget: {_currentBudget}");

            // Audio
            if (unit.Data.breakSound != null)
                AudioSource.PlayClipAtPoint(unit.Data.breakSound, unit.transform.position);

            unit.DestroyStructure();
            // Stay in Delete mode for deleting more
        }

        // ─────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────

        private Vector3 SnapToGrid(Vector3 pos)
        {
            return new Vector3(
                Mathf.Round(pos.x / gridSize) * gridSize,
                pos.y,
                Mathf.Round(pos.z / gridSize) * gridSize
            );
        }
    }
}
