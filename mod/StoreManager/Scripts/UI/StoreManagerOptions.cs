#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigAmbitions.Mods;
using StoreManager.Domain;
using StoreManager.Interop;
using StoreManager.Runtime;
using UnityEngine;

namespace StoreManager.UI
{
    /// <summary>
    /// The mod's panel under Options → Mods (reachable in-game via the pause menu). v1 uses only
    /// built-in ModOptions controls: pick the manager, edit the global defaults, and two info
    /// buttons. Per-store assignment + limits are console commands in v1 (a store-picker panel
    /// is Phase 2). Re-registered on city load and whenever the plan set changes so it reflects
    /// live state.
    /// </summary>
    public static class StoreManagerOptions
    {
        private static string _modId = "StoreManager";
        private static GlobalDefaults _defaults = GlobalDefaults.Default();
        private static ModContext? _ctx;

        public static void Register(ModContext ctx, GlobalDefaults defaults)
        {
            _ctx = ctx;
            _modId = ctx.ModId;
            _defaults = defaults;
            Rebuild();
        }

        public static void Unregister()
        {
            try { OptionsService.RemoveModOptions(_modId); } catch { }
        }

        /// <summary>Call after city load or after plans change.</summary>
        public static void Rebuild()
        {
            var o = new ModOptions().AddHeader("storemanager_options_header");

            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null)
            {
                o.AddButton("storemanager_opt_no_city", null);
            }
            else
            {
                var hq = GameApi.GetHeadquarters().FirstOrDefault();
                if (string.IsNullOrEmpty(hq.Address))
                {
                    o.AddButton("storemanager_opt_no_hq", null);
                }
                else
                {
                    var cands = GameApi.GetManagerCandidates(hq.Address);
                    if (cands.Count == 0)
                    {
                        o.AddButton("storemanager_opt_no_candidates", null);
                    }
                    else
                    {
                        var labels = new List<string> { "storemanager_opt_pick_manager" };
                        labels.AddRange(cands.Select(c => c.ToString()));
                        int current = 0;
                        var adopted = dir.Plans.FirstOrDefault();
                        if (adopted != null)
                        {
                            var idx = cands.FindIndex(c => c.Id == adopted.ManagerEmployeeId);
                            if (idx >= 0) current = idx + 1;
                        }
                        o.AddDropdown("sm_manager", "storemanager_opt_manager_label", labels.ToArray(), current, i =>
                        {
                            if (i <= 0) return;
                            var pick = cands[i - 1];
                            var res = dir.AdoptManager(hq.Address, pick.Id);
                            Toast(res);
                            Rebuild();
                        });
                    }

                    o.AddButton("storemanager_opt_list_stores", ListStores);
                    o.AddButton("storemanager_opt_status", () => Debugging.StoreManagerCommands.PrintStatus());
                }
            }

            o.AddSplitter().AddHeader("storemanager_opt_defaults_header");

            o.AddSlider("sm_def_budget", "storemanager_opt_def_budget", 0, 30000,
                (int)_defaults.WeeklyRestockBudgetCap, v => _defaults.WeeklyRestockBudgetCap = v, "storemanager_opt_money_suffix");

            o.AddSlider("sm_def_days", "storemanager_opt_def_days", 1, 30,
                _defaults.TargetDaysOfStock, v => _defaults.TargetDaysOfStock = v, "storemanager_opt_days_suffix");

            o.AddDropdown("sm_def_staffing", "storemanager_opt_def_staffing",
                new[] { "storemanager_staffing_lean", "storemanager_staffing_normal", "storemanager_staffing_generous" },
                (int)_defaults.Staffing, i => _defaults.Staffing = (StaffingLevel)i);

            o.AddSplitter();

            try
            {
                OptionsService.Register(_modId, o);
                _ctx?.Logger.Info("Store Manager options (re)registered.");
            }
            catch (Exception e) { Debug.LogError("[StoreManager] options register failed: " + e.Message); }
        }

        private static void ListStores()
        {
            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null) return;
            var stores = GameApi.GetSupervisableStores();
            Debug.Log($"[StoreManager] your stores ({stores.Count}) — use StoreManager.Assign <n>:");
            for (int i = 0; i < stores.Count; i++)
            {
                var sup = dir.PlanSupervising(stores[i].Address) != null ? "  [supervised]" : "";
                Debug.Log($"  {i}: {stores[i].Name}{sup}");
            }
            Feedback.Toast(Feedback.Level.Info, "storemanager_notify_store_list",
                new Dictionary<string, string> { { "count", stores.Count.ToString() } });
        }

        private static void Toast(ActionResult r)
        {
            Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked",
                new Dictionary<string, string> { { "msg", r.Message } });
            Debug.Log("[StoreManager] " + (r.Ok ? "" : "blocked: ") + r.Message);
        }
    }
}
