#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Entities;
using Helpers;
using BigAmbitions.Characters.Skills;

namespace StoreManager.Interop
{
    /// <summary>
    /// Operations on employees that carry the custom <c>sm:skill_storemanager</c> skill:
    /// recruiting one, and — for a clean uninstall / a degraded session — re-skilling them back to
    /// the vanilla fallback so the save never holds an orphan primary skill (D15).
    /// </summary>
    public static class RoleEmployees
    {
        public const string ManagerSkill = SkillRegistry.StoreManagerSkill;
        public const string VanillaFallback = SkillRegistry.VanillaFallbackSkill;
        private const float StartingSkill = 20f;

        // ── recruit ─────────────────────────────────────────────────────────────
        public readonly struct HireResult
        {
            public bool Ok { get; }
            public string Message { get; }
            public string EmployeeId { get; }
            public HireResult(bool ok, string message, string id) { Ok = ok; Message = message; EmployeeId = id; }
        }

        /// <summary>
        /// Generate a Store Manager candidate and hire them directly onto the HQ. Skips the
        /// Recruitment Agency dropdown (which would need the deferred <c>employeePrimarySkills</c>
        /// mutation). The player then schedules them at an HQ desk and adopts them in the panel.
        /// </summary>
        public static HireResult Recruit(string hqAddress)
        {
            var addr = GameApi.HqAddressObject(hqAddress);
            if (addr == null) return new HireResult(false, "couldn't resolve the HQ address", "");

            EmployeeInstance? cand;
            try
            {
                cand = RecruitmentHelper.GenerateCandidate(ManagerSkill, StartingSkill, (Address)addr, null, 0f);
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] GenerateCandidate threw: " + e);
                return new HireResult(false, "candidate generation failed: " + e.Message, "");
            }
            if (cand == null) return new HireResult(false, "candidate generation returned nothing", "");

            try
            {
                var cd = cand.characterData;
                if (cd != null && string.IsNullOrEmpty(cd.name)) cd.name = "Store Manager";
            }
            catch { }

            // best-effort vanilla hire (finishes negotiation + books the signing cost)
            try
            {
                TryFinishNegotiation(cand);
                EmployeeHelper.HireCandidate(cand);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoreManager] HireCandidate threw, falling back to direct roster insert: " + e.Message);
            }

            // guarantee it's in the live roster and assigned to the HQ (same insert the SelfTest uses)
            try
            {
                if (EmployeeHelper.GetEmployeeById(cand.id, false) == null)
                {
                    EmployeeHelper.GetEmployeeInstances().Add(cand);
                    EmployeeHelper.EmployeeInstancesDictionary[cand.id] = cand;
                }
                if (cand.assignedAddress == null)
                    typeof(EmployeeInstance).GetField("assignedAddress")?.SetValue(cand, addr);
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] roster insert failed: " + e);
                return new HireResult(false, "couldn't add the manager to your roster: " + e.Message, "");
            }

            float wage = SafeWage();
            return new HireResult(true,
                $"{cand.characterData?.name ?? "A Store Manager"} hired at the HQ (~${wage:N0}/h). " +
                "Assign them to the HQ in My Employees, then pick them in the panel.",
                cand.id);
        }

        private static void TryFinishNegotiation(EmployeeInstance cand)
        {
            try
            {
                var m = typeof(EmployeeHelper).GetMethod("FinishPendingNegotiation",
                    BindingFlags.Public | BindingFlags.Static);
                m?.Invoke(null, new object[] { cand, true });
            }
            catch { /* not all candidates have an open negotiation */ }
        }

        private static float SafeWage()
        {
            try { return EmployeeHelper.CalculateHourlyWageForSkill(ManagerSkill, StartingSkill); }
            catch { return 0f; }
        }

        // ── enumerate / re-skill ────────────────────────────────────────────────
        public static List<EmployeeInstance> AllWithManagerSkill()
        {
            var result = new List<EmployeeInstance>();
            try
            {
                foreach (var e in EmployeeHelper.GetEmployeeInstances())
                {
                    if (e == null) continue;
                    try { if (SkillNames(e).Contains(ManagerSkill)) result.Add(e); } catch { }
                }
            }
            catch { }
            return result;
        }

        public static int ReskillAllToVanilla()
        {
            int changed = 0;
            foreach (var e in AllWithManagerSkill())
                if (ReskillOne(e)) changed++;
            if (changed > 0) Debug.Log($"[StoreManager] re-skilled {changed} manager(s) to {VanillaFallback}.");
            return changed;
        }

        /// <summary>Rename this employee's <c>sm:skill_storemanager</c> entry to the vanilla fallback in place.</summary>
        public static bool ReskillOne(EmployeeInstance e)
        {
            try
            {
                var list = SkillListOf(e);
                if (list == null) return false;

                bool hasFallbackAlready = false;
                int idx = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    var name = SkillNameAt(list, i);
                    if (name == VanillaFallback) hasFallbackAlready = true;
                    if (name == ManagerSkill && idx < 0) idx = i;
                }
                if (idx < 0) return false;

                if (hasFallbackAlready)
                {
                    list.RemoveAt(idx);   // avoid a duplicate primary
                }
                else
                {
                    var s = list[idx];
                    // Skill is a struct with a public string field 'name'
                    var f = s!.GetType().GetField("name");
                    if (f == null) return false;
                    f.SetValue(s, VanillaFallback);
                    list[idx] = s;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[StoreManager] ReskillOne failed: " + ex.Message);
                return false;
            }
        }

        // ── skill-list reflection (EmployeeInstance.skills is [Obsolete] but still the store) ──
        private static IList? SkillListOf(EmployeeInstance e)
        {
            var f = typeof(EmployeeInstance).GetField("skills",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(e) as IList;
        }

        private static IEnumerable<string> SkillNames(EmployeeInstance e)
        {
            var list = SkillListOf(e);
            if (list == null) yield break;
            for (int i = 0; i < list.Count; i++)
            {
                var n = SkillNameAt(list, i);
                if (n != null) yield return n;
            }
        }

        private static string? SkillNameAt(IList list, int i)
        {
            try
            {
                var s = list[i];
                return s?.GetType().GetField("name")?.GetValue(s) as string;
            }
            catch { return null; }
        }
    }
}
