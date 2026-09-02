#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Helpers;
using IngameDebugConsole;   // CommandHelper (ExternalPlugins.dll)
using StoreManager.Interop;
using StoreManager.Runtime;
using UnityEngine;

namespace StoreManager.Debugging
{
    /// <summary>
    /// In-game debug console commands — the power-user + test surface (both critiques kept these).
    /// Open the console with backquote (needs the game launched with <c>-console</c>).
    /// </summary>
    public static class StoreManagerCommands
    {
        private static ManagerDirectory? _dir;

        private static readonly Action _managers = Managers;
        private static readonly Action<int> _adopt = Adopt;
        private static readonly Action _drop = Drop;
        private static readonly Action _stores = Stores;
        private static readonly Action<int> _assign = Assign;
        private static readonly Action<int> _unassign = Unassign;
        private static readonly Action<int, int> _setCap = SetCap;
        private static readonly Action<int, int> _days = Days;
        private static readonly Action _status = Status;
        private static readonly Action _planWeek = PlanWeek;
        private static readonly Action _selfTest = SelfTest;
        private static readonly Action _recruit = Recruit;
        private static readonly Action _safeRemove = SafeRemove;

        public static void Register(ManagerDirectory dir)
        {
            _dir = dir;
            CommandHelper.AddCommand("StoreManager.Managers", "List eligible Purchasing Agents at your HQ.", _managers);
            CommandHelper.AddCommand("StoreManager.Adopt", "Adopt manager <n> from StoreManager.Managers.", _adopt, "n");
            CommandHelper.AddCommand("StoreManager.Drop", "Drop the current Store Manager (restores contracts).", _drop);
            CommandHelper.AddCommand("StoreManager.Stores", "List your supervisable stores.", _stores);
            CommandHelper.AddCommand("StoreManager.Assign", "Assign store <n> from StoreManager.Stores.", _assign, "n");
            CommandHelper.AddCommand("StoreManager.Unassign", "Unassign store <n>.", _unassign, "n");
            CommandHelper.AddCommand("StoreManager.SetCap", "Set store <n> weekly restock budget to <amount>.", _setCap, "n", "amount");
            CommandHelper.AddCommand("StoreManager.Days", "Set store <n> target days-of-stock to <days>.", _days, "n", "days");
            CommandHelper.AddCommand("StoreManager.Status", "Print plan status.", _status);
            CommandHelper.AddCommand("StoreManager.PlanWeek", "Force the weekly restock pass now (test).", _planWeek);
            CommandHelper.AddCommand("StoreManager.SelfTest",
                "End-to-end: inject a manager, adopt, assign a store with a contract, run the weekly pass, log before/after, undo. DO NOT SAVE after.", _selfTest);
            CommandHelper.AddCommand("StoreManager.Recruit",
                "TEST SHORTCUT: directly hire a Store Manager onto your HQ. Normally recruit via phone -> Recruitment Agency.", _recruit);
            CommandHelper.AddCommand("StoreManager.SafeRemove",
                "Uninstall prep: re-skill every Store Manager to Purchasing Agent and drop all plans, so deleting the mod folder is safe.", _safeRemove);
        }

        public static void Unregister()
        {
            foreach (var d in new Delegate[] { _managers, _adopt, _drop, _stores, _assign, _unassign, _setCap, _days, _status, _planWeek, _selfTest, _recruit, _safeRemove })
                try { CommandHelper.RemoveCommand(d); } catch { }
            _dir = null;
        }

        // ── shared with the options panel ──────────────────────────────────────
        public static void PrintStatus() => Status();

        // ── commands ──────────────────────────────────────────────────────────
        private static string Hq() => GameApi.GetHeadquarters().FirstOrDefault().Address ?? "";

        private static void Managers()
        {
            var hq = Hq();
            if (string.IsNullOrEmpty(hq)) { Log("no HQ found — rent an office first"); return; }
            var cands = GameApi.GetManagerCandidates(hq);
            Log($"eligible managers at HQ ({cands.Count}):");
            for (int i = 0; i < cands.Count; i++) Log($"  {i}: {cands[i]}");
            if (cands.Count == 0) Log("  (run StoreManager.Recruit to hire one, then schedule them on an HQ desk)");
        }

        private static void Recruit()
        {
            if (!RoleSystemState.IsActive) { Log("role disabled: " + RoleSystemState.Reason); return; }
            var hq = Hq();
            if (string.IsNullOrEmpty(hq)) { Log("no HQ found — rent an office first"); return; }
            var r = RoleEmployees.Recruit(hq);
            Log((r.Ok ? "" : "failed: ") + r.Message);
            StoreManager.UI.StoreManagerOptions.Rebuild();
        }

        private static void SafeRemove()
        {
            int reskilled = RoleEmployees.ReskillAllToVanilla();
            int dropped = 0;
            if (_dir != null)
                foreach (var p in _dir.Plans.ToList().Select(p => p.ManagerEmployeeId).ToList())
                { _dir.DropManager(p); dropped++; }
            _dir?.Save();
            Log($"SafeRemove: re-skilled {reskilled} manager(s) to {RoleEmployees.VanillaFallback}, dropped {dropped} plan(s). " +
                "Save the game now, then it's safe to delete the mod folder.");
            StoreManager.UI.StoreManagerOptions.Rebuild();
        }

        private static void Adopt(int n)
        {
            if (_dir == null) return;
            var hq = Hq();
            var cands = GameApi.GetManagerCandidates(hq);
            if (n < 0 || n >= cands.Count) { Log("bad index — run StoreManager.Managers"); return; }
            Report(_dir.AdoptManager(hq, cands[n].Id));
            StoreManager.UI.StoreManagerOptions.Rebuild();
        }

        private static void Drop()
        {
            if (_dir == null) return;
            var plan = _dir.Plans.FirstOrDefault();
            if (plan == null) { Log("no Store Manager to drop"); return; }
            _dir.DropManager(plan.ManagerEmployeeId);
            Log("dropped; delivery contracts restored");
            StoreManager.UI.StoreManagerOptions.Rebuild();
        }

        private static void Stores()
        {
            if (_dir == null) return;
            var stores = GameApi.GetSupervisableStores();
            Log($"supervisable stores ({stores.Count}):");
            for (int i = 0; i < stores.Count; i++)
            {
                var sup = _dir.PlanSupervising(stores[i].Address) != null ? "  [supervised]" : "";
                var hasC = DeliveryContracts.HasContract(stores[i].Address) ? "" : "  (no delivery contract)";
                Log($"  {i}: {stores[i].Name}{sup}{hasC}");
            }
        }

        private static string? StoreAddr(int n)
        {
            var stores = GameApi.GetSupervisableStores();
            if (n < 0 || n >= stores.Count) { Log("bad index — run StoreManager.Stores"); return null; }
            return stores[n].Address;
        }

        private static string? MgrId() => _dir?.Plans.FirstOrDefault()?.ManagerEmployeeId;

        private static void Assign(int n)
        {
            var mgr = MgrId(); var addr = StoreAddr(n);
            if (_dir == null || mgr == null || addr == null) { Log("adopt a manager first"); return; }
            Report(_dir.AssignStore(mgr, addr));
        }

        private static void Unassign(int n)
        {
            var mgr = MgrId(); var addr = StoreAddr(n);
            if (_dir == null || mgr == null || addr == null) return;
            Report(_dir.UnassignStore(mgr, addr));
        }

        private static void SetCap(int n, int amount)
        {
            var mgr = MgrId(); var addr = StoreAddr(n);
            if (_dir == null || mgr == null || addr == null) return;
            Report(_dir.SetCap(mgr, addr, amount));
        }

        private static void Days(int n, int days)
        {
            var mgr = MgrId(); var addr = StoreAddr(n);
            if (_dir == null || mgr == null || addr == null) return;
            Report(_dir.SetTargetDays(mgr, addr, days));
        }

        private static void Status()
        {
            Log(RoleSystemState.Summary());
            Log(StatusSummary());
            if (_dir == null) return;
            foreach (var p in _dir.Plans)
            {
                var m = GameApi.FindManager(p.ManagerEmployeeId);
                Log($"MANAGER {m?.Name ?? p.ManagerEmployeeId}  hq={p.HqAddress}  dormant={p.Dormant}  " +
                    $"stores={p.Assignments.Count}/{GameApi.MaxStores(p.HqAddress, p.ManagerEmployeeId)}  " +
                    $"weekSpend=${p.Week.RestockSpend:N0}");
                foreach (var a in p.Assignments)
                    Log($"  - {a.StoreName}  cap=${a.WeeklyRestockBudgetCap:N0}  targetDays={a.TargetDaysOfStock}  " +
                        $"spent=${a.SpentThisWeek:N0}  contract={(DeliveryContracts.HasContract(a.StoreAddress) ? "yes" : "MISSING")}");
            }
        }

        /// <summary>One-line status for the panel toast.</summary>
        public static string StatusSummary()
        {
            if (_dir == null) return "no city loaded";
            if (_dir.ReadOnly) return "read-only mode this session (saved data unreadable)";
            if (_dir.Plans.Count == 0) return "no Store Manager adopted yet";
            var p = _dir.Plans[0];
            var m = GameApi.FindManager(p.ManagerEmployeeId);
            int missing = p.Assignments.Count(a => !DeliveryContracts.HasContract(a.StoreAddress));
            return $"{m?.Name ?? "manager"}: {p.Assignments.Count} store(s)" +
                   (p.Dormant ? " — DORMANT (schedule them at the HQ)" : "") +
                   $", ${p.Week.RestockSpend:N0}/wk" +
                   (missing > 0 ? $", {missing} store(s) have no delivery contract" : "");
        }

        private static void PlanWeek()
        {
            if (_dir == null) return;
            _dir.RunWeeklyPlanning();
            Log("weekly restock pass ran — see toasts + the phone thread");
        }

        /// <summary>Console entry — logs the full trace.</summary>
        public static void SelfTest() => Log(SelfTestCore());

        /// <summary>
        /// Panel entry — runs the same check and surfaces a one-line result as a toast + phone
        /// message so it's visible without the console.
        /// </summary>
        public static void SelfTestFromPanel()
        {
            var summary = SelfTestCore();
            bool ok = summary.StartsWith("PASS");
            Interop.Feedback.Toast(ok ? Interop.Feedback.Level.Success : Interop.Feedback.Level.Warning,
                "storemanager_notify_ok", new Dictionary<string, string> { { "msg", summary } });
            Interop.Feedback.Message("storemanager_selftest_msg", new Dictionary<string, string> { { "result", summary } });
        }

        /// <summary>
        /// Headless end-to-end check of the v2 loop against the live save. In-memory only —
        /// injects a throwaway Purchasing Agent, exercises adopt → assign → weekly plan → drop
        /// (restore), then cleans up. Returns a one-line PASS/FAIL summary; logs the full trace.
        /// </summary>
        public static string SelfTestCore()
        {
            if (_dir == null) { Log("SelfTest: no city loaded"); return "FAIL: no city loaded"; }
            if (!RoleSystemState.IsActive)
            {
                Log("SelfTest: role system disabled — " + RoleSystemState.Reason);
                return "FAIL: Store Manager role disabled on this build (" + RoleSystemState.Reason + ")";
            }

            var stores = GameApi.GetSupervisableStores();
            int withContract = stores.Count(s => DeliveryContracts.HasContract(s.Address));
            Log($"SelfTest: {stores.Count} supervisable store(s); {withContract} with a delivery contract");
            var store = stores.FirstOrDefault(s => DeliveryContracts.HasContract(s.Address));
            if (string.IsNullOrEmpty(store.Address)) store = stores.FirstOrDefault();
            if (string.IsNullOrEmpty(store.Address)) { Log("SelfTest: no supervisable store"); return "FAIL: you own no supervisable store"; }

            var hq = Hq();
            string anchor = string.IsNullOrEmpty(hq) ? store.Address : hq;
            EmployeeInstance? throwaway = null;
            var cands = (string.IsNullOrEmpty(hq) ? new List<GameApi.EmpRef>() : GameApi.GetManagerCandidates(hq))
                        .Where(c => !GameApi.IsBoundToVanillaPlan(c.Id)).ToList();
            string mgrId;
            if (cands.Count > 0) { mgrId = cands[0].Id; Log($"SelfTest: using free agent {cands[0]}"); }
            else
            {
                try
                {
                    throwaway = EmployeeHelper.CreateAIEmployeeInstance(GameApi.ManagerSkill);
                    throwaway.characterData.name = "SELFTEST Agent";
                    var addrObj = GameApi.HqAddressObject(anchor);
                    if (addrObj != null)
                        typeof(EmployeeInstance).GetField("assignedAddress")?.SetValue(throwaway, addrObj);
                    EmployeeHelper.GetEmployeeInstances().Add(throwaway);
                    EmployeeHelper.EmployeeInstancesDictionary[throwaway.id] = throwaway;
                    mgrId = throwaway.id;
                    Log($"SelfTest: injected throwaway agent {mgrId} at {anchor}");
                }
                catch (Exception e) { Log("SelfTest: could not create test agent: " + e.Message); return "FAIL: couldn't create a test agent (" + e.Message + ")"; }
            }

            _dir.SuppressSave = true;
            string result;
            try
            {
                string before = DeliveryContracts.Describe(store.Address);
                Log($"SelfTest: '{store.Name}' BEFORE  →  {before}");

                Report(_dir.AdoptManager(anchor, mgrId, skipScheduleCheck: true));
                var plan = _dir.PlanForManager(mgrId);
                if (plan == null) { CleanupThrowaway(throwaway); _dir.SuppressSave = false; return "FAIL: adopt was blocked"; }
                plan.Dormant = false;

                Report(_dir.AssignStore(mgrId, store.Address));
                Report(_dir.SetCap(mgrId, store.Address, 100000m));
                Report(_dir.SetTargetDays(mgrId, store.Address, 14));

                _dir.RunWeeklyPlanning();

                string after = DeliveryContracts.Describe(store.Address);
                var w = plan.Week;
                Log($"SelfTest: '{store.Name}' AFTER   →  {after}");
                Log($"SelfTest: tally spend=${w.RestockSpend:N0} orders={w.OrdersPlaced} covered={w.StoresCovered} " +
                    $"caps={w.BudgetCapsHit} attention=[{string.Join(" | ", w.AttentionItems)}]");

                _dir.DropManager(mgrId);
                string restored = DeliveryContracts.Describe(store.Address);
                Log($"SelfTest: '{store.Name}' RESTORED → {restored}");

                bool restoredOk = restored == before;
                result = restoredOk && w.StoresCovered == 1
                    ? $"PASS: {store.Name} restocked (${w.RestockSpend:N0}/wk, {w.OrdersPlaced} lines), contract restored exactly."
                    : $"CHECK: covered={w.StoresCovered}, restoredExact={restoredOk}. See Player.log for detail.";
            }
            catch (Exception e) { Log("SelfTest threw: " + e); result = "FAIL: exception — " + e.Message; }
            finally
            {
                CleanupThrowaway(throwaway);
                _dir.SuppressSave = false;
                Log("SelfTest: done — don't save the game right after (a throwaway employee was briefly in memory).");
            }
            return result;
        }

        private static void CleanupThrowaway(EmployeeInstance? e)
        {
            if (e == null) return;
            try
            {
                EmployeeHelper.GetEmployeeInstances().Remove(e);
                EmployeeHelper.EmployeeInstancesDictionary.Remove(e.id);
            }
            catch { }
        }

        private static void Report(ActionResult r) => Log((r.Ok ? "" : "blocked: ") + r.Message);
        private static void Log(string s) => Debug.Log("[StoreManager] " + s);
    }
}
