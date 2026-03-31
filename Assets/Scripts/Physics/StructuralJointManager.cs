using System.Collections.Generic;
using UnityEngine;
using BuildingSimulation.Building;

namespace BuildingSimulation.Physics
{
    /// <summary>
    /// Detects adjacent building parts and connects them with FixedJoints.
    /// Break force is set to the minimum of the two connected materials' break forces.
    /// </summary>
    public class StructuralJointManager : MonoBehaviour
    {
        public static StructuralJointManager Instance { get; private set; }

        [Header("Connection Settings")]
        [Tooltip("How far to search for neighboring parts when connecting joints")]
        [SerializeField] private float connectionRadius = 0.5f;

        [Tooltip("Layer mask for detecting building parts")]
        [SerializeField] private LayerMask buildingLayerMask = ~0;

        private readonly List<FixedJoint> _allJoints = new List<FixedJoint>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Find neighboring building parts and create FixedJoints to connect them.
        /// </summary>
        public void ConnectToNeighbors(BuildingPart newPart)
        {
            if (newPart == null) return;

            var rb = newPart.GetComponent<Rigidbody>();
            if (rb == null) return;

            // Use OverlapBox based on the part's bounds
            var col = newPart.GetComponent<Collider>();
            if (col == null) return;

            Vector3 center = col.bounds.center;
            Vector3 halfExtents = col.bounds.extents + Vector3.one * connectionRadius;

            Collider[] neighbors = UnityEngine.Physics.OverlapBox(center, halfExtents, newPart.transform.rotation, buildingLayerMask);

            foreach (var neighborCol in neighbors)
            {
                if (neighborCol.gameObject == newPart.gameObject) continue;

                var neighborRb = neighborCol.attachedRigidbody;
                var neighborPart = neighborCol.GetComponent<BuildingPart>() ?? neighborCol.GetComponentInParent<BuildingPart>();

                // Create joint on the new part
                var joint = newPart.gameObject.AddComponent<FixedJoint>();
                
                // If neighbor has a Rigidbody, connect to it.
                // If not, it's considered "Ground" or "Environment", so we connect to nothing (anchored to world space).
                if (neighborRb != null)
                {
                    joint.connectedBody = neighborRb;
                }

                // Break force = minimum of the two materials
                float breakForceA = newPart.MaterialData != null ? newPart.MaterialData.breakForce : 500000f;
                float breakForceB = (neighborPart != null && neighborPart.MaterialData != null) 
                    ? neighborPart.MaterialData.breakForce 
                    : 1000000f; // Static environment is very strong
                
                joint.breakForce = Mathf.Min(breakForceA, breakForceB);
                joint.breakTorque = joint.breakForce * 0.5f;

                _allJoints.Add(joint);
            }
        }

        /// <summary>
        /// Clear all tracked joints (used on reset).
        /// </summary>
        public void ClearAll()
        {
            _allJoints.Clear();
        }
    }
}
