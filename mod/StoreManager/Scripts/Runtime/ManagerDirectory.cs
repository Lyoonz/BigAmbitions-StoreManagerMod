#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;
using UnityEngine;

namespace StoreManager.Runtime
{
    public readonly struct ActionResult
    {
        public bool Ok { get; }
        public string Message { get; }
        private ActionResult(bool ok, string msg) { Ok = ok; Message = msg; }
        public static ActionResult Yes(string msg = "") => new(true, msg);
        public static ActionResult No(string msg) => new(false, msg);
    }

    /// <summary>
    /// Owns the <see cref="StoreManagerPlan"/>s for the loaded save. All the tick entry points
    /// (OnNewDay / OnNewHour / Save) are called from the game's unguarded event invoke, so every
    /// one wraps its body in try/catch — an exception here must never abort the game's day or save.
    /// </summary>
    public sealed class ManagerDirectory
    {
        private const string SaveKey = "StoreManager.plans.v1";

        private readonly List<StoreManagerPlan> _plans = new();
        private readonly GlobalDefaults _defaults;
        private readonly HashSet<string> _dormantNotified = new();

        private bool _readOnly;          // set when the persisted blob is present but unparseable
        private bool _teardownArmed;     // first tick after load may run the destructive reconcile
        private int _lastHourReconcile = -99;

        public ManagerDirectory(GlobalDefaults defaults) => _defaults = defaults;

        public IReadOnlyList<StoreManagerPlan> Plans => _plans;
        public GlobalDefaults Defaults => _defaults;
        public bool ReadOnly => _readOnly;

        // ── persistence ─────────────────────────────────────────────────────────
        public void Load()
        {
            _plans.Clear();
            var result = Serialization.Load(GameApi.LoadModData(SaveKey));
            switch (result.Status)
            {
                case Serialization.LoadStatus.Corrupt:
                    _readOnly = true;
                    Debug.LogError("[StoreManager] saved plans are present but unreadable — running read-only, will NOT overwrite.");
                    Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_data_corrupt", null, "sm_corrupt");
                    return;
                case Serialization.LoadStatus.Ok:
                    _plans.AddRange(result.Plans);
                    break;
            }
            // Do NOT run the destructive teardown from Load — the employee subsystem may not be
            // ready yet. Arm it for the first real tick.
            _teardownArmed = true;
            SafeReconcile(announce: false, allowTeardown: false);
        }

        public void Save()
        {
            if (_readOnly) return;
            try
            {
                var json = Serialization.Serialize(_plans);
                if (!string.IsNullOrEmpty(json)) GameApi.SaveModData(SaveKey, json!);
            }
            catch (Exception e) { Debug.LogError("[StoreManager] Save failed: " + e.Message); }
        }

        public void Detach()
        {
            Save();
            _plans.Clear();
        }

        // ── lookup ──────────────────────────────────────────────────────────────
        public StoreManagerPlan? PlanForManager(string employeeId) =>
            _plans.FirstOrDefault(p => p.ManagerEmployeeId == employeeId);

        public StoreManagerPlan? PlanSupervising(string storeAddress) =>
            _plans.FirstOrDefault(p => p.Supervises(storeAddress));

        // ── player actions ──────────────────────────────────────────────────────
        public ActionResult AdoptManager(string hqAddress, string employeeId) =>
            AdoptManager(hqAddress, employeeId, skipScheduleCheck: false);

        public ActionResult AdoptManager(string hqAddress, string employeeId, bool skipScheduleCheck)
        {
            if (_readOnly) return ActionResult.No("store-manager data is in read-only mode this session");
            if (_plans.Any(p => p.ManagerEmployeeId == employeeId))
                return ActionResult.No("that employee is already a Store Manager");
            if (!GameApi.EmployeeExists(employeeId))
                return ActionResult.No("employee not found");
            if (!GameApi.HasManagerSkill(employeeId))
                return ActionResult.No("employee doesn't have the Purchasing Agent skill");
            if (GameApi.IsBoundToVanillaPlan(employeeId))
                return ActionResult.No("that employee already runs an HR / Logistics / Pricing / Purchasing plan at the office");
            if (!skipScheduleCheck && GameApi.IsScheduledAtHq(employeeId, hqAddress) != true)
                return ActionResult.No("schedule the manager on an HQ desk first (BizMan → HQ → Schedule)");

            var m = GameApi.FindManager(employeeId);
            var plan = new StoreManagerPlan { ManagerEmployeeId = employeeId, HqAddress = hqAddress };
            _plans.Add(plan);
            Save();
            Feedback.Toast(Feedback.Level.Success, "storemanager_notify_hired",
                new() { { "name", m?.Name ?? "Your manager" } }, "sm_hired_" + employeeId,
                () => GameApi.OpenEmployeeCard(employeeId));
            Feedback.Message("storemanager_msg_hired", new() { { "name", m?.Name ?? "Your manager" } });
            return ActionResult.Yes($"{m?.Name} is now a Store Manager. Assign them stores.");
        }

        public void DropManager(string employeeId)
        {
            var plan = PlanForManager(employeeId);
            if (plan == null) return;
            foreach (var a in plan.Assignments.ToList()) RestoreAndRemove(plan, a);
            _plans.Remove(plan);
            _dormantNotified.Remove(employeeId);
            Save();
        }

        public ActionResult AssignStore(string employeeId, string storeAddress)
        {
            if (_readOnly) return ActionResult.No("store-manager data is in read-only mode this session");
            var plan = PlanForManager(employeeId);
            if (plan == null) return ActionResult.No("no such Store Manager — adopt one first");
            if (plan.Supervises(storeAddress)) return ActionResult.No("already supervising that store");
            if (PlanSupervising(storeAddress) != null) return ActionResult.No("another Store Manager already supervises that store");

            int cap = GameApi.MaxStores(plan.HqAddress, employeeId);
            if (plan.Assignments.Count >= cap)
                return ActionResult.No($"at the skill cap ({cap} store(s)) — train the manager or drop a store");
            if (!GameApi.StoreStillOwned(storeAddress))
                return ActionResult.No("you don't own/rent that store");

            var a = StoreAssignment.New(storeAddress, GameApi.StoreName(storeAddress), _defaults);
            a.OriginalContract = DeliveryContracts.Snapshot(storeAddress);
            plan.Assignments.Add(a);
            Save();

            Feedback.Toast(Feedback.Level.Success, "storemanager_notify_store_assigned", new() { { "store", a.StoreName } });
            var hint = DeliveryContracts.HasContract(storeAddress)
                ? "" : " — note: this store has no delivery contract yet; set one up in its BizMan Deliveries tab.";
            return ActionResult.Yes($"Now supervising {a.StoreName}.{hint}");
        }

        public ActionResult UnassignStore(string employeeId, string storeAddress)
        {
            var plan = PlanForManager(employeeId);
            var a = plan?.Find(storeAddress);
            if (plan == null || a == null) return ActionResult.No("not supervising that store");
            bool applied = RestoreAndRemove(plan, a);
            Save();
            Feedback.Toast(Feedback.Level.Info, "storemanager_notify_store_unassigned", new() { { "store", a.StoreName } });
            return ActionResult.Yes(applied
                ? $"Stopped supervising {a.StoreName}; its delivery contract was restored."
                : $"Stopped supervising {a.StoreName}; its contract will be restored after Monday's delivery.");
        }

        public ActionResult SetCap(string employeeId, string storeAddress, decimal cap)
        {
            var a = PlanForManager(employeeId)?.Find(storeAddress);
            if (a == null) return ActionResult.No("not supervising that store");
            a.WeeklyRestockBudgetCap = Math.Max(0m, cap);
            Save();
            return ActionResult.Yes($"{a.StoreName}: weekly restock budget = ${a.WeeklyRestockBudgetCap:N0}");
        }

        public ActionResult SetTargetDays(string employeeId, string storeAddress, int days)
        {
            var a = PlanForManager(employeeId)?.Find(storeAddress);
            if (a == null) return ActionResult.No("not supervising that store");
            a.TargetDaysOfStock = Math.Min(60, Math.Max(1, days));
            Save();
            return ActionResult.Yes($"{a.StoreName}: target stock = {a.TargetDaysOfStock} days");
        }

        // ── ticks (all called from the game's unguarded event invoke) ───────────
        public void OnNewDay()
        {
            try
            {
                bool allowTeardown = _teardownArmed && GameApi.EmployeeSubsystemReady();
                if (allowTeardown) _teardownArmed = false;
                SafeReconcile(announce: true, allowTeardown: allowTeardown || GameApi.EmployeeSubsystemReady());
                DrainPendingRestores();
                if (GameApi.IsWeeklyPlanningDay) RunWeeklyPlanning();
            }
            catch (Exception e) { Debug.LogError("[StoreManager] OnNewDay failed: " + e); }
        }

        /// <summary>Light hourly check: catch a fired/unscheduled manager sooner than the daily tick.</summary>
        public void OnNewHour()
        {
            try
            {
                int h = GameApi.CurrentDay * 24;   // coarse throttle key
                if (h - _lastHourReconcile < 3) return;
                _lastHourReconcile = h;
                SafeReconcile(announce: true, allowTeardown: GameApi.EmployeeSubsystemReady());
                DrainPendingRestores();
            }
            catch (Exception e) { Debug.LogError("[StoreManager] OnNewHour failed: " + e); }
        }

        public void RunWeeklyPlanning()
        {
            if (_readOnly) return;
            var planner = new WeeklyRestockPlanner();
            foreach (var plan in _plans.ToList())
            {
                if (plan.Dormant) continue;
                try
                {
                    plan.Week.Reset();
                    foreach (var a in plan.Assignments) a.SpentThisWeek = 0m;
                    planner.Run(plan);
                    WeeklyDigest.Send(WeeklyDigest.Compose(plan));
                }
                catch (Exception e) { Debug.LogError("[StoreManager] weekly plan failed for a manager: " + e); }
            }
            Save();
        }

        // ── reconcile ───────────────────────────────────────────────────────────
        private void SafeReconcile(bool announce, bool allowTeardown)
        {
            try { Reconcile(announce, allowTeardown); }
            catch (Exception e) { Debug.LogError("[StoreManager] Reconcile failed: " + e); }
        }

        private void Reconcile(bool announce, bool allowTeardown)
        {
            bool dirty = false;
            foreach (var plan in _plans.ToList())
            {
                // manager gone entirely -> tear down, but only when we can trust the employee list
                if (!GameApi.EmployeeExists(plan.ManagerEmployeeId))
                {
                    if (!allowTeardown) { plan.Dormant = true; continue; }
                    foreach (var a in plan.Assignments.ToList()) RestoreAndRemove(plan, a);
                    _plans.Remove(plan);
                    _dormantNotified.Remove(plan.ManagerEmployeeId);
                    dirty = true;
                    if (announce)
                        Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_manager_gone", null, "sm_gone_" + plan.ManagerEmployeeId);
                    continue;
                }

                // scheduled? null = couldn't tell -> keep the previous Dormant state
                var scheduled = GameApi.IsScheduledAtHq(plan.ManagerEmployeeId, plan.HqAddress);
                if (scheduled == false && !plan.Dormant)
                {
                    plan.Dormant = true; dirty = true;
                    if (announce && _dormantNotified.Add(plan.ManagerEmployeeId))
                        Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_dormant", null, "sm_dormant_" + plan.ManagerEmployeeId);
                }
                else if (scheduled == true && plan.Dormant)
                {
                    plan.Dormant = false; dirty = true;
                    _dormantNotified.Remove(plan.ManagerEmployeeId);
                    if (announce)
                        Feedback.Toast(Feedback.Level.Success, "storemanager_notify_active", null, "sm_active_" + plan.ManagerEmployeeId);
                }

                foreach (var a in plan.Assignments.ToList())
                {
                    if (!GameApi.StoreStillOwned(a.StoreAddress))
                    {
                        RestoreAndRemove(plan, a); dirty = true;
                        if (announce)
                            Feedback.Toast(Feedback.Level.Info, "storemanager_notify_store_lost", new() { { "store", a.StoreName } });
                    }
                }
            }
            if (dirty) Save();
        }

        private void DrainPendingRestores()
        {
            bool dirty = false;
            foreach (var plan in _plans)
            {
                foreach (var p in plan.PendingRestores.ToList())
                {
                    bool applied = p.Snapshot != null
                        ? DeliveryContracts.Restore(p.StoreAddress, p.Snapshot)
                        : DeliveryContracts.Disable(p.StoreAddress);
                    if (applied) { plan.PendingRestores.Remove(p); dirty = true; }
                }
            }
            if (dirty) Save();
        }

        /// <returns>true if the contract restore/disable applied now; false = queued for after the Monday lock.</returns>
        private static bool RestoreAndRemove(StoreManagerPlan plan, StoreAssignment a)
        {
            bool applied = a.OriginalContract != null
                ? DeliveryContracts.Restore(a.StoreAddress, a.OriginalContract)
                : DeliveryContracts.Disable(a.StoreAddress);

            if (!applied)
                plan.PendingRestores.Add(new PendingRestore
                {
                    StoreAddress = a.StoreAddress,
                    StoreName = a.StoreName,
                    Snapshot = a.OriginalContract,
                });

            plan.Assignments.Remove(a);
            return applied;
        }
    }
}
