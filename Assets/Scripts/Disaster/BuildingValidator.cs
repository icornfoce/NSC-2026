using UnityEngine;
using BuildingSimulation.Building;
using BuildingSimulation.Data;

namespace BuildingSimulation.Disaster
{
    /// <summary>
    /// Validates building requirements before simulation can start:
    /// - Building height must meet minimum threshold
    /// - At least one door must be present
    /// </summary>
    public class BuildingValidator : MonoBehaviour
    {
        public static BuildingValidator Instance { get; private set; }

        [Header("Validation Rules")]
        [Tooltip("Minimum building height in meters to pass validation")]
        [SerializeField] private float minimumHeight = 0.5f; // Lowered from 5f

        [Tooltip("Minimum number of doors required")]
        [SerializeField] private int minimumDoors = 0; // Lowered from 1

        [Tooltip("Force start regardless of validation")]
        public bool forceStart = false;

        [Header("Status")]
        [SerializeField] private string lastValidationMessage = "";
        public string LastMessage => lastValidationMessage;

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
        /// Validate the current building. Returns true if all conditions are met.
        /// </summary>
        public bool Validate()
        {
            var parts = BuildingSystem.Instance?.PlacedParts;
            if (parts == null || parts.Count == 0)
            {
                lastValidationMessage = "No building parts placed!";
                Debug.LogWarning(lastValidationMessage);
                return false;
            }

            // --- Check building height ---
            float lowestY = float.MaxValue;
            float highestY = float.MinValue;

            foreach (var part in parts)
            {
                if (part == null) continue;
                float bottomY = part.transform.position.y - part.CurrentScale.y * 0.5f;
                float topY = part.transform.position.y + part.CurrentScale.y * 0.5f;

                if (bottomY < lowestY) lowestY = bottomY;
                if (topY > highestY) highestY = topY;
            }

            float totalHeight = highestY - lowestY;
            if (totalHeight < minimumHeight)
            {
                lastValidationMessage = $"Building too short! Height: {totalHeight:F1}m, Required: {minimumHeight:F1}m";
                Debug.LogWarning(lastValidationMessage);
                return false;
            }

            // --- Check door count ---
            int doorCount = 0;
            foreach (var part in parts)
            {
                if (part == null) continue;
                if (part.PartData != null && part.PartData.partType == PartType.Door)
                {
                    doorCount++;
                }
            }

            if (doorCount < minimumDoors)
            {
                lastValidationMessage = $"Not enough doors! Found: {doorCount}, Required: {minimumDoors}";
                Debug.LogWarning(lastValidationMessage);
                return false;
            }

            lastValidationMessage = $"Validation passed! Height: {totalHeight:F1}m, Doors: {doorCount}";
            Debug.Log(lastValidationMessage);
            return true;
        }
    }
}
