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

                // exactly what vanilla SetUpEmployeeCounter does: set the "Count" label text
                var countT = clone.transform.Find("Count");
                var countLbl = countT != null ? countT.GetComponent<TextMeshProUGUI>() : null;
                if (countLbl != null) countLbl.text = count.ToString();

                // role-name label: just repoint the copied localisation key — the component does
                // its own layout work on enable, same as every vanilla row.
                var nameT = clone.transform.Find("Label");
                var lc = nameT != null ? nameT.GetComponent<Localizor.LanguageChangeEvent.TextLocalizationComponent>() : null;
                if (lc != null) { try { lc.Key = "storemanager_hqcard_label"; } catch { } }
                else if (nameT?.GetComponent<TextMeshProUGUI>() is { } nt) nt.text = Loc.T("storemanager_hqcard_label", "Store Manager");

                var btn = clone.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    var addr = hq.Address;
                    btn.onClick.AddListener(() => BizManTabPatch.OpenHqTab(addr));
                }

                // copy the live child positions from the good sibling once the layout has settled
                clone.AddComponent<HqCounterAlign>().Init(anchor);
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

    /// <summary>
    /// The cloned HQ counter renders shifted despite identical serialized geometry (the vanilla
    /// layout does something to it we can't see). This copies the live rect of the "Count" and
    /// "Label" children from a known-good sibling for a few frames after the card appears.
    /// </summary>
    public sealed class HqCounterAlign : MonoBehaviour
    {
        private Transform? _good;
        private static bool _dumped;
        private int _t;

        public void Init(Transform good) => _good = good;

        private void OnEnable() { _t = 0; }

        private void LateUpdate()
        {
            _t++;
            // match the good sibling's whole button rect (X only — the layout owns Y stacking) + its children
            var self = (RectTransform)transform;
            var src = _good as RectTransform;
            if (src != null)
            {
                self.anchorMin = src.anchorMin; self.anchorMax = src.anchorMax; self.pivot = src.pivot;
                self.sizeDelta = new Vector2(src.sizeDelta.x, self.sizeDelta.y);
                self.anchoredPosition = new Vector2(src.anchoredPosition.x, self.anchoredPosition.y);
                self.localScale = src.localScale;
                Copy(src.Find("Count"), transform.Find("Count"));
                Copy(src.Find("Label"), transform.Find("Label"));
            }

            if (!_dumped && _t == 45)
            {
                _dumped = true;
                var sb = new System.Text.StringBuilder("\n[StoreManager] HQ counter POST-LAYOUT:\n");
                var parent = transform.parent;
                sb.AppendLine($"PARENT {parent?.name} comps=[{string.Join(",", System.Array.ConvertAll(parent != null ? parent.GetComponents<Component>() : System.Array.Empty<Component>(), c => c.GetType().Name))}]");
                if (_good != null) Row("GOOD ", (RectTransform)_good, sb);
                Row("CLONE", self, sb);
                Debug.Log(sb.ToString());
            }
        }

        private static void Row(string tag, RectTransform rt, System.Text.StringBuilder sb)
        {
            void W(RectTransform r, int d)
            {
                var tmp = r.GetComponent<TMPro.TextMeshProUGUI>();
                sb.AppendLine($"{tag} {new string(' ', d * 2)}{r.name} pos={r.anchoredPosition:F0} size={r.rect.size:F0} world={((RectTransform)r).position:F0}"
                    + (tmp != null ? $"  \"{tmp.text}\" align={tmp.alignment}" : ""));
                for (int i = 0; i < r.childCount; i++) if (r.GetChild(i) is RectTransform c) W(c, d + 1);
            }
            W(rt, 0);
        }

        private static void Copy(Transform? src, Transform? dst)
        {
            if (src is not RectTransform s || dst is not RectTransform d) return;
            d.anchorMin = s.anchorMin; d.anchorMax = s.anchorMax; d.pivot = s.pivot;
            d.sizeDelta = s.sizeDelta; d.anchoredPosition = s.anchoredPosition; d.localScale = s.localScale;
        }
    }
}
