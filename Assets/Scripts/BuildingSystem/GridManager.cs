using System.Collections.Generic;
using UnityEngine;
using Simulation.Data;

namespace Simulation.Building
{
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        [Header("Grid Settings")]
        public float gridSize = 1f;
        public float fallbackHeightStep = 1f; // How tall is one cell/floor? fallback
        public StructureData levelHeightReference; // Reference for the height unit

        public float CurrentHeightStep => levelHeightReference != null ? levelHeightReference.size.y : fallbackHeightStep;

        private Dictionary<Vector3Int, GridCell> _grid = new Dictionary<Vector3Int, GridCell>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            int x = Mathf.RoundToInt(worldPos.x / (gridSize > 0 ? gridSize : 1f));
            int y = Mathf.RoundToInt(worldPos.y / (CurrentHeightStep > 0 ? CurrentHeightStep : 1f));
            int z = Mathf.RoundToInt(worldPos.z / (gridSize > 0 ? gridSize : 1f));
            return new Vector3Int(x, y, z);
        }

        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * gridSize,
                gridPos.y * CurrentHeightStep,
                gridPos.z * gridSize
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
                // Can't place floor if floor already exists
                if (cell != null && cell.HasFloor) return false;
                return true; 
            }
            else if (buildType == BuildType.Structure)
            {
                // Must have a floor to place structure
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
