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
            // หา CameraController จากกล้องหลัก
            if (mainCamera != null)
                _cameraController = mainCamera.GetComponent<Simulation.Camera.CameraController>();
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
                bool isClear = IsAreaClear(placePos, _currentHitPos, ghostBuilder.CurrentRotation, _selectedData);
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

            if (GridManager.Instance != null)
            {
                // Unregister the unit from its old grid position when picked up
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(_movingUnit.transform.position);
                GridManager.Instance.UnregisterPlacement(gridPos, _movingUnit);
            }

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
                
                bool isClear = IsAreaClear(placePos, _currentHitPos, ghostBuilder.CurrentRotation, _movingUnit.Data);
                ghostBuilder.SetValid(isClear);
            }

            if (Input.GetMouseButtonDown(0) && _hasValidTarget && ghostBuilder.IsValid)
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
            
            if (GridManager.Instance != null)
            {
                // Register placement mathematically on the grid
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(position);
                GridManager.Instance.RegisterPlacement(gridPos, unit);
            }

            // Ignore collisions with deeply overlapping objects so they don't explode and lose HP
            IgnoreOverlappingCollisions(unit);

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
            
            if (GridManager.Instance != null)
            {
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(position);
                GridManager.Instance.RegisterPlacement(gridPos, _movingUnit);
            }

            // Re-eval ignoring collisions in new location
            IgnoreOverlappingCollisions(_movingUnit);

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
            if (GridManager.Instance != null)
            {
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(unit.transform.position);
                GridManager.Instance.UnregisterPlacement(gridPos, unit);
            }

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
            if (GridManager.Instance == null) return hitPoint;

            BuildType currentBuildType = BuildType.Object;
            if (_selectedData != null) currentBuildType = _selectedData.buildType;
            else if (_movingUnit != null && _movingUnit.Data != null) currentBuildType = _movingUnit.Data.buildType;

            float x = hitPoint.x;
            float z = hitPoint.z;
            float y = hitPoint.y;

            if (currentBuildType == BuildType.Floor)
            {
                // Floors MUST lock to the exact center of the grid cell
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(hitPoint);
                Vector3 cellWorldPos = GridManager.Instance.GridToWorld(gridPos);
                x = cellWorldPos.x;
                y = cellWorldPos.y;
                z = cellWorldPos.z;
            }
            else
            {
                // Non-floors can be placed freely on the floor, subject to optional grid snap setting
                // Use half-grid snapping for walls/objects so they align to edges/corners perfectly.
                float snapStep = (gridSize > 0 ? gridSize : 1f) * 0.5f; 
                x = useGridSnap ? Mathf.Round(hitPoint.x / snapStep) * snapStep : hitPoint.x;
                z = useGridSnap ? Mathf.Round(hitPoint.z / snapStep) * snapStep : hitPoint.z;

                // Find a supporting floor near this x/z coordinate to get the exact Y height
                float epsilon = 0.1f;
                Vector3[] checks = { 
                    new Vector3(x, hitPoint.y, z), 
                    new Vector3(x + epsilon, hitPoint.y, z), 
                    new Vector3(x - epsilon, hitPoint.y, z), 
                    new Vector3(x, hitPoint.y, z + epsilon), 
                    new Vector3(x, hitPoint.y, z - epsilon) 
                };

                bool foundFloor = false;
                foreach(var checkPos in checks)
                {
                    Vector3Int tryGrid = GridManager.Instance.WorldToGrid(checkPos);
                    GridCell cell = GridManager.Instance.GetCell(tryGrid);
                    if (cell != null && cell.HasFloor)
                    {
                        y = cell.Floor.transform.position.y + GetPivotToTopOffset(cell.Floor.Data.prefab);
                        foundFloor = true;
                        break;
                    }
                }

                if (!foundFloor)
                {
                    // Fallback if no floor found (which generally means placement is rejected anyway)
                    Vector3Int fallbackGrid = GridManager.Instance.WorldToGrid(hitPoint);
                    y = GridManager.Instance.GridToWorld(fallbackGrid).y;
                }
            }

            // Bring the bottom of the placed object precisely to 'y'
            y += _pivotToBottomOffset;

            // Add exactly 1mm to break perfect mesh contact and stabilize PhysX
            y += 0.001f;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Distance from the prefab pivot to its TOP face (positive value).
        /// </summary>
        private float GetPivotToTopOffset(GameObject prefab)
        {
            (Vector3 center, Vector3 size) = GetPrefabBounds(prefab);
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
            return (size.y * 0.5f) - center.y;
        }

        private (Vector3 center, Vector3 size) GetPrefabBounds(GameObject prefab)
        {
            if (prefab == null) return (Vector3.zero, Vector3.one);

            Bounds combinedLocalBounds = new Bounds();
            bool initialized = false;

            // 1. Check Colliders (Primary source for physics bounds)
            Collider[] colliders = prefab.GetComponentsInChildren<Collider>(true);
            foreach (var col in colliders)
            {
                Bounds localB;
                if (col is BoxCollider bc) 
                    localB = new Bounds(bc.center, bc.size);
                else if (col is CapsuleCollider cc)
                {
                    Vector3 capsuleSize = new Vector3(cc.radius * 2, cc.radius * 2, cc.radius * 2);
                    if (cc.direction == 0) capsuleSize.x = cc.height;
                    else if (cc.direction == 1) capsuleSize.y = cc.height;
                    else capsuleSize.z = cc.height;
                    localB = new Bounds(cc.center, capsuleSize);
                }
                else if (col is SphereCollider sc)
                    localB = new Bounds(sc.center, new Vector3(sc.radius * 2, sc.radius * 2, sc.radius * 2));
                else if (col is MeshCollider mc && mc.sharedMesh != null) 
                    localB = mc.sharedMesh.bounds;
                else 
                    localB = col.bounds; // Note: primitive bounds might be world-relative if called in specific contexts

                // Transform 8 corners into prefab-root local space to handle rotation/scale accurately
                Vector3 min = localB.min;
                Vector3 max = localB.max;
                Vector3[] corners = {
                    new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
                };

                foreach (var corner in corners)
                {
                    // To Local: Root <- World <- Child
                    Vector3 worldPoint = col.transform.TransformPoint(corner);
                    Vector3 localPoint = prefab.transform.InverseTransformPoint(worldPoint);
                    
                    if (!initialized) { combinedLocalBounds = new Bounds(localPoint, Vector3.zero); initialized = true; }
                    else { combinedLocalBounds.Encapsulate(localPoint); }
                }
            }

            // 2. Fallback to Renderers if no colliders were found
            if (!initialized)
            {
                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    Bounds localB = mf.sharedMesh.bounds;
                    Vector3 min = localB.min;
                    Vector3 max = localB.max;
                    Vector3[] corners = {
                        new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                        new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                        new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                        new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
                    };

                    foreach (var corner in corners)
                    {
                        Vector3 worldPoint = r.transform.TransformPoint(corner);
                        Vector3 localPoint = prefab.transform.InverseTransformPoint(worldPoint);
                        
                        if (!initialized) { combinedLocalBounds = new Bounds(localPoint, Vector3.zero); initialized = true; }
                        else { combinedLocalBounds.Encapsulate(localPoint); }
                    }
                }
            }

            if (!initialized) return (Vector3.zero, Vector3.one);

            // [FIX] The combinedLocalBounds is in unscaled local space. We MUST multiply it by the prefab root's 
            // localScale to get the true real-world offset that the object will occupy when instantiated.
            Vector3 finalCenter = Vector3.Scale(combinedLocalBounds.center, prefab.transform.localScale);
            Vector3 finalSize = Vector3.Scale(combinedLocalBounds.size, prefab.transform.localScale);

            return (finalCenter, finalSize);
        }

        private string GetGridPositionString(Vector3 position)
        {
            if (GridManager.Instance != null)
            {
                return GridManager.Instance.WorldToGrid(position).ToString();
            }
            return position.ToString();
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

            // Shrunk bounds by 15% (multiplier 1.7 instead of 2.0 or 1.9) to allow corners to touch perfectly!
            return new Bounds(worldCenter, extents * 1.7f); 
        }

        private bool IsAreaClear(Vector3 placePos, Vector3 hitPos, float rotation, StructureData structureData)
        {
            if (GridManager.Instance == null || structureData == null) return true;

            // 1. Check Floor Support (Grid)
            if (structureData.buildType == BuildType.Floor)
            {
                Vector3Int gridPos = GridManager.Instance.WorldToGrid(hitPos);
                if (!GridManager.Instance.CanPlaceObject(gridPos, structureData.buildType)) return false;
            }
            else
            {
                // Non-floors (Walls/Objects) can sit on edges. Evaluate using mathematically snapped placePos (not raw cursor hitPos)
                // Use a check radius of half-a-grid to ensure if the Wall is on the exact boundary, it touches the supporting floor.
                float epsilon = (gridSize > 0 ? gridSize : 1f) * 0.55f; 
                
                // Construct a base position that sits safely near the floor elevation
                Vector3 basePos = new Vector3(placePos.x, hitPos.y, placePos.z);

                Vector3[] checks = { 
                    basePos,
                    basePos + new Vector3(epsilon, 0, 0),
                    basePos + new Vector3(-epsilon, 0, 0),
                    basePos + new Vector3(0, 0, epsilon),
                    basePos + new Vector3(0, 0, -epsilon)
                };

                bool hasSupport = false;
                foreach(var checkPos in checks)
                {
                    Vector3Int tryGrid = GridManager.Instance.WorldToGrid(checkPos);
                    if (GridManager.Instance.CanPlaceObject(tryGrid, structureData.buildType)) 
                    {
                        hasSupport = true;
                        break;
                    }
                }
                if (!hasSupport) return false;
            }

            // 2. Check Physical Overlaps (Bounding Box)
            Bounds boundsA = GetGridBounds(placePos, rotation, structureData);

            foreach (var unit in _placedStructures)
            {
                if (unit == _movingUnit || unit == null) continue;

                // Ensure same position/rotation perfectly overlapping duplicate prevention
                float dist = Vector3.Distance(placePos, unit.transform.position);
                float rotDiff = Quaternion.Angle(Quaternion.Euler(0, rotation, 0), Quaternion.Euler(0, unit.Rotation, 0));
                
                if (dist < 0.1f && rotDiff < 1f && structureData == unit.Data) return false;

                // Permissive checks
                if (structureData != unit.Data) continue;

                Bounds boundsB = GetGridBounds(unit.transform.position, unit.Rotation, unit.Data);

                if (boundsA.Intersects(boundsB)) return false;
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
    }
}
