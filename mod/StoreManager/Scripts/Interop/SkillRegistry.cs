#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using BigAmbitions.Characters.Skills;

namespace StoreManager.Interop
{
    /// <summary>
    /// Builds the mod's custom <see cref="SkillData"/> (<c>sm:skill_storemanager</c>) at runtime and
    /// keeps it present in <see cref="SkillHelper"/>'s private <c>Skills</c> dictionary, which the
    /// game wipes and rebuilds on every save-load / new-game. Injection happens from a Harmony
    /// prefix on <c>SkillHelper.OnSkillDataLoaded</c> (primary) and a <c>[ModEntryOnCityLoad]</c>
    /// backstop — both call <see cref="EnsureInjected"/>, which is idempotent.
    ///
    /// D15: the skill is deliberately tagless (probe-verified: <c>BuildTagCache()</c> does not
    /// throw and <c>HasTag(...)</c> returns false) — no forced full-time, no job demands, no
    /// hours-per-week requirement. <c>baseHourlyWage = 46</c> lands a hire near $30 given the
    /// game's ~0.5 salary multiplier (probe: vanilla managers use base 30).
    /// </summary>
    public static class SkillRegistry
    {
        public const string StoreManagerSkill = "sm:skill_storemanager";
        public const string VanillaFallbackSkill = "ba:skill_purchasingagent";
        private const float BaseHourlyWage = 46f;

        private static SkillData? _skill;
        private static bool _buildAttempted;
        private static IDictionary? _skillsDict;      // SkillHelper.Skills, non-generic view
        private static bool _reflectionFailed;

        /// <summary>True once the SkillData is built and its <c>BuildTagCache()</c> succeeded.</summary>
        public static bool SkillBuilt { get; private set; }

        /// <summary>True if the last <see cref="EnsureInjected"/> put the skill into the live dict.</summary>
        public static bool InjectedThisSession { get; private set; }

        public static SkillData? Skill
        {
            get
            {
                if (_skill != null || _buildAttempted) return _skill;
                _buildAttempted = true;
                _skill = TryBuild();
                return _skill;
            }
        }

        // ── build ───────────────────────────────────────────────────────────────
        private static SkillData? TryBuild()
        {
            try
            {
                var sd = ScriptableObject.CreateInstance<SkillData>();
                sd.name = StoreManagerSkill;
                sd.skillName = StoreManagerSkill;
                sd.baseHourlyWage = BaseHourlyWage;
                sd.trainingCostMultiplier = 1f;
                sd.secondarySkill = string.Empty;               // MUST be empty — kills the secondary-skill branch
                sd.secondarySkillRange = new Vector2Int(5, 20);
                sd.possibleDealbreakers = new List<string>();
                sd.icon28 = null;                                // one cosmetic consumer, null-tolerated
                sd.associatedColorGradient = BuildGradient();    // MUST be non-null — ScheduleHelper calls .Evaluate() unconditionally

                try
                {
                    sd.BuildTagCache();
                    SkillBuilt = true;
                }
                catch (Exception e)
                {
                    Debug.LogError("[StoreManager] SkillData.BuildTagCache() threw — role system will stay dormant: " + e);
                    SkillBuilt = false;
                    return null;
                }

                Debug.Log($"[StoreManager] built custom skill '{StoreManagerSkill}' (baseHourlyWage={BaseHourlyWage}).");
                return sd;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] failed to build custom SkillData: " + e);
                SkillBuilt = false;
                return null;
            }
        }

        private static Gradient BuildGradient()
        {
            var g = new Gradient();
            var c = new Color(0.20f, 0.55f, 0.90f);
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // ── inject ──────────────────────────────────────────────────────────────
        /// <summary>
        /// Ensure the custom skill is in <c>SkillHelper.Skills</c>. Safe to call repeatedly and from
        /// any thread the game calls us on. Returns true if the skill is present afterwards.
        /// </summary>
        public static bool EnsureInjected()
        {
            var skill = Skill;
            if (skill == null) { InjectedThisSession = false; return false; }

            var dict = SkillsDict();
            if (dict == null) { InjectedThisSession = false; return false; }

            try
            {
                if (!dict.Contains(StoreManagerSkill))
                    dict[StoreManagerSkill] = skill;
                InjectedThisSession = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] skill injection failed: " + e.Message);
                InjectedThisSession = false;
                return false;
            }
        }

        /// <summary>Add the skill to a list the game is about to hand to <c>OnSkillDataLoaded</c>.</summary>
        public static void AddToLoadList(IList<SkillData> list)
        {
            var skill = Skill;
            if (skill == null || list == null) return;
            try
            {
                foreach (var s in list) if (s != null && s.skillName == StoreManagerSkill) return;
                list.Add(skill);
            }
            catch (Exception e) { Debug.LogWarning("[StoreManager] AddToLoadList failed: " + e.Message); }
        }

        public static bool IsRegistered()
        {
            var dict = SkillsDict();
            try { return dict != null && dict.Contains(StoreManagerSkill); } catch { return false; }
        }

        private static IDictionary? SkillsDict()
        {
            if (_skillsDict != null) return _skillsDict;
            if (_reflectionFailed) return null;
            try
            {
                var f = typeof(SkillHelper).GetField("Skills",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                _skillsDict = f?.GetValue(null) as IDictionary;
                if (_skillsDict == null) { _reflectionFailed = true; Debug.LogError("[StoreManager] SkillHelper.Skills not reachable — role system dormant."); }
                return _skillsDict;
            }
            catch (Exception e)
            {
                _reflectionFailed = true;
                Debug.LogError("[StoreManager] SkillHelper.Skills reflection threw: " + e.Message);
                return null;
            }
        }

        /// <summary>Structural self-check for the kill-switch — every handle the role system needs.</summary>
        public static bool StructuralCheck(out string detail)
        {
            var problems = new List<string>();
            if (SkillsDict() == null) problems.Add("SkillHelper.Skills unreachable");
            if (typeof(SkillHelper).GetMethod("OnSkillDataLoaded", BindingFlags.Public | BindingFlags.Static) == null)
                problems.Add("OnSkillDataLoaded missing");
            if (typeof(SkillHelper).GetMethod("GetData", new[] { typeof(string) }) == null)
                problems.Add("GetData(string) missing");
            if (Skill == null) problems.Add("SkillData build failed");
            detail = problems.Count == 0 ? "ok" : string.Join("; ", problems);
            return problems.Count == 0;
        }
    }
}
