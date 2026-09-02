#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
    /// shows Pricing / Logistics / Purchasing Agent / HR / Headhunter counts. Postfix on
    /// <c>UI.Smartphone.Apps.BizMan.HeadquartersList.SetUpEntry(BuildingRegistration)</c>: clone the
    /// live <c>EmployeeCounter/PurchasingAgents</c> button, set the count, route its click to the
    /// mod tab, and — crucially — register its two labels with the row's <c>EqualWidthLabelGroup</c>
    /// components so the digit + name line up with the vanilla rows.
    /// </summary>
    public static class HqCardPatch
    {
        public const string TypeName = "UI.Smartphone.Apps.BizMan.HeadquartersList";
        private const string CounterId = "StoreManagers";
        private const string AnchorId = "PurchasingAgents";

        public static bool Patched { get; private set; }
        public static string? Disabled { get; private set; }

        private static Type? _t;
        private static Type? _ewlgType;
        private static FieldInfo? _ewlgLabels;
        private static MethodInfo? _ewlgSchedule;

        public static bool Resolve()
        {
            if (_t != null) return Disabled == null;
            try
            {
                _t = AccessTools.TypeByName(TypeName);
                if (_t == null) { Disabled = "HeadquartersList type not found"; return false; }
                _ewlgType = AccessTools.TypeByName("EqualWidthLabelGroup");
                _ewlgLabels = _ewlgType != null ? AccessTools.Field(_ewlgType, "labels") : null;
                _ewlgSchedule = _ewlgType != null
                    ? (AccessTools.Method(_ewlgType, "ScheduleMatch") ?? AccessTools.Method(_ewlgType, "Match"))
                    : null;
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

                var tpl = FindTemplate();
                var entry = tpl != null && tpl.parent != null && tpl.parent.childCount > 0
                    ? tpl.parent.GetChild(tpl.parent.childCount - 1)
                    : null;
                if (entry == null) return;

                var counterRow = entry.Find("EmployeeCounter");
                var anchor = counterRow?.Find(AnchorId);
                if (counterRow == null || anchor == null) return;
                if (counterRow.Find(CounterId) != null) return;

                var clone = UnityEngine.Object.Instantiate(anchor.gameObject, counterRow);
                clone.name = CounterId;
                clone.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);

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

                var cloneCount = clone.transform.Find("Count")?.GetComponent<TMP_Text>();
                var cloneLabel = clone.transform.Find("Label")?.GetComponent<TMP_Text>();
                var anchorCount = anchor.Find("Count")?.GetComponent<TMP_Text>();
                var anchorLabel = anchor.Find("Label")?.GetComponent<TMP_Text>();

                if (cloneCount != null) cloneCount.text = count.ToString();

                var lc = cloneLabel != null ? cloneLabel.GetComponent<Localizor.LanguageChangeEvent.TextLocalizationComponent>() : null;
                if (lc != null) { try { lc.Key = "storemanager_hqcard_label"; } catch { } }
                if (cloneLabel != null) cloneLabel.text = Loc.T("storemanager_hqcard_label", "Store Manager");

                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    var addr = hq.Address;
                    btn.onClick.AddListener(() => BizManTabPatch.OpenHqTab(addr));
                }

                // register the two new labels with the EqualWidthLabelGroup(s) that equalise the row
                RegisterWithEqualWidth(counterRow, anchorCount, cloneCount, anchorLabel, cloneLabel);
            }
            catch (Exception e) { Debug.LogError("[StoreManager] HQ card postfix swallowed: " + e); }
        }

        private static void RegisterWithEqualWidth(Transform counterRow, TMP_Text? anchorCount, TMP_Text? cloneCount,
                                                   TMP_Text? anchorLabel, TMP_Text? cloneLabel)
        {
            if (_ewlgType == null || _ewlgLabels == null) return;
            try
            {
                foreach (var comp in counterRow.GetComponents(_ewlgType))
                {
                    if (_ewlgLabels.GetValue(comp) is not IList labels) continue;
                    if (anchorCount != null && cloneCount != null && labels.Contains(anchorCount) && !labels.Contains(cloneCount))
                        labels.Add(cloneCount);
                    if (anchorLabel != null && cloneLabel != null && labels.Contains(anchorLabel) && !labels.Contains(cloneLabel))
                        labels.Add(cloneLabel);
                    try { _ewlgSchedule?.Invoke(comp, null); } catch { }
                }
            }
            catch (Exception e) { Debug.LogWarning("[StoreManager] EqualWidth register failed: " + e.Message); }
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
