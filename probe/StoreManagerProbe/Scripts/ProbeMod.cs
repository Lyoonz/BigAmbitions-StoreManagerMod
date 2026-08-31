#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using Entities;                 // EmployeeInstance, BuildingRegistration
using Helpers;                  // EmployeeHelper
using UnityEngine;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────────────────
//  PHASE 0 PROBE — throwaway. Not shipped. This is the go/no-go gate.
//
//  Goal: prove three writes are possible from an official-SDK mod, with no BepInEx:
//    F9  → reassign an employee's task            (PROBE 1)
//    F10 → add a shift to tomorrow's roster       (PROBE 2)
//    F11 → place a restock order                  (PROBE 3)
//    F8  → dump the workforce object graph to the log (discovery aid)
//
//  Fill each TODO with the real class name found by decompiling the game assemblies
//  (ILSpy/dnSpy over the DLLs the SDK imported). Then run in a loaded city, watch the
//  log, and fill in probe/StoreManagerProbe/REPORT.md.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.ProbeInit))]

namespace StoreManagerProbe
{
    [ModEntryOnCityLoad]
    public sealed class ProbeInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _host = new GameObject("StoreManagerProbe");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            var runner = _host.AddComponent<ProbeRunner>();
            runner.Logger = context.Logger;
            context.Logger.Info("Probe armed. F8 dump · F9 reassign · F10 shift · F11 restock.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            return Task.CompletedTask;
        }
    }

    public sealed class ProbeRunner : MonoBehaviour
    {
        // PHASE0: exact type of ModContext.Logger — likely IModLogger. Adjust if the SDK names it
        // differently; only .Info(string) is used here.
        public IModLogger? Logger;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F8].wasPressedThisFrame) SafeRun("DUMP", DumpWorkforceGraph);
            if (kb[Key.F9].wasPressedThisFrame) SafeRun("PROBE 1 reassign", ProbeReassignTask);
            if (kb[Key.F10].wasPressedThisFrame) SafeRun("PROBE 2 shift", ProbeWriteShift);
            if (kb[Key.F11].wasPressedThisFrame) SafeRun("PROBE 3 restock", ProbeRestock);
        }

        private void SafeRun(string label, Action action)
        {
            try
            {
                Log($"── {label} ──");
                action();
                Log($"{label}: completed without exception");
            }
            catch (Exception e)
            {
                Log($"{label}: FAILED — {e.GetType().Name}: {e.Message}");
            }
        }

        // ── discovery ───────────────────────────────────────────────────────────
        //  Phase 0 (decompile) confirmed these real types — see PHASE0-FINDINGS.md:
        //    SaveGameManager.Current.BuildingRegistrations : List<BuildingRegistration>
        //    BuildingRegistration.scheduleDays / businessTypeName / Address / RentedByPlayer
        //    Helpers.EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo{ withAssignedAddress })
        //    Entities.EmployeeInstance { id, hourlyWage, assignedAddress, assignedWorkStationItems, ... }
        //    ScheduleDay { workShifts:List<WorkShift>, AddWorkShift(), RemoveWorkShift() }
        //    WorkShift { startingHour, endingHour, employeeId, itemInstanceId, type }
        //    Buildings.Schedule.ScheduleAutoFiller  (Google.OrTools CP-SAT)
        //    GameManager.ChangeMoneySafe(float, TransactionInfo, ...)
        //    GlobalEvents.onNewDay / onNewHour / onSaveGame  (static Action)
        private void DumpWorkforceGraph()
        {
            foreach (var b in SaveGameManager.Current.BuildingRegistrations)
            {
                if (!(b.RentedByPlayer || b.BuildingOwnedByPlayer)) continue;
                Log($"STORE {b.Address} type={b.businessTypeName} days={b.scheduleDays?.Count}");
                var emps = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address });
                foreach (var e in emps)
                    Log($"  EMP {e.id} wage={e.hourlyWage} stations=[{string.Join(",", e.assignedWorkStationItems)}] " +
                        $"weeklyHrs={e.assignedWeeklyHours} absent={e.isAbsent}");
                var today = b.scheduleDays?.FirstOrDefault(d => d.isOpen);
                if (today != null)
                    foreach (var s in today.workShifts)
                        Log($"  SHIFT emp={s.employeeId} {s.startingHour}-{s.endingHour} station={s.itemInstanceId} type={s.type}");
            }
            // TODO: also log the ScheduleAutoFiller ctor params you need, and search GameVariables for difficulty.
        }

        // ── PROBE 1 ─────────────────────────────────────────────────────────────
        private void ProbeReassignTask()
        {
            var b = FirstPlayerStore();
            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address }).First();
            Log($"before: stations=[{string.Join(",", emp.assignedWorkStationItems)}]");
            // TODO: set emp.assignedWorkStationItems to a restock-station itemInstanceId, OR call
            //   EmployeeStationController.AssignEmployee / UnassignEmployee on the in-world Employee.
            emp.UpdateAssignedWorkStationItems();
            Log($"after:  stations=[{string.Join(",", emp.assignedWorkStationItems)}]  " +
                "// WATCH in-game: does the employee walk to restock, and does it survive a save/reload?");
        }

        // ── PROBE 2 ─────────────────────────────────────────────────────────────
        private void ProbeWriteShift()
        {
            var b = FirstPlayerStore();
            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address }).First();
            var day = b.scheduleDays[0]; // TODO: pick tomorrow's DayOfWeekOrdered index
            var shift = new WorkShift { startingHour = 9, endingHour = 13, employeeId = emp.id, itemInstanceId = "" /* TODO station id */ };
            day.AddWorkShift(shift);
            emp.UpdateWeeklyHoursAndDays();
            Log($"added shift; day now has {day.workShifts.Count} shifts. // WATCH: shows in BizMan schedule? emp turns up?");
            // Variant B: construct ScheduleAutoFiller and Run() — TODO once ctor is known.
        }

        // ── PROBE 3 ─────────────────────────────────────────────────────────────
        private void ProbeRestock()
        {
            var b = FirstPlayerStore();
            // TODO: gather shelf ItemInstances, then either:
            //   A) ReStockingHelper.RedistributeStockByPercentage(b, itemInstances)  — warehouse→shelf only
            //   B) the wholesale/importer purchase path + GameManager.ChangeMoneySafe for the cash side.
            Log("restock: TODO wire the supplier-order call. // WATCH: cash decrements, delivery arrives.");
        }

        private static BuildingRegistration FirstPlayerStore() =>
            SaveGameManager.Current.BuildingRegistrations.First(x =>
                (x.RentedByPlayer || x.BuildingOwnedByPlayer) && x.businessTypeName != "ba:businesstype_empty");

        private void Log(string msg)
        {
            Logger?.Info(msg);
            Debug.Log($"[StoreManagerProbe] {msg}");
        }
    }
}
