#nullable enable
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BigAmbitions.Characters.Skills;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Keeps <c>sm:skill_storemanager</c> alive across the game's skill reloads and guards the
    /// ~18 unguarded <c>SkillHelper.GetData(...)</c> dereferences.
    ///
    /// <para><b>Prefix on <c>OnSkillDataLoaded</c></b> is the load-bearing hook: it fires at
    /// <c>SaveGameManager</c> load time, <i>before</i> the save is deserialized and before the
    /// <c>CompatibilityFixesEA03</c> passes that would otherwise NPE on a hired manager's now-orphan
    /// primary skill. Adding to the incoming list means the game's own <c>Skills.Add</c> registers
    /// it — no post-hoc dictionary poke needed for the happy path.</para>
    ///
    /// <para><b>Postfix on both <c>GetData</c> overloads</b> is defence in depth: any residual miss
    /// returns the cached SkillData instead of null.</para>
    /// </summary>
    public static class SkillHelperPatches
    {
        public static bool OnSkillDataLoadedPatched { get; private set; }
        public static bool GetDataPatched { get; private set; }

        [HarmonyPatch(typeof(SkillHelper), nameof(SkillHelper.OnSkillDataLoaded))]
        private static class OnSkillDataLoaded_Patch
        {
            private static void Prefix(IList<SkillData> skillData)
            {
                OnSkillDataLoadedPatched = true;
                SkillRegistry.AddToLoadList(skillData);
            }

            // Belt: if the game ever changes OnSkillDataLoaded to not iterate the list we passed,
            // the postfix still forces the entry in.
            private static void Postfix()
            {
                SkillRegistry.EnsureInjected();
            }
        }

        [HarmonyPatch(typeof(SkillHelper), nameof(SkillHelper.GetData), new[] { typeof(string) })]
        private static class GetData_String_Patch
        {
            private static void Postfix(string skillName, ref SkillData __result)
            {
                GetDataPatched = true;
                var mod = SkillRegistry.Skill;
                if (__result == null && mod != null && skillName == SkillRegistry.StoreManagerSkill)
                    __result = mod;
            }
        }

        [HarmonyPatch(typeof(SkillHelper), nameof(SkillHelper.GetData), new[] { typeof(Skill) })]
        private static class GetData_Skill_Patch
        {
            private static void Postfix(Skill skill, ref SkillData __result)
            {
                var mod = SkillRegistry.Skill;
                if (__result == null && mod != null && skill.name == SkillRegistry.StoreManagerSkill)
                    __result = mod;
            }
        }
    }
}
