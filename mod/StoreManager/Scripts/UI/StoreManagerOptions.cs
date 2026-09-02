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
    /// The panel under Options → Mods. Built only from ModOptions primitives (no Harmony/bundle).
    ///
    /// Two things about the game's ModOptions that shape this code:
    ///  1. Every control's OnValueChanged fires ONCE during render (to sync its initial state), and
    ///     OptionsService.Register invokes that render synchronously. So a callback that always
    ///     rebuilds = infinite recursion. Guards: `_building` re-entrancy flag + every callback is
    ///     idempotent (no-op when the value already matches current state).
    ///  2. Persistable controls (sliders/dropdowns) load a stored PlayerPrefs value on render, which
    ///     would clobber the mod's own data. Every persistable id is suffixed with a per-rebuild
    ///     generation so it never has a stored value and always starts from the value we pass.
    /// AddButton's caption is a fixed prefab placeholder, so one-shot actions use a dropdown.
    /// Slider value labels substitute `{value}`.
    /// </summary>
    public static class StoreManagerOptions
    {
        private static string _modId = "StoreManager";
        private static GlobalDefaults _defaults = GlobalDefaults.Default();
        private static ModContext? _ctx;
        private static string? _configureStore;

        private static bool _building;
        private static int _gen;

        /// <summary>Diagnostic — how many times the panel has been rebuilt (probe checks this doesn't run away).</summary>
        public static int RebuildCount { get; private set; }

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
            if (_building) return;                 // nested request during the synchronous render — ignore
            _building = true;
            _gen++;
            RebuildCount++;
            try
            {
                var o = new ModOptions().AddHeader("storemanager_options_header");
                try { BuildBody(o); }
                catch (Exception e) { Debug.LogError("[StoreManager] options build failed: " + e); }
                try { OptionsService.Register(_modId, o); }   // fires OnChanged -> game renders synchronously here
                catch (Exception e) { Debug.LogError("[StoreManager] options register failed: " + e.Message); }
            }
            finally { _building = false; }
        }

        /// <summary>A callback asking for a rebuild. If we're mid-render, the current build already
        /// reflects live state, so skip; otherwise rebuild now (its synchronous render is guarded).</summary>
        private static void RequestRebuild()
        {
            if (!_building) Rebuild();
        }

        private static string Id(string baseId) => baseId + "_v" + _gen;

        // ── actions ────────────────────────────────────────────────────────────
        private static readonly string[] ActionChoices =
        {
            "storemanager_act_pick", "storemanager_act_recruit", "storemanager_act_selftest",
            "storemanager_act_status", "storemanager_act_planweek", "storemanager_act_saferemove",
        };

        private static void AddActions(ModOptions o)
        {
            o.AddDropdown(Id("sm_action"), "storemanager_act_label", ActionChoices, 0, i =>
            {
                if (_building || i <= 0) return;
                var dir = Core.StoreManagerCityMod.Active;
                switch (i)
                {
                    case 1: // recruit
                    {
                        var hq = GameApi.GetHeadquarters().FirstOrDefault();
                        if (!RoleSystemState.IsActive)
                            Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_blocked", D("msg", RoleSystemState.Reason));
                        else if (string.IsNullOrEmpty(hq.Address))
                            Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_blocked", D("msg", "rent an office (HQ) first"));
                        else
                        {
                            var r = RoleEmployees.Recruit(hq.Address);
                            Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                                r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked", D("msg", r.Message));
                        }
                        break;
                    }
                    case 2: Debugging.StoreManagerCommands.SelfTestFromPanel(); break;
                    case 3: Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok",
                                D("msg", Debugging.StoreManagerCommands.StatusSummary())); break;
                    case 4:
                        if (dir != null && dir.Plans.Count > 0) dir.RunWeeklyPlanning();
                        else Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_blocked", D("msg", "no Store Manager to run"));
                        break;
                    case 5: // safe uninstall
                    {
                        int n = RoleEmployees.ReskillAllToVanilla();
                        if (dir != null)
                            foreach (var id in dir.Plans.Select(p => p.ManagerEmployeeId).ToList()) dir.DropManager(id);
                        dir?.Save();
                        Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok",
                            D("msg", $"Re-skilled {n} manager(s) to Purchasing Agent, dropped all plans. Save now, then deleting the mod is safe."));
                        break;
                    }
                }
                RequestRebuild();   // fresh id -> dropdown returns to "— choose —"
            });
        }

        // ── body ───────────────────────────────────────────────────────────────
        private static void BuildBody(ModOptions o)
        {
            AddActions(o);

            if (!RoleSystemState.IsActive)
                o.AddHeader("storemanager_opt_role_disabled");
            else if (Interop.Harmony.BizManTabPatch.Patched)
                o.AddHeader("storemanager_opt_hqtab_hint");

            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null) { o.AddHeader("storemanager_opt_no_city"); AddDefaults(o); return; }
            if (dir.ReadOnly) { o.AddHeader("storemanager_notify_data_corrupt"); AddDefaults(o); return; }

            var hq = GameApi.GetHeadquarters().FirstOrDefault();
            if (string.IsNullOrEmpty(hq.Address)) { o.AddHeader("storemanager_opt_no_hq"); AddDefaults(o); return; }

            var cands = GameApi.GetManagerCandidates(hq.Address);
            var plan = dir.Plans.FirstOrDefault();

            if (cands.Count == 0 && plan == null) { o.AddHeader("storemanager_opt_recruit_hint"); AddDefaults(o); return; }

            // ── manager picker ─────────────────────────────────────────────────
            o.AddSplitter().AddHeader("storemanager_opt_stores_header");
            var mgrLabels = new List<string> { "storemanager_opt_pick_manager" };
            mgrLabels.AddRange(cands.Select(c => c.ToString()));
            string? currentMgr = plan?.ManagerEmployeeId;
            int mgrCurrent = 0;
            if (currentMgr != null)
            {
                var idx = cands.FindIndex(c => c.Id == currentMgr);
                mgrCurrent = idx >= 0 ? idx + 1 : 0;
            }
            o.AddDropdown(Id("sm_manager"), "storemanager_opt_manager_label", mgrLabels.ToArray(), mgrCurrent, i =>
            {
                if (_building) return;
                string? desired = (i >= 1 && i - 1 < cands.Count) ? cands[i - 1].Id : null;
                if (desired == currentMgr) return;                  // idempotent — no real change
                if (currentMgr != null) dir.DropManager(currentMgr);
                if (desired != null) Announce(dir.AdoptManager(hq.Address, desired));
                else Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok", D("msg", "Manager dropped."));
                _configureStore = null;
                RequestRebuild();
            });

            if (plan == null) { AddDefaults(o); return; }

            // ── supervised-store toggles ──────────────────────────────────────
            int cap = GameApi.MaxStores(plan.HqAddress, plan.ManagerEmployeeId);
            foreach (var s in GameApi.GetSupervisableStores())
            {
                string addr = s.Address;
                bool supervised = plan.Supervises(addr);
                bool atCap = !supervised && plan.Assignments.Count >= cap;
                string label = s.Name + (atCap ? "  (skill cap reached)" : "")
                                       + (!DeliveryContracts.HasContract(addr) ? "  (no delivery contract)" : "");
                o.AddToggle(Id("sm_store_" + Sanitize(addr)), label, supervised, on =>
                {
                    if (_building || on == plan.Supervises(addr)) return;   // idempotent
                    if (on) Announce(dir.AssignStore(plan.ManagerEmployeeId, addr));
                    else Announce(dir.UnassignStore(plan.ManagerEmployeeId, addr));
                    if (!on && _configureStore == addr) _configureStore = null;
                    RequestRebuild();
                });
            }

            // ── per-store limits ──────────────────────────────────────────────
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
                o.AddDropdown(Id("sm_cfg_store"), "storemanager_opt_config_label", cfgLabels.ToArray(), cfgCurrent, i =>
                {
                    if (_building) return;
                    string? sel = (i >= 1 && i - 1 < plan.Assignments.Count) ? plan.Assignments[i - 1].StoreAddress : null;
                    if (sel == _configureStore) return;               // idempotent
                    _configureStore = sel;
                    RequestRebuild();
                });

                var target = _configureStore != null ? plan.Find(_configureStore) : null;
                if (target != null)
                {
                    var t = target;
                    o.AddSlider(Id("sm_cfg_budget"), "storemanager_opt_def_budget", 0, 30000, (int)t.WeeklyRestockBudgetCap,
                        v => { if (!_building && v != (int)t.WeeklyRestockBudgetCap) dir.SetCap(plan.ManagerEmployeeId, t.StoreAddress, v); },
                        "storemanager_opt_money_suffix");
                    o.AddSlider(Id("sm_cfg_days"), "storemanager_opt_def_days", 1, 30, t.TargetDaysOfStock,
                        v => { if (!_building && v != t.TargetDaysOfStock) dir.SetTargetDays(plan.ManagerEmployeeId, t.StoreAddress, v); },
                        "storemanager_opt_days_suffix");
                    o.AddDropdown(Id("sm_cfg_staffing"), "storemanager_opt_def_staffing", StaffingChoices, (int)t.Staffing,
                        i => { if (!_building && i != (int)t.Staffing) { t.Staffing = (StaffingLevel)i; dir.Save(); } });
                }
            }

            AddDefaults(o);
        }

        private static readonly string[] StaffingChoices =
            { "storemanager_staffing_lean", "storemanager_staffing_normal", "storemanager_staffing_generous" };

        private static void AddDefaults(ModOptions o)
        {
            o.AddSplitter().AddHeader("storemanager_opt_defaults_header");
            o.AddSlider(Id("sm_def_budget"), "storemanager_opt_def_budget", 0, 30000, (int)_defaults.WeeklyRestockBudgetCap,
                v => { if (!_building) _defaults.WeeklyRestockBudgetCap = v; }, "storemanager_opt_money_suffix");
            o.AddSlider(Id("sm_def_days"), "storemanager_opt_def_days", 1, 30, _defaults.TargetDaysOfStock,
                v => { if (!_building) _defaults.TargetDaysOfStock = v; }, "storemanager_opt_days_suffix");
            o.AddDropdown(Id("sm_def_staffing"), "storemanager_opt_def_staffing", StaffingChoices, (int)_defaults.Staffing,
                i => { if (!_building) _defaults.Staffing = (StaffingLevel)i; });
            o.AddSplitter();
        }

        private static void Announce(ActionResult r)
        {
            Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked", D("msg", r.Message));
            Debug.Log("[StoreManager] " + (r.Ok ? "" : "blocked: ") + r.Message);
        }

        private static Dictionary<string, string> D(string k, string v) => new() { { k, v } };

        private static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }
}
