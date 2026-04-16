using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Simulation.Data;
using Simulation.Building;
using Simulation.Physics;

namespace Simulation.Mission
{
    /// <summary>
    /// Core game-loop controller.
    ///
    /// Phases:
    ///   Building  → Player places structures (budget enforced)
    ///   Simulating → Physics runs, disasters fire in sequence, survival timer counts
    ///   Result     → Win or Lose event fires, waiting for Reset or Next Level
    ///
    /// Usage:
    ///   1. Assign a MissionData asset in the Inspector (or call LoadMission() from UI).
    ///   2. Wire OnMissionComplete / OnMissionFail UnityEvents to your result UI.
    ///   3. SimulationManager.StartSimulation() → MissionSystem detects the phase change.
    /// </summary>
    public class MissionSystem : MonoBehaviour
    {
        public static MissionSystem Instance { get; private set; }

        // ─── Inspector ───────────────────────────────────────────────
        [Header("Mission")]
        [Tooltip("Drag a MissionData asset here to auto-load on Start.")]
        [SerializeField] private MissionData defaultMission;

        [Header("References")]
        [SerializeField] private DisasterManager disasterManager;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = true;

        // ─── Runtime State ────────────────────────────────────────────
        private MissionData _activeMission;
        private MissionPhase _phase = MissionPhase.Idle;
        private float _survivalTimer = 0f;
        private bool _allStructuresBroken = false;

        // ─── Events ───────────────────────────────────────────────────
        public event System.Action<MissionData> OnMissionLoaded;
        public event System.Action OnMissionComplete;
        public event System.Action OnMissionFail;
        public event System.Action<float, float> OnSurvivalTimerTick; // (elapsed, total)

        // ─── Properties ───────────────────────────────────────────────
        public MissionPhase Phase => _phase;
        public MissionData ActiveMission => _activeMission;
        public float SurvivalElapsed => _survivalTimer;
        public bool IsSimulating => _phase == MissionPhase.Simulating;

        // ─────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // Auto-find DisasterManager if not assigned
            if (disasterManager == null)
                disasterManager = FindFirstObjectByType<DisasterManager>();
        }

        private void Start()
        {
            if (defaultMission != null)
                LoadMission(defaultMission);
        }

        private void Update()
        {
            if (_phase != MissionPhase.Simulating) return;

            // Poll whether SimulationManager has stopped externally
            if (SimulationManager.Instance != null && !SimulationManager.Instance.IsSimulating)
            {
                // Simulation stopped from outside — pause our timer
                return;
            }

            _survivalTimer += Time.deltaTime;
            OnSurvivalTimerTick?.Invoke(_survivalTimer, _activeMission != null ? _activeMission.targetSurvivalTime : 1f);

            // ── Lose check ── all structures must be broken
            CheckAllBroken();
            if (_allStructuresBroken)
            {
                TriggerFail();
                return;
            }

            // ── Win check ── survived long enough
            if (_activeMission != null && _survivalTimer >= _activeMission.targetSurvivalTime)
            {
                TriggerComplete();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────

        /// <summary>Load a mission and reset state. Call before the building phase.</summary>
        public void LoadMission(MissionData mission)
        {
            if (mission == null) { Debug.LogWarning("[MissionSystem] LoadMission called with null data."); return; }

            _activeMission = mission;
            _phase = MissionPhase.Building;
            _survivalTimer = 0f;
            _allStructuresBroken = false;

            // Apply budget to BuildingSystem
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.SetBudget(mission.startingBudget);

            if (showDebugLogs)
                Debug.Log($"[MissionSystem] Loaded mission: <color=cyan>{mission.missionName}</color>");

            OnMissionLoaded?.Invoke(mission);
        }

        /// <summary>
        /// Called by UI / SimulationManager when the player presses "Simulate".
        /// Transitions Building → Simulating and fires disasters.
        /// </summary>
        public void BeginSimulation()
        {
            if (_phase != MissionPhase.Building)
            {
                Debug.LogWarning("[MissionSystem] BeginSimulation called but not in Building phase.");
                return;
            }

            _phase = MissionPhase.Simulating;
            _survivalTimer = 0f;
            _allStructuresBroken = false;

            // Tell the physics manager to start
            if (SimulationManager.Instance != null)
                SimulationManager.Instance.StartSimulation();

            // Fire disasters from the mission
            if (disasterManager != null && _activeMission != null)
                disasterManager.ExecuteDisasters(_activeMission.disasters);

            if (showDebugLogs)
                Debug.Log($"[MissionSystem] <color=green>▶ Simulation started</color>. Target survival: {_activeMission?.targetSurvivalTime}s");
        }

        /// <summary>Reset all structures and return to Building phase.</summary>
        public void ResetMission()
        {
            // Stop simulation
            if (SimulationManager.Instance != null)
                SimulationManager.Instance.StopSimulation();

            if (disasterManager != null)
                disasterManager.CancelDisasters();

            // Destroy all placed structures and restore budget
            if (BuildingSystem.Instance != null)
                BuildingSystem.Instance.ResetAllStructures();

            _phase = MissionPhase.Building;
            _survivalTimer = 0f;
            _allStructuresBroken = false;

            // Re-apply mission budget
            if (_activeMission != null && BuildingSystem.Instance != null)
                BuildingSystem.Instance.SetBudget(_activeMission.startingBudget);

            if (showDebugLogs)
                Debug.Log("[MissionSystem] Mission reset — back to Building phase.");
        }

        // ─────────────────────────────────────────────────────────────
        // Internal Checks
        // ─────────────────────────────────────────────────────────────

        private void CheckAllBroken()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            if (units.Length == 0)
            {
                _allStructuresBroken = true;
                return;
            }

            foreach (var u in units)
            {
                if (u == null) continue;
                // A unit is "alive" if it still has HP and the object is active
                if (u.gameObject.activeInHierarchy && u.CurrentHP > 0)
                {
                    _allStructuresBroken = false;
                    return;
                }
            }
            _allStructuresBroken = true;
        }

        private void TriggerComplete()
        {
            if (_phase == MissionPhase.Result) return;
            _phase = MissionPhase.Result;

            if (SimulationManager.Instance != null)
                SimulationManager.Instance.StopSimulation();

            if (showDebugLogs)
                Debug.Log($"[MissionSystem] <color=yellow>★ MISSION COMPLETE ★</color> — survived {_survivalTimer:F1}s");

            OnMissionComplete?.Invoke();
        }

        private void TriggerFail()
        {
            if (_phase == MissionPhase.Result) return;
            _phase = MissionPhase.Result;

            if (SimulationManager.Instance != null)
                SimulationManager.Instance.StopSimulation();

            if (disasterManager != null)
                disasterManager.CancelDisasters();

            if (showDebugLogs)
                Debug.Log("[MissionSystem] <color=red>✗ MISSION FAILED</color> — all structures broke.");

            OnMissionFail?.Invoke();
        }

        public enum MissionPhase { Idle, Building, Simulating, Result }
    }
}
