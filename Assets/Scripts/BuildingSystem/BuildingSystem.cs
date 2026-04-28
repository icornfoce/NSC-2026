using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    /// <summary>
    /// 3D Building System
    /// - Place on ground or on top of existing structures
    /// - Stacks upward infinitely
    /// - Size comes from Prefab bounds (Pivot-aware)
    /// - Ghost follows mouse smoothly
    /// Modes: Idle, Placing, Moving, Deleting
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        public enum BuildMode { Idle, Placing, Moving, Deleting, Painting }

        public static BuildingSystem Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private bool useGridSnap = true;
        [SerializeField] private float gridSize = 1f;
        [Tooltip("Number of grid columns (X axis)")]
        [SerializeField] private int gridColumns = 10;
        [Tooltip("Number of grid rows (Z axis)")]
        [SerializeField] private int gridRows = 10;
        [Tooltip("If true, Y axis also snaps to grid increments so all structures share the same base levels.")]
        [SerializeField] private bool snapYToGrid = true;

        [Header("Layer Masks")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask structureLayer;

        [Header("Height Settings")]
        [Tooltip("Vertical distance between floors. If pillarReference is assigned, this will be auto-set in Start.")]
        [SerializeField] private float heightStep = 3.0f;

        [Tooltip("Optional: Assign your Pillar/Column structure here. Its height will automatically define the Height Step for the whole building.")]
        [SerializeField] private StructureData pillarReference;

        [Header("Budget")]
        [SerializeField] private float initialBudget = 1000f;
        private float _currentBudget;

        [Header("General SFX / VFX")]
        [SerializeField] private AudioClip generalPlaceSound;
        [SerializeField] private AudioClip generalSellSound;
        [SerializeField] private AudioClip generalPaintSound;
        [SerializeField] private AudioClip generalUndoSound;
        [SerializeField] private AudioClip generalRedoSound;
        [SerializeField] private AudioClip generalErrorSound;
        [SerializeField] private GameObject generalSellVFX;

        [Header("References")]
        [SerializeField] private UnityEngine.Camera mainCamera;
        [SerializeField] private GhostBuilder ghostBuilder;
        // อ่าน OccludedColliders จาก CameraController เพื่อข้ามตอน Raycast
        private Simulation.Camera.CameraController _cameraController;

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

        // Frame cooldown: prevents the same click that selects a structure from also placing it
        private bool _justEnteredPlacing = false;

        // Floor-level system
        private int _currentFloor = 0;
        private int _maxOccupiedFloor = 0;

        // Undo / Redo System
        private class BuildAction
        {
            public System.Action Undo;
            public System.Action Redo;
        }
        private Stack<BuildAction> _undoStack = new Stack<BuildAction>();
        private Stack<BuildAction> _redoStack = new Stack<BuildAction>();

        // State for Move Command Undo
        private Vector3 _moveOriginalPos;
        private float _moveOriginalRot;
        private Collider _moveOriginalTargetCol;

        public float CurrentBudget => _currentBudget;
        public BuildMode CurrentMode => _currentMode;
        public bool IsPlacing => _currentMode == BuildMode.Placing;
        public bool IsMoving => _currentMode == BuildMode.Moving;
        public bool IsDeleting => _currentMode == BuildMode.Deleting;
        public bool IsPainting => _currentMode == BuildMode.Painting;
        public MaterialData SelectedMaterial => _selectedMaterial;
        public int CurrentFloor => _currentFloor;
        public int MaxOccupiedFloor => _maxOccupiedFloor;
        public int GridColumns => gridColumns;
        public int GridRows => gridRows;
        public float GetGridSize => gridSize;

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
            
            // Auto-set heightStep from pillarReference if available
            if (pillarReference != null && pillarReference.prefab != null)
            {
                (Vector3 center, Vector3 size) = GetPrefabBounds(pillarReference.prefab);
                heightStep = size.y;
                Debug.Log($"[BuildingSystem] Auto-set Height Step to {heightStep} from '{pillarReference.structureName}' height.");
            }

            // หา CameraController จากกล้องหลัก
            if (mainCamera != null)
                _cameraController = mainCamera.GetComponent<Simulation.Camera.CameraController>();
        }

        private void Update()
        {
            UpdateRaycast();
            HandleHoverHighlight();
            HandleFloorSwitch();

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
                case BuildMode.Painting:
                    HandlePaintingMode();
                    break;
                default:
                    break;
            }

            HandleUndoRedoInput();
        }

        // --------------------------------------------------------------------------------
        // UNDO / REDO
        // --------------------------------------------------------------------------------

        private void HandleUndoRedoInput()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand))
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        Redo();
                    else
                        Undo();
                }
                else if (Input.GetKeyDown(KeyCode.Y))
                {
                    Redo();
                }
            }
        }

        private void ExecuteCommand(System.Action execute, System.Action undo)
        {
            execute();
            _undoStack.Push(new BuildAction { Undo = undo, Redo = execute });
            _redoStack.Clear(); // Any new action clears the redo history
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var action = _undoStack.Pop();
                action.Undo();
                _redoStack.Push(action);
                
                if (generalUndoSound != null) AudioSource.PlayClipAtPoint(generalUndoSound, mainCamera.transform.position);
                RecalculateMaxFloor();
            }
            else if (generalErrorSound != null)
            {
                AudioSource.PlayClipAtPoint(generalErrorSound, mainCamera.transform.position);
            }
        }

        public void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var action = _redoStack.Pop();
                action.Redo();
                _undoStack.Push(action);

                if (generalRedoSound != null) AudioSource.PlayClipAtPoint(generalRedoSound, mainCamera.transform.position);
                RecalculateMaxFloor();
            }
            else if (generalErrorSound != null)
            {
                AudioSource.PlayClipAtPoint(generalErrorSound, mainCamera.transform.position);
            }
        }

        // --------------------------------------------------------------------------------
        // FLOOR SWITCHING (Q/E)
        // --------------------------------------------------------------------------------

        private void HandleFloorSwitch()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                // Go DOWN one floor (minimum = 0)
                if (_currentFloor > 0)
                {
                    _currentFloor--;
                    NotifyCameraFloorChanged();
                }
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                // Go UP one floor (max = highest occupied floor + 1)
                if (_currentFloor < _maxOccupiedFloor + 1)
                {
                    _currentFloor++;
                    NotifyCameraFloorChanged();
                }
            }
        }

        /// <summary>
        /// Recalculate the highest floor that has at least one structure placed on it.
        /// Called after placing, moving, or deleting structures.
        /// </summary>
        private void RecalculateMaxFloor()
        {
            _maxOccupiedFloor = 0;
            foreach (var unit in _placedStructures)
            {
                if (unit == null) continue;
                int floor = GetFloorFromY(unit.transform.position.y);
                if (floor > _maxOccupiedFloor) _maxOccupiedFloor = floor;
            }
        }

        /// <summary>
        /// Convert a world Y position to a floor index (0-based).
        /// Floor 0 = ground level, Floor 1 = one heightStep up, etc.
        /// </summary>
        public int GetFloorFromY(float worldY)
        {
            float step = heightStep > 0f ? heightStep : gridSize;
            return Mathf.Max(0, Mathf.RoundToInt(worldY / step));
        }

        /// <summary>
        /// Get the world Y position for a given floor index.
        /// </summary>
        public float GetFloorY(int floor)
        {
            float step = heightStep > 0f ? heightStep : gridSize;
            return floor * step;
        }

        private void NotifyCameraFloorChanged()
        {
            if (_cameraController != null)
            {
                _cameraController.SetFloorView(_currentFloor, GetFloorY(_currentFloor));
            }
        }

        public void TriggerCameraShake(float intensity)
        {
            if (_cameraController != null)
            {
                _cameraController.TriggerShake(intensity);
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

            LayerMask combinedMask = groundLayer | structureLayer;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

            // ใช้ RaycastAll แล้วเรียงจากใกล้ → ไกล
            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(ray, 500f, combinedMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // อ่านชุด occluded colliders จากกล้อง (ถ้ามี)
            var occluded = _cameraController != null ? _cameraController.OccludedColliders : null;

            foreach (var hit in hits)
            {
                // ข้าม collider ที่กล้องกำลังทำโปร่งใสอยู่
                if (occluded != null && occluded.Contains(hit.collider)) continue;

                _currentHitPos      = hit.point;
                _currentHitNormal   = hit.normal;
                _currentHitCollider = hit.collider;
                _hasValidTarget     = true;
                break; // เจอ hit แรกที่ไม่ถูกบัง → หยุด
            }
        }

        // --------------------------------------------------------------------------------
        // HOVER HIGHLIGHT (Select items to Move/Delete)
        // --------------------------------------------------------------------------------

        private void HandleHoverHighlight()
        {
            // Only highlight in Move, Delete, or Paint modes when NOT currently holding an object
            bool canHighlight = _currentMode == BuildMode.Moving || _currentMode == BuildMode.Deleting || _currentMode == BuildMode.Painting;
            bool isHolding = _currentMode == BuildMode.Moving && _movingUnit != null;

            if (!canHighlight || isHolding)
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

            // Skip the frame where we just entered placing mode (prevents UI click from placing)
            if (_justEnteredPlacing)
            {
                _justEnteredPlacing = false;
                return;
            }

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
            
            // Save original state for Undo
            _moveOriginalPos = unit.transform.position;
            _moveOriginalRot = unit.Rotation;
            var joint = unit.GetComponent<Joint>();
            if (joint != null && joint.connectedBody != null)
                _moveOriginalTargetCol = joint.connectedBody.GetComponentInChildren<Collider>();
            else
                _moveOriginalTargetCol = null;
                
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
        // PAINT MODE
        // --------------------------------------------------------------------------------

        private void HandlePaintingMode()
        {
            if (Input.GetMouseButtonDown(1)) { ExitMode(); return; }

            if (Input.GetMouseButtonDown(0) && _hoveredUnit != null && _selectedMaterial != null)
            {
                ApplyMaterialToStructure(_hoveredUnit, _selectedMaterial);
            }
        }

        // --------------------------------------------------------------------------------
        // PUBLIC INTERFACE (for UI)
        // --------------------------------------------------------------------------------

        public void SelectStructure(StructureData data)
        {
            // Save current material before ExitMode clears it
            MaterialData savedMaterial = _selectedMaterial;
            ExitMode();
            _selectedData = data;
            _selectedMaterial = savedMaterial; // Restore material selection
            _currentMode = BuildMode.Placing;
            _justEnteredPlacing = true; // Prevent this frame's click from placing

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

        public void EnterPaintMode()
        {
            ExitMode();
            _currentMode = BuildMode.Painting;
        }

        public void SelectMaterial(MaterialData material)
        {
            _selectedMaterial = material;
            // Material selection persists across mode changes
            // Ghost appearance update could be added here if desired
        }

        public void ExitMode()
        {
            if (_movingUnit != null) _movingUnit.gameObject.SetActive(true);
            _movingUnit = null;
            _selectedData = null;
            // Material persists across mode changes — don't clear it here
            _currentMode = BuildMode.Idle;
            _justEnteredPlacing = false;
            ghostBuilder.DestroyGhost();
            ClearHover();
        }

        /// <summary>
        /// Fully clear the selected material (e.g. from a "reset material" button).
        /// </summary>
        public void ClearMaterial()
        {
            _selectedMaterial = null;
        }

        // --------------------------------------------------------------------------------
        // INTERNAL LOGIC
        // --------------------------------------------------------------------------------

        private void PlaceStructure(Vector3 position, float rotation, Collider targetCollider = null)
        {
            MaterialData mat = _selectedMaterial != null ? _selectedMaterial : _selectedData.defaultMaterial;
            float materialPrice = mat != null ? mat.priceModifier : 0f;
            float totalCost = _selectedData.basePrice + materialPrice;

            GameObject obj = Instantiate(_selectedData.prefab, position, Quaternion.Euler(0, rotation, 0));
            SetLayerRecursively(obj, structureLayer);
            obj.name = $"{_selectedData.prefab.name} {GetGridPositionString(position)}";

            StructureUnit unit = obj.GetComponent<StructureUnit>() ?? obj.AddComponent<StructureUnit>();
            unit.Initialize(_selectedData, mat, rotation);
            
            // Start disabled so the Command can enable it
            obj.SetActive(false);

            ExecuteCommand(
                execute: () => {
                    _currentBudget -= totalCost;
                    obj.SetActive(true);
                    AttachJoint(obj, targetCollider);
                    _placedStructures.Add(unit);
                    IgnoreOverlappingCollisions(unit);

                    if (mat != null)
                    {
                        if (mat.placeSound != null) AudioSource.PlayClipAtPoint(mat.placeSound, position);
                        if (mat.placeVFX != null) Instantiate(mat.placeVFX, position, Quaternion.identity);
                    }
                    else if (generalPlaceSound != null)
                    {
                        AudioSource.PlayClipAtPoint(generalPlaceSound, position);
                    }
                },
                undo: () => {
                    _currentBudget += totalCost;
                    _placedStructures.Remove(unit);
                    
                    var joints = obj.GetComponents<Joint>();
                    foreach (var j in joints) Destroy(j);
                    
                    obj.SetActive(false);
                }
            );

            RecalculateMaxFloor();
        }

        private void ConfirmMove(Vector3 position, float rotation, Collider targetCollider = null)
        {
            Vector3 oldPos = _moveOriginalPos;
            float oldRot = _moveOriginalRot;
            Collider oldTarget = _moveOriginalTargetCol;
            StructureUnit unit = _movingUnit;

            ExecuteCommand(
                execute: () => {
                    unit.transform.position = position;
                    unit.transform.rotation = Quaternion.Euler(0, rotation, 0);
                    unit.SetRotation(rotation);
                    unit.name = $"{unit.Data.prefab.name} {GetGridPositionString(position)}";
                    unit.gameObject.SetActive(true);
                    AttachJoint(unit.gameObject, targetCollider);
                    IgnoreOverlappingCollisions(unit);

                    if (unit.CurrentMaterial != null && unit.CurrentMaterial.placeSound != null) 
                        AudioSource.PlayClipAtPoint(unit.CurrentMaterial.placeSound, position);
                    else if (generalPlaceSound != null)
                        AudioSource.PlayClipAtPoint(generalPlaceSound, position);
                },
                undo: () => {
                    unit.transform.position = oldPos;
                    unit.transform.rotation = Quaternion.Euler(0, oldRot, 0);
                    unit.SetRotation(oldRot);
                    unit.name = $"{unit.Data.prefab.name} {GetGridPositionString(oldPos)}";
                    unit.gameObject.SetActive(true);
                    AttachJoint(unit.gameObject, oldTarget);
                    IgnoreOverlappingCollisions(unit);
                }
            );

            _movingUnit = null;
            ghostBuilder.DestroyGhost();
            RecalculateMaxFloor();
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

                // Ignore physics collision between the structure and ALL colliders of the target
                // (e.g. if the ground has multiple colliders, ignore all of them).
                Collider[] myColliders = structureObj.GetComponentsInChildren<Collider>();
                Collider[] targetColliders = targetCollider.transform.root.GetComponentsInChildren<Collider>();
                
                foreach (var col in myColliders)
                {
                    foreach (var targetCol in targetColliders)
                    {
                        UnityEngine.Physics.IgnoreCollision(col, targetCol, true);
                    }
                }
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
            float sellPrice = unit.Data.basePrice + materialPrice;

            Collider targetCol = null;
            var joint = unit.GetComponent<Joint>();
            if (joint != null && joint.connectedBody != null)
            {
                targetCol = joint.connectedBody.GetComponentInChildren<Collider>();
            }

            ExecuteCommand(
                execute: () => {
                    _currentBudget += sellPrice;
                    _placedStructures.Remove(unit);
                    
                    var joints = unit.GetComponents<Joint>();
                    foreach (var j in joints) Destroy(j);
                    
                    unit.gameObject.SetActive(false);
                    
                    if (generalSellSound != null) AudioSource.PlayClipAtPoint(generalSellSound, unit.transform.position);
                    if (generalSellVFX != null) Instantiate(generalSellVFX, unit.transform.position, Quaternion.identity);
                },
                undo: () => {
                    _currentBudget -= sellPrice;
                    unit.gameObject.SetActive(true);
                    AttachJoint(unit.gameObject, targetCol);
                    _placedStructures.Add(unit);
                    IgnoreOverlappingCollisions(unit);
                }
            );

            RecalculateMaxFloor();
        }

        private void ApplyMaterialToStructure(StructureUnit unit, MaterialData material)
        {
            if (unit.CurrentMaterial == material) return;

            MaterialData oldMaterial = unit.CurrentMaterial;
            MaterialData newMaterial = material;
            
            float oldPrice = oldMaterial != null ? oldMaterial.priceModifier : 0f;
            float newPrice = newMaterial.priceModifier;
            float diff = newPrice - oldPrice;

            if (_currentBudget < diff)
            {
                if (generalErrorSound != null) AudioSource.PlayClipAtPoint(generalErrorSound, mainCamera.transform.position);
                return; // Can't afford
            }

            ExecuteCommand(
                execute: () => {
                    _currentBudget -= diff;
                    unit.ChangeMaterial(newMaterial);
                    
                    var stress = unit.GetComponent<Simulation.Physics.StructuralStress>();
                    if (stress != null)
                    {
                        float comp = unit.Data.baseMaxCompression + newMaterial.compressionModifier;
                        float tens = unit.Data.baseMaxTension     + newMaterial.tensionModifier;
                        stress.InitializeStress(unit.CurrentHP, comp, tens);
                    }

                    if (newMaterial.placeSound != null) 
                        AudioSource.PlayClipAtPoint(newMaterial.placeSound, unit.transform.position);
                    else if (generalPaintSound != null)
                        AudioSource.PlayClipAtPoint(generalPaintSound, unit.transform.position);
                        
                    if (newMaterial.placeVFX != null) Instantiate(newMaterial.placeVFX, unit.transform.position, Quaternion.identity);
                },
                undo: () => {
                    _currentBudget += diff;
                    unit.ChangeMaterial(oldMaterial);
                    
                    var stress = unit.GetComponent<Simulation.Physics.StructuralStress>();
                    if (stress != null)
                    {
                        float comp = unit.Data.baseMaxCompression + (oldMaterial != null ? oldMaterial.compressionModifier : 0f);
                        float tens = unit.Data.baseMaxTension     + (oldMaterial != null ? oldMaterial.tensionModifier : 0f);
                        stress.InitializeStress(unit.CurrentHP, comp, tens);
                    }
                }
            );
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
                if (hitUnit != null && hitUnit.Data != null && hitUnit.Data.prefab != null)
                {
                    // Use real prefab bounds for consistent Y calculation
                    float hitUnitPivotToBottom = GetPivotToBottomOffset(hitUnit.Data.prefab);
                    float hitUnitPivotToTop    = GetPivotToTopOffset(hitUnit.Data.prefab);

                    // bottomY = world Y of the bottom face of hitUnit
                    float bottomY = hitUnit.transform.position.y - hitUnitPivotToBottom;
                    // topY    = world Y of the top face of hitUnit
                    float topY    = hitUnit.transform.position.y + hitUnitPivotToTop;

                    // Determine if the ray hit a SIDE face (horizontal normal)
                    // vs a TOP/BOTTOM face (vertical normal)
                    bool isSideHit = Mathf.Abs(_currentHitNormal.y) < 0.5f;

                    if (isSideHit)
                    {
                        // Side placement: place at the same base level as the hit structure
                        // so structures can be built outward horizontally
                        y = bottomY;
                    }
                    else if (hitUnit.Data.placementSinkThrough)
                    {
                        // Sink-through: start from the BOTTOM of the floor/slab
                        // so placed wall shares the same ground level as walls on bare ground
                        y = bottomY;
                    }
                    else
                    {
                        // Normal stacking: place on TOP of the hit structure
                        y = topY;
                    }
                }
            }

            // Snap the desired BASE/BOTTOM position to the heightStep grid so all structures share the same discrete height levels
            if (snapYToGrid)
            {
                float yStep = heightStep > 0f ? heightStep : gridSize;
                if (yStep > 0f)
                {
                    y = Mathf.Round(y / yStep) * yStep;
                }
            }

            // Finally, add pivot-to-bottom offset of the piece being placed so its bottom lands exactly at y
            y += _pivotToBottomOffset;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Distance from the prefab pivot to its TOP face (positive value).
        /// </summary>
        private float GetPivotToTopOffset(GameObject prefab)
        {
            (Vector3 center, Vector3 size) = GetPrefabBounds(prefab);
            // top = center.y + size.y * 0.5f  →  offset from pivot = center.y + size.y * 0.5f
            return center.y + size.y * 0.5f;
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
                Vector3 size = Vector3.Scale(bc.size, bc.transform.lossyScale);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

                Vector3 offsetFromRoot = bc.transform.TransformPoint(bc.center) - prefab.transform.position;
                Vector3 unrotatedOffset = Quaternion.Inverse(prefab.transform.rotation) * offsetFromRoot;

                return (unrotatedOffset, size);
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
                    
                    Vector3 worldCenter = r.transform.TransformPoint(localB.center);
                    Vector3 offsetFromRoot = worldCenter - prefab.transform.position;
                    Vector3 unrotatedCenter = Quaternion.Inverse(prefab.transform.rotation) * offsetFromRoot;
                    
                    Vector3 worldSize = Vector3.Scale(localB.size, r.transform.lossyScale);
                    worldSize = new Vector3(Mathf.Abs(worldSize.x), Mathf.Abs(worldSize.y), Mathf.Abs(worldSize.z));
                    
                    Bounds transformedBounds = new Bounds(unrotatedCenter, worldSize);

                    if (!boundsInitialized) { prefabBounds = transformedBounds; boundsInitialized = true; }
                    else { prefabBounds.Encapsulate(transformedBounds); }
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

            (Vector3 localCenter, Vector3 localSize) = GetPrefabBounds(data.prefab);
            Vector3 extents = localSize * 0.5f;

            // Swap X and Z for 90 or 270 degree rotations
            if (Mathf.Abs(rotation % 180f) > 45f)
            {
                extents = new Vector3(extents.z, extents.y, extents.x);
            }

            // Calculate world center using the exact calculated rotation
            Quaternion globalRot = Quaternion.Euler(0, rotation, 0) * data.prefab.transform.rotation;
            Vector3 worldCenter = position + (globalRot * localCenter);

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

                // Check for exact duplicate placements regardless of allowOverlap
                float dist = Vector3.Distance(position, unit.transform.position);
                float rotDiff = Quaternion.Angle(Quaternion.Euler(0, rotation, 0), Quaternion.Euler(0, unit.Rotation, 0));
                
                if (dist < 0.1f && rotDiff < 1f)
                {
                    // Perfectly overlapping identical positions are never allowed
                    return false; 
                }

                // If they are DIFFERENT structural types, we allow them to overlap!
                if (structureData != unit.Data)
                {
                    continue;
                }

                // If both allow overlap and are the same type, skip
                if (structureData.allowOverlap || (unit.Data != null && unit.Data.allowOverlap)) 
                {
                    continue;
                }

                Bounds boundsB = GetGridBounds(unit.transform.position, unit.Rotation, unit.Data);

                if (boundsA.Intersects(boundsB))
                {
                    return false;
                }
            }

            return true;
        }

        private void IgnoreOverlappingCollisions(StructureUnit newUnit)
        {
            if (newUnit == null || newUnit.Data == null || newUnit.Data.prefab == null) return;
            
            Collider[] myColliders = newUnit.GetComponentsInChildren<Collider>(true);
            if (myColliders.Length == 0) return;

            (Vector3 localCenter, Vector3 localSize) = GetPrefabBounds(newUnit.Data.prefab);
            
            // To deeply catch any horizontal overlap (including kissing corners) without ignoring the ground/ceiling:
            // We use 55% for X/Z to cast a slightly wider net horizontally.
            // We use 45% for Y to shrink it vertically, ensuring we don't accidentally ignore collisions with the floor it stands on.
            Vector3 halfExtents = new Vector3(
                localSize.x * 0.55f, 
                localSize.y * 0.45f, 
                localSize.z * 0.55f
            );
            
            Quaternion rot = Quaternion.Euler(0, newUnit.Rotation, 0) * newUnit.Data.prefab.transform.rotation;
            Vector3 worldCenter = newUnit.transform.position + rot * localCenter;

            Collider[] hits = UnityEngine.Physics.OverlapBox(worldCenter, halfExtents, rot, structureLayer);

            foreach (var hit in hits)
            {
                // Skip our own colliders
                if (hit.transform.root == newUnit.transform.root) continue;

                foreach (var col in myColliders)
                {
                    // Ignore physics collisions completely so they don't produce extreme repulsion forces & HP loss
                    UnityEngine.Physics.IgnoreCollision(col, hit, true);
                }
            }
        }

        // --------------------------------------------------------------------------------
        // GRID VISUALIZATION (Scene View)
        // --------------------------------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw the grid in Scene view based on gridColumns × gridRows
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.3f);

            float totalWidth  = gridColumns * gridSize;
            float totalDepth  = gridRows * gridSize;
            float startX = -totalWidth * 0.5f;
            float startZ = -totalDepth * 0.5f;

            // Draw current floor level
            float floorY = Application.isPlaying ? GetFloorY(_currentFloor) : 0f;

            // Vertical lines (along Z)
            for (int x = 0; x <= gridColumns; x++)
            {
                float xPos = startX + x * gridSize;
                Gizmos.DrawLine(
                    new Vector3(xPos, floorY, startZ),
                    new Vector3(xPos, floorY, startZ + totalDepth)
                );
            }

            // Horizontal lines (along X)
            for (int z = 0; z <= gridRows; z++)
            {
                float zPos = startZ + z * gridSize;
                Gizmos.DrawLine(
                    new Vector3(startX, floorY, zPos),
                    new Vector3(startX + totalWidth, floorY, zPos)
                );
            }

            // Draw border in a brighter color
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.7f);
            Vector3 bottomLeft  = new Vector3(startX, floorY, startZ);
            Vector3 bottomRight = new Vector3(startX + totalWidth, floorY, startZ);
            Vector3 topLeft     = new Vector3(startX, floorY, startZ + totalDepth);
            Vector3 topRight    = new Vector3(startX + totalWidth, floorY, startZ + totalDepth);
            Gizmos.DrawLine(bottomLeft, bottomRight);
            Gizmos.DrawLine(bottomRight, topRight);
            Gizmos.DrawLine(topRight, topLeft);
            Gizmos.DrawLine(topLeft, bottomLeft);
        }
#endif
    }
}
