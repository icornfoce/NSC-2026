using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BuildingSimulation.Building;
using BuildingSimulation.Data;
using BuildingSimulation.Physics;
using BuildingSimulation.Disaster;

namespace BuildingSimulation.UI
{
    /// <summary>
    /// Manages all UI elements: budget display, part selector, material selector,
    /// NPC placer, simulation toggle, disaster buttons, and validation status.
    /// </summary>
    public class BuildingUI : MonoBehaviour
    {
        [Header("Budget")]
        [SerializeField] private TextMeshProUGUI budgetText;

        [Header("Part Selector")]
        [SerializeField] private Button pillarButton;
        [SerializeField] private Button wallButton;
        [SerializeField] private Button floorButton;
        [SerializeField] private Button stairsButton;
        [SerializeField] private Button doorButton;
        [SerializeField] private Button deleteButton;

        [Header("Part Data Assets")]
        [SerializeField] private BuildingPartData pillarData;
        [SerializeField] private BuildingPartData wallData;
        [SerializeField] private BuildingPartData floorData;
        [SerializeField] private BuildingPartData stairsData;
        [SerializeField] private BuildingPartData doorData;

        [Header("Material Selector")]
        [SerializeField] private TMP_Dropdown materialDropdown;
        [SerializeField] private BuildingMaterialData[] availableMaterials;

        [Header("NPC Placement")]
        [SerializeField] private Button placeNPCButton;
        [SerializeField] private TMP_Dropdown npcDropdown;
        [SerializeField] private NPCData[] availableNPCs;
        [SerializeField] private TextMeshProUGUI npcCountText;

        [Header("Simulation")]
        [SerializeField] private Button simulationButton;
        [SerializeField] private TextMeshProUGUI simulationButtonText;

        [Header("Disasters")]
        [SerializeField] private Button earthquakeButton;
        [SerializeField] private Button windButton;

        [Header("Status")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Button clearAllButton;

        private int _selectedNPCIndex;

        private void Start()
        {
            SetupPartButtons();
            SetupMaterialDropdown();
            SetupNPCUI();
            SetupSimulationButton();
            SetupDisasterButtons();
            SetupBudgetDisplay();
            SetupClearButton();

            UpdateSimulationUI(false);
        }

        private void Update()
        {
            // Update NPC count display
            if (npcCountText != null && NPCPlacer.Instance != null)
            {
                npcCountText.text = $"NPCs: {NPCPlacer.Instance.PlacedNPCs.Count}";
            }
        }

        // ─── Budget ────────────────────────────────────────────────

        private void SetupBudgetDisplay()
        {
            if (BudgetManager.Instance != null)
            {
                BudgetManager.Instance.OnBudgetChanged.AddListener(UpdateBudgetText);
                UpdateBudgetText(BudgetManager.Instance.CurrentBudget);
            }
        }

        private void UpdateBudgetText(float budget)
        {
            if (budgetText != null)
                budgetText.text = $"Budget: ${budget:F0}";
        }

        // ─── Part Selector ─────────────────────────────────────────

        private void SetupPartButtons()
        {
            if (pillarButton != null && pillarData != null)
                pillarButton.onClick.AddListener(() => SelectPart(pillarData));

            if (wallButton != null && wallData != null)
                wallButton.onClick.AddListener(() => SelectPart(wallData));

            if (floorButton != null && floorData != null)
                floorButton.onClick.AddListener(() => SelectPart(floorData));

            if (stairsButton != null && stairsData != null)
                stairsButton.onClick.AddListener(() => SelectPart(stairsData));

            if (doorButton != null && doorData != null)
                doorButton.onClick.AddListener(() => SelectPart(doorData));

            if (deleteButton != null)
                deleteButton.onClick.AddListener(() => BuildingSystem.Instance?.ToggleDeleteMode());
        }

        private void SelectPart(BuildingPartData data)
        {
            // Cancel NPC placement if active
            NPCPlacer.Instance?.CancelPlacement();

            BuildingSystem.Instance?.SelectPart(data);
            SetStatus($"Selected: {data.partName}");
        }

        // ─── Material Dropdown ─────────────────────────────────────

        private void SetupMaterialDropdown()
        {
            if (materialDropdown == null || availableMaterials == null) return;

            materialDropdown.ClearOptions();
            var options = new System.Collections.Generic.List<string>();
            foreach (var mat in availableMaterials)
            {
                options.Add(mat != null ? mat.materialName : "Unknown");
            }
            materialDropdown.AddOptions(options);

            materialDropdown.onValueChanged.AddListener(OnMaterialSelected);

            if (availableMaterials.Length > 0)
                OnMaterialSelected(0);
        }

        private void OnMaterialSelected(int index)
        {
            if (index < 0 || index >= availableMaterials.Length) return;
            BuildingSystem.Instance?.SetMaterial(availableMaterials[index]);
            SetStatus($"Material: {availableMaterials[index].materialName}");
        }

        // ─── NPC Placement ─────────────────────────────────────────

        private void SetupNPCUI()
        {
            // Setup NPC dropdown
            if (npcDropdown != null && availableNPCs != null && availableNPCs.Length > 0)
            {
                npcDropdown.ClearOptions();
                var options = new System.Collections.Generic.List<string>();
                foreach (var npc in availableNPCs)
                {
                    options.Add(npc != null ? $"{npc.npcName} (${npc.cost:F0})" : "Unknown");
                }
                npcDropdown.AddOptions(options);
                npcDropdown.onValueChanged.AddListener(OnNPCSelected);
                _selectedNPCIndex = 0;
            }

            // Setup place NPC button
            if (placeNPCButton != null)
            {
                placeNPCButton.onClick.AddListener(OnPlaceNPC);
            }
        }

        private void OnNPCSelected(int index)
        {
            _selectedNPCIndex = index;
        }

        private void OnPlaceNPC()
        {
            if (availableNPCs == null || availableNPCs.Length == 0) return;
            if (_selectedNPCIndex < 0 || _selectedNPCIndex >= availableNPCs.Length) return;

            // Cancel building placement if active
            BuildingSystem.Instance?.CancelPlacement();

            NPCPlacer.Instance?.SelectNPC(availableNPCs[_selectedNPCIndex]);
            SetStatus($"Placing NPC: {availableNPCs[_selectedNPCIndex].npcName}");
        }

        // ─── Simulation ────────────────────────────────────────────

        private void SetupSimulationButton()
        {
            if (simulationButton != null)
            {
                simulationButton.onClick.AddListener(OnSimulationToggle);
            }

            if (SimulationManager.Instance != null)
            {
                SimulationManager.Instance.OnSimulationStateChanged += UpdateSimulationUI;
            }
        }

        private void OnSimulationToggle()
        {
            SimulationManager.Instance?.ToggleSimulation();
        }

        private void UpdateSimulationUI(bool simulating)
        {
            if (simulationButtonText != null)
                simulationButtonText.text = simulating ? "Stop Simulation" : "Start Simulation";

            // Enable/disable disaster buttons based on simulation state
            if (earthquakeButton != null) earthquakeButton.interactable = simulating;
            if (windButton != null) windButton.interactable = simulating;

            // Disable building & NPC buttons during simulation
            bool canBuild = !simulating;
            if (pillarButton != null) pillarButton.interactable = canBuild;
            if (wallButton != null) wallButton.interactable = canBuild;
            if (floorButton != null) floorButton.interactable = canBuild;
            if (stairsButton != null) stairsButton.interactable = canBuild;
            if (doorButton != null) doorButton.interactable = canBuild;
            if (deleteButton != null) deleteButton.interactable = canBuild;
            if (materialDropdown != null) materialDropdown.interactable = canBuild;
            if (placeNPCButton != null) placeNPCButton.interactable = canBuild;
            if (npcDropdown != null) npcDropdown.interactable = canBuild;

            // Show validation result
            if (simulating && BuildingValidator.Instance != null)
            {
                SetStatus(BuildingValidator.Instance.LastMessage);
            }
        }

        // ─── Disasters ─────────────────────────────────────────────

        private void SetupDisasterButtons()
        {
            if (earthquakeButton != null)
                earthquakeButton.onClick.AddListener(OnEarthquake);

            if (windButton != null)
                windButton.onClick.AddListener(OnWind);
        }

        private void OnEarthquake()
        {
            DisasterManager.Instance?.TriggerEarthquake();
            SetStatus("EARTHQUAKE triggered!");
        }

        private void OnWind()
        {
            DisasterManager.Instance?.TriggerWind();
            SetStatus("WIND triggered!");
        }

        // ─── Clear All ─────────────────────────────────────────────

        private void SetupClearButton()
        {
            if (clearAllButton != null)
                clearAllButton.onClick.AddListener(OnClearAll);
        }

        private void OnClearAll()
        {
            SimulationManager.Instance?.StopSimulation();
            BuildingSystem.Instance?.ClearAll();
            NPCPlacer.Instance?.ClearAllNPCs();
            SetStatus("All cleared. Budget reset.");
        }

        // ─── Status ────────────────────────────────────────────────

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void OnDestroy()
        {
            if (BudgetManager.Instance != null)
                BudgetManager.Instance.OnBudgetChanged.RemoveListener(UpdateBudgetText);

            if (SimulationManager.Instance != null)
                SimulationManager.Instance.OnSimulationStateChanged -= UpdateSimulationUI;
        }
    }
}
