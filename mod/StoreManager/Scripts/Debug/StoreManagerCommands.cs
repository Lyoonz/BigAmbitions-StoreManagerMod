#nullable enable
using System;
using System.Linq;
using Entities;
using Helpers;
using IngameDebugConsole;   // CommandHelper — the game's own console-command wrapper (ExternalPlugins.dll)
using StoreManager.Domain;
using StoreManager.Interop;
using StoreManager.Runtime;
using UnityEngine;

namespace StoreManager.Debugging
{
    /// <summary>
    /// In-game debug-console commands (open the console with the debug key). A stand-in for the
    /// real hiring UI until Phase 1 builds one — lets a playtester assign a manager and drive
    /// the daily loop. Registered by <see cref="Core.StoreManagerCityMod"/>.
    /// </summary>
    public static class StoreManagerCommands
    {
        private static ManagerDirectory? _directory;

        // keep delegate refs so RemoveCommand (delegate overload — the string overload is internal) can match
        private static readonly Action<string, int> _hire = Hire;
        private static readonly Action _status = Status;
        private static readonly Action _runDay = RunDay;
        private static readonly Action _runWeek = RunWeek;

        public static void Register(ManagerDirectory directory, IGameBindings game)
        {
            _directory = directory;
            CommandHelper.AddCommand("StoreManager.Hire",
                "Assign a leader to the store you're standing in. role = manager | assistant, skill 1-5.",
                _hire, "role", "skill");
            CommandHelper.AddCommand("StoreManager.Status", "List managed stores.", _status);
            CommandHelper.AddCommand("StoreManager.RunDay", "Force the manager daily loop now.", _runDay);
            CommandHelper.AddCommand("StoreManager.RunWeek", "Force the weekly digest now.", _runWeek);
        }

        public static void Unregister()
        {
            CommandHelper.RemoveCommand(_hire);
            CommandHelper.RemoveCommand(_status);
            CommandHelper.RemoveCommand(_runDay);
            CommandHelper.RemoveCommand(_runWeek);
            _directory = null;
        }

        private static BuildingRegistration? CurrentStore()
        {
            var reg = BuildingManager.Instance?.buildingRegistration;
            if (reg == null || !(reg.RentedByPlayer || reg.BuildingOwnedByPlayer))
            {
                Debug.LogWarning("[StoreManager] Stand inside a business you own/rent first.");
                return null;
            }
            return reg;
        }

        private static void Hire(string role, int skill)
        {
            if (_directory == null) return;
            var reg = CurrentStore();
            if (reg == null) return;

            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = reg.Address }).FirstOrDefault();
            if (emp == null) { Debug.LogWarning("[StoreManager] This store has no employees to promote. Hire one first."); return; }

            var rank = role.ToLowerInvariant().StartsWith("assist") ? ManagerRank.AssistantManager : ManagerRank.Manager;
            var store = new GameRef(reg.Address.ToString(), reg.BusinessName, reg);
            var empRef = new GameRef(emp.id, emp.characterData?.name ?? emp.id, emp);
            _directory.AssignManager(store, empRef, new ManagementSkill(skill), rank);
            Debug.Log($"[StoreManager] {emp.characterData?.name} is now {rank} of '{reg.BusinessName}' at skill {skill}.");
        }

        private static void Status()
        {
            if (_directory == null) return;
            if (_directory.Stores.Count == 0) { Debug.Log("[StoreManager] No managed stores."); return; }
            foreach (var s in _directory.Stores)
                Debug.Log($"[StoreManager] '{s.Store.DisplayName}' mgr={s.Data.ManagerEmployeeId ?? "-"}({s.Data.ManagerSkill}) " +
                          $"asst={s.Data.AssistantEmployeeId ?? "-"}({s.Data.AssistantSkill}) " +
                          $"weekRevenue={s.Data.CurrentWeek.Revenue} attention={s.Data.CurrentWeek.AttentionItems.Count}");
        }

        private static void RunDay() { _directory?.OnDayElapsed(); Debug.Log("[StoreManager] daily loop ran."); }
        private static void RunWeek() { _directory?.OnWeekElapsed(); Debug.Log("[StoreManager] weekly digest ran."); }
    }
}
