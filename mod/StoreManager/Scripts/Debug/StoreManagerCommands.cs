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
        private static IGameBindings? _game;

        // keep delegate refs so RemoveCommand (delegate overload — the string overload is internal) can match
        private static readonly Action<string, int> _hire = Hire;
        private static readonly Action _status = Status;
        private static readonly Action _runDay = RunDay;
        private static readonly Action _runWeek = RunWeek;
        private static readonly Action<int> _selfTest = SelfTest;

        public static void Register(ManagerDirectory directory, IGameBindings game)
        {
            _directory = directory;
            _game = game;
            CommandHelper.AddCommand("StoreManager.Hire",
                "Assign a leader to the store you're standing in. role = manager | assistant, skill 1-5.",
                _hire, "role", "skill");
            CommandHelper.AddCommand("StoreManager.Status", "List managed stores.", _status);
            CommandHelper.AddCommand("StoreManager.RunDay", "Force the manager daily loop now.", _runDay);
            CommandHelper.AddCommand("StoreManager.RunWeek", "Force the weekly digest now.", _runWeek);
            CommandHelper.AddCommand("StoreManager.SelfTest",
                "End-to-end: hire a skill-N manager here, run a full week of the real loop, log the digest, undo. Never saved.",
                _selfTest, "skill");
        }

        public static void Unregister()
        {
            CommandHelper.RemoveCommand(_hire);
            CommandHelper.RemoveCommand(_status);
            CommandHelper.RemoveCommand(_runDay);
            CommandHelper.RemoveCommand(_runWeek);
            CommandHelper.RemoveCommand(_selfTest);
            _directory = null;
            _game = null;
        }

        /// <summary>
        /// Runs the real runtime loop end-to-end against the live game and logs outcomes.
        /// Injects a throwaway employee if the store has none; removes everything afterward.
        /// Nothing is saved (the caller is expected not to save the game after running this).
        /// </summary>
        public static void SelfTest(int skill) => SelfTest(skill, useFirstStoreIfNotInOne: true);

        public static void SelfTest(int skill, bool useFirstStoreIfNotInOne)
        {
            if (_directory == null || _game == null) { Debug.LogWarning("[StoreManager] Load a city first."); return; }
            var reg = CurrentStore();
            if (reg == null && useFirstStoreIfNotInOne)
            {
                reg = SaveGameManager.Current.BuildingRegistrations.FirstOrDefault(r =>
                    (r.RentedByPlayer || r.BuildingOwnedByPlayer) && r.scheduleDays != null && r.scheduleDays.Count > 0
                    && r.businessTypeName != "ba:businesstype_empty");
                if (reg != null) Debug.Log($"[StoreManager] SelfTest: not in a store — using '{reg.BusinessName}'.");
            }
            if (reg == null) return;

            EmployeeInstance? throwaway = null;
            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = reg.Address }).FirstOrDefault();
            if (emp == null)
            {
                throwaway = EmployeeHelper.CreateAIEmployeeInstance("ba:skill_customerservice");
                throwaway.characterData.name = "SELFTEST Worker";
                throwaway.assignedAddress = reg.Address;
                throwaway.hourlyWage = 18f;
                EmployeeHelper.GetEmployeeInstances().Add(throwaway);
                EmployeeHelper.EmployeeInstancesDictionary[throwaway.id] = throwaway;
                emp = throwaway;
                Debug.Log($"[StoreManager] SelfTest: injected throwaway employee {emp.id}");
            }

            var store = new GameRef(reg.Address.ToString(), reg.BusinessName, reg);
            var empRef = new GameRef(emp.id, emp.characterData?.name ?? emp.id, emp);
            var managed = _directory.AssignManager(store, empRef, new ManagementSkill(skill), ManagerRank.Manager);
            Debug.Log($"[StoreManager] SelfTest: {emp.characterData?.name} = Manager of '{reg.BusinessName}' at skill {skill}. Running 7 days...");

            for (int d = 1; d <= 7; d++)
            {
                try
                {
                    managed.RunDay();
                    var w = managed.Data.CurrentWeek;
                    Debug.Log($"[StoreManager] SelfTest day {d}: restockSpend={w.RestockSpend:C0} " +
                              $"shifts={w.ShiftsCovered}/{w.ShiftsTotal} complaints={w.ComplaintsResolved}/{w.ComplaintsTotal} " +
                              $"mistakes={w.MistakeCount} (cost {w.MistakeCost:C0})");
                }
                catch (Exception e) { Debug.LogError($"[StoreManager] SelfTest day {d} threw: {e}"); }
            }

            try
            {
                var report = managed.CloseWeek();
                Debug.Log($"[StoreManager] SelfTest digest: '{report.StoreName}' revenue={report.Revenue:C0} ({report.RevenueDelta:C0}) " +
                          $"restock={report.RestockSpend:C0} shifts={report.ShiftsCovered}/{report.ShiftsTotal} " +
                          $"complaints={report.ComplaintsResolved}/{report.ComplaintsTotal} " +
                          $"mistakes={report.MistakeCount} (cost {report.MistakeCost:C0}) " +
                          $"attention=[{string.Join(" | ", report.AttentionItems)}]");
            }
            catch (Exception e) { Debug.LogError($"[StoreManager] SelfTest digest threw: {e}"); }

            _directory.RemoveLeadership(store, ManagerRank.Manager);
            if (throwaway != null)
            {
                EmployeeHelper.GetEmployeeInstances().Remove(throwaway);
                EmployeeHelper.EmployeeInstancesDictionary.Remove(throwaway.id);
            }
            Debug.Log("[StoreManager] SelfTest: done, manager + throwaway employee removed. DO NOT SAVE this session.");
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

        /// <summary>Shared by the debug command and the options-menu button.</summary>
        public static void Hire(string role, int skill)
        {
            if (_directory == null) { Debug.LogWarning("[StoreManager] Load a city first."); return; }
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

        public static void Status()
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
