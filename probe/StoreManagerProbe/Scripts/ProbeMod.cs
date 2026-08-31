#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using BAModAPI;
using Entities;                 // EmployeeInstance, BuildingRegistration
using Helpers;                  // EmployeeHelper
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  PHASE 0 PROBE — throwaway, not shipped.
//  Runs headless on city load: dumps the workforce graph, then attempts the three
//  writes and re-reads after a delay so the log shows whether the sim kept them.
//  Everything goes to Player.log with the [StoreManagerProbe] prefix.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.ProbeInit))]
[assembly: RegisterModClass(typeof(StoreManagerProbe.ProbeAutoLoad))]

namespace StoreManagerProbe
{
    /// <summary>
    /// Headless test aid: at the main menu, load the last save (same as clicking "Continue")
    /// so the city-load probe runs without a human. LoadAsync(null,true) resolves the save from
    /// PlayerPrefSettings.LastSaveGameName. Never saves — the probe is read-mostly and self-cleans.
    /// </summary>
    [ModEntryMainMenu]
    public sealed class ProbeAutoLoad : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _host = new GameObject("StoreManagerProbeAutoLoad");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<AutoLoadRunner>().Logger = context.Logger;
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            return Task.CompletedTask;
        }
    }

    public sealed class AutoLoadRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private void Start() => StartCoroutine(Go());
        private IEnumerator Go()
        {
            yield return new WaitForSeconds(6f);
            Logger?.Info("AutoLoad: calling SaveGameManager.LoadAsync(null, true) — loading last save.");
            System.Threading.Tasks.Task<bool>? t = null;
            try { t = SaveGameManager.LoadAsync(null, true); }
            catch (Exception e) { Logger?.Error(e); yield break; }
            while (t != null && !t.IsCompleted) yield return null;
            Logger?.Info($"AutoLoad: LoadAsync completed = {t?.Result}");
        }
    }

    [ModEntryOnCityLoad]
    public sealed class ProbeInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            context.Logger.Info("Probe loaded — running headless sequence on city load.");
            _host = new GameObject("StoreManagerProbe");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<ProbeRunner>().Logger = context.Logger;
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
        public IModLogger? Logger;

        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(3f);
            Safe("DUMP", DumpWorkforceGraph);

            yield return new WaitForSeconds(1f);
            string? empId = null, storeSig = null;
            Safe("PROBE1 reassign", () => ProbeReassignTask(out empId, out storeSig));

            yield return new WaitForSeconds(1f);
            Safe("PROBE2 shift", ProbeWriteShift);

            // let a few in-game minutes pass, then re-check whether the writes stuck
            yield return new WaitForSeconds(20f);
            Safe("RECHECK", () => Recheck(empId, storeSig));
        }

        private void Safe(string label, Action a)
        {
            try { Log($"── {label} ──"); a(); Log($"{label}: ok"); }
            catch (Exception e) { Log($"{label}: FAILED {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
        }

        private void DumpWorkforceGraph()
        {
            var sgm = SaveGameManager.Current;
            Log($"day={TimeHelper.CurrentDay} dow={TimeHelper.GetDayOfWeek()} regs={sgm?.BuildingRegistrations?.Count}");
            foreach (var b in sgm!.BuildingRegistrations)
            {
                if (!(b.RentedByPlayer || b.BuildingOwnedByPlayer)) continue;
                Log($"STORE '{b.BusinessName}' addr={b.Address} type={b.businessTypeName} " +
                    $"days={b.scheduleDays?.Count} sat={b.satisfaction?.overall} avgDailyIncome={b.GetAvgDailyIncome(1)}");
                var emps = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address });
                foreach (var e in emps)
                    Log($"  EMP {e.id} wage={e.hourlyWage} stations=[{string.Join(",", e.assignedWorkStationItems)}] " +
                        $"weeklyHrs={e.assignedWeeklyHours} absent={e.isAbsent} skillPrimary={e.GetPrimarySkill()}");
                if (b.scheduleDays != null)
                    foreach (var d in b.scheduleDays)
                        foreach (var s in d.workShifts)
                            Log($"  SHIFT day={d.day} emp={s.employeeId} {s.startingHour}-{s.endingHour} station={s.itemInstanceId}");
            }
        }

        private BuildingRegistration? FirstStore() =>
            SaveGameManager.Current?.BuildingRegistrations?.FirstOrDefault(x =>
                (x.RentedByPlayer || x.BuildingOwnedByPlayer) && x.businessTypeName != "ba:businesstype_empty");

        private void ProbeReassignTask(out string? empId, out string? storeSig)
        {
            empId = null; storeSig = null;
            var b = FirstStore();
            if (b == null) { Log("no player store"); return; }
            storeSig = b.Address.ToString();
            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address }).FirstOrDefault();
            if (emp == null) { Log("store has no employees"); return; }
            empId = emp.id;
            Log($"emp {emp.id} stations before = [{string.Join(",", emp.assignedWorkStationItems)}]");
            // Non-destructive: just re-run the game's own recompute and log. A real reassignment
            // needs a target station id which we don't want to guess blindly in a probe.
            emp.UpdateAssignedWorkStationItems();
            emp.UpdateWeeklyHoursAndDays();
            Log($"emp {emp.id} stations after recompute = [{string.Join(",", emp.assignedWorkStationItems)}]");
        }

        private void ProbeWriteShift()
        {
            var b = FirstStore();
            if (b?.scheduleDays == null || b.scheduleDays.Count == 0) { Log("no schedule"); return; }
            var emp = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address }).FirstOrDefault();
            if (emp == null) { Log("no employee"); return; }
            var day = b.scheduleDays[0];
            int before = day.workShifts.Count;
            var shift = new WorkShift { startingHour = 9, endingHour = 13, employeeId = emp.id, itemInstanceId = "" };
            day.AddWorkShift(shift);
            emp.UpdateWeeklyHoursAndDays();
            Log($"day {day.day}: shifts {before} -> {day.workShifts.Count} (added 09-13 for {emp.id})");
            _addedShiftDay = day;
            _addedShift = shift;
        }

        private ScheduleDay? _addedShiftDay;
        private WorkShift? _addedShift;

        private void Recheck(string? empId, string? storeSig)
        {
            if (_addedShiftDay != null && _addedShift != null)
                Log($"recheck: added shift still present = {_addedShiftDay.workShifts.Contains(_addedShift)} " +
                    $"(day now has {_addedShiftDay.workShifts.Count})");
            if (empId != null)
            {
                var e = EmployeeHelper.GetEmployeeById(empId, false);
                Log($"recheck: emp {empId} stations = [{string.Join(",", e?.assignedWorkStationItems ?? new System.Collections.Generic.List<string>())}]");
            }
            // clean up the probe shift so we don't leave junk in the save
            if (_addedShiftDay != null && _addedShift != null)
            {
                _addedShiftDay.RemoveWorkShift(_addedShift);
                Log("probe shift removed");
            }
        }

        private void Log(string msg)
        {
            Logger?.Info(msg);
            Debug.Log($"[StoreManagerProbe] {msg}");
        }
    }
}
