#nullable enable
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BigAmbitions.Characters.Skills;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Owns the single <see cref="global::HarmonyLib.Harmony"/> instance for the mod. Patching is
    /// the load-bearing part of v3 (D15): without the <c>SkillHelper.OnSkillDataLoaded</c> hook a
    /// save that already holds an <c>sm:skill_*</c> employee would NPE in the load-time compat
    /// fixes before <c>[ModEntryOnCityLoad]</c> ever runs. Every step is guarded — a patch failure
    /// drops the role system to a safe state (see <see cref="Runtime.RoleSystemState"/>), it never
    /// throws out of a lifecycle hook.
    /// </summary>
    public static class HarmonyBootstrap
    {
        public const string HarmonyId = "com.storemanager.mod";

        private static global::HarmonyLib.Harmony? _harmony;
        public static bool Patched { get; private set; }
        public static string? LastError { get; private set; }

        /// <summary>Idempotent. Returns true when every patch in the mod applied.</summary>
        public static bool EnsurePatched()
        {
            if (Patched) return true;
            try
            {
                _harmony ??= new global::HarmonyLib.Harmony(HarmonyId);
                _harmony.PatchAll(typeof(HarmonyBootstrap).Assembly);

                // Verify by inspecting Harmony's patch registry — the patch bodies only *run* later
                // (OnSkillDataLoaded fires on save-load), so we can't wait for them to flip a flag.
                bool skillLoad = IsPatchedByUs(AccessTools.Method(typeof(SkillHelper), nameof(SkillHelper.OnSkillDataLoaded)));
                bool getDataStr = IsPatchedByUs(AccessTools.Method(typeof(SkillHelper), nameof(SkillHelper.GetData), new[] { typeof(string) }));
                bool getDataSkill = IsPatchedByUs(AccessTools.Method(typeof(SkillHelper), nameof(SkillHelper.GetData), new[] { typeof(Skill) }));

                Patched = skillLoad && getDataStr;   // GetData(Skill) is a bonus, not required
                LastError = Patched ? null
                    : $"OnSkillDataLoaded={skillLoad}, GetData(string)={getDataStr}, GetData(Skill)={getDataSkill}";

                // Non-load-bearing: the HQ BizMan tab. Failure just disables that tab.
                try
                {
                    if (BizManTabPatch.Resolve()) BizManTabPatch.EnsurePatched(_harmony);
                }
                catch (Exception e) { Debug.LogError("[StoreManager] BizMan tab patch setup threw: " + e); }

                if (Patched) Debug.Log($"[StoreManager] Harmony patches applied (GetData(Skill)={getDataSkill}, HQ tab={BizManTabPatch.Patched}).");
                else Debug.LogError("[StoreManager] Harmony patch incomplete: " + LastError);
                return Patched;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogError("[StoreManager] Harmony bootstrap threw: " + e);
                return false;
            }
        }

        public static void Unpatch()
        {
            try { _harmony?.UnpatchSelf(); }
            catch (Exception e) { Debug.LogWarning("[StoreManager] UnpatchSelf failed: " + e.Message); }
            finally { Patched = false; }
        }

        private static bool IsPatchedByUs(MethodBase? method)
        {
            if (method == null) return false;
            try
            {
                var info = global::HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null) return false;
                return info.Prefixes.Any(p => p.owner == HarmonyId)
                    || info.Postfixes.Any(p => p.owner == HarmonyId);
            }
            catch { return false; }
        }
    }
}
