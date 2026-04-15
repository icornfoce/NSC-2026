using UnityEngine;

namespace Simulation.Building
{
    public class GridCell 
    {
        public Vector3Int Coordinates { get; private set; }
        
        public StructureUnit Floor { get; set; }
        public StructureUnit Wall { get; set; }
        public StructureUnit Object { get; set; }

        public GridCell(Vector3Int coordinates)
        {
            Coordinates = coordinates;
        }

        public bool HasFloor => Floor != null;
        public bool HasWall => Wall != null;
        public bool HasObject => Object != null;
    }
}
