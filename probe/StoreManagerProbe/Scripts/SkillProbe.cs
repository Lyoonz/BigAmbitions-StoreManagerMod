#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  v3 SKILL PROBE — headless, one run. Answers the "must verify before coding
//  Phase A" list in docs/DESIGN-v3.md. Read-only except a temporary dict entry
//  + a throwaway employee, both removed at the end. DO NOT SAVE after a run.
//  Output: Player.log, prefix [SKILLPROBE].
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.SkillProbeInit))]

namespace StoreManagerProbe
{
    [ModEntryOnCityLoad]
    public sealed class SkillProbeInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext c) { _host = new GameObject("SMSkillProbe"); UnityEngine.Object.DontDestroyOnLoad(_host); _host.AddComponent<SkillProbeRunner>().Logger = c.Logger; return Task.CompletedTask; }
        public Task OnUnloadAsync() { if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; return Task.CompletedTask; }
    }

    public sealed class SkillProbeRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private const string MOD_SKILL = "sm:skill_storemanager";
        private Assembly[] _asms = Array.Empty<Assembly>();

        private void Start() => StartCoroutine(Run());
        private IEnumerator Run()
        {
            yield return new WaitForSeconds(6f);
            _asms = AppDomain.CurrentDomain.GetAssemblies();
            try { Probe(); } catch (Exception e) { L("FATAL " + e); }
        }

        private void Probe()
        {
            L("==================== SKILL PROBE START ====================");
            var skillData = FindType("SkillData");          // BigAmbitions.Characters.Skills.SkillData
            var skillHelper = FindType("SkillHelper");
            var empHelper = FindType("EmployeeHelper");
            if (skillData == null || skillHelper == null) { L("SkillData/SkillHelper type not found — abort"); return; }

            ProbeV3ModIfLoaded(skillHelper, empHelper);

            // ── 1. dump vanilla baselines (wage calibration) ────────────────────
            var getData = skillHelper.GetMethod("GetData", new[] { typeof(string) });
            foreach (var vs in new[] { "ba:skill_customerservice", "ba:skill_purchasingagent", "ba:skill_hrmanager", "ba:skill_logisticsmanager" })
            {
                var d = getData?.Invoke(null, new object[] { vs });
                L($"VANILLA {vs}: baseHourlyWage={FieldF(d, "baseHourlyWage")}  trainingCostMult={FieldF(d, "trainingCostMultiplier")}  secondarySkill='{FieldS(d, "secondarySkill")}'  icon28={(GetField(d, "icon28") == null ? "null" : "set")}  gradient={(GetField(d, "associatedColorGradient") == null ? "null" : "set")}");
            }

            // ── 2. build a runtime SkillData ───────────────────────────────────
            object? sd = null;
            try
            {
                sd = typeof(ScriptableObject).GetMethod("CreateInstance", new[] { typeof(Type) })!.Invoke(null, new object[] { skillData });
                SetField(sd, "skillName", MOD_SKILL);
                (sd as ScriptableObject)!.name = MOD_SKILL;
                SetField(sd, "baseHourlyWage", 24f);
                SetField(sd, "trainingCostMultiplier", 1f);
                SetField(sd, "secondarySkill", string.Empty);
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(new Color(0.20f, 0.55f, 0.90f), 0f), new GradientColorKey(new Color(0.20f, 0.55f, 0.90f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                SetField(sd, "associatedColorGradient", grad);
                SetField(sd, "possibleDealbreakers", new List<string>());
                var vec2i = FindType("Vector2Int") ?? typeof(Vector2Int);
                try { SetField(sd, "secondarySkillRange", Activator.CreateInstance(vec2i, 5, 20)); } catch { }
                L("built SkillData OK");
            }
            catch (Exception e) { L("SkillData build FAILED: " + e); return; }

            // ── 3. BuildTagCache + HasTag behaviour (the big unknown) ──────────
            var btc = skillData.GetMethod("BuildTagCache", Type.EmptyTypes) ?? skillData.BaseType?.GetMethod("BuildTagCache", Type.EmptyTypes);
            L("BuildTagCache method: " + (btc == null ? "NOT FOUND" : btc.DeclaringType?.Name + "." + btc.Name));
            try { btc?.Invoke(sd, null); L("BuildTagCache(): ok, no throw"); }
            catch (Exception e) { L("BuildTagCache(): THREW — " + e.GetType().Name + ": " + e.InnerException?.Message ?? e.Message); }

            var hasTagInt = skillData.GetMethod("HasTag", new[] { typeof(int) }) ?? skillData.BaseType?.GetMethod("HasTag", new[] { typeof(int) });
            var hasTagStr = skillData.GetMethod("HasTag", new[] { typeof(string) }) ?? skillData.BaseType?.GetMethod("HasTag", new[] { typeof(string) });
            foreach (var tag in new[] { "hashoursperweekdemand", "forcefulltime", "canhavejobdemands", "affectssecurity" })
            {
                try { L($"  HasTag(\"{tag}\") = {hasTagStr?.Invoke(sd, new object[] { tag })}"); }
                catch (Exception e) { L($"  HasTag(\"{tag}\") THREW: {e.InnerException?.GetType().Name ?? e.GetType().Name}"); }
            }

            // ── 4. inject into SkillHelper.Skills + verify GetData ─────────────
            var skillsField = skillHelper.GetField("Skills", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            L("SkillHelper.Skills field: " + (skillsField == null ? "NOT FOUND (name changed?)" : skillsField.FieldType.Name));
            var dict = skillsField?.GetValue(null) as IDictionary;
            bool injected = false;
            if (dict != null && sd != null)
            {
                try { dict[MOD_SKILL] = sd; injected = true; L("injected into Skills dict"); }
                catch (Exception e) { L("dict inject failed: " + e.Message); }
            }
            var back = getData?.Invoke(null, new object[] { MOD_SKILL });
            L($"GetData(\"{MOD_SKILL}\") after inject = {(back == null ? "NULL" : "the SkillData")}");

            // ── 5. wage ───────────────────────────────────────────────────────
            var calcWage = empHelper?.GetMethod("CalculateHourlyWageForSkill", new[] { typeof(string), typeof(float) });
            try { L($"CalculateHourlyWageForSkill(\"{MOD_SKILL}\", 20) = {calcWage?.Invoke(null, new object[] { MOD_SKILL, 20f })}  (target ~30)"); }
            catch (Exception e) { L("wage calc THREW: " + (e.InnerException?.Message ?? e.Message)); }
            try { L($"CalculateHourlyWageForSkill(\"{MOD_SKILL}\", 50) = {calcWage?.Invoke(null, new object[] { MOD_SKILL, 50f })}"); } catch { }

            // ── 6. fake employee holding ONLY the mod skill; hourly-security path ─
            object? ei = null;
            MethodInfo? hasAnyTagStr = null;
            try
            {
                ei = empHelper!.GetMethod("CreateAIEmployeeInstance", new[] { typeof(string) })!.Invoke(null, new object[] { MOD_SKILL });
                hasAnyTagStr = ei!.GetType().GetMethod("HasAnySkillWithTag", new[] { typeof(string) });
                var getPrim = ei.GetType().GetMethod("GetPrimarySkill");
                L($"  employee.GetPrimarySkill() = '{getPrim?.Invoke(ei, null)}'  (skillData present)");
                foreach (var t in new[] { "affectssecurity", "forcefulltime" })
                {
                    try { L($"  employee.HasAnySkillWithTag(\"{t}\") = {hasAnyTagStr?.Invoke(ei, new object[] { t })}"); }
                    catch (Exception e) { L($"  employee.HasAnySkillWithTag(\"{t}\") THREW: {e.InnerException?.GetType().Name}"); }
                }
            }
            catch (Exception e) { L("fake-employee probe threw: " + e.Message); }

            // ── 7. SIMULATE folder-delete: SkillData gone, employee still carries the skill ─
            //     This is check #3 — every vanilla site that would run on the next save LOAD.
            if (injected && dict != null)
            {
                dict.Remove(MOD_SKILL);
                L("--- SIMULATE mod folder-delete: SkillData removed, employee keeps sm:skill_storemanager as PRIMARY ---");

                Probe1("SkillHelper.GetData(string)", () => getData?.Invoke(null, new object[] { MOD_SKILL }));
                Probe1("EmployeeHelper.CalculateHourlyWageForSkill(string,20)  [EmployeeHelper.cs:361 .baseHourlyWage]",
                       () => calcWage?.Invoke(null, new object[] { MOD_SKILL, 20f }));

                // GetData(Skill) overload + CalculateHourlyWageForSkill(Skill) — the exact path CompatibilityFixesEA03 runs on load
                var skillStruct = FindType("Skill");
                object? skillVal = null;
                if (skillStruct != null)
                {
                    try { skillVal = Activator.CreateInstance(skillStruct); skillStruct.GetField("name")?.SetValue(skillVal, MOD_SKILL); } catch { }
                }
                var getDataSkill = skillHelper.GetMethod("GetData", new[] { skillStruct ?? typeof(object) });
                var calcWageSkill = empHelper?.GetMethod("CalculateHourlyWageForSkill", new[] { skillStruct ?? typeof(object) });
                if (skillVal != null)
                {
                    Probe1("SkillHelper.GetData(Skill)", () => getDataSkill?.Invoke(null, new[] { skillVal }));
                    Probe1("EmployeeHelper.CalculateHourlyWageForSkill(Skill)  [EmployeeHelper.cs:353 .baseHourlyWage]",
                           () => calcWageSkill?.Invoke(null, new[] { skillVal }));
                }

                // employee-level tag reads after removal (EmployeeInstance.cs:230 — null-safe? returns true?)
                if (ei != null && hasAnyTagStr != null)
                    foreach (var t in new[] { "affectssecurity", "forcefulltime", "canhavejobdemands" })
                        Probe1($"employee.HasAnySkillWithTag(\"{t}\")  [post-removal]", () => hasAnyTagStr.Invoke(ei, new object[] { t }));

                // the compat fix itself, invoked directly if we can find it
                var compatFix = FindType("EnsureAllFullTimeEmployeesHaveFullTimeDemand");
                if (compatFix != null)
                {
                    L("  found CompatibilityFixesEA03.EnsureAllFullTimeEmployeesHaveFullTimeDemand — attempting direct run");
                    try
                    {
                        var inst = Activator.CreateInstance(compatFix);
                        var run = compatFix.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name is "Apply" or "Run" or "Execute" or "Fix");
                        if (run != null) { run.Invoke(inst, run.GetParameters().Length == 0 ? null : new object?[run.GetParameters().Length]); L("  compat fix ran without throwing"); }
                        else L("  compat fix entry method not found by name — signature: " + string.Join(", ", compatFix.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).Distinct()));
                    }
                    catch (Exception e) { L("  compat fix THREW: " + (e.InnerException?.GetType().Name + ": " + e.InnerException?.Message ?? e.Message)); }
                }
                else L("  CompatibilityFixesEA03 fix type not found by simple name (may be nested) — rely on the GetData(Skill) result above");

                L("  VERDICT INPUT: if any line above says NPE/throw, an sm:skill_* PRIMARY bricks a folder-deleted save -> Option 2, OR the onSaveGame/onNewDay data-repair must re-skill before serialize.");
            }

            // ── 8. AI rivals + OfficeBusinessSimulator ─────────────────────────
            try
            {
                var sgm = FindType("SaveGameManager");
                var cur = sgm?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var brs = cur?.GetType().GetProperty("BuildingRegistrations")?.GetValue(cur) as IEnumerable;
                int rivalHq = 0, playerHq = 0;
                if (brs != null)
                    foreach (var b in brs)
                    {
                        var bt = b.GetType();
                        var type = bt.GetField("businessTypeName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(b)?.ToString();
                        if (type != "ba:businesstype_headquarters") continue;
                        bool rented = (bool?)bt.GetProperty("RentedByPlayer")?.GetValue(b) == true;
                        if (rented) playerHq++; else rivalHq++;
                    }
                L($"HQ registrations: player={playerHq} rival/AI={rivalHq}  (rival>0 => appending to employeePrimarySkills contaminates AI generation)");
            }
            catch (Exception e) { L("HQ ownership check threw: " + e.Message); }
            L("OfficeBusinessSimulator present: " + (FindType("OfficeBusinessSimulator") != null));

            L("==================== SKILL PROBE END — DO NOT SAVE ====================");
        }

        // ── v3 mod, if deployed alongside the probe: is the custom skill actually live? ──
        private void ProbeV3ModIfLoaded(Type skillHelper, Type? empHelper)
        {
            var boot = FindTypeFull("StoreManager.Interop.Harmony.HarmonyBootstrap");
            var roleState = FindTypeFull("StoreManager.Runtime.RoleSystemState");
            var reg = FindTypeFull("StoreManager.Interop.SkillRegistry");
            if (boot == null && roleState == null) { L("V3: StoreManager v3 mod not loaded — skipping live-skill checks"); return; }

            L("---- V3 LIVE-SKILL CHECKS ----");
            try { L("V3 HarmonyBootstrap.Patched = " + boot?.GetProperty("Patched")?.GetValue(null)
                     + "  LastError=" + boot?.GetProperty("LastError")?.GetValue(null)); } catch (Exception e) { L("V3 boot read threw: " + e.Message); }
            try { L("V3 RoleSystemState.State = " + roleState?.GetProperty("State")?.GetValue(null)
                     + "  Reason=" + roleState?.GetProperty("Reason")?.GetValue(null)); } catch (Exception e) { L("V3 roleState read threw: " + e.Message); }
            try { L("V3 SkillRegistry.SkillBuilt=" + reg?.GetProperty("SkillBuilt")?.GetValue(null)
                     + " InjectedThisSession=" + reg?.GetProperty("InjectedThisSession")?.GetValue(null)
                     + " IsRegistered=" + reg?.GetMethod("IsRegistered")?.Invoke(null, null)); } catch (Exception e) { L("V3 reg read threw: " + e.Message); }

            var getData = skillHelper.GetMethod("GetData", new[] { typeof(string) });
            try { L("V3 SkillHelper.GetData(\"sm:skill_storemanager\") = " +
                    (getData?.Invoke(null, new object[] { "sm:skill_storemanager" }) == null ? "NULL (skill NOT live!)" : "the SkillData")); }
            catch (Exception e) { L("V3 GetData threw: " + e.Message); }

            var calcWage = empHelper?.GetMethod("CalculateHourlyWageForSkill", new[] { typeof(string), typeof(float) });
            try { L("V3 wage(sm,20)=" + calcWage?.Invoke(null, new object[] { "sm:skill_storemanager", 20f })
                     + "  wage(sm,50)=" + calcWage?.Invoke(null, new object[] { "sm:skill_storemanager", 50f }) + "  (target ~30)"); }
            catch (Exception e) { L("V3 wage threw: " + e.Message); }

            var desk = FindTypeFull("StoreManager.Interop.HqDeskAccess");
            try { L("V3 HqDeskAccess.Applied=" + desk?.GetProperty("Applied")?.GetValue(null)
                     + " AllDesksReady=" + desk?.GetMethod("AllDesksReady")?.Invoke(null, null)); }
            catch (Exception e) { L("V3 desk read threw: " + e.Message); }
            L("---- end V3 checks ----");
        }

        private Type? FindTypeFull(string full) => _asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.FullName == full);

        // helpers
        private void Probe1(string label, Func<object?> act)
        {
            try { var r = act(); L($"  OK   {label}  => {(r == null ? "null" : r)}"); }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie ? tie.InnerException : e;
                L($"  NPE/THROW  {label}  => {inner?.GetType().Name}: {inner?.Message}");
            }
        }

        private Type? FindType(string n) => _asms.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == n && (t.Namespace == null || !t.Namespace.StartsWith("System")));
        private static object? GetField(object? o, string f) { if (o == null) return null; try { return o.GetType().GetField(f)?.GetValue(o); } catch { return null; } }
        private static string FieldF(object? o, string f) { var v = GetField(o, f); return v?.ToString() ?? "?"; }
        private static string FieldS(object? o, string f) { var v = GetField(o, f); return v?.ToString() ?? ""; }
        private static void SetField(object? o, string f, object? v) { o?.GetType().GetField(f)?.SetValue(o, v); }
        private void L(string s) { Logger?.Info(s); Debug.Log("[SKILLPROBE] " + s); }
    }
}
