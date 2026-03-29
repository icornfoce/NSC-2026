using System.Collections.Generic;
using UnityEngine;
using BuildingSimulation.Building;

namespace BuildingSimulation.Physics
{
    /// <summary>
    /// Manages the simulation lifecycle: toggling gravity on building parts
    /// and activating/deactivating NPCs.
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        public static SimulationManager Instance { get; private set; }

        [Header("State")]
        [SerializeField] private bool isSimulating;
        public bool IsSimulating => isSimulating;

        public System.Action<bool> OnSimulationStateChanged;

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
        /// Toggle simulation on/off.
        /// </summary>
        public void ToggleSimulation()
        {
            if (isSimulating)
                StopSimulation();
            else
                StartSimulation();
        }

        /// <summary>
        /// Enable gravity on all building parts and activate placed NPCs.
        /// </summary>
        public void StartSimulation()
        {
            if (isSimulating) return;

            // Validate building first
            if (Disaster.BuildingValidator.Instance != null)
            {
                if (!Disaster.BuildingValidator.Instance.Validate())
                {
                    Debug.LogWarning("Building validation failed! Cannot start simulation.");
                    return;
                }
            }

            isSimulating = true;

            // Enable gravity on all parts
            var parts = BuildingSystem.Instance?.PlacedParts;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part != null)
                        part.SetGravity(true);
                }
            }

            // Activate all manually placed NPCs
            if (NPCPlacer.Instance != null)
            {
                foreach (var npc in NPCPlacer.Instance.PlacedNPCs)
                {
                    if (npc != null) npc.Activate();
                }
            }

            // Disable building/NPC placement during simulation
            BuildingSystem.Instance?.CancelPlacement();
            NPCPlacer.Instance?.CancelPlacement();

            OnSimulationStateChanged?.Invoke(true);
            Debug.Log("Simulation STARTED — gravity enabled, NPCs activated.");
        }

        /// <summary>
        /// Disable gravity, deactivate NPCs, return to build mode.
        /// </summary>
        public void StopSimulation()
        {
            if (!isSimulating) return;
            isSimulating = false;

            // Disable gravity on all parts
            var parts = BuildingSystem.Instance?.PlacedParts;
            if (parts != null)
            {
                foreach (var part in parts)
                {
                    if (part != null)
                        part.SetGravity(false);
                }
            }

            // Deactivate all NPCs
            if (NPCPlacer.Instance != null)
            {
                foreach (var npc in NPCPlacer.Instance.PlacedNPCs)
                {
                    if (npc != null) npc.Deactivate();
                }
            }

            OnSimulationStateChanged?.Invoke(false);
            Debug.Log("Simulation STOPPED — returned to build mode.");
        }
    }
}
