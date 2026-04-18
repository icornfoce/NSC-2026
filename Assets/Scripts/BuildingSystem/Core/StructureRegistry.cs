using UnityEngine;
using System.Collections.Generic;

namespace Simulation.Building
{
    /// <summary>
    /// ศูนย์กลางลงทะเบียน StructureUnit ที่วางไว้ทั้งหมด
    /// ใช้แทน FindObjectsByType ที่ช้าเมื่อมีวัตถุจำนวนมาก
    ///
    /// BuildingSystem จะ Register/Unregister ตอนวาง/ลบ
    /// Physics ดึงรายการจากที่นี่แทนการ search ทั้ง Scene
    /// </summary>
    public static class StructureRegistry
    {
        private static readonly List<StructureUnit> _all = new List<StructureUnit>();

        /// <summary>Read-only snapshot of all registered structures.</summary>
        public static IReadOnlyList<StructureUnit> All => _all;
        public static int Count => _all.Count;

        public static void Register(StructureUnit unit)
        {
            if (unit != null && !_all.Contains(unit))
                _all.Add(unit);
        }

        public static void Unregister(StructureUnit unit)
        {
            _all.Remove(unit);
        }

        /// <summary>
        /// ล้างทั้งหมด — เรียกตอน ResetAllStructures เพื่อป้องกัน stale refs
        /// </summary>
        public static void Clear()
        {
            _all.Clear();
        }
    }
}
