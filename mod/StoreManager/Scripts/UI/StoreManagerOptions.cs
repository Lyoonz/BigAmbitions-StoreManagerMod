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
    /// The panel under Options → Mods. With the HQ BizMan "Filiaalmanagers" tab live (the normal
    /// case) this panel is intentionally tiny: the per-store defaults for newly assigned stores,
    /// and a "safe uninstall" action. The full manager/store fallback only appears when the tab
    /// patch didn't apply.
    ///
    /// ModOptions quirks this works around: every control's OnValueChanged fires once during the
    /// synchronous render, so callbacks bail while <c>_building</c> is set; persistable ids carry a
    /// per-rebuild <c>_gen</c> suffix so a stored PlayerPrefs value never clobbers mod state.
    /// </summary>
    public static class StoreManagerOptions
    {
        private static string _modId = "StoreManager";
        private static GlobalDefaults _defaults = GlobalDefaults.Default();
        private static ModContext? _ctx;

        private static bool _building;
        private static int _gen;

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
            if (_building) return;
            _building = true;
            _gen++;
            RebuildCount++;
            try
            {
                var o = new ModOptions();
                try { BuildBody(o); }
                catch (Exception e) { Debug.LogError("[StoreManager] options build failed: " + e); }
                try { OptionsService.Register(_modId, o); }
                catch (Exception e) { Debug.LogError("[StoreManager] options register failed: " + e.Message); }
            }
            finally { _building = false; }
        }

        private static void RequestRebuild()
        {
            if (!_building) Rebuild();
        }

        private static string Id(string baseId) => baseId + "_v" + _gen;

        // ── body ───────────────────────────────────────────────────────────────
        private static void BuildBody(ModOptions o)
        {
            if (!RoleSystemState.IsActive)
            {
                o.AddHeader("storemanager_opt_role_disabled");
                AddDefaults(o);
                AddUninstall(o);
                return;
            }

            bool tabLive = Interop.Harmony.BizManTabPatch.Patched;
            if (!tabLive)
            {
                o.AddHeader("storemanager_opt_notab_hint");
                AddFallbackControls(o);
            }

            AddDefaults(o);
            AddUninstall(o);
        }

        // ── defaults for newly assigned stores ─────────────────────────────────
        private static void AddDefaults(ModOptions o)
        {
            o.AddHeader("storemanager_opt_defaults_header");
            o.AddSlider(Id("sm_def_budget"), "storemanager_opt_def_budget", 0, 30000, (int)_defaults.WeeklyRestockBudgetCap,
                v => { if (!_building) _defaults.WeeklyRestockBudgetCap = v; }, "storemanager_opt_money_suffix");
            o.AddSlider(Id("sm_def_days"), "storemanager_opt_def_days", 1, 30, _defaults.TargetDaysOfStock,
                v => { if (!_building) _defaults.TargetDaysOfStock = v; }, "storemanager_opt_days_suffix");
            o.AddSlider(Id("sm_def_margin"), "storemanager_opt_def_margin", 0, 100, _defaults.SafetyMarginPercent,
                v => { if (!_building) _defaults.SafetyMarginPercent = v; }, "storemanager_opt_pct_suffix");
        }

        // ── safe uninstall ─────────────────────────────────────────────────────
        private static readonly string[] UninstallChoices =
            { "storemanager_opt_uninstall_pick", "storemanager_opt_uninstall_do" };

        private static void AddUninstall(ModOptions o)
        {
            o.AddSplitter();
            o.AddDropdown(Id("sm_uninstall"), "storemanager_opt_uninstall_label", UninstallChoices, 0, i =>
            {
                if (_building || i != 1) return;
                var dir = Core.StoreManagerCityMod.Active;
                int n = RoleEmployees.ReskillAllToVanilla();
                if (dir != null)
                    foreach (var id in dir.Plans.Select(p => p.ManagerEmployeeId).ToList()) dir.DropManager(id);
                dir?.Save();
                Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok",
                    D("msg", $"Re-skilled {n} manager(s) to Purchasing Agent and dropped all plans. Save now — then it's safe to delete the mod folder."));
                RequestRebuild();
            });
        }

        // ── fallback (only when the HQ tab patch failed) ───────────────────────
        private static readonly string[] StaffingChoices =
            { "storemanager_staffing_lean", "storemanager_staffing_normal", "storemanager_staffing_generous" };

        private static void AddFallbackControls(ModOptions o)
        {
            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null || dir.ReadOnly) return;
            var hq = GameApi.GetHeadquarters().FirstOrDefault();
            if (string.IsNullOrEmpty(hq.Address)) return;

            var cands = GameApi.GetManagerCandidates(hq.Address);
            var plan = dir.Plans.FirstOrDefault();
            if (cands.Count == 0 && plan == null) { o.AddSplitter().AddHeader("storemanager_opt_recruit_hint"); return; }

            o.AddSplitter().AddHeader("storemanager_opt_stores_header");

            var mgrLabels = new List<string> { "storemanager_opt_pick_manager" };
            mgrLabels.AddRange(cands.Select(c => c.ToString()));
            string? currentMgr = plan?.ManagerEmployeeId;
            int mgrCurrent = currentMgr != null ? cands.FindIndex(c => c.Id == currentMgr) + 1 : 0;
            o.AddDropdown(Id("sm_manager"), "storemanager_opt_manager_label", mgrLabels.ToArray(), Math.Max(0, mgrCurrent), i =>
            {
                if (_building) return;
                string? desired = (i >= 1 && i - 1 < cands.Count) ? cands[i - 1].Id : null;
                if (desired == currentMgr) return;
                if (currentMgr != null) dir.DropManager(currentMgr);
                if (desired != null) Announce(dir.AdoptManager(hq.Address, desired));
                RequestRebuild();
            });

            if (plan == null) return;

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
                    if (_building || on == plan.Supervises(addr)) return;
                    if (on) Announce(dir.AssignStore(plan.ManagerEmployeeId, addr));
                    else Announce(dir.UnassignStore(plan.ManagerEmployeeId, addr));
                    RequestRebuild();
                });
            }
        }

        private static void Announce(ActionResult r)
        {
            // the directory already toasts on success — only surface failures here
            if (!r.Ok)
                Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_blocked", D("msg", r.Message));
            Debug.Log("[StoreManager] " + (r.Ok ? "" : "blocked: ") + r.Message);
        }

        private static Dictionary<string, string> D(string k, string v) => new() { { k, v } };

        private static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }
}
