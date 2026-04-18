using UnityEngine;
using System.Collections.Generic;
using Simulation.Data;

namespace Simulation.Building
{
    /// <summary>
    /// ระบบก่อสร้างตึก 3D (Building System)
    /// - วางชิ้นส่วนบนพื้นหรือซ้อนทับบนโครงสร้างเดิม
    /// - รองรับการสร้างตึกสูง/ต่อเติมชั้น
    /// - ขนาดอ้างอิงจาก Bounds ของ Prefab
    /// - แสดง Ghost Preview ตามตำแหน่งเมาส์
    /// โหมด: Idle, Placing, Moving, Deleting
    /// </summary>
    public class BuildingSystem : MonoBehaviour
    {
        public enum BuildMode { Idle, Placing, Moving, Deleting }

        public static BuildingSystem Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private bool useGridSnap = true;
        [SerializeField] private float gridSize = 1f;  // fallback — ถ้ามี GridManager จะใช้ CurrentGridSize แทน

        [Header("Layer Masks")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private LayerMask structureLayer;

        [Header("Layer Level Settings")]
        public int currentLevel = 0;
        [Tooltip("The actual GameObject containing the grid renderer/mesh. It will be moved up/down based on the current level.")]
        public Transform gridVisual;

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
        /// <summary>Read-only access for external systems (Physics, etc.)</summary>
        public IReadOnlyList<StructureUnit> PlacedStructures => _placedStructures;
        private StructureUnit _movingUnit;
        private StructureUnit _hoveredUnit;

        // Placement Temp Data
        private Vector3 _currentHitPos;
        private Vector3 _currentHitNormal;
        private Collider _currentHitCollider;
        private float _pivotToBottomOffset = 0f;
        private bool _hasValidTarget;

        public float CurrentBudget => _currentBudget;
        public float InitialBudget => initialBudget;
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
            // Q/E เปลี่ยนชั้นได้เสมอ แม้ขณะจำลอง
            HandleLevelNavigation();

            // ขณะจำลองอยู่ ปิดโหมดสร้างทั้งหมด แต่ยังดูได้
            if (Simulation.Physics.SimulationManager.Instance != null && Simulation.Physics.SimulationManager.Instance.IsSimulating)
            {
                if (_currentMode != BuildMode.Idle) ExitMode();
                return;
            }

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
        // LEVEL NAVIGATION
        // --------------------------------------------------------------------------------

        private void HandleLevelNavigation()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                ChangeLevel(1);
            }
            else if (Input.GetKeyDown(KeyCode.Q))
            {
                ChangeLevel(-1);
            }
        }

        private void ChangeLevel(int delta)
        {
            currentLevel += delta;

            // จำกัดชั้น: ต่ำสุด 0, สูงสุด = ชั้นบนสุดที่มีของวางอยู่ + 1
            int maxLevel = GetHighestOccupiedLevel() + 1;
            currentLevel = Mathf.Clamp(currentLevel, 0, maxLevel);
            
            float step = GridManager.Instance != null ? GridManager.Instance.CurrentHeightStep : 3f;
            float newY = currentLevel * step;
            
            if (_cameraController != null)
            {
                Vector3 pivot = _cameraController.PivotPoint;
                pivot.y = newY;
                _cameraController.FocusOn(pivot);
            }

            if (gridVisual != null)
            {
                gridVisual.position = new Vector3(gridVisual.position.x, newY, gridVisual.position.z);
            }
        }

        /// <summary>
        /// คำนวณชั้นสูงสุดที่มีวัตถุวางอยู่ โดยอ้างอิงจาก Y position ของวัตถุทั้งหมด
        /// </summary>
        private int GetHighestOccupiedLevel()
        {
            if (_placedStructures.Count == 0) return 0;

            float step = GridManager.Instance != null ? GridManager.Instance.CurrentHeightStep : 3f;
            if (step <= 0f) step = 3f;

            float highestY = 0f;
            foreach (var unit in _placedStructures)
            {
                if (unit == null) continue;
                if (unit.transform.position.y > highestY)
                    highestY = unit.transform.position.y;
            }

            return Mathf.FloorToInt(highestY / step);
        }

        // --------------------------------------------------------------------------------
        // GRID VISUAL — ซ่อน/แสดง (เรียกจาก SimulationManager)
        // --------------------------------------------------------------------------------

        /// <summary>ซ่อน Grid Visual — เรียกตอนเริ่มจำลอง</summary>
        public void HideGridVisual()
        {
            if (gridVisual != null) gridVisual.gameObject.SetActive(false);
        }

        /// <summary>แสดง Grid Visual — เรียกตอนหยุดจำลองหรือรีเซ็ต</summary>
        public void ShowGridVisual()
        {
            if (gridVisual != null) gridVisual.gameObject.SetActive(true);
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

            bool foundPhysical = false;
            foreach (var hit in hits)
            {
                // ข้าม collider ที่กล้องกำลังทำโปร่งใสอยู่
                if (occluded != null && occluded.Contains(hit.collider)) continue;

                _currentHitPos      = hit.point;
                _currentHitNormal   = hit.normal;
                _currentHitCollider = hit.collider;
                _hasValidTarget     = true;
                foundPhysical = true;
                break; // เจอ hit แรกที่ไม่ถูกบัง → หยุด
            }

            float targetY = currentLevel * (GridManager.Instance != null ? GridManager.Instance.CurrentHeightStep : 3f);
            Plane levelPlane = new Plane(Vector3.up, new Vector3(0, targetY, 0));
            
            if (levelPlane.Raycast(ray, out float distance))
            {
                Vector3 planePoint = ray.GetPoint(distance);
                
                if (!foundPhysical || _currentHitPos.y < targetY - 0.1f)
                {
                    _currentHitPos = planePoint;
                    _currentHitNormal = Vector3.up;
                    _currentHitCollider = null;
                    _hasValidTarget = true;
                }
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

        /// <summary>
        /// Override the current budget (called by MissionSystem when loading a mission).
        /// Also updates the serialized initialBudget so Reset can restore it.
        /// </summary>
        public void SetBudget(float amount)
        {
            initialBudget = amount;
            _currentBudget = amount;
        }

        /// <summary>
        /// Destroy every placed structure and clear the grid.
        /// Called by MissionSystem.ResetMission() for a full rebuild.
        /// Budget is NOT restored here — caller is responsible for that.
        /// </summary>
        public void ResetAllStructures()
        {
            ExitMode();

            // Destroy all placed GameObjects and clear the list
            foreach (var unit in _placedStructures)
            {
                if (unit != null) Destroy(unit.gameObject);
            }
            _placedStructures.Clear();
            StructureRegistry.Clear();

            // Clear the entire grid registry
            if (GridManager.Instance != null)
                GridManager.Instance.ClearAll();

            // Restore budget
            _currentBudget = initialBudget;
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
            StructureRegistry.Register(unit);
            
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
            StructureRegistry.Unregister(unit);
            unit.DestroyStructure();
        }

        // --------------------------------------------------------------------------------
        // HELPER FUNCTIONS
        // --------------------------------------------------------------------------------

        private Vector3 CalculatePlacementPosition(Vector3 hitPoint)
        {
            if (GridManager.Instance == null) return hitPoint;

            BuildType currentBuildType = BuildType.Structure;
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
                // Non-floors: snap XZ to half-grid for edge/corner alignment
                float gs = GridManager.Instance != null ? GridManager.Instance.CurrentGridSize : gridSize;
                float snapStep = (gs > 0 ? gs : 1f) * 0.5f; 
                x = useGridSnap ? Mathf.Round(hitPoint.x / snapStep) * snapStep : hitPoint.x;
                z = useGridSnap ? Mathf.Round(hitPoint.z / snapStep) * snapStep : hitPoint.z;

                // [Fix 1] บังคับให้ Y สนิทกับพื้นระดับชั้น (ไม่ต้องกลัวมันลอยหรือยื่นออกจากขอบเมื่อเล็งผิด)
                float hs = GridManager.Instance != null ? GridManager.Instance.CurrentHeightStep : 3f;
                // ถ้าเป็นชั้น 0 ก็คือ 0, ชั้นถัดไปก็ความสูง 1 ชั้น
                y = currentLevel * hs;

                // Auto-Stack: ถ้ามี structure อยู่ที่จุดเดียวกัน (XZ) → วางซ้อนบนหัวอัตโนมัติ
                StructureData currentData = _selectedData != null ? _selectedData : (_movingUnit != null ? _movingUnit.Data : null);
                if (currentData != null)
                {
                    float tolerance = snapStep * 0.5f;
                    float highestTop = y;

                    foreach (var unit in _placedStructures)
                    {
                        if (unit == null || unit == _movingUnit) continue;
                        
                        float dx = Mathf.Abs(unit.transform.position.x - x);
                        float dz = Mathf.Abs(unit.transform.position.z - z);
                        
                        if (dx < tolerance && dz < tolerance)
                        {
                            // หาจุดสูงสุดของ structure ที่อยู่ตำแหน่งเดียวกัน
                            float topY = unit.transform.position.y + GetPivotToTopOffset(unit.Data.prefab);
                            if (topY > highestTop)
                                highestTop = topY;
                        }
                    }
                    y = highestTop;
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

            Vector3 extents;
            Vector3 localCenter = Vector3.zero;

            // [Fix 3] ถ้ามีการตั้งค่า size ใน Data (ไม่เป็น 1,1,1) ให้ใช้ size นั้นเป็นขนาดบล็อกเลยเพื่อความเป๊ะเวลาทับกัน
            if (data.size != Vector3.one)
            {
                extents = data.size * 0.5f;
                localCenter = new Vector3(0, extents.y, 0); // สมมติว่าจุดหมุนอยู่ฐาน
            }
            else
            {
                // ถ้าค่า default ให้คำนวณจาก prefab
                Vector3 localSize;
                (localCenter, localSize) = GetPrefabBounds(data.prefab);
                extents = localSize * 0.5f;
            }

            // Swap X and Z for 90 or 270 degree rotations
            if (Mathf.Abs(rotation % 180f) > 45f)
            {
                extents = new Vector3(extents.z, extents.y, extents.x);
            }

            // Calculate world center using the exact calculated rotation
            Quaternion globalRot = Quaternion.Euler(0, rotation, 0) * data.prefab.transform.rotation;
            Vector3 worldCenter = position + (globalRot * localCenter);

            // Use 1.99f instead of 2.0f to represent 99.5% full size, 
            // allowing models to barely touch faces without registering as a hard overlap overlap block
            return new Bounds(worldCenter, extents * 1.99f); 
        }

        private bool IsAreaClear(Vector3 placePos, Vector3 hitPos, float rotation, StructureData structureData)
        {
            if (structureData == null) return true;

            // 1. Support Check — ทุกชิ้นต้องมีฐานรองรับ (ห้ามลอย)
            //    Floor ชั้น 0 วางได้เลย (อยู่บนพื้น), ชั้นสูงกว่าต้องมี support ข้างล่าง
            bool isGroundFloor = (structureData.buildType == BuildType.Floor && currentLevel <= 0);

            if (!isGroundFloor)
            {
                Bounds bounds = GetGridBounds(placePos, rotation, structureData);
                // เช็คบริเวณ "ใต้ฐาน" ของ Structure
                Vector3 center = bounds.center - new Vector3(0, bounds.extents.y, 0);
                Vector3 extents = bounds.extents;
                
                extents.x *= 0.8f;
                extents.z *= 0.8f;
                extents.y = 0.25f;

                Transform selfRoot = _movingUnit != null ? _movingUnit.transform : null;
                bool hasSupport = Simulation.Physics.SupportQuery.HasAnySupportBelow(
                    center, extents, structureLayer | groundLayer, selfRoot);

                if (!hasSupport) return false;
            }

            // 2. Object Overlap Check (Cannot sink into another)
            Bounds boundsA = GetGridBounds(placePos, rotation, structureData);

            foreach (var unit in _placedStructures)
            {
                if (unit == _movingUnit || unit == null) continue;

                // Ensure same position/rotation perfectly overlapping duplicate prevention
                float dist = Vector3.Distance(placePos, unit.transform.position);
                float rotDiff = Quaternion.Angle(Quaternion.Euler(0, rotation, 0), Quaternion.Euler(0, unit.Rotation, 0));
                
                // Check if they are exactly the same type. If they are different, allow them to overlap.
                if (structureData == unit.Data)
                {
                    // Hard overlap check: วัตถุชนดิเดียวกันทับกันไม่ได้
                    // ลดขนาดกล่อง 10% เพื่อให้วางชิดผิวกันได้ แต่ห้ามซ้อนทับ
                    Bounds boundsB = GetGridBounds(unit.transform.position, unit.Rotation, unit.Data);
                    boundsB.Expand(-0.1f);
                    
                    Bounds testBoundsA = boundsA;
                    testBoundsA.Expand(-0.1f);

                    if (testBoundsA.Intersects(boundsB)) return false;
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
            
            // To deeply catch any overlap (including kissing corners/edges and adjacent floors):
            // We use 55% for all axes. By ignoring collision with the structural floor beneath/adjacent to it,
            // we eliminate false collision impulses that would otherwise sum up and continuously damage edge walls.
            Vector3 halfExtents = new Vector3(
                localSize.x * 0.55f, 
                localSize.y * 0.55f, 
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
