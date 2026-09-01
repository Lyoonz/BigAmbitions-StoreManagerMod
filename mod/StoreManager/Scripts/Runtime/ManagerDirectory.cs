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
    /// Owns the set of <see cref="StoreManagerPlan"/>s for the loaded save: adopt/drop a manager,
    /// assign/unassign stores, reconcile against the live game, and run the weekly restock pass.
    /// One instance per city session. State lives in <c>GameInstance.modData</c> (D13).
    /// </summary>
    public sealed class ManagerDirectory
    {
        private const string SaveKey = "StoreManager.plans.v1";

        private readonly List<StoreManagerPlan> _plans = new();
        private readonly GlobalDefaults _defaults;
        private readonly HashSet<string> _dormantNotified = new();

        public ManagerDirectory(GlobalDefaults defaults) => _defaults = defaults;

        public IReadOnlyList<StoreManagerPlan> Plans => _plans;
        public GlobalDefaults Defaults => _defaults;

        // ── persistence ─────────────────────────────────────────────────────────
        public void Load()
        {
            _plans.Clear();
            _plans.AddRange(Serialization.Deserialize(GameApi.LoadModData(SaveKey)));
            Reconcile(announce: false);
        }

        public void Save() => GameApi.SaveModData(SaveKey, Serialization.Serialize(_plans));

        /// <summary>OnUnloadAsync: flush, drop in-memory state (modData entry stays, re-adopted on reinstall).</summary>
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
        public ActionResult AdoptManager(string hqAddress, string employeeId)
        {
            if (_plans.Any(p => p.ManagerEmployeeId == employeeId))
                return ActionResult.No("that employee is already a Store Manager");
            if (!GameApi.EmployeeExists(employeeId))
                return ActionResult.No("employee not found");
            var m = GameApi.FindManager(employeeId);
            if (!GameApi.HasManagerSkill(employeeId))
                return ActionResult.No("employee doesn't have the Purchasing Agent skill");
            if (GameApi.IsBoundToVanillaPlan(employeeId))
                return ActionResult.No("that employee is already assigned to an HR / Logistics / Pricing manager plan");
            if (!GameApi.IsScheduledAtHq(employeeId, hqAddress))
                return ActionResult.No("schedule the manager on an HQ desk first (BizMan → HQ → Schedule)");

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
            foreach (var a in plan.Assignments.ToList())
                RestoreAndRemove(plan, a);
            _plans.Remove(plan);
            _dormantNotified.Remove(employeeId);
            Save();
        }

        public ActionResult AssignStore(string employeeId, string storeAddress)
        {
            var plan = PlanForManager(employeeId);
            if (plan == null) return ActionResult.No("no such Store Manager — adopt one first");
            if (plan.Supervises(storeAddress)) return ActionResult.No("already supervising that store");

            var other = PlanSupervising(storeAddress);
            if (other != null) return ActionResult.No("another Store Manager already supervises that store");

            int cap = GameApi.MaxStores(plan.HqAddress, employeeId);
            if (plan.Assignments.Count >= cap)
                return ActionResult.No($"at the skill cap ({cap} store(s)) — train the manager or drop a store");

            if (!GameApi.StoreStillOwned(storeAddress))
                return ActionResult.No("you don't own/rent that store");

            var a = StoreAssignment.New(storeAddress, GameApi.StoreName(storeAddress), _defaults);
            a.OriginalContract = DeliveryContracts.Snapshot(storeAddress);
            plan.Assignments.Add(a);
            Save();

            Feedback.Toast(Feedback.Level.Success, "storemanager_notify_store_assigned",
                new() { { "store", a.StoreName } }, null);
            var hint = DeliveryContracts.HasContract(storeAddress)
                ? "" : " — note: this store has no delivery contract yet; set one up in its BizMan Deliveries tab.";
            return ActionResult.Yes($"Now supervising {a.StoreName}.{hint}");
        }

        public ActionResult UnassignStore(string employeeId, string storeAddress)
        {
            var plan = PlanForManager(employeeId);
            var a = plan?.Find(storeAddress);
            if (plan == null || a == null) return ActionResult.No("not supervising that store");
            RestoreAndRemove(plan, a);
            Save();
            Feedback.Toast(Feedback.Level.Info, "storemanager_notify_store_unassigned", new() { { "store", a.StoreName } });
            return ActionResult.Yes($"Stopped supervising {a.StoreName}; its delivery contract was restored.");
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
            a.TargetDaysOfStock = Math.Max(1, days);
            Save();
            return ActionResult.Yes($"{a.StoreName}: target stock = {a.TargetDaysOfStock} days");
        }

        // ── ticks ───────────────────────────────────────────────────────────────
        public void OnNewDay()
        {
            Reconcile(announce: true);
            if (GameApi.IsWeeklyPlanningDay)
                RunWeeklyPlanning();
        }

        public void OnJobChange() => Reconcile(announce: true);

        public void RunWeeklyPlanning()
        {
            var planner = new WeeklyRestockPlanner();
            foreach (var plan in _plans.ToList())
            {
                if (plan.Dormant) continue;
                planner.Run(plan);
                var report = WeeklyDigest.Compose(plan);
                WeeklyDigest.Send(report);
                plan.Week.Reset();
                foreach (var a in plan.Assignments) a.SpentThisWeek = 0m;
            }
            Save();
        }

        // ── reconcile ───────────────────────────────────────────────────────────
        private void Reconcile(bool announce)
        {
            foreach (var plan in _plans.ToList())
            {
                // manager gone entirely -> tear the plan down
                if (!GameApi.EmployeeExists(plan.ManagerEmployeeId))
                {
                    foreach (var a in plan.Assignments.ToList()) RestoreAndRemove(plan, a);
                    _plans.Remove(plan);
                    _dormantNotified.Remove(plan.ManagerEmployeeId);
                    if (announce)
                        Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_manager_gone", null, "sm_gone_" + plan.ManagerEmployeeId);
                    continue;
                }

                // manager unscheduled at HQ -> dormant (contracts left; planner skips)
                bool scheduled = GameApi.IsScheduledAtHq(plan.ManagerEmployeeId, plan.HqAddress);
                if (!scheduled && !plan.Dormant)
                {
                    plan.Dormant = true;
                    if (announce && _dormantNotified.Add(plan.ManagerEmployeeId))
                        Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_dormant", null, "sm_dormant_" + plan.ManagerEmployeeId);
                }
                else if (scheduled && plan.Dormant)
                {
                    plan.Dormant = false;
                    _dormantNotified.Remove(plan.ManagerEmployeeId);
                    if (announce)
                        Feedback.Toast(Feedback.Level.Success, "storemanager_notify_active", null, "sm_active_" + plan.ManagerEmployeeId);
                }

                // stores no longer owned -> drop them
                foreach (var a in plan.Assignments.ToList())
                {
                    if (!GameApi.StoreStillOwned(a.StoreAddress))
                    {
                        plan.Assignments.Remove(a);
                        if (announce)
                            Feedback.Toast(Feedback.Level.Info, "storemanager_notify_store_lost", new() { { "store", a.StoreName } });
                    }
                }
            }
        }

        private static void RestoreAndRemove(StoreManagerPlan plan, StoreAssignment a)
        {
            if (a.OriginalContract != null)
                DeliveryContracts.Restore(a.StoreAddress, a.OriginalContract);
            else
                DeliveryContracts.Disable(a.StoreAddress);
            plan.Assignments.Remove(a);
        }
    }
}
