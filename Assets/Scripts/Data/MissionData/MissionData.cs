using UnityEngine;
using System.Collections.Generic;

namespace Simulation.Data
{
    [CreateAssetMenu(fileName = "New Mission Data", menuName = "Simulation/Mission Data")]
    public class MissionData : ScriptableObject
    {
        [Header("Mission Info")]
        public string missionName = "Level 1";
        [TextArea(3, 5)]
        public string description = "รายละเอียดของภารกิจ...";

        [Header("Budget Settings")]
        [Tooltip("งบประมาณเริ่มต้นสำหรับภารกิจนี้")]
        public float startingBudget = 1000f;

        [Header("Available Inventory")]
        [Tooltip("วัสดุที่อนุญาตให้ใช้ได้ในด่านนี้ (ถ้าว่างไว้คือใช้ได้ทั้งหมด)")]
        public List<MaterialData> allowedMaterials;
        
        [Tooltip("ชิ้นส่วนโครงสร้างที่อนุญาตให้ใช้ได้ในด่านนี้ (เสา, พื้น ฯลฯ)")]
        public List<StructureData> allowedStructures;

        [Header("Simulation Challenges")]
        [Tooltip("ภัยพิบัติที่จะเกิดในด่านนี้เพื่อทดสอบสิ่งก่อสร้าง (ถ้ามี)")]
        public List<DisasterData> disasters;
        
        [Tooltip("เวลาที่โครงสร้างต้องยืนหยัดได้รอดปลอดภัย (วินาที)")]
        public float targetSurvivalTime = 5f;
    }
}
