using System.Collections.Generic;
using UnityEngine;
using Simulation.Data;

namespace Simulation.Building
{
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        [Header("Grid References")]
        [Tooltip("ลาก StructureData ของ Floor เข้ามา — ระบบจะใช้ขนาดจาก Prefab นี้เป็นขนาด 1 ช่อง Grid (X/Z)")]
        public StructureData floorReference;

        [Tooltip("ลาก StructureData ของเสาเข้ามา — ระบบจะใช้ความสูงจาก Prefab นี้เป็น 1 ชั้น (Y)")]
        public StructureData levelHeightReference;

        [Header("Fallback (ใช้เมื่อไม่ได้ลาก Reference)")]
        public float fallbackGridSize = 1f;
        public float fallbackHeightStep = 1f;

        /// <summary>ขนาด 1 ช่อง Grid (X/Z) — อ้างอิงจาก Floor Prefab</summary>
        public float CurrentGridSize
        {
            get
            {
                if (floorReference != null)
                {
                    // [Fix 2] ใช้ขนาด size ที่ตั้งใน Data ตรงๆ (กว้าง x ยาว ใช้อันที่ยาวสุด)
                    return Mathf.Max(floorReference.size.x, floorReference.size.z);
                }
                return fallbackGridSize;
            }
        }

        /// <summary>ความสูง 1 ชั้น (Y) — อ้างอิงจากเสา Prefab</summary>
        public float CurrentHeightStep => levelHeightReference != null ? levelHeightReference.GetActualSize().y : fallbackHeightStep;

        private Dictionary<Vector3Int, GridCell> _grid = new Dictionary<Vector3Int, GridCell>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            float gs = CurrentGridSize > 0 ? CurrentGridSize : 1f;
            float hs = CurrentHeightStep > 0 ? CurrentHeightStep : 1f;

            int x = Mathf.RoundToInt(worldPos.x / gs);
            int y = Mathf.RoundToInt(worldPos.y / hs);
            int z = Mathf.RoundToInt(worldPos.z / gs);
            return new Vector3Int(x, y, z);
        }

        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * CurrentGridSize,
                gridPos.y * CurrentHeightStep,
                gridPos.z * CurrentGridSize
            );
        }

        public GridCell GetCell(Vector3Int gridPos)
        {
            if (_grid.TryGetValue(gridPos, out GridCell cell))
            {
                return cell;
            }
            return null;
        }

        public GridCell GetOrCreateCell(Vector3Int gridPos)
        {
            if (!_grid.TryGetValue(gridPos, out GridCell cell))
            {
                cell = new GridCell(gridPos);
                _grid[gridPos] = cell;
            }
            return cell;
        }

        public bool CanPlaceObject(Vector3Int gridPos, BuildType buildType)
        {
            GridCell cell = GetCell(gridPos);
            
            if (buildType == BuildType.Floor)
            {
                // ห้ามวาง Floor ซ้ำในช่องเดียวกัน
                if (cell != null && cell.HasFloor) return false;
                return true; 
            }
            else if (buildType == BuildType.Structure)
            {
                // Structure ต้องมี Floor อยู่ก่อน
                if (cell == null || !cell.HasFloor) return false;
                
                return true;
            }
            
            return true;
        }

        public void RegisterPlacement(Vector3Int gridPos, StructureUnit unit)
        {
            GridCell cell = GetOrCreateCell(gridPos);
            BuildType type = unit.Data != null ? unit.Data.buildType : BuildType.Structure;

            if (type == BuildType.Floor) cell.Floor = unit;
            else if (type == BuildType.Structure) cell.Structure = unit;
        }

        public void UnregisterPlacement(Vector3Int gridPos, StructureUnit unit)
        {
            GridCell cell = GetCell(gridPos);
            if (cell == null) return;

            if (cell.Floor == unit) cell.Floor = null;
            else if (cell.Structure == unit) cell.Structure = null;
            
            // Clean up empty cells
            if (!cell.HasFloor && !cell.HasStructure)
            {
                _grid.Remove(gridPos);
            }
        }

        /// <summary>
        /// Wipe every cell in the grid. Called by BuildingSystem.ResetAllStructures().
        /// </summary>
        public void ClearAll()
        {
            _grid.Clear();
        }
    }
}
