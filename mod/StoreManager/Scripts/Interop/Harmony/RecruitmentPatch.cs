#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using StoreManager.Runtime;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Makes the custom "Store Manager" skill selectable in the vanilla Recruitment Agency dialog
    /// (phone → Recruitment Agency) exactly like Purchasing Agent / HR — so hiring one goes through
    /// the normal City Workforce campaign flow, not a mod shortcut.
    ///
    /// <para>Postfix on <c>UI.Dialog.RecruitmentSettings.SelectBusiness</c>: after the game computes
    /// <c>businessSkills = employeePrimarySkills ∩ availableEmployeeSkills</c>, we append
    /// <c>sm:skill_storemanager</c> when the chosen business is the player's HQ and repopulate the
    /// skill dropdown. Nothing in the shared <c>BusinessType</c> / agency settings is mutated, so
    /// there is zero AI-staffing or rival-defense contamination.</para>
    /// </summary>
    public static class RecruitmentPatch
    {
        public const string TypeName = "UI.Dialog.RecruitmentSettings";
        private const string HqBusinessType = "ba:businesstype_headquarters";

        public static bool Patched { get; private set; }
        public static string? Disabled { get; private set; }

        private static Type? _t;
        private static FieldInfo? _fSelectedBusiness, _fBusinessSkills, _fSkillsDropdown;
        private static MethodInfo? _mSetOptions;

        public static bool Resolve()
        {
            if (_t != null) return _t != null && Disabled == null;
            try
            {
                _t = AccessTools.TypeByName(TypeName);
                if (_t == null) { Disabled = "RecruitmentSettings type not found"; return false; }
                _fSelectedBusiness = AccessTools.Field(_t, "selectedBusiness");
                _fBusinessSkills = AccessTools.Field(_t, "businessSkills");
                _fSkillsDropdown = AccessTools.Field(_t, "skillsDropdown");
                if (_fSelectedBusiness == null || _fBusinessSkills == null || _fSkillsDropdown == null)
                {
                    Disabled = $"fields changed (selectedBusiness={_fSelectedBusiness != null}, businessSkills={_fBusinessSkills != null}, skillsDropdown={_fSkillsDropdown != null})";
                    return false;
                }
                var dropType = _fSkillsDropdown.FieldType;
                _mSetOptions = dropType.GetMethod("SetOptions", new[] { typeof(List<string>), typeof(bool), typeof(int), typeof(List<string>) })
                            ?? dropType.GetMethod("SetOptions");
                return true;
            }
            catch (Exception e) { Disabled = "resolve threw: " + e.Message; return false; }
        }

        public static void EnsurePatched(global::HarmonyLib.Harmony harmony)
        {
            if (Patched || _t == null || Disabled != null) return;
            try
            {
                var target = AccessTools.Method(_t, "SelectBusiness");
                if (target == null) { Disabled = "SelectBusiness not found"; return; }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(RecruitmentPatch), nameof(SelectBusiness_Postfix)));
                Patched = true;
                Debug.Log("[StoreManager] Recruitment Agency patch applied.");
            }
            catch (Exception e) { Disabled = "patch threw: " + e.Message; }
        }

        private static void SelectBusiness_Postfix(object __instance)
        {
            try
            {
                if (!RoleSystemState.IsActive) return;

                var reg = _fSelectedBusiness!.GetValue(__instance);
                if (reg == null) return;
                var rt = reg.GetType();
                var typeName = rt.GetField("businessTypeName")?.GetValue(reg) as string;
                bool rented = (bool)(rt.GetProperty("RentedByPlayer")?.GetValue(reg) ?? rt.GetField("RentedByPlayer")?.GetValue(reg) ?? false);
                if (typeName != HqBusinessType || !rented) return;

                if (_fBusinessSkills!.GetValue(__instance) is not List<string> skills) return;
                if (skills.Contains(SkillRegistry.StoreManagerSkill)) return;
                skills.Add(SkillRegistry.StoreManagerSkill);

                var dropdown = _fSkillsDropdown!.GetValue(__instance);
                if (dropdown != null && _mSetOptions != null)
                {
                    var ps = _mSetOptions.GetParameters();
                    object[] args = ps.Length switch
                    {
                        1 => new object[] { new List<string>(skills) },
                        >= 3 => Fill(ps, new List<string>(skills)),
                        _ => new object[] { new List<string>(skills), true },
                    };
                    _mSetOptions.Invoke(dropdown, args);
                }
            }
            catch (Exception e) { Debug.LogError("[StoreManager] recruitment postfix swallowed: " + e); }
        }

        private static object[] Fill(ParameterInfo[] ps, List<string> opts)
        {
            var a = new object[ps.Length];
            a[0] = opts;
            for (int i = 1; i < ps.Length; i++)
                a[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue! : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType)! : null!);
            return a;
        }
    }
}
