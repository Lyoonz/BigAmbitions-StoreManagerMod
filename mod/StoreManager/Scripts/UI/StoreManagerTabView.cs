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

        private void OnEnable()
        {
            try
            {
                Build();
                var rt = (RectTransform)transform;
                if (_content != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                    var first = _content.childCount > 0 ? (RectTransform)_content.GetChild(0) : null;
                    Debug.Log($"[StoreManager] tab view built. container={rt.rect.size} panel-parent={_content.parent?.name} " +
                              $"col={_content.rect.size} kids={_content.childCount} firstKid={(first != null ? first.rect.size.ToString() : "n/a")} " +
                              $"canvas={GetComponentInParent<Canvas>()?.name}");
                }
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
                _content = UiKit.Column(transform);
            UiKit.Clear(_content);

            UiKit.Label(_content, "storemanager_bizmantab_title".L("Store Managers"), 26f, UiKit.HeaderColor, FontStyles.Bold);

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

            // ── how to get a manager (recruit via the agency, then schedule) ───
            UiKit.Spacer(_content, 2f);
            UiKit.Label(_content,
                "storemanager_bizmantab_recruithow".L("Recruit a Store Manager through your phone → Recruitment Agency (pick this HQ, skill \"Store Manager\"), then give them a desk shift in BizMan → HQ → Schedule. They'll show up below."),
                12f, UiKit.MutedColor);

            // ── manager ───────────────────────────────────────────────────────
            if (plan == null)
            {
                UiKit.SectionHeader(_content, "storemanager_bizmantab_pickmgr".L("Eligible managers"));
                if (cands.Count == 0)
                {
                    UiKit.Label(_content, "storemanager_opt_no_candidates".L("No eligible Store Manager yet."), 14f, UiKit.MutedColor);
                    return;
                }
                foreach (var c in cands)
                {
                    var row = UiKit.Row(_content.transform, card: true);
                    var nm = UiKit.Label(row.transform, c.ToString(), 15f);
                    UiKit.Flexible(nm.gameObject);
                    var cid = c.Id;
                    var btn = UiKit.Button(row.transform, "storemanager_bizmantab_make".L("Make Store Manager"), () =>
                    {
                        Announce(dir.AdoptManager(hq.Address, cid));
                        Rebuild();
                    }, 30f, UiKit.AccentColor);
                    UiKit.FixedWidth(btn.gameObject, 230f);
                }
                return;
            }

            var mgr = GameApi.FindManager(plan.ManagerEmployeeId);
            int cap = GameApi.MaxStores(plan.HqAddress, plan.ManagerEmployeeId);
            UiKit.SectionHeader(_content, "storemanager_opt_stores_header".L("Manager & supervised stores"));

            var mgrRow = UiKit.Row(_content.transform, card: true, height: 44f);
            var ml = UiKit.Label(mgrRow.transform,
                (mgr?.Name ?? "manager") + (plan.Dormant ? "   ·   " + "storemanager_bizmantab_dormant".L("idle (not scheduled)") : ""),
                16f, plan.Dormant ? UiKit.MutedColor : UiKit.HeaderColor, FontStyles.Bold);
            UiKit.Flexible(ml.gameObject);
            var drop = UiKit.Button(mgrRow.transform, "storemanager_bizmantab_drop".L("Remove"), () =>
            {
                dir.DropManager(plan.ManagerEmployeeId);
                Feedback.Toast(Feedback.Level.Info, "storemanager_notify_ok", D("Manager removed."));
                Rebuild();
            }, 30f);
            UiKit.FixedWidth(drop.gameObject, 150f);

            UiKit.Label(_content, string.Format("storemanager_bizmantab_cap".L("Supervising {0} of {1} stores (skill cap)"),
                plan.Assignments.Count, cap), 12f, UiKit.MutedColor);

            // ── stores ────────────────────────────────────────────────────────
            foreach (var s in GameApi.GetSupervisableStores())
            {
                string addr = s.Address;
                bool supervised = plan.Supervises(addr);
                var a = plan.Find(addr);
                bool hasContract = DeliveryContracts.HasContract(addr);
                bool atCap = !supervised && plan.Assignments.Count >= cap;

                var row = UiKit.Row(_content.transform, card: supervised, height: 38f);
                var lbl = UiKit.Label(row.transform,
                    s.Name + (hasContract ? "" : "   " + "storemanager_bizmantab_nocontract".L("(no delivery contract)")),
                    15f, supervised ? UiKit.HeaderColor : UiKit.MutedColor);
                UiKit.Flexible(lbl.gameObject);

                var tgl = UiKit.Button(row.transform,
                    supervised ? "storemanager_bizmantab_supervising".L("Supervising ✓")
                               : "storemanager_bizmantab_assign".L("Assign"),
                    () =>
                    {
                        if (supervised) Announce(dir.UnassignStore(plan.ManagerEmployeeId, addr));
                        else Announce(dir.AssignStore(plan.ManagerEmployeeId, addr));
                        Rebuild();
                    }, 28f, supervised ? UiKit.AccentColor : null, enabled: supervised || !atCap);
                UiKit.FixedWidth(tgl.gameObject, 200f);

                if (supervised && a != null)
                {
                    var assignment = a;
                    var lim = UiKit.Row(_content.transform, false, 10f, 36f);

                    UiKit.FixedWidth(UiKit.Label(lim.transform, "storemanager_bizmantab_budget".L("Weekly budget $"), 12f, UiKit.MutedColor).gameObject, 150f);
                    UiKit.NumberField(lim.transform, ((long)assignment.WeeklyRestockBudgetCap).ToString(), 120f, s =>
                    {
                        if (TryParseAmount(s, out var v) && v != assignment.WeeklyRestockBudgetCap)
                        { dir.SetCap(plan.ManagerEmployeeId, assignment.StoreAddress, v); Rebuild(); }
                    });

                    UiKit.FixedWidth(UiKit.Label(lim.transform, "storemanager_bizmantab_days".L("Stock buffer (days)"), 12f, UiKit.MutedColor).gameObject, 180f);
                    UiKit.NumberField(lim.transform, assignment.TargetDaysOfStock.ToString(), 70f, s =>
                    {
                        if (int.TryParse(new string(s.Where(char.IsDigit).ToArray()), out var d) && d != assignment.TargetDaysOfStock)
                        { dir.SetTargetDays(plan.ManagerEmployeeId, assignment.StoreAddress, d); Rebuild(); }
                    });
                }
            }
        }

        private static bool TryParseAmount(string s, out decimal value)
        {
            var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
            if (decimal.TryParse(digits, out value)) return true;
            value = 0m; return false;
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
