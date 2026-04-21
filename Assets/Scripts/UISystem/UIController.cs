using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;

namespace Simulation.UI
{
    /// <summary>
    /// สคริปต์จัดการ UI ด้วย DoTween 
    /// - ช่วยให้ Canvas Group ค่อยๆ ปรากฏ (Fade In) / หายไป (Fade Out)
    /// - ทำปุ่มขยาย (Scale Up) เมื่อเอาเมาส์ไปชี้ (Hover)
    /// </summary>
    public class UIController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Fade Settings (Canvas Group)")]
        [Tooltip("ถ้าใส่ Canvas Group เข้ามา จะทำงานตอน Fade")]
        [SerializeField] private CanvasGroup canvasGroup;
        [Tooltip("ต้องการให้ Fade In อัตโนมัติเมื่อเปิด (Enabled) หรือไม่")]
        [SerializeField] private bool fadeOnEnable = true;
        [SerializeField] private float fadeDuration = 0.4f;

        [Header("Hover Settings (Button)")]
        [Tooltip("ต้องการให้ขยายปุ่มเมื่อเอาเมาส์ชี้หรือไม่")]
        [SerializeField] private bool scaleOnHover = true;
        [Tooltip("ขนาดที่ต้องการให้ขยาย (เช่น 1.1 ตัวใหญ่ขึ้น 10%)")]
        [SerializeField] private float hoverScaleMultiplier = 1.1f;
        [SerializeField] private float scaleDuration = 0.2f;

        private Vector3 originalScale;

        private void Awake()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }
            originalScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (fadeOnEnable && canvasGroup != null)
            {
                ShowPanel();
            }
        }

        private void OnDisable()
        {
            // ป้องกันบัคสเกลค้างเมื่อถูกปิดในขณะที่เมาส์ยังชี้อยู่
            transform.localScale = originalScale;
            
            // ยกเลิก Tween เผื่อมีค้างอยู่
            transform.DOKill();
            if (canvasGroup != null) canvasGroup.DOKill();
        }

        /// <summary>
        /// สั่งให้หน้าต่าง/ปุ่ม หรือ Canvas Group ค่อยๆ Fade ขึ้นมา
        /// </summary>
        public void ShowPanel()
        {
            gameObject.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                // ป้องกันไม่ให้คลิกได้ตอนกำลังค่อยๆ โปร่งใส (ถ้าต้องการ) แต่ส่วนใหญ่ตั้งเป็น true เพื่อให้พร้อมคลิก
                canvasGroup.blocksRaycasts = true; 
                canvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.OutQuad);
            }
        }

        /// <summary>
        /// สั่งให้ Canvas Group ค่อยๆ Fade หาย แล้วปิด (SetActive = false)
        /// </summary>
        public void HidePanel()
        {
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false; // กันการคลิกตอนกำลังจาง
                canvasGroup.DOFade(0f, fadeDuration).SetEase(Ease.InQuad).OnComplete(() =>
                {
                    gameObject.SetActive(false);
                });
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// อนิเมชันขยายปุ่ม เมื่อเอาเมาส์ไปชี้ (Hover)
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!scaleOnHover) return;

            // หยุด Tween เก่าและรัน Tween แบบ OutBack ให้มันเด้งสมูทๆ
            transform.DOKill();
            transform.DOScale(originalScale * hoverScaleMultiplier, scaleDuration).SetEase(Ease.OutBack);
        }

        /// <summary>
        /// อนิเมชันกลับสู่ขนาดเดิม เมื่อเอาเมาส์ออกจากปุ่ม
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!scaleOnHover) return;

            // หยุด Tween เก่าและหดกลับมาที่สเกลเดิม
            transform.DOKill();
            transform.DOScale(originalScale, scaleDuration).SetEase(Ease.OutQuad);
        }
    }
}
