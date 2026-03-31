using UnityEngine;
using UnityEngine.UI;
using BuildingSimulation.Building;
using BuildingSimulation.Data;

namespace BuildingSimulation.UI
{
    /// <summary>
    /// Popup UI that appears when clicking a building part.
    /// Provides Move, Delete, and Change Material options.
    /// </summary>
    public class ContextUI : MonoBehaviour
    {
        public static ContextUI Instance { get; private set; }

        [Header("UI Elements")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button moveButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button materialButton;

        private BuildingPart _currentPart;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            if (panel != null) panel.SetActive(false);
        }

        private void Start()
        {
            if (moveButton != null) moveButton.onClick.AddListener(OnMoveClick);
            if (deleteButton != null) deleteButton.onClick.AddListener(OnDeleteClick);
            if (materialButton != null) materialButton.onClick.AddListener(OnMaterialClick);
        }

        /// <summary>
        /// Show the context menu at a specific world position.
        /// </summary>
        public void Show(BuildingPart part, Vector3 worldPosition)
        {
            _currentPart = part;
            if (panel == null) return;

            panel.SetActive(true);

            // Position the UI panel at the screen position of the part
            Vector3 screenPos = UnityEngine.Camera.main.WorldToScreenPoint(worldPosition);
            panel.transform.position = screenPos + new Vector3(80, 80, 0); // Offset slightly
        }

        public void Hide()
        {
            _currentPart = null;
            if (panel != null) panel.SetActive(false);
        }

        private void OnMoveClick()
        {
            if (_currentPart == null) return;
            BuildingSystem.Instance?.StartMovingPart(_currentPart);
            Hide();
        }

        private void OnDeleteClick()
        {
            if (_currentPart == null) return;
            BuildingSystem.Instance?.RemovePart(_currentPart);
            Hide();
        }

        private void OnMaterialClick()
        {
            if (_currentPart == null) return;
            BuildingSystem.Instance?.ApplySelectedMaterialToPart(_currentPart);
            Hide();
        }
    }
}
