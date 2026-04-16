using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using Simulation.Building;
using Simulation.Mission;
using Simulation.Physics;

namespace Simulation.UI
{
    /// <summary>
    /// Mission HUD + Result Screen controller.
    ///
    /// Attach to a Canvas GameObject.
    /// Wire the serialized fields to your UI elements in the Inspector:
    ///
    ///  ── HUD (always visible) ──────────────────────────────────────
    ///   missionNameText          → Mission name (top of screen)
    ///   phaseText                → "กำลังสร้าง..." / "กำลังจำลอง..." etc.
    ///   budgetText               → "งบ: ฿ 1,000"
    ///   timerText                → Survival countdown "00:00 / 00:05"
    ///   timerFill                → Image (Filled) for timer progress bar
    ///
    ///  ── Building Controls ─────────────────────────────────────────
    ///   buildingPanel            → Panel containing build buttons (hidden during sim)
    ///   simulateButton           → "▶ จำลอง" button
    ///   resetButton              → "↺ รีเซ็ต" button
    ///
    ///  ── Result Screen ─────────────────────────────────────────────
    ///   resultPanel              → WIN / LOSE panel (hidden normally)
    ///   resultTitleText          → "ผ่านด่าน!" / "โครงสร้างพัง!"
    ///   resultSubtitleText       → "รอดมาได้ 5.0 วินาที" etc.
    ///   resultActionButton       → "ลองอีกครั้ง" button (calls ResetMission)
    /// </summary>
    public class MissionUIController : MonoBehaviour
    {
        // ── HUD ──────────────────────────────────────────────────────
        [Header("HUD - Mission Info")]
        [SerializeField] private TMP_Text missionNameText;
        [SerializeField] private TMP_Text missionDescriptionText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text budgetText;

        [Header("HUD - Survival Timer")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private Image timerFill;
        [SerializeField] private CanvasGroup timerGroup;

        [Header("Building Controls Panel")]
        [Tooltip("Panel that holds all building buttons — hidden during simulation.")]
        [SerializeField] private CanvasGroup buildingPanel;
        [SerializeField] private Button simulateButton;
        [SerializeField] private Button resetButton;

        [Header("Result Screen")]
        [SerializeField] private CanvasGroup resultPanel;
        [SerializeField] private TMP_Text resultTitleText;
        [SerializeField] private TMP_Text resultSubtitleText;
        [SerializeField] private Button resultActionButton;
        [SerializeField] private TMP_Text resultActionButtonText;

        [Header("Colors")]
        [SerializeField] private Color winColor   = new Color(0.2f, 0.85f, 0.3f);
        [SerializeField] private Color loseColor  = new Color(0.9f, 0.2f, 0.2f);
        [SerializeField] private Color buildColor = new Color(0.3f, 0.7f, 1.0f);
        [SerializeField] private Color simColor   = new Color(1.0f, 0.75f, 0.1f);

        [Header("Animation")]
        [SerializeField] private float panelFadeDuration = 0.4f;

        // ── Runtime ───────────────────────────────────────────────────
        private float _totalTime  = 1f;

        // ─────────────────────────────────────────────────────────────

        private void Start()
        {
            // Hide result panel immediately (no animation on start)
            if (resultPanel != null)
            {
                resultPanel.alpha          = 0f;
                resultPanel.interactable   = false;
                resultPanel.blocksRaycasts = false;
                resultPanel.gameObject.SetActive(false);
            }

            // Hide timer until simulation starts
            if (timerGroup != null)
            {
                timerGroup.alpha = 0f;
                timerGroup.gameObject.SetActive(false);
            }

            // Wire button callbacks
            if (simulateButton != null)
                simulateButton.onClick.AddListener(OnSimulateClicked);

            if (resetButton != null)
                resetButton.onClick.AddListener(OnResetClicked);

            if (resultActionButton != null)
                resultActionButton.onClick.AddListener(OnResetClicked);

            // Subscribe to MissionSystem events
            if (MissionSystem.Instance != null)
            {
                MissionSystem.Instance.OnMissionLoaded      += HandleMissionLoaded;
                MissionSystem.Instance.OnSurvivalTimerTick  += HandleTimerTick;
                MissionSystem.Instance.OnMissionComplete     += HandleMissionComplete;
                MissionSystem.Instance.OnMissionFail         += HandleMissionFail;
            }
            else
            {
                // MissionSystem might not exist yet, use delayed subscription
                Debug.LogWarning("[MissionUIController] MissionSystem not found on Start. Events will not be subscribed.");
            }

            // Refresh budget display every frame (simple polling — avoids extra events)
            RefreshBudgetDisplay();
            SetPhaseDisplay(MissionSystem.MissionPhase.Building);
        }

        private void OnDestroy()
        {
            if (MissionSystem.Instance != null)
            {
                MissionSystem.Instance.OnMissionLoaded      -= HandleMissionLoaded;
                MissionSystem.Instance.OnSurvivalTimerTick  -= HandleTimerTick;
                MissionSystem.Instance.OnMissionComplete     -= HandleMissionComplete;
                MissionSystem.Instance.OnMissionFail         -= HandleMissionFail;
            }
        }

        private void Update()
        {
            // Poll budget every frame so it updates instantly as player spends/refunds
            RefreshBudgetDisplay();
        }

        // ─────────────────────────────────────────────────────────────
        // Button Callbacks
        // ─────────────────────────────────────────────────────────────

        private void OnSimulateClicked()
        {
            if (MissionSystem.Instance == null) return;

            // Use MissionSystem if available, otherwise fall back to SimulationManager
            if (MissionSystem.Instance.Phase == MissionSystem.MissionPhase.Building)
            {
                MissionSystem.Instance.BeginSimulation();
                ShowSimulatingUI();
            }
        }

        private void OnResetClicked()
        {
            HideResultPanel();

            if (MissionSystem.Instance != null)
                MissionSystem.Instance.ResetMission();
            else if (SimulationManager.Instance != null)
                SimulationManager.Instance.ResetSimulation();

            ShowBuildingUI();
        }

        // ─────────────────────────────────────────────────────────────
        // MissionSystem Event Handlers
        // ─────────────────────────────────────────────────────────────

        private void HandleMissionLoaded(Simulation.Data.MissionData data)
        {
            if (missionNameText != null)
                missionNameText.text = data.missionName;

            if (missionDescriptionText != null)
                missionDescriptionText.text = data.description;

            _totalTime = data.targetSurvivalTime;

            if (timerText != null)
                timerText.text = FormatTime(0f, _totalTime);

            if (timerFill != null)
                timerFill.fillAmount = 0f;

            ShowBuildingUI();
        }

        private void HandleTimerTick(float elapsed, float total)
        {
            _totalTime = total;
            float progress = Mathf.Clamp01(elapsed / total);

            if (timerText != null)
                timerText.text = FormatTime(elapsed, total);

            if (timerFill != null)
                timerFill.fillAmount = progress;
        }

        private void HandleMissionComplete()
        {
            string survived = MissionSystem.Instance != null
                ? $"รอดมาได้ {MissionSystem.Instance.SurvivalElapsed:F1} วินาที!"
                : "โครงสร้างผ่านการทดสอบ!";

            ShowResultPanel(
                title:      "★ ผ่านด่าน! ★",
                subtitle:   survived,
                titleColor: winColor,
                actionText: "ลองอีกครั้ง"
            );
        }

        private void HandleMissionFail()
        {
            string elapsed = MissionSystem.Instance != null
                ? $"โครงสร้างรอดได้ {MissionSystem.Instance.SurvivalElapsed:F1} / {_totalTime:F1} วินาที"
                : "โครงสร้างทั้งหมดพัง!";

            ShowResultPanel(
                title:      "✗ โครงสร้างพัง!",
                subtitle:   elapsed,
                titleColor: loseColor,
                actionText: "ลองอีกครั้ง"
            );
        }

        // ─────────────────────────────────────────────────────────────
        // UI State Management
        // ─────────────────────────────────────────────────────────────

        private void ShowBuildingUI()
        {
            SetPhaseDisplay(MissionSystem.MissionPhase.Building);

            // Fade in building panel
            FadeCanvasGroup(buildingPanel, 1f, true);

            // Show simulate button, hide reset button (or show both)
            if (simulateButton != null) simulateButton.gameObject.SetActive(true);

            // Hide timer
            if (timerGroup != null)
            {
                timerGroup.gameObject.SetActive(false);
                timerGroup.alpha = 0f;
            }
        }

        private void ShowSimulatingUI()
        {
            SetPhaseDisplay(MissionSystem.MissionPhase.Simulating);

            // Fade out building panel (player can't build during simulation)
            FadeCanvasGroup(buildingPanel, 0f, false);

            // Show timer
            if (timerGroup != null)
            {
                timerGroup.gameObject.SetActive(true);
                timerGroup.DOFade(1f, panelFadeDuration);
            }
        }

        private void ShowResultPanel(string title, string subtitle, Color titleColor, string actionText)
        {
            if (resultPanel == null) return;

            resultPanel.gameObject.SetActive(true);
            resultPanel.alpha = 0f;
            resultPanel.interactable   = true;
            resultPanel.blocksRaycasts = true;

            if (resultTitleText != null)
            {
                resultTitleText.text  = title;
                resultTitleText.color = titleColor;
            }
            if (resultSubtitleText != null)
                resultSubtitleText.text = subtitle;
            if (resultActionButtonText != null)
                resultActionButtonText.text = actionText;

            // Punch-scale animation
            resultPanel.transform.localScale = Vector3.one * 0.8f;
            resultPanel.DOFade(1f, panelFadeDuration);
            resultPanel.transform.DOScale(1f, panelFadeDuration).SetEase(Ease.OutBack);
        }

        private void HideResultPanel()
        {
            if (resultPanel == null) return;
            resultPanel.DOFade(0f, panelFadeDuration * 0.5f).OnComplete(() =>
            {
                resultPanel.interactable   = false;
                resultPanel.blocksRaycasts = false;
                resultPanel.gameObject.SetActive(false);
            });
        }

        // ─────────────────────────────────────────────────────────────
        // Display Helpers
        // ─────────────────────────────────────────────────────────────

        private void SetPhaseDisplay(MissionSystem.MissionPhase phase)
        {
            if (phaseText == null) return;

            switch (phase)
            {
                case MissionSystem.MissionPhase.Building:
                    phaseText.text  = "● กำลังสร้าง";
                    phaseText.color = buildColor;
                    break;
                case MissionSystem.MissionPhase.Simulating:
                    phaseText.text  = "▶ กำลังจำลอง";
                    phaseText.color = simColor;
                    break;
                case MissionSystem.MissionPhase.Result:
                    phaseText.text  = "■ สิ้นสุดการจำลอง";
                    phaseText.color = Color.white;
                    break;
                default:
                    phaseText.text  = "";
                    break;
            }
        }

        private void RefreshBudgetDisplay()
        {
            if (budgetText == null) return;
            if (BuildingSystem.Instance == null) return;

            float budget = BuildingSystem.Instance.CurrentBudget;
            budgetText.text = $"งบ: ฿ {budget:N0}";

            // Flash red if low (< 20% of initial budget)
            float initial = BuildingSystem.Instance.InitialBudget;
            if (initial > 0 && budget / initial < 0.2f)
                budgetText.color = loseColor;
            else
                budgetText.color = Color.white;
        }

        private void FadeCanvasGroup(CanvasGroup cg, float targetAlpha, bool interactable)
        {
            if (cg == null) return;
            cg.DOKill();
            cg.DOFade(targetAlpha, panelFadeDuration);
            cg.interactable   = interactable;
            cg.blocksRaycasts = interactable;
        }

        private string FormatTime(float elapsed, float total)
        {
            // Format as  "03.2s / 05.0s"
            float remaining = Mathf.Max(0f, total - elapsed);
            return $"{remaining:F1}s / {total:F1}s";
        }
    }
}
