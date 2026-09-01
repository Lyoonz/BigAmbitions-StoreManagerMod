#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        }

        public static void Unregister()
        {
            foreach (var d in new Delegate[] { _managers, _adopt, _drop, _stores, _assign, _unassign, _setCap, _days, _status, _planWeek })
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

        private static void Report(ActionResult r) => Log((r.Ok ? "" : "blocked: ") + r.Message);
        private static void Log(string s) => Debug.Log("[StoreManager] " + s);
    }
}
