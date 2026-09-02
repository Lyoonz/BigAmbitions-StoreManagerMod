#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Localizor.LanguageChangeEvent;
using StoreManager.Runtime;
using StoreManager.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Injects a "Filiaalmanagers" tab into the HQ BizMan screen — the same screen as Purchasing
    /// Agents / HR / Logistics. One Postfix on the private <c>BizManBusiness.SetUpTabs</c> (the single
    /// choke point, re-run on every open and every business switch). It:
    ///  1. lazily builds a menu button (cloned from the live "PurchasingAgents" sibling) + a content
    ///     container hosting <see cref="StoreManagerTabView"/>, both named <c>StoreManagers</c>;
    ///  2. inserts <c>StoreManagers</c> into the private <c>_tabs</c> list right after
    ///     <c>PurchasingAgents</c> when the shown business is an HQ (mandatory — <c>BizManBusiness</c>
    ///     kicks the player off any tab not in <c>_tabs</c>);
    ///  3. re-shows the button (the stock trailing loop hides everything not in <c>_tabs</c>).
    ///
    /// Stock <c>SetTab</c> fully services a correctly-named child, so it is NOT patched. Every
    /// reflection handle is null-checked once; on any miss the tab is disabled and the headless
    /// supervision layer + the Options→Mods panel carry on.
    /// </summary>
    public static class BizManTabPatch
    {
        public const string TabId = "StoreManagers";
        private const string AnchorTab = "PurchasingAgents";
        private const string HqBusinessType = "ba:businesstype_headquarters";

        public static bool Available { get; private set; }
        public static bool Patched { get; private set; }
        public static string? Disabled { get; private set; }

        private static Type? _bizType;
        private static FieldInfo? _fMenu, _fContainers, _fTabs, _fReg;
        private static MethodInfo? _mSetTab;

        /// <summary>Resolve the private members. Call before <see cref="EnsurePatched"/>.</summary>
        public static bool Resolve()
        {
            if (_bizType != null) return Available;
            try
            {
                _bizType = AccessTools.TypeByName("BizManBusiness");
                if (_bizType == null) return Fail("BizManBusiness type not found");

                _fMenu = AccessTools.Field(_bizType, "menu");
                _fContainers = AccessTools.Field(_bizType, "containers");
                _fTabs = AccessTools.Field(_bizType, "_tabs");
                _fReg = AccessTools.Field(_bizType, "buildingRegistration");
                _mSetTab = AccessTools.Method(_bizType, "SetTab", new[] { typeof(string) });

                if (_fMenu == null || _fContainers == null || _fTabs == null || _fReg == null || _mSetTab == null)
                    return Fail($"BizManBusiness members changed (menu={_fMenu != null}, containers={_fContainers != null}, _tabs={_fTabs != null}, buildingRegistration={_fReg != null}, SetTab={_mSetTab != null})");

                Available = true;
                return true;
            }
            catch (Exception e) { return Fail("resolve threw: " + e.Message); }
        }

        public static void EnsurePatched(global::HarmonyLib.Harmony harmony)
        {
            if (Patched || !Available) return;
            try
            {
                var target = AccessTools.Method(_bizType, "SetUpTabs");
                if (target == null) { Fail("SetUpTabs not found"); return; }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(BizManTabPatch), nameof(SetUpTabs_Postfix)));
                Patched = true;
                Debug.Log("[StoreManager] BizMan HQ tab patch applied.");
            }
            catch (Exception e) { Fail("patch threw: " + e.Message); }
        }

        private static bool Fail(string why)
        {
            Available = false;
            Disabled = why;
            Debug.LogError("[StoreManager] HQ BizMan tab disabled — " + why + " (panel + supervision still work).");
            return false;
        }

        // ── the postfix ─────────────────────────────────────────────────────────
        private static void SetUpTabs_Postfix(object __instance)
        {
            try
            {
                if (!Available || !RoleSystemState.IsActive) return;

                var menu = _fMenu!.GetValue(__instance) as Transform;
                var containers = _fContainers!.GetValue(__instance) as Transform;
                var tabs = _fTabs!.GetValue(__instance) as IList<string>;
                var reg = _fReg!.GetValue(__instance);
                if (menu == null || containers == null || tabs == null) return;

                bool isHq = false;
                try
                {
                    if (reg != null)
                    {
                        var rt = reg.GetType();
                        bool rented = (bool)(rt.GetProperty("RentedByPlayer")?.GetValue(reg) ?? rt.GetField("RentedByPlayer")?.GetValue(reg) ?? false);
                        var typeName = rt.GetField("businessTypeName")?.GetValue(reg) as string;
                        isHq = rented && typeName == HqBusinessType;
                    }
                }
                catch { isHq = false; }

                var btn = menu.Find(TabId);

                if (!isHq)
                {
                    if (btn != null) btn.gameObject.SetActive(false);
                    return;
                }

                if (btn == null)
                {
                    btn = BuildTab(__instance, menu, containers);
                    if (btn == null) return;
                }

                if (!tabs.Contains(TabId))
                {
                    int at = tabs.IndexOf(AnchorTab);
                    if (at >= 0 && at + 1 <= tabs.Count) tabs.Insert(at + 1, TabId);
                    else tabs.Add(TabId);
                }
                btn.gameObject.SetActive(true);
            }
            catch (Exception e) { Debug.LogError("[StoreManager] SetUpTabs postfix swallowed: " + e); }
        }

        // ── build the button + container ────────────────────────────────────────
        private static Transform? BuildTab(object instance, Transform menu, Transform containers)
        {
            try
            {
                var proto = menu.Find(AnchorTab) ?? (menu.childCount > 0 ? menu.GetChild(menu.childCount - 1) : null);
                if (proto == null) { Fail("no menu button to clone"); return null; }

                var go = UnityEngine.Object.Instantiate(proto.gameObject, menu);
                go.name = TabId;
                go.transform.SetSiblingIndex(proto.GetSiblingIndex() + 1);

                var donorTmp = proto.GetComponent<TextMeshProUGUI>();
                UiKit.AdoptStyleFrom(donorTmp);

                // label
                var label = Loc.T("storemanager_bizmantab_menu", "Store Managers");
                var locComp = go.GetComponent<TextLocalizationComponent>();
                if (locComp != null)
                {
                    try { locComp.Key = "storemanager_bizmantab_menu"; } catch { }
                    try { locComp.SetValue(label); } catch { }
                }
                else
                {
                    var tmp = go.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = label;
                }

                // onClick -> stock private SetTab(id)
                var button = go.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        try { _mSetTab!.Invoke(instance, new object[] { TabId }); }
                        catch (Exception e) { Debug.LogError("[StoreManager] tab SetTab invoke threw: " + e); }
                    });
                }

                // content container: clone the vanilla PurchasingAgents container to inherit its
                // exact RectTransform + layout, then strip its scripts and children and host our view.
                var protoC = containers.Find(AnchorTab);
                GameObject cGo;
                if (protoC != null)
                {
                    cGo = UnityEngine.Object.Instantiate(protoC.gameObject, containers);
                    cGo.name = TabId;
                    cGo.SetActive(false);
                    // remove every game MonoBehaviour on the clone (their Awake never ran while inactive)
                    foreach (var comp in cGo.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (comp == null) continue;
                        try { UnityEngine.Object.DestroyImmediate(comp); } catch { }
                    }
                    // clear child objects — keep only the root RectTransform
                    for (int i = cGo.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.DestroyImmediate(cGo.transform.GetChild(i).gameObject);

                    // vanilla tabs fade in via a CanvasGroup — a clone inherits alpha 0 / no raycasts
                    var cg = cGo.GetComponent<CanvasGroup>();
                    if (cg != null) { cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true; }
                    // a nested Canvas with overrideSorting could bury it
                    var nested = cGo.GetComponent<Canvas>();
                    if (nested != null) UnityEngine.Object.DestroyImmediate(nested);

                    // force full-stretch inside `containers` (all vanilla tab containers fill it)
                    var rt = (RectTransform)cGo.transform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                    rt.localScale = Vector3.one;

                    Debug.Log("[StoreManager] tab clone components: " +
                        string.Join(", ", System.Array.ConvertAll(cGo.GetComponents<Component>(), c => c.GetType().Name)) +
                        $" (canvasGroup={(cg != null ? cg.alpha.ToString() : "none")})");
                }
                else
                {
                    cGo = new GameObject(TabId, typeof(RectTransform));
                    var crt = cGo.GetComponent<RectTransform>();
                    crt.SetParent(containers, false);
                    crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
                    crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
                    crt.localScale = Vector3.one;
                    cGo.SetActive(false);
                }
                cGo.AddComponent<StoreManagerTabView>();
                Debug.Log($"[StoreManager] tab container ready (cloned={protoC != null}).");

                Debug.Log("[StoreManager] built HQ 'Store Managers' tab.");
                return go.transform;
            }
            catch (Exception e) { Fail("BuildTab threw: " + e.Message); return null; }
        }
    }
}
