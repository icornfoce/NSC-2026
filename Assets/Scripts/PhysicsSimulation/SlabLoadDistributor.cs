using UnityEngine;
using System.Collections.Generic;
using Simulation.Building;

namespace Simulation.Physics
{
    [RequireComponent(typeof(StructureUnit))]
    public class SlabLoadDistributor : MonoBehaviour
    {
        [Header("Load Distribution Settings")]
        [Tooltip("Search radius for supporting pillars or walls.")]
        [SerializeField] private float searchRadius = 1.5f;

        private StructureUnit _myUnit;
        private Rigidbody _rb;
        private int _structureLayerMask;

        private void Awake()
        {
            _myUnit = GetComponent<StructureUnit>();
            _rb = GetComponent<Rigidbody>();
            _structureLayerMask = 1 << gameObject.layer;
        }

        private void FixedUpdate()
        {
            if (_myUnit == null || _myUnit.CurrentHP <= 0) return;

            // F = mg. We use baseMass for simulation. We can add more dynamic weight calculations here if needed.
            float totalWeight = _myUnit.Data.baseMass * 9.81f; 

            // Include any continuous collision force resting on this slab to pass down the chain
            var stress = GetComponent<StructuralStress>();
            if (stress != null)
            {
                // The slab's own stress component tracks the continuous collision force of objects resting on it!
                totalWeight += stress.LastCollisionForce;
            }

            // Find supports (walls/pillars) within radius
            Collider[] hits = UnityEngine.Physics.OverlapSphere(transform.position, searchRadius, _structureLayerMask);
            
            float totalWeightFactor = 0f;
            Dictionary<StructuralStress, float> supportFactors = new Dictionary<StructuralStress, float>();

            foreach (var hit in hits)
            {
                if (hit.transform.root == transform.root) continue; // Skip self

                StructureUnit supportUnit = hit.GetComponentInParent<StructureUnit>();
                
                // Only distribute to Walls or Objects (Pillars) - NOT other Floors
                if (supportUnit == null || supportUnit.Data.buildType == Simulation.Data.BuildType.Floor) continue;

                StructuralStress supportStress = supportUnit.GetComponent<StructuralStress>();
                if (supportStress != null && !supportStress.IsBroken && !supportFactors.ContainsKey(supportStress))
                {
                    // Basic check: Is it underneath us? (Y should be lower or equal)
                    // We add a tiny margin (0.1f) in case the wall top aligns exactly with the floor center.
                    if (supportUnit.transform.position.y <= transform.position.y + 0.1f)
                    {
                        float distance = Vector3.Distance(transform.position, supportUnit.transform.position);
                        
                        // Per user request: add 0.001f to prevent division by zero
                        float factor = 1f / (distance + 0.001f);
                        
                        supportFactors[supportStress] = factor;
                        totalWeightFactor += factor;
                    }
                }
            }

            // Distribute the total load to all found supports
            if (totalWeightFactor > 0f)
            {
                foreach (var kvp in supportFactors)
                {
                    float distributedLoad = totalWeight * (kvp.Value / totalWeightFactor);
                    kvp.Key.AddDistributedLoad(distributedLoad);
                }
            }
            else
            {
                // Unhandled unsupported slab logic. 
                // Normally native fixed joints hold it, but it might break over time from its own weight.
            }
        }
    }
}
