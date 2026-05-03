using UnityEngine;
using System.Collections.Generic;

namespace Simulation.Mission
{
    /// <summary>
    /// รายการภัยพิบัติที่จะเกิดขึ้นในด่าน
    /// ใส่ใน List ของ MissionData ได้หลายตัว
    /// </summary>
    [System.Serializable]
    public class DisasterEntry
    {
        [Tooltip("ข้อมูลภัยพิบัติ (ScriptableObject)")]
        public DisasterData disasterData;

        [Tooltip("เวลาเริ่มต้นหลังจากเริ่ม Simulate (วินาที)")]
        public float startTime = 5f;
    }

    /// <summary>
    /// ScriptableObject สำหรับกำหนดค่าด่าน (Mission)
    /// สร้างจาก Create > Simulation > Mission Data
    /// </summary>
    [CreateAssetMenu(fileName = "New Mission", menuName = "Simulation/Mission Data")]
    public class MissionData : ScriptableObject
    {
        [Header("Mission Info")]
        public string missionName;
        [TextArea(2, 4)]
        public string description;

        [Header("Building Requirements")]
        [Tooltip("จำนวนชั้นขั้นต่ำที่ต้องสร้าง")]
        [Min(1)]
        public int requiredFloors = 1;

        [Tooltip("พื้นที่ขั้นต่ำต่อชั้น (ตารางเมตร = จำนวนช่อง Grid)")]
        [Min(1)]
        public int requiredAreaPerFloor = 4;

        [Header("Population Requirements")]
        [Tooltip("จำนวนคนที่ต้องรองรับ (PersonTarget ที่ต้องวาง)")]
        [Min(1)]
        public int requiredPopulation = 1;

        [Header("Budget")]
        [Tooltip("งบประมาณเริ่มต้นของด่านนี้")]
        public float startingBudget = 1000f;

        [Header("Disasters")]
        [Tooltip("รายการภัยพิบัติที่จะเกิดขึ้นระหว่าง Simulate")]
        public List<DisasterEntry> disasters = new List<DisasterEntry>();

        [Header("Simulation")]
        [Tooltip("ระยะเวลาจำลองทั้งหมด (วินาที) — เมื่อหมดเวลาจะประเมินผลอัตโนมัติ")]
        public float simulationDuration = 30f;
    }
}
