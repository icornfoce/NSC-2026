using UnityEngine;
using TMPro; // ต้องใช้สำหรับ TMP_Dropdown
using System.Collections.Generic;
using Simulation.Data;
using Simulation.Building;

namespace Simulation.UI
{
    public class MaterialDropdownUI : MonoBehaviour
    {
        [Header("UI Reference")]
        [SerializeField] private TMP_Dropdown dropdown;

        [Header("Materials Data")]
        [Tooltip("ลาก MaterialData asset ทั้งหมด (เช่น Wood, Iron, Concrete) มาใส่ที่นี่")]
        [SerializeField] private List<MaterialData> materials;

        private void Start()
        {
            if (dropdown == null) dropdown = GetComponent<TMP_Dropdown>();
            
            SetupDropdown();
        }

        private void SetupDropdown()
        {
            if (dropdown == null || materials == null || materials.Count == 0) return;

            // 1. ล้างข้อมูลเก่า
            dropdown.ClearOptions();

            // 2. สร้างรายการชื่อวัสดุ
            List<string> options = new List<string>();
            foreach (var mat in materials)
            {
                options.Add(mat.materialName);
            }

            // 3. ใส่ข้อมูลลง Dropdown
            dropdown.AddOptions(options);

            // 4. ผูกฟังก์ชันเข้ากับเหตุการณ์เมื่อเปลี่ยนค่า (Value Changed)
            dropdown.onValueChanged.AddListener(OnDropdownValueChanged);

            // 5. มั่นใจว่าเลือกอันแรกเป็นค่าเริ่มต้น ทั้งใน UI และในระบบสร้าง
            if (materials.Count > 0)
            {
                dropdown.value = 0;             // บังคับให้ UI แสดงอันแรก
                dropdown.RefreshShownValue();   // อัปเดตการแสดงผลหน้าจอ
                OnDropdownValueChanged(0);      // ส่งค่าวัสดุตัวแรกไปให้ BuildingSystem
            }
        }

        private void OnDropdownValueChanged(int index)
        {
            if (index < 0 || index >= materials.Count) return;

            MaterialData selectedMat = materials[index];

            // ส่งข้อมูลวัสดุที่เลือกไปยัง BuildingSystem
            if (BuildingSystem.Instance != null)
            {
                BuildingSystem.Instance.SelectMaterial(selectedMat);
                Debug.Log($"<color=green>Selected Material:</color> {selectedMat.materialName}");
            }
        }
    }
}
