#nullable enable
using System;
using System.Linq;
using StoreManager.Interop;
using StoreManager.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreManager.UI
{
    /// <summary>
    /// The content of the injected HQ BizMan "Filiaalmanagers" tab. Rebuilt every time the tab's
    /// container is activated (stock <c>SetTab</c> toggles it, firing <see cref="OnEnable"/>).
    /// Mirrors <see cref="StoreManagerOptions"/> — hire, adopt, assign stores, set per-store limits —
    /// but as native uGUI so it lives inside the HQ screen like the vanilla manager tabs.
    ///
    /// The ModOptions panel stays as a fallback and does the same things.
    /// </summary>
    public sealed class StoreManagerTabView : MonoBehaviour
    {
        private RectTransform? _content;
        private const decimal BudgetStep = 500m;

        private void OnEnable()
        {
            try
            {
                Build();
                var rt = (RectTransform)transform;
                Debug.Log($"[StoreManager] tab view built. container rect={rt.rect.size} content children={(_content != null ? _content.childCount : -1)}");
            }
            catch (Exception e) { Debug.LogError("[StoreManager] tab view build failed: " + e); }
        }

        public void Rebuild()
        {
            if (isActiveAndEnabled) { try { Build(); } catch (Exception e) { Debug.LogError("[StoreManager] tab rebuild failed: " + e); } }
        }

        private void Build()
        {
            if (_content == null)
            {
                var srGo = UiKit.Container("Scroll", transform);
                var srt = UiKit.Rect(srGo);
                srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
                srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
                _content = UiKit.ScrollColumn(srGo.transform);
            }
            UiKit.Clear(_content);

            UiKit.Label(_content, "storemanager_bizmantab_title".L("Store Managers"), 24f, UiKit.TextColor, FontStyles.Bold);
            UiKit.Spacer(_content, 4f);

            if (!RoleSystemState.IsActive)
            {
                UiKit.Label(_content, "storemanager_opt_role_disabled".L(
                    "The Store Manager role is disabled on this game build — supervision paused, no data lost."),
                    15f, UiKit.MutedColor);
                return;
            }

            var dir = Core.StoreManagerCityMod.Active;
            if (dir == null) { UiKit.Label(_content, "storemanager_opt_no_city".L("Load a save to manage stores."), 15f, UiKit.MutedColor); return; }
            if (dir.ReadOnly) { UiKit.Label(_content, "storemanager_notify_data_corrupt".L("Saved data unreadable — read-only this session."), 15f, UiKit.MutedColor); return; }

            var hq = GameApi.GetHeadquarters().FirstOrDefault();
            if (string.IsNullOrEmpty(hq.Address))
            {
                UiKit.Label(_content, "storemanager_opt_no_hq".L("Rent an office (Headquarters) first."), 15f, UiKit.MutedColor);
                return;
            }

            var plan = dir.Plans.FirstOrDefault();
            var cands = GameApi.GetManagerCandidates(hq.Address);

            // ── hire ──────────────────────────────────────────────────────────
            UiKit.Button(_content, "storemanager_act_recruit".L("Hire a new Store Manager onto my HQ"), () =>
            {
                var r = RoleEmployees.Recruit(hq.Address);
                Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                    r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked", D(r.Message));
                Rebuild();
            }, 38f, UiKit.AccentColor);

            UiKit.Label(_content, GameApi.RequireHqShift
                ? "storemanager_bizmantab_shifthint".L("New hires need a desk shift here (HQ → Schedule) before you can pick them.")
                : "storemanager_bizmantab_assignhint".L("New hires need to be assigned to this HQ before you can pick them."),
                13f, UiKit.MutedColor);
            UiKit.Spacer(_content);

            // ── manager ───────────────────────────────────────────────────────
            if (plan == null)
            {
                UiKit.Label(_content, "storemanager_bizmantab_pickmgr".L("Eligible managers"), 17f, UiKit.TextColor, FontStyles.Bold);
                if (cands.Count == 0)
                {
                    UiKit.Label(_content, "storemanager_opt_no_candidates".L("No eligible Store Manager yet."), 14f, UiKit.MutedColor);
                    return;
                }
                foreach (var c in cands)
                {
                    var row = UiKit.Row(_content.transform);
                    var nm = UiKit.Label(row.transform, c.ToString(), 15f);
                    UiKit.Flexible(nm.gameObject);
                    var cid = c.Id;
                    var btn = UiKit.Button(row.transform, "storemanager_bizmantab_make".L("Make Filiaalmanager"), () =>
                    {
                        Announce(dir.AdoptManager(hq.Address, cid));
                        Rebuild();
                    }, 30f);
                    UiKit.FixedWidth(btn.gameObject, 190f);
                }
                return;
            }

            var mgr = GameApi.FindManager(plan.ManagerEmployeeId);
            var mgrRow = UiKit.Row(_content.transform, 8f, 34f);
            var ml = UiKit.Label(mgrRow.transform,
                (mgr?.Name ?? "manager") + (plan.Dormant ? "  —  " + "storemanager_bizmantab_dormant".L("idle (not scheduled)") : ""),
                16f, plan.Dormant ? UiKit.MutedColor : UiKit.TextColor, FontStyles.Bold);
            UiKit.Flexible(ml.gameObject);
            var drop = UiKit.Button(mgrRow.transform, "storemanager_bizmantab_drop".L("Remove"), () =>
            {
                dir.DropManager(plan.ManagerEmployeeId);
                Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok", D("Manager removed."));
                Rebuild();
            }, 30f);
            UiKit.FixedWidth(drop.gameObject, 120f);

            int cap = GameApi.MaxStores(plan.HqAddress, plan.ManagerEmployeeId);
            UiKit.Label(_content, string.Format("storemanager_bizmantab_cap".L("Supervising {0} of {1} stores (skill cap)"),
                plan.Assignments.Count, cap), 13f, UiKit.MutedColor);
            UiKit.Spacer(_content);

            // ── stores ────────────────────────────────────────────────────────
            UiKit.Label(_content, "storemanager_opt_stores_header".L("Supervised stores"), 17f, UiKit.TextColor, FontStyles.Bold);
            foreach (var s in GameApi.GetSupervisableStores())
            {
                string addr = s.Address;
                bool supervised = plan.Supervises(addr);
                var a = plan.Find(addr);

                var row = UiKit.Row(_content.transform, 8f, 32f);
                bool hasContract = DeliveryContracts.HasContract(addr);
                var lbl = UiKit.Label(row.transform,
                    s.Name + (hasContract ? "" : "  " + "storemanager_bizmantab_nocontract".L("(no delivery contract)")),
                    15f, supervised ? UiKit.TextColor : UiKit.MutedColor);
                UiKit.Flexible(lbl.gameObject);

                bool atCap = !supervised && plan.Assignments.Count >= cap;
                var tgl = UiKit.Button(row.transform,
                    supervised ? "storemanager_bizmantab_supervising".L("Supervising ✓")
                               : atCap ? "storemanager_bizmantab_atcap".L("cap reached")
                                       : "storemanager_bizmantab_assign".L("Assign"),
                    () =>
                    {
                        if (supervised) Announce(dir.UnassignStore(plan.ManagerEmployeeId, addr));
                        else Announce(dir.AssignStore(plan.ManagerEmployeeId, addr));
                        Rebuild();
                    }, 28f, supervised ? UiKit.AccentColor : UiKit.ButtonColor);
                UiKit.FixedWidth(tgl.gameObject, 150f);

                if (supervised && a != null)
                {
                    var lim = UiKit.Row(_content.transform, 6f, 28f);
                    UiKit.FixedWidth(UiKit.Label(lim.transform, "storemanager_opt_def_budget".L("Weekly budget"), 13f, UiKit.MutedColor).gameObject, 130f);
                    MoneyStepper(lim.transform, a, dir, plan);
                    UiKit.FixedWidth(UiKit.Label(lim.transform, "storemanager_opt_def_days".L("Target days"), 13f, UiKit.MutedColor).gameObject, 100f);
                    DayStepper(lim.transform, a, dir, plan);
                }
            }
        }

        private void MoneyStepper(Transform parent, Domain.StoreAssignment a, ManagerDirectory dir, Domain.StoreManagerPlan plan)
        {
            var minus = UiKit.Button(parent, "−", () => { dir.SetCap(plan.ManagerEmployeeId, a.StoreAddress, a.WeeklyRestockBudgetCap - BudgetStep); Rebuild(); }, 26f);
            UiKit.FixedWidth(minus.gameObject, 34f);
            UiKit.FixedWidth(UiKit.Label(parent, $"${a.WeeklyRestockBudgetCap:N0}", 14f).gameObject, 84f);
            var plus = UiKit.Button(parent, "+", () => { dir.SetCap(plan.ManagerEmployeeId, a.StoreAddress, a.WeeklyRestockBudgetCap + BudgetStep); Rebuild(); }, 26f);
            UiKit.FixedWidth(plus.gameObject, 34f);
        }

        private void DayStepper(Transform parent, Domain.StoreAssignment a, ManagerDirectory dir, Domain.StoreManagerPlan plan)
        {
            var minus = UiKit.Button(parent, "−", () => { dir.SetTargetDays(plan.ManagerEmployeeId, a.StoreAddress, a.TargetDaysOfStock - 1); Rebuild(); }, 26f);
            UiKit.FixedWidth(minus.gameObject, 34f);
            UiKit.FixedWidth(UiKit.Label(parent, a.TargetDaysOfStock.ToString(), 14f).gameObject, 40f);
            var plus = UiKit.Button(parent, "+", () => { dir.SetTargetDays(plan.ManagerEmployeeId, a.StoreAddress, a.TargetDaysOfStock + 1); Rebuild(); }, 26f);
            UiKit.FixedWidth(plus.gameObject, 34f);
        }

        private static void Announce(ActionResult r)
        {
            Feedback.Toast(r.Ok ? Feedback.Level.Success : Feedback.Level.Warning,
                r.Ok ? "storemanager_notify_ok" : "storemanager_notify_blocked", D(r.Message));
        }

        private static System.Collections.Generic.Dictionary<string, string> D(string msg) => new() { { "msg", msg } };
    }

    internal static class LocExt
    {
        /// <summary>Localise a key; fall back to the given literal if the key is missing.</summary>
        public static string L(this string key, string fallback) => Interop.Loc.T(key, fallback);
    }
}
