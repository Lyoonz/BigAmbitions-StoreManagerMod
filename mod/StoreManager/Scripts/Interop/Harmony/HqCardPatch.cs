#nullable enable
using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Entities;
using Helpers;
using StoreManager.Runtime;
using StoreManager.UI;
using TMPro;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Adds a "Filiaalmanagers" counter to the HQ card on the BizMan landing page — the row that
    /// shows Prijsmanager / Logistiek / Inkoper / HR / Headhunter counts. Postfix on
    /// <c>UI.Smartphone.Apps.BizMan.HeadquartersList.SetUpEntry(BuildingRegistration)</c>: clone the
    /// live <c>EmployeeCounter/PurchasingAgents</c> button, count the HQ's <c>sm:skill_storemanager</c>
    /// employees, and route its click to the mod tab. Fully guarded; failure just omits the counter.
    /// </summary>
    public static class HqCardPatch
    {
        public const string TypeName = "UI.Smartphone.Apps.BizMan.HeadquartersList";
        private const string CounterId = "StoreManagers";
        private const string AnchorId = "PurchasingAgents";

        public static bool Patched { get; private set; }
        public static string? Disabled { get; private set; }

        private static Type? _t;
        private static bool _dumped;

        private static void DumpRt(string tag, Transform root, System.Text.StringBuilder sb)
        {
            void Walk(Transform t, int d)
            {
                var rt = t as RectTransform;
                var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
                sb.Append(tag).Append(' ').Append(new string(' ', d * 2)).Append(t.name)
                  .Append("  anchoredPos=").Append(rt != null ? rt.anchoredPosition.ToString("F0") : "-")
                  .Append(" size=").Append(rt != null ? rt.sizeDelta.ToString("F0") : "-")
                  .Append(" anchors=").Append(rt != null ? $"{rt.anchorMin.x:F2},{rt.anchorMin.y:F2}-{rt.anchorMax.x:F2},{rt.anchorMax.y:F2}" : "-")
                  .Append(" pivot=").Append(rt != null ? rt.pivot.ToString("F2") : "-");
                if (tmp != null) sb.Append(" TMP=\"").Append(tmp.text).Append("\" align=").Append(tmp.alignment);
                sb.AppendLine();
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), d + 1);
            }
            Walk(root, 0);
        }

        public static bool Resolve()
        {
            if (_t != null) return Disabled == null;
            try
            {
                _t = AccessTools.TypeByName(TypeName);
                if (_t == null) { Disabled = "HeadquartersList type not found"; return false; }
                return true;
            }
            catch (Exception e) { Disabled = "resolve threw: " + e.Message; return false; }
        }

        public static void EnsurePatched(global::HarmonyLib.Harmony harmony)
        {
            if (Patched || _t == null || Disabled != null) return;
            try
            {
                var target = AccessTools.Method(_t, "SetUpEntry");
                if (target == null) { Disabled = "SetUpEntry not found"; return; }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(HqCardPatch), nameof(SetUpEntry_Postfix)));
                Patched = true;
                Debug.Log("[StoreManager] HQ card counter patch applied.");
            }
            catch (Exception e) { Disabled = "patch threw: " + e.Message; }
        }

        private static void SetUpEntry_Postfix(BuildingRegistration __0)
        {
            try
            {
                if (!RoleSystemState.IsActive || __0 == null) return;
                var hq = __0;

                // The clone lives on the freshly-instantiated entry, which is the last child of the
                // template's parent (HeadquartersList clones then SetActive(true) at the end).
                var tpl = FindTemplate();
                var entry = tpl != null && tpl.parent != null && tpl.parent.childCount > 0
                    ? tpl.parent.GetChild(tpl.parent.childCount - 1)
                    : null;
                if (entry == null) return;

                var counterRow = entry.Find("EmployeeCounter");
                var anchor = counterRow?.Find(AnchorId);
                if (counterRow == null || anchor == null) return;
                if (counterRow.Find(CounterId) != null) return;   // already added

                var clone = UnityEngine.Object.Instantiate(anchor.gameObject, counterRow);
                clone.name = CounterId;
                clone.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);

                // count sm:skill_storemanager employees assigned to this HQ
                int count = 0;
                try
                {
                    count = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo
                    {
                        withAssignedAddress = hq.Address,
                        withSkills = new[] { SkillRegistry.StoreManagerSkill },
                        excludeBeingReplaced = true,
                    }).Count;
                }
                catch { }

                // "Count" is a direct child (same as the vanilla SetUpEmployeeCounter path)
                var countT = clone.transform.Find("Count");
                var countLbl = countT != null ? countT.GetComponent<TextMeshProUGUI>() : null;
                if (countLbl != null) countLbl.text = count.ToString();

                // role-name label: set the TMP text DIRECTLY and neutralise the loc component so it
                // never re-resolves the copied key and shifts the layout.
                foreach (var t in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (t == countLbl || (countT != null && t.transform.IsChildOf(countT))) continue;
                    var lc = t.GetComponent<Localizor.LanguageChangeEvent.TextLocalizationComponent>();
                    if (lc != null) lc.enabled = false;
                    t.text = Loc.T("storemanager_hqcard_label", "Filiaalmanager");
                    break;
                }

                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    var addr = hq.Address;
                    btn.onClick.AddListener(() => BizManTabPatch.OpenHqTab(addr));
                }

                if (!_dumped)
                {
                    _dumped = true;
                    var sb = new System.Text.StringBuilder("\n[StoreManager] HQ counter geometry:\n");
                    DumpRt("ANCHOR", anchor, sb);
                    DumpRt("CLONE ", clone.transform, sb);
                    Debug.Log(sb.ToString());
                }
            }
            catch (Exception e) { Debug.LogError("[StoreManager] HQ card postfix swallowed: " + e); }
        }

        private static Transform? FindTemplate()
        {
            try
            {
                var list = UnityEngine.Object.FindObjectOfType(_t!);
                var f = AccessTools.Field(_t, "headquartersEntry");
                return f?.GetValue(list) as Transform;
            }
            catch { return null; }
        }
    }
}
