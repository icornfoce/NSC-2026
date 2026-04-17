using UnityEngine;
using DG.Tweening;

namespace Simulation.UI
{
    /// <summary>
    /// ระบบคุม Animation ของ UI ทั้งหมด เช่น Pop up เลือกวัสดุ, Slide หน้าต่าง 
    /// และรวมถึงหน้าต่างหลัก AboveUI / BelowUI
    /// </summary>
    public class UIAnimationManager : MonoBehaviour
    {
        public static UIAnimationManager Instance { get; private set; }

        [Header("--- References : Main HUD ---")]
        [Tooltip("UI ส่วนบนของจอภาพ (เช่น StartBG, Menu)")]
        public RectTransform aboveUI;
        [Tooltip("UI ส่วนล่างของจอภาพ (เช่น ปุ่มเลือก Structure/Furniture)")]
        public RectTransform belowUI;

        [Header("--- References : Radial Menus ---")]
        [Tooltip("หน้าต่างวงกลม Material UI (ต้องการ Component CanvasGroup ด้วย)")]
        public CanvasGroup materialUI;
        [Tooltip("หน้าต่างวงกลม Structure UI (ต้องการ Component CanvasGroup ด้วย)")]
        public CanvasGroup structureUI;
        
        [Header("--- References : Bottom Panels ---")]
        [Tooltip("Panel ย่อยสำหรับตัวเลือกการสร้าง Structure")]
        public RectTransform structurePanel;
        [Tooltip("Panel ย่อยสำหรับตัวเลือกการสร้าง Furniture")]
        public RectTransform furniturePanel;


        [Header("--- Config : Main HUD Settings ---")]
        public float hudSlideDuration = 0.5f;
        public float aboveUIShowY = 0f;
        public float aboveUIHideY = 300f;   // วิ่งขึ้นไปซ่อนด้านบนจอ (ค่าบวก)
        public float belowUIShowY = 0f;
        public float belowUIHideY = -300f;  // วิ่งลงมาซ่อนด้านล่างจอ (ค่าลบ)

        [Header("--- Config : Sub Panel Slide Settings ---")]
        public float slideDuration = 0.4f;
        public float panelShowY = 0f;
        public float panelHideY = -300f;  
        public Ease slideInEase = Ease.OutBack;
        public Ease slideOutEase = Ease.InBack;

        [Header("--- Config : Pop-up Settings ---")]
        public float popupDuration = 0.3f;
        public Ease popupInEase = Ease.OutBack;
        public Ease popupOutEase = Ease.InBack;

        [Header("--- Config : Button Effects ---")]
        public float hoverDuration = 0.15f;
        public Vector3 hoverScale = new Vector3(1.1f, 1.1f, 1.1f);
        public float clickDuration = 0.1f;
        public Vector3 clickPunchScale = new Vector3(-0.1f, -0.1f, -0.1f);

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            // ซ่อน Radial menu ไว้เป็นค่าเริ่มต้นตอนเริ่มเกม
            ForceHideRadial(materialUI);
            ForceHideRadial(structureUI);
        }

        // ==========================================
        // 0. Main HUD (AboveUI / BelowUI)
        // ==========================================
        
        /// <summary>เรียกเพื่อแสดง UI หลักทั้งบนและล่างพร้อมกัน</summary>
        public void ShowMainHUD()
        {
            ShowAboveUI();
            ShowBelowUI();
        }

        /// <summary>เรียกเพื่อซ่อน UI หลักทั้งบนและล่างพร้อมกัน</summary>
        public void HideMainHUD()
        {
            HideAboveUI();
            HideBelowUI();
        }

        public void ShowAboveUI() // เลื่อนลงมาแสดง
        {
            if (aboveUI == null) return;
            KillUI(aboveUI);
            aboveUI.gameObject.SetActive(true);
            aboveUI.DOAnchorPosY(aboveUIShowY, hudSlideDuration).SetEase(slideInEase);
        }

        public void HideAboveUI() // เลื่อนกลับขึ้นไปซ่อน
        {
            if (aboveUI == null) return;
            KillUI(aboveUI);
            aboveUI.DOAnchorPosY(aboveUIHideY, hudSlideDuration).SetEase(slideOutEase)
                   .OnComplete(() => aboveUI.gameObject.SetActive(false));
        }

        public void ShowBelowUI() // เลื่อนขึ้นมาแสดง
        {
            if (belowUI == null) return;
            KillUI(belowUI);
            belowUI.gameObject.SetActive(true);
            belowUI.DOAnchorPosY(belowUIShowY, hudSlideDuration).SetEase(slideInEase);
        }

        public void HideBelowUI() // เลื่อนกลับลงไปซ่อน
        {
            if (belowUI == null) return;
            KillUI(belowUI);
            belowUI.DOAnchorPosY(belowUIHideY, hudSlideDuration).SetEase(slideOutEase)
                   .OnComplete(() => belowUI.gameObject.SetActive(false));
        }

        // ==========================================
        // 1. Radial Menus 
        // ==========================================
        public void ShowMaterialUI() => AnimateRadialShow(materialUI);
        public void HideMaterialUI() => AnimateRadialHide(materialUI);
        
        public void ShowStructureUI() => AnimateRadialShow(structureUI);
        public void HideStructureUI() => AnimateRadialHide(structureUI);

        private void AnimateRadialShow(CanvasGroup cg)
        {
            if (cg == null) return;
            KillUI(cg);
            
            cg.gameObject.SetActive(true);
            cg.alpha = 0f;
            cg.transform.localScale = Vector3.zero;

            cg.DOFade(1f, popupDuration);
            cg.transform.DOScale(Vector3.one, popupDuration).SetEase(popupInEase);
        }

        private void AnimateRadialHide(CanvasGroup cg)
        {
            if (cg == null) return;
            KillUI(cg);

            cg.DOFade(0f, popupDuration);
            cg.transform.DOScale(Vector3.zero, popupDuration).SetEase(popupOutEase)
              .OnComplete(() => {
                  cg.gameObject.SetActive(false);
                  cg.alpha = 1f; 
              });
        }

        private void ForceHideRadial(CanvasGroup cg)
        {
            if (cg == null) return;
            cg.gameObject.SetActive(false);
            cg.alpha = 0f;
            cg.transform.localScale = Vector3.zero;
        }

        // ==========================================
        // 2. Bottom Sub Panels 
        // ==========================================
        public void OpenStructurePanel() => AnimateSlideUp(structurePanel);
        public void CloseStructurePanel() => AnimateSlideDown(structurePanel);
        
        public void OpenFurniturePanel() => AnimateSlideUp(furniturePanel);
        public void CloseFurniturePanel() => AnimateSlideDown(furniturePanel);

        public void ShowStructureHideFurniture()
        {
            CloseFurniturePanel();
            OpenStructurePanel();
        }
        
        public void ShowFurnitureHideStructure()
        {
            CloseStructurePanel();
            OpenFurniturePanel();
        }

        private void AnimateSlideUp(RectTransform panel)
        {
            if (panel == null) return;
            KillUI(panel);
            
            panel.gameObject.SetActive(true);
            panel.DOAnchorPosY(panelShowY, slideDuration).SetEase(slideInEase);
        }

        private void AnimateSlideDown(RectTransform panel)
        {
            if (panel == null) return;
            KillUI(panel);
            
            panel.DOAnchorPosY(panelHideY, slideDuration).SetEase(slideOutEase)
                 .OnComplete(() => panel.gameObject.SetActive(false));
        }

        // ==========================================
        // 3. Generic Button Hover / Click Effect
        // ==========================================
        
        // แก้จาก Transform เป็น GameObject เพื่อป้องกัน ArgumentException จาก Event Trigger
        public void OnButtonHoverEnter(GameObject target)
        {
            if (target == null) return;
            target.transform.DOKill();
            target.transform.DOScale(hoverScale, hoverDuration).SetEase(Ease.OutSine);
        }

        public void OnButtonHoverExit(GameObject target)
        {
            if (target == null) return;
            target.transform.DOKill();
            target.transform.DOScale(Vector3.one, hoverDuration).SetEase(Ease.OutSine);
        }

        public void OnButtonClick(GameObject target)
        {
            if (target == null) return;
            target.transform.DOPunchScale(clickPunchScale, clickDuration, 1, 0.5f);
        }

        // ==========================================
        // Helpers
        // ==========================================
        private void KillUI(Component comp)
        {
            comp.DOKill();
            comp.transform.DOKill();
        }
    }
}
