using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Simulation.Building;
using Simulation.Mission;
using Simulation.Physics;

namespace Simulation.UI
{
    /// <summary>
    /// UI สำหรับแสดงข้อมูลด่าน, งบประมาณ และผลการประเมิน
    /// </summary>
    public class MissionUI : MonoBehaviour
    {
        [Header("Budget UI")]
        [SerializeField] private TextMeshProUGUI budgetText;

        [Header("Mission Info UI")]
        [SerializeField] private GameObject missionPanel;
        [SerializeField] private TextMeshProUGUI missionNameText;
        [SerializeField] private TextMeshProUGUI missionDescText;
        [SerializeField] private TextMeshProUGUI timerText;

        [Header("Requirements UI")]
        [SerializeField] private TextMeshProUGUI floorsStatusText;
        [SerializeField] private TextMeshProUGUI areaStatusText;
        [SerializeField] private TextMeshProUGUI populationStatusText;

        [Header("Simulation Controls")]
        [SerializeField] private Button startSimButton;
        [SerializeField] private TextMeshProUGUI startButtonText;

        [Header("Results UI")]
        [SerializeField] private GameObject resultsPanel;
        [SerializeField] private TextMeshProUGUI resultTitleText;
        [SerializeField] private GameObject[] starIcons; // ลากรูปดาว 3 ดวงมาใส่

        [Header("Error Messages")]
        [SerializeField] private TextMeshProUGUI errorText;
        [SerializeField] private float errorDisplayDuration = 3f;

        [Header("Style")]
        [SerializeField] private Color completedColor = new Color(0f, 0.6f, 0f); // เขียวเข้ม

        private Color _originalFloorsColor;
        private Color _originalAreaColor;
        private Color _originalPopulationColor;

        private void Start()
        {
            // เชื่อมต่อ Events จาก MissionManager
            if (MissionManager.Instance != null)
            {
                MissionManager.Instance.OnMissionStarted += HandleMissionStarted;
                MissionManager.Instance.OnMissionCompleted += HandleMissionCompleted;
                MissionManager.Instance.OnValidationFailed += HandleValidationFailed;
            }

            if (startSimButton != null)
            {
                startSimButton.onClick.AddListener(OnStartButtonClick);
            }

            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (errorText != null) errorText.gameObject.SetActive(false);
            
            // เก็บสีเริ่มต้น
            if (floorsStatusText != null) _originalFloorsColor = floorsStatusText.color;
            if (areaStatusText != null) _originalAreaColor = areaStatusText.color;
            if (populationStatusText != null) _originalPopulationColor = populationStatusText.color;

            UpdateMissionInfo();
        }

        private void Update()
        {
            // อัปเดตเงินทุกเฟรม
            if (budgetText != null && BuildingSystem.Instance != null)
            {
                budgetText.text = $"${BuildingSystem.Instance.CurrentBudget:F0}";
                budgetText.color = BuildingSystem.Instance.CurrentBudget >= 0 ? Color.white : Color.red;
            }

            // อัปเดตเวลาและสถานะด่าน
            if (MissionManager.Instance != null && MissionManager.Instance.IsMissionActive)
            {
                if (timerText != null)
                {
                    timerText.text = $"Time: {MissionManager.Instance.SimulationTimeRemaining:F1}s";
                }
                
                if (missionPanel != null && !missionPanel.activeSelf) missionPanel.SetActive(true);
            }

            // อัปเดตสถานะเงื่อนไข (แบบ Realtime ก่อนเริ่ม)
            if (MissionManager.Instance != null && !MissionManager.Instance.IsMissionActive)
            {
                UpdateRequirementStatus();
            }
        }

        private void UpdateMissionInfo()
        {
            if (MissionManager.Instance == null || MissionManager.Instance.CurrentMission == null) return;

            var mission = MissionManager.Instance.CurrentMission;
            if (missionNameText != null) missionNameText.text = mission.missionName;
            if (missionDescText != null) missionDescText.text = mission.description;
        }

        private void UpdateRequirementStatus()
        {
            if (MissionManager.Instance == null || MissionManager.Instance.CurrentMission == null)
            {
                if (floorsStatusText != null) floorsStatusText.text = "";
                if (areaStatusText != null) areaStatusText.text = "";
                if (populationStatusText != null) populationStatusText.text = "";
                return;
            }

            var mission = MissionManager.Instance.CurrentMission;
            var stats = MissionManager.Instance.GetCurrentStats();

            // แสดงสถานะ ชั้น
            if (floorsStatusText != null)
            {
                bool ok = stats.floors >= mission.requiredFloors;
                floorsStatusText.text = $"Floors: {stats.floors}/{mission.requiredFloors}";
                floorsStatusText.color = ok ? completedColor : _originalFloorsColor;
            }

            // แสดงสถานะ พื้นที่
            if (areaStatusText != null)
            {
                bool ok = stats.area >= mission.requiredAreaPerFloor;
                areaStatusText.text = $"Area: {stats.area}/{mission.requiredAreaPerFloor} m²";
                areaStatusText.color = ok ? completedColor : _originalAreaColor;
            }

            // แสดงสถานะ คน
            if (populationStatusText != null)
            {
                bool ok = stats.people >= mission.requiredPopulation;
                populationStatusText.text = $"People: {stats.people}/{mission.requiredPopulation}";
                populationStatusText.color = ok ? completedColor : _originalPopulationColor;
            }
        }

        private void OnStartButtonClick()
        {
            if (MissionManager.Instance == null) return;

            if (MissionManager.Instance.IsMissionActive)
            {
                // ถ้ากำลังเล่นอยู่ ปุ่มนี้อาจทำหน้าที่ Stop
                MissionManager.Instance.EndMission();
                if (startButtonText != null) startButtonText.text = "START";
            }
            else
            {
                // เริ่ม Mission (ซึ่งจะเรียก StartSimulation ให้อัตโนมัติ)
                MissionManager.Instance.StartMission();
            }
        }

        private void HandleMissionStarted()
        {
            if (startButtonText != null) startButtonText.text = "STOP";
            if (resultsPanel != null) resultsPanel.SetActive(false);
            if (errorText != null) errorText.gameObject.SetActive(false);
        }

        private void HandleMissionCompleted(int stars)
        {
            if (startButtonText != null) startButtonText.text = "RESTART";
            
            if (resultsPanel != null)
            {
                resultsPanel.SetActive(true);
                if (resultTitleText != null) resultTitleText.text = "MISSION COMPLETE!";
                
                // แสดงดาวตามจำนวนที่ได้
                for (int i = 0; i < starIcons.Length; i++)
                {
                    starIcons[i].SetActive(i < stars);
                }
            }
        }

        private void HandleValidationFailed(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
                errorText.gameObject.SetActive(true);
                CancelInvoke(nameof(HideError));
                Invoke(nameof(HideError), errorDisplayDuration);
            }
        }

        private void HideError()
        {
            if (errorText != null) errorText.gameObject.SetActive(false);
        }
    }
}
