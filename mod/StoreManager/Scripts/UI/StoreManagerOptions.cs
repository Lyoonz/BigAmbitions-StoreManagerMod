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
    /// The mod's panel under Options → Mods (reachable in-game via the pause menu). Built only
    /// from ModOptions primitives — no Harmony, no asset bundle. Re-registered (which live-rebuilds
    /// the panel, since OptionsService.Register fires OnChanged) whenever state changes.
    ///
    /// Layout:
    ///   Store Manager  [dropdown: pick / change the manager]
    ///   — supervised stores —
    ///   [toggle] Store A            (on = assigned, capped at the manager's skill limit)
    ///   [toggle] Store B
    ///   — configure —
    ///   Configure store [dropdown]  →  Weekly budget [slider]  Target days [slider]  Staffing [dropdown]
    /// </summary>
    public static class StoreManagerOptions
    {
        private static string _modId = "StoreManager";
        private static GlobalDefaults _defaults = GlobalDefaults.Default();
        private static ModContext? _ctx;
        private static string? _configureStore;   // address currently bound to the sliders

        public static void Register(ModContext ctx, GlobalDefaults defaults)
        {
            _ctx = ctx; _modId = ctx.ModId; _defaults = defaults;
            Rebuild();
        }

        public static void Unregister()
        {
            try { OptionsService.RemoveModOptions(_modId); } catch { }
        }

        public static void Rebuild()
        {
            var o = new ModOptions().AddHeader("storemanager_options_header");
            try { BuildBody(o); }
            catch (Exception e) { Debug.LogError("[StoreManager] options build failed: " + e); }

            try { OptionsService.Register(_modId, o); }
            catch (Exception e) { Debug.LogError("[StoreManager] options register failed: " + e.Message); }
        }

        private static void BuildBody(ModOptions o)
        {
            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null) { o.AddButton("storemanager_opt_no_city", null); AddDefaults(o); return; }

            var hq = GameApi.GetHeadquarters().FirstOrDefault();
            if (string.IsNullOrEmpty(hq.Address)) { o.AddButton("storemanager_opt_no_hq", null); AddDefaults(o); return; }

            var cands = GameApi.GetManagerCandidates(hq.Address);
            var plan = dir.Plans.FirstOrDefault();

            // ── manager picker ─────────────────────────────────────────────────
            if (cands.Count == 0 && plan == null)
            {
                o.AddButton("storemanager_opt_no_candidates", null);
                AddDefaults(o);
                return;
            }

            var mgrLabels = new List<string> { "storemanager_opt_pick_manager" };
            mgrLabels.AddRange(cands.Select(c => c.ToString()));
            int mgrCurrent = 0;
            if (plan != null)
            {
                var idx = cands.FindIndex(c => c.Id == plan.ManagerEmployeeId);
                mgrCurrent = idx >= 0 ? idx + 1 : 0;
            }
            o.AddDropdown("sm_manager", "storemanager_opt_manager_label", mgrLabels.ToArray(), mgrCurrent, i =>
            {
                if (i <= 0)
                {
                    if (plan != null) { dir.DropManager(plan.ManagerEmployeeId); Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok", D("msg", "Manager dropped.")); }
                }
                else
                {
                    var pick = cands[i - 1];
                    if (plan == null || plan.ManagerEmployeeId != pick.Id)
                    {
                        if (plan != null) dir.DropManager(plan.ManagerEmployeeId);
                        Announce(dir.AdoptManager(hq.Address, pick.Id));
                    }
                }
                _configureStore = null;
                Rebuild();
            });

            if (plan == null)
            {
                o.AddButton("storemanager_opt_selftest", Debugging.StoreManagerCommands.SelfTestFromPanel);
                AddDefaults(o);
                return;
            }

            // ── supervised-store toggles ───────────────────────────────────────
            int cap = GameApi.MaxStores(plan.HqAddress, plan.ManagerEmployeeId);
            o.AddSplitter().AddHeader("storemanager_opt_stores_header");

            var stores = GameApi.GetSupervisableStores();
            foreach (var s in stores)
            {
                bool supervised = plan.Supervises(s.Address);
                string addr = s.Address;
                bool atCap = !supervised && plan.Assignments.Count >= cap;
                string label = s.Name + (atCap ? "  (skill cap reached)" : "")
                                       + (!DeliveryContracts.HasContract(addr) ? "  (no delivery contract)" : "");
                o.AddToggle("sm_store_" + Sanitize(addr), label, supervised, on =>
                {
                    if (on) Announce(dir.AssignStore(plan.ManagerEmployeeId, addr));
                    else Announce(dir.UnassignStore(plan.ManagerEmployeeId, addr));
                    if (!on && _configureStore == addr) _configureStore = null;
                    Rebuild();
                });
            }

            o.AddButton("storemanager_opt_status", () =>
                Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok",
                    new Dictionary<string, string> { { "msg", Debugging.StoreManagerCommands.StatusSummary() } }));
            o.AddButton("storemanager_opt_planweek", () => dir.RunWeeklyPlanning());
            o.AddButton("storemanager_opt_selftest", Debugging.StoreManagerCommands.SelfTestFromPanel);

            // ── per-store config ──────────────────────────────────────────────
            if (plan.Assignments.Count > 0)
            {
                o.AddSplitter().AddHeader("storemanager_opt_config_header");

                var cfgLabels = new List<string> { "storemanager_opt_config_pick" };
                cfgLabels.AddRange(plan.Assignments.Select(a => a.StoreName));
                int cfgCurrent = 0;
                if (_configureStore != null)
                {
                    var idx = plan.Assignments.FindIndex(a => a.StoreAddress == _configureStore);
                    cfgCurrent = idx >= 0 ? idx + 1 : 0;
                }
                o.AddDropdown("sm_cfg_store", "storemanager_opt_config_label", cfgLabels.ToArray(), cfgCurrent, i =>
                {
                    _configureStore = i <= 0 ? null : plan.Assignments[i - 1].StoreAddress;
                    Rebuild();
                });

                var target = _configureStore != null ? plan.Find(_configureStore) : null;
                if (target != null)
                {
                    o.AddSlider("sm_cfg_budget", "storemanager_opt_def_budget", 0, 30000,
                        (int)target.WeeklyRestockBudgetCap,
                        v => dir.SetCap(plan.ManagerEmployeeId, target.StoreAddress, v),
                        "storemanager_opt_money_suffix");

                    o.AddSlider("sm_cfg_days", "storemanager_opt_def_days", 1, 30,
                        target.TargetDaysOfStock,
                        v => dir.SetTargetDays(plan.ManagerEmployeeId, target.StoreAddress, v),
                        "storemanager_opt_days_suffix");

                    o.AddDropdown("sm_cfg_staffing", "storemanager_opt_def_staffing",
                        StaffingChoices, (int)target.Staffing,
                        i => { target.Staffing = (StaffingLevel)i; dir.Save(); });
                }
            }

            AddDefaults(o);
        }

        private static readonly string[] StaffingChoices =
            { "storemanager_staffing_lean", "storemanager_staffing_normal", "storemanager_staffing_generous" };

        private static void AddDefaults(ModOptions o)
        {
            o.AddSplitter().AddHeader("storemanager_opt_defaults_header");
            o.AddSlider("sm_def_budget", "storemanager_opt_def_budget", 0, 30000,
                (int)_defaults.WeeklyRestockBudgetCap, v => _defaults.WeeklyRestockBudgetCap = v, "storemanager_opt_money_suffix");
            o.AddSlider("sm_def_days", "storemanager_opt_def_days", 1, 30,
                _defaults.TargetDaysOfStock, v => _defaults.TargetDaysOfStock = v, "storemanager_opt_days_suffix");
            o.AddDropdown("sm_def_staffing", "storemanager_opt_def_staffing", StaffingChoices,
                (int)_defaults.Staffing, i => _defaults.Staffing = (StaffingLevel)i);
            o.AddSplitter();
        }

        private static void Announce(ActionResult r)
        {
            Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked", D("msg", r.Message));
            Debug.Log("[StoreManager] " + (r.Ok ? "" : "blocked: ") + r.Message);
        }

        private static Dictionary<string, string> D(string k, string v) => new() { { k, v } };

        private static string Sanitize(string s)
        {
            var chars = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            return new string(chars);
        }
    }
}
