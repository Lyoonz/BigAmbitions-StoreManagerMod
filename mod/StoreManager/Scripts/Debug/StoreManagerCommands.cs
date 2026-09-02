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
                "End-to-end: inject a Purchasing Agent, adopt, assign a store with a contract, run the weekly pass, log before/after, undo. DO NOT SAVE after.", _selfTest);
        }

        public static void Unregister()
        {
            foreach (var d in new Delegate[] { _managers, _adopt, _drop, _stores, _assign, _unassign, _setCap, _days, _status, _planWeek, _selfTest })
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
            if (cands.Count == 0) Log("  (recruit a Purchasing Agent, hire them, assign to the HQ, and schedule them)");
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
            if (_dir == null) { Log("no city loaded"); return; }
            if (_dir.Plans.Count == 0) { Log("no Store Manager adopted"); return; }
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

        private static void PlanWeek()
        {
            if (_dir == null) return;
            _dir.RunWeeklyPlanning();
            Log("weekly restock pass ran — see toasts + the phone thread");
        }

        /// <summary>
        /// Headless end-to-end check of the v2 loop against the live save. In-memory only —
        /// injects a throwaway Purchasing Agent if needed, exercises adopt → assign → weekly
        /// plan → drop (restore), logs contract state before/after, then cleans up.
        /// </summary>
        public static void SelfTest()
        {
            if (_dir == null) { Log("SelfTest: no city loaded"); return; }

            // 1. a store — prefer one that already has a delivery contract (the interesting path)
            var stores = GameApi.GetSupervisableStores();
            Log($"SelfTest: {stores.Count} supervisable store(s); " +
                $"{stores.Count(s => DeliveryContracts.HasContract(s.Address))} with a delivery contract");
            var store = stores.FirstOrDefault(s => DeliveryContracts.HasContract(s.Address));
            if (string.IsNullOrEmpty(store.Address)) store = stores.FirstOrDefault();
            if (string.IsNullOrEmpty(store.Address)) { Log("SelfTest: no supervisable store at all — bail"); return; }

            // 2. a manager — an HQ if there is one, else pin the agent to the store itself
            var hq = Hq();
            string anchor = string.IsNullOrEmpty(hq) ? store.Address : hq;
            EmployeeInstance? throwaway = null;
            var cands = (string.IsNullOrEmpty(hq) ? new List<GameApi.EmpRef>() : GameApi.GetManagerCandidates(hq))
                        .Where(c => !GameApi.IsBoundToVanillaPlan(c.Id)).ToList();   // skip agents already on a vanilla plan
            string mgrId;
            if (cands.Count > 0) { mgrId = cands[0].Id; Log($"SelfTest: using free agent {cands[0]}"); }
            else
            {
                try
                {
                    throwaway = EmployeeHelper.CreateAIEmployeeInstance(GameApi.ManagerSkill);
                    throwaway.characterData.name = "SELFTEST Agent";
                    var addrObj = GameApi.HqAddressObject(anchor);   // live Address object of the anchor building
                    if (addrObj != null)
                        typeof(EmployeeInstance).GetField("assignedAddress")?.SetValue(throwaway, addrObj);
                    EmployeeHelper.GetEmployeeInstances().Add(throwaway);
                    EmployeeHelper.EmployeeInstancesDictionary[throwaway.id] = throwaway;
                    mgrId = throwaway.id;
                    Log($"SelfTest: injected throwaway Purchasing Agent {mgrId} anchored at {anchor} (hq={(string.IsNullOrEmpty(hq) ? "none" : hq)})");
                }
                catch (Exception e) { Log("SelfTest: could not create test agent: " + e.Message); return; }
            }

            _dir.SuppressSave = true;   // nothing this test does touches the real save's modData
            try
            {
                Log($"SelfTest: store '{store.Name}' contract BEFORE  →  {DeliveryContracts.Describe(store.Address)}");

                Report(_dir.AdoptManager(anchor, mgrId, skipScheduleCheck: true));
                var plan = _dir.PlanForManager(mgrId);
                if (plan == null) { Log("SelfTest: adopt failed"); CleanupThrowaway(throwaway); return; }
                plan.Dormant = false;

                Report(_dir.AssignStore(mgrId, store.Address));
                Report(_dir.SetCap(mgrId, store.Address, 100000m));   // generous cap so we see full order
                Report(_dir.SetTargetDays(mgrId, store.Address, 14));

                _dir.RunWeeklyPlanning();

                Log($"SelfTest: store '{store.Name}' contract AFTER   →  {DeliveryContracts.Describe(store.Address)}");
                var a = plan.Find(store.Address);
                Log($"SelfTest: week tally  spend=${plan.Week.RestockSpend:N0} orders={plan.Week.OrdersPlaced} " +
                    $"covered={plan.Week.StoresCovered} capsHit={plan.Week.BudgetCapsHit} " +
                    $"attention=[{string.Join(" | ", plan.Week.AttentionItems)}]");

                _dir.DropManager(mgrId);
                Log($"SelfTest: store '{store.Name}' contract RESTORED → {DeliveryContracts.Describe(store.Address)}");
            }
            catch (Exception e) { Log("SelfTest threw: " + e); }
            finally
            {
                CleanupThrowaway(throwaway);
                _dir.SuppressSave = false;
                Log("SelfTest: done — DO NOT SAVE this session (a throwaway employee was briefly in memory).");
            }
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
