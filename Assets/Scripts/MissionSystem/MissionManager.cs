using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Simulation.Building;
using Simulation.Character;
using Simulation.Physics;

namespace Simulation.Mission
{
    /// <summary>
    /// Mission Manager — ควบคุมระบบด่าน
    /// 1. ตรวจสอบเงื่อนไขก่อนเริ่ม (จำนวนชั้น, พื้นที่, คน)
    /// 2. เรียกภัยพิบัติตามเวลาที่กำหนด
    /// 3. ประเมินผลเป็น 3 ดาว (คนรอด, ตึกไม่พัง, เงิน+)
    /// 
    /// ใช้งาน: แปะ Component นี้ที่ GameObject เดียวกับ SimulationManager
    /// หรือ GameObject ใดก็ได้ แล้ว Assign MissionData ใน Inspector
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }

        [Header("Current Mission")]
        [SerializeField] private MissionData currentMission;

        [Header("State (Read Only)")]
        [SerializeField] private bool isMissionActive = false;
        [SerializeField] private int lastStarRating = 0;
        [SerializeField] private float simulationTimer = 0f;

        // ── Runtime State ──
        private List<DisasterBase> _activeDisasters = new List<DisasterBase>();
        private List<Coroutine> _disasterCoroutines = new List<Coroutine>();

        // Tracking สำหรับประเมินผล
        private int _initialPeopleCount;
        private int _initialStructureCount;
        private float _budgetBeforeSimulation;

        // ── Events ──
        /// <summary>เรียกเมื่อ Mission เริ่ม</summary>
        public event System.Action OnMissionStarted;

        /// <summary>เรียกเมื่อ Mission จบ พร้อมจำนวนดาว</summary>
        public event System.Action<int> OnMissionCompleted;

        /// <summary>เรียกเมื่อเงื่อนไข Pre-check ไม่ผ่าน พร้อมข้อความ</summary>
        public event System.Action<string> OnValidationFailed;

        /// <summary>เรียกเมื่อภัยพิบัติเริ่ม</summary>
        public event System.Action<DisasterData> OnDisasterStarted;

        // ── Public Properties ──
        public MissionData CurrentMission => currentMission;
        public bool IsMissionActive => isMissionActive;
        public int LastStarRating => lastStarRating;
        public float SimulationTimer => simulationTimer;
        public float SimulationTimeRemaining => currentMission != null ? Mathf.Max(0, currentMission.simulationDuration - simulationTimer) : 0f;

        // ────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(this);
        }

        private void Update()
        {
            if (!isMissionActive) return;

            simulationTimer += Time.deltaTime;

            // เช็คว่าหมดเวลาแล้วหรือยัง
            if (currentMission != null && simulationTimer >= currentMission.simulationDuration)
            {
                EndMission();
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// กำหนด Mission ที่จะเล่น
        /// </summary>
        public void SetMission(MissionData mission)
        {
            currentMission = mission;

            // ตั้ง budget ตาม mission
            if (BuildingSystem.Instance != null && mission != null)
            {
                BuildingSystem.Instance.SetBudget(mission.startingBudget);
            }
        }

        /// <summary>
        /// เริ่ม Mission — จะเรียก SimulationManager.StartSimulation() ด้วย
        /// ถ้ามี Mission จะตรวจเงื่อนไขก่อน ถ้าไม่มีจะเริ่มแบบ Free-play
        /// </summary>
        public void StartMission()
        {
            if (isMissionActive) return;

            // 1. ถ้ามี Mission ให้ตรวจ Pre-check
            if (currentMission != null)
            {
                string validationError = ValidatePreConditions();
                if (validationError != null)
                {
                    Debug.LogWarning($"[MissionManager] Validation failed: {validationError}");
                    OnValidationFailed?.Invoke(validationError);
                    return;
                }
            }

            isMissionActive = true;
            simulationTimer = 0f;

            // บันทึกสถานะก่อนเริ่ม
            _initialPeopleCount = CountAlivePeople();
            _initialStructureCount = CountIntactStructures();
            _budgetBeforeSimulation = BuildingSystem.Instance != null ? BuildingSystem.Instance.CurrentBudget : 0f;

            // เริ่ม Simulation (ฟิสิกส์ + NavMesh + NPC)
            if (SimulationManager.Instance != null && !SimulationManager.Instance.IsSimulating)
            {
                SimulationManager.Instance.StartSimulation();
            }

            // Schedule ภัยพิบัติ (ถ้ามี)
            if (currentMission != null)
            {
                ScheduleDisasters();
                Debug.Log($"<color=cyan>★ Mission Started: {currentMission.missionName}</color>");
            }
            else
            {
                Debug.Log("<color=cyan>▶ Free-play Simulation Started</color>");
            }

            OnMissionStarted?.Invoke();
        }

        /// <summary>
        /// คืนค่าสถิติปัจจุบัน (ชั้น, พื้นที่, คน) เพื่อเอาไปแสดงใน UI
        /// </summary>
        public (int floors, int area, int people) GetCurrentStats()
        {
            return (CountFloors(), CountAreaPerFloor(), CountPlacedPeople());
        }

        /// <summary>
        /// จบ Mission — หยุด Simulation, ประเมินผล, แสดงดาว
        /// </summary>
        public void EndMission()
        {
            if (!isMissionActive) return;
            isMissionActive = false;

            // หยุดภัยพิบัติทั้งหมด
            StopAllDisasters();

            // ประเมินผล
            lastStarRating = EvaluateResult();

            // แสดงผล
            Debug.Log($"<color=yellow>★ Mission Complete: {currentMission.missionName} — {lastStarRating} Star(s)!</color>");

            OnMissionCompleted?.Invoke(lastStarRating);

            // หยุด Simulation
            if (SimulationManager.Instance != null && SimulationManager.Instance.IsSimulating)
            {
                SimulationManager.Instance.StopSimulation();
            }
        }

        // ────────────────────────────────────────────────────────────────
        // Pre-Condition Validation
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ตรวจสอบว่าผู้เล่นสร้างตามเงื่อนไขครบหรือยัง
        /// คืน null ถ้าผ่าน หรือ string บอกเหตุผลถ้าไม่ผ่าน
        /// </summary>
        private string ValidatePreConditions()
        {
            if (currentMission == null) return "No mission assigned.";

            // 1. Check Floor Count
            int floors = CountFloors();
            if (floors < currentMission.requiredFloors)
            {
                return $"Not enough floors! Need {currentMission.requiredFloors} (Current: {floors})";
            }

            // 2. Check Area Per Floor
            int areaPerFloor = CountAreaPerFloor();
            if (areaPerFloor < currentMission.requiredAreaPerFloor)
            {
                return $"Not enough area per floor! Need {currentMission.requiredAreaPerFloor} m² (Current: {areaPerFloor} m²)";
            }

            // 3. Check Population
            int people = CountPlacedPeople();
            if (people < currentMission.requiredPopulation)
            {
                return $"Not enough population! Need {currentMission.requiredPopulation} (Current: {people})";
            }

            return null; // All passed
        }

        // ────────────────────────────────────────────────────────────────
        // Counting Helpers
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// นับจำนวนชั้นจากตำแหน่ง Y ของ Structure ทั้งหมด
        /// ใช้ heightStep จาก BuildingSystem เป็นตัวแบ่งชั้น
        /// </summary>
        private int CountFloors()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            if (units.Length == 0) return 0;

            float heightStep = BuildingSystem.Instance != null ? BuildingSystem.Instance.GetGridSize : 3f;
            HashSet<int> floorLevels = new HashSet<int>();

            foreach (var unit in units)
            {
                if (unit == null || !unit.gameObject.activeSelf) continue;
                // กรอง: นับเฉพาะชิ้นส่วนประเภท Normal (Floor, Foundation) ไม่นับ Wall/Door
                if (unit.Data != null && unit.Data.structureType != Simulation.Data.StructureType.Normal) continue;

                int floor = Mathf.RoundToInt(unit.transform.position.y / heightStep);
                floorLevels.Add(floor);
            }

            return floorLevels.Count;
        }

        /// <summary>
        /// นับพื้นที่ต่อชั้น (ช่อง Grid ที่มี Structure วางอยู่ในชั้นที่มีมากที่สุด)
        /// 1 ช่อง = 1 ตารางเมตร
        /// </summary>
        private int CountAreaPerFloor()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            if (units.Length == 0) return 0;

            float heightStep = BuildingSystem.Instance != null ? BuildingSystem.Instance.GetGridSize : 3f;
            float gridSize = BuildingSystem.Instance != null ? BuildingSystem.Instance.GetGridSize : 1f;

            // นับพื้นที่ต่อชั้น
            Dictionary<int, HashSet<Vector2Int>> floorAreas = new Dictionary<int, HashSet<Vector2Int>>();

            foreach (var unit in units)
            {
                if (unit == null || !unit.gameObject.activeSelf) continue;
                if (unit.Data == null) continue;
                // นับเฉพาะ Normal structures (Floor ฯลฯ)
                if (unit.Data.structureType != Simulation.Data.StructureType.Normal) continue;

                int floor = Mathf.RoundToInt(unit.transform.position.y / heightStep);
                if (!floorAreas.ContainsKey(floor))
                {
                    floorAreas[floor] = new HashSet<Vector2Int>();
                }

                // คำนวณช่อง Grid ที่ Structure นี้ครอบครอง (ตาม size)
                int sizeX = Mathf.Max(1, Mathf.RoundToInt(unit.Data.size.x));
                int sizeZ = Mathf.Max(1, Mathf.RoundToInt(unit.Data.size.z));

                int gridX = Mathf.RoundToInt(unit.transform.position.x / gridSize);
                int gridZ = Mathf.RoundToInt(unit.transform.position.z / gridSize);

                for (int dx = 0; dx < sizeX; dx++)
                {
                    for (int dz = 0; dz < sizeZ; dz++)
                    {
                        floorAreas[floor].Add(new Vector2Int(gridX + dx, gridZ + dz));
                    }
                }
            }

            // คืนค่าชั้นที่มีพื้นที่มากที่สุด
            int maxArea = 0;
            foreach (var kvp in floorAreas)
            {
                if (kvp.Value.Count > maxArea) maxArea = kvp.Value.Count;
            }

            return maxArea;
        }

        /// <summary>
        /// นับจำนวน PersonTarget ที่ผู้เล่นวางไว้ในฉาก
        /// </summary>
        private int CountPlacedPeople()
        {
            PersonTarget[] targets = FindObjectsByType<PersonTarget>(FindObjectsSortMode.None);
            return targets.Length;
        }

        /// <summary>
        /// นับจำนวนคนที่ยังมีชีวิตอยู่ (PersonAI ที่ยัง active)
        /// </summary>
        private int CountAlivePeople()
        {
            PersonAI[] people = FindObjectsByType<PersonAI>(FindObjectsSortMode.None);
            int alive = 0;
            foreach (var p in people)
            {
                if (p != null && p.gameObject.activeSelf) alive++;
            }
            return alive;
        }

        /// <summary>
        /// นับจำนวน Structure ที่ยังไม่พัง
        /// </summary>
        private int CountIntactStructures()
        {
            StructureUnit[] units = FindObjectsByType<StructureUnit>(FindObjectsSortMode.None);
            int intact = 0;
            foreach (var unit in units)
            {
                if (unit == null || !unit.gameObject.activeSelf) continue;
                var stress = unit.GetComponent<StructuralStress>();
                if (stress == null || !stress.IsBroken)
                {
                    intact++;
                }
            }
            return intact;
        }

        // ────────────────────────────────────────────────────────────────
        // Disaster Scheduling
        // ────────────────────────────────────────────────────────────────

        private void ScheduleDisasters()
        {
            _activeDisasters.Clear();
            _disasterCoroutines.Clear();

            if (currentMission.disasters == null) return;

            foreach (var entry in currentMission.disasters)
            {
                if (entry.disasterData == null) continue;
                Coroutine co = StartCoroutine(DisasterScheduleCoroutine(entry));
                _disasterCoroutines.Add(co);
            }
        }

        private IEnumerator DisasterScheduleCoroutine(DisasterEntry entry)
        {
            // รอจนถึงเวลาเริ่ม
            yield return new WaitForSeconds(entry.startTime);

            if (!isMissionActive) yield break;

            // สร้าง Disaster ตาม Type
            DisasterBase disaster = CreateDisaster(entry.disasterData);
            if (disaster == null) yield break;

            _activeDisasters.Add(disaster);
            OnDisasterStarted?.Invoke(entry.disasterData);

            Debug.Log($"<color=red>⚠ Disaster: {entry.disasterData.disasterName}</color>");

            // เริ่มภัยพิบัติ
            disaster.Start();
        }

        private DisasterBase CreateDisaster(DisasterData data)
        {
            switch (data.disasterType)
            {
                case DisasterType.Earthquake:     return new EarthquakeDisaster(data, this);
                case DisasterType.Flood:          return new FloodDisaster(data, this);
                case DisasterType.StrongWind:     return new StrongWindDisaster(data, this);
                case DisasterType.UFOAbduction:   return new UFOAbductionDisaster(data, this);
                case DisasterType.DragonFire:     return new DragonFireDisaster(data, this);
                case DisasterType.AcidRain:       return new AcidRainDisaster(data, this);
                case DisasterType.Tornado:        return new TornadoDisaster(data, this);
                default:
                    Debug.LogWarning($"[MissionManager] Unknown disaster type: {data.disasterType}");
                    return null;
            }
        }

        private void StopAllDisasters()
        {
            // หยุด Coroutine ทั้งหมด
            foreach (var co in _disasterCoroutines)
            {
                if (co != null) StopCoroutine(co);
            }
            _disasterCoroutines.Clear();

            // หยุด Disaster ที่กำลังทำงาน
            foreach (var disaster in _activeDisasters)
            {
                if (disaster.IsRunning) disaster.Stop();
            }
            _activeDisasters.Clear();
        }

        // ────────────────────────────────────────────────────────────────
        // Star Rating Evaluation
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluate Mission result → 0-3 stars
        /// ★ Star 1: Everyone survived
        /// ★ Star 2: No structural damage
        /// ★ Star 3: Positive budget (Profit)
        /// </summary>
        private int EvaluateResult()
        {
            int stars = 0;

            // ★ Star 1: All survived
            int alivePeople = CountAlivePeople();
            bool allSurvived = alivePeople >= _initialPeopleCount && _initialPeopleCount > 0;

            if (allSurvived)
            {
                stars++;
                Debug.Log("  ★ Star 1: Everyone survived! ✓");
            }
            else
            {
                Debug.Log($"  ☆ Star 1: Survivors {alivePeople}/{_initialPeopleCount} ✗");
            }

            // ★ Star 2: No damage
            int intactStructures = CountIntactStructures();
            bool noBroken = intactStructures >= _initialStructureCount && _initialStructureCount > 0;

            if (noBroken)
            {
                stars++;
                Debug.Log("  ★ Star 2: No structural damage! ✓");
            }
            else
            {
                Debug.Log($"  ☆ Star 2: Intact structures {intactStructures}/{_initialStructureCount} ✗");
            }

            // ★ Star 3: Profit
            float currentBudget = BuildingSystem.Instance != null ? BuildingSystem.Instance.CurrentBudget : 0f;
            bool isProfit = currentBudget > 0f;

            if (isProfit)
            {
                stars++;
                Debug.Log($"  ★ Star 3: Positive budget ({currentBudget:F0}) ✓");
            }
            else
            {
                Debug.Log($"  ☆ Star 3: Negative budget ({currentBudget:F0}) ✗");
            }

            return stars;
        }

        // ────────────────────────────────────────────────────────────────
        // Debug / Inspector
        // ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [ContextMenu("Debug: Validate Pre-Conditions")]
        private void DebugValidate()
        {
            string result = ValidatePreConditions();
            if (result == null)
                Debug.Log("<color=green>[MissionManager] All pre-conditions passed! ✓</color>");
            else
                Debug.LogWarning($"[MissionManager] {result}");
        }

        [ContextMenu("Debug: Count Stats")]
        private void DebugCountStats()
        {
            Debug.Log($"Floors: {CountFloors()}, Area/Floor: {CountAreaPerFloor()}, People: {CountPlacedPeople()}, Alive: {CountAlivePeople()}, Structures: {CountIntactStructures()}");
        }
#endif
    }
}
