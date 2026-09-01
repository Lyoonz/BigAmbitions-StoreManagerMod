#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  PHASE-B REFLECTION DUMP — de-obfuscate the real API of the meta-role surface.
//  The decompile collapses many members to `n`; this dumps the SHIPPING assembly's
//  real signatures + verifies the design's key assumptions. Runs headless on city load.
//  Output: Player.log, lines prefixed [SMDUMP].
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.ReflectionDumpInit))]

namespace StoreManagerProbe
{
    [ModEntryOnCityLoad]
    public sealed class ReflectionDumpInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext context)
        {
            _host = new GameObject("SMReflectionDump");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<ReflectionDumpRunner>().Logger = context.Logger;
            return Task.CompletedTask;
        }
        public Task OnUnloadAsync() { if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; return Task.CompletedTask; }
    }

    public sealed class ReflectionDumpRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private void Start() => StartCoroutine(Run());

        private System.Collections.IEnumerator Run()
        {
            yield return new WaitForSeconds(4f);
            try { DoDump(); } catch (Exception e) { L("FATAL " + e); }
        }

        private Assembly[] _asms = Array.Empty<Assembly>();

        private void DoDump()
        {
            _asms = AppDomain.CurrentDomain.GetAssemblies();
            L("==================== SM REFLECTION DUMP START ====================");
            L("assemblies: " + string.Join(", ", _asms.Select(a => a.GetName().Name).Where(n => n != null && (n.Contains("BigAmbitions") || n.Contains("BAMod")))));

            // 1. GameInstance.modData
            var gi = FindType("GameInstance");
            L("--- GameInstance ---");
            if (gi != null)
            {
                DumpFields(gi, f => f.FieldType.Name.Contains("Dictionary") || f.Name.ToLower().Contains("mod") || f.Name.ToLower().Contains("custom") || f.Name.ToLower().Contains("data"));
                var md = gi.GetField("modData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                L("modData field: " + (md == null ? "NOT FOUND" : md.FieldType.FullName));
            }

            // 2. manager-plan surface — full member dump
            foreach (var tn in new[] {
                "HrManagerPlan","LogisticsManagerPlan","LogisticsManagerPlanDestination","PricingManagerPlan",
                "HrManagerHelper","LogisticsManagerHelper","PricingManagerHelper",
                "ScheduleHelper","RecruitmentHelper","DeliveryHelper","BuildingHelper",
                "SkillHelper","EmployeeHelper" })
            {
                var t = FindType(tn);
                if (t == null) { L($"--- {tn}: NOT FOUND ---"); continue; }
                L($"--- {t.FullName} ---");
                DumpMembers(t);
            }

            // 3. EmployeeInstance skill/schedule methods
            var ei = FindType("EmployeeInstance");
            if (ei != null)
            {
                L("--- EmployeeInstance (skill/schedule/assign members) ---");
                foreach (var m in ei.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => { var n = m.Name.ToLower(); return n.Contains("skill") || n.Contains("shift") || n.Contains("schedul") || n.Contains("assign") || n.Contains("hasskill") || n.Contains("primary"); }))
                    L("  " + Sig(m));
            }

            // 4. GlobalEvents real fields
            var ge = FindType("GlobalEvents");
            if (ge != null)
            {
                L("--- GlobalEvents (static delegates) ---");
                foreach (var f in ge.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    L($"  {f.Name} : {f.FieldType.Name}");
            }

            // 5. skills present + which have an HQ plan type
            try
            {
                var sh = FindType("SkillHelper");
                var allNames = sh?.GetProperty("AllSkillNames", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IEnumerable;
                if (allNames != null) L("SKILLS: " + string.Join(", ", allNames.Cast<object>().Select(o => o?.ToString())));
            }
            catch (Exception e) { L("skill enum failed: " + e.Message); }
            L("PLAN TYPES in game: " + string.Join(", ", _asms.SelectMany(SafeTypes)
                .Where(t => t.Name.EndsWith("ManagerPlan") || t.Name.EndsWith("Plan"))
                .Select(t => t.Name).Distinct().OrderBy(x => x)));

            // 6. the live save: HQ + workstations + delivery contracts
            DumpLiveSave();

            L("==================== SM REFLECTION DUMP END ====================");
        }

        private void DumpLiveSave()
        {
            L("--- LIVE SAVE ---");
            var sgm = FindType("SaveGameManager");
            var current = sgm?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (current == null) { L("SaveGameManager.Current == null"); return; }
            var giType = current.GetType();
            // modData actual content
            var mdf = giType.GetField("modData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (mdf != null)
            {
                var md = mdf.GetValue(current) as IDictionary;
                L($"modData: {(md == null ? "null" : md.Count + " entries: " + string.Join(",", md.Keys.Cast<object>()))}");
            }
            // delivery contracts
            var dcf = giType.GetField("DeliveryContracts", BindingFlags.Public | BindingFlags.Instance) ?? giType.GetProperty("DeliveryContracts")?.DeclaringType?.GetField("DeliveryContracts");
            var dc = giType.GetProperty("DeliveryContracts")?.GetValue(current) ?? dcf?.GetValue(current);
            if (dc is IEnumerable dce)
                foreach (var c in dce)
                    L("  CONTRACT " + DumpObj(c, new[] { "businessAddress", "wholesaleAddress", "enabled", "repeatingOrder", "nextDeliveryDay" }));

            // building registrations -> HQ + player stores + assignable workstations
            var brs = giType.GetProperty("BuildingRegistrations")?.GetValue(current) as IEnumerable;
            if (brs == null) { L("no BuildingRegistrations"); return; }
            foreach (var b in brs)
            {
                var bt = b.GetType();
                var owned = (bool?)(bt.GetProperty("RentedByPlayer")?.GetValue(b)) == true || (bool?)(bt.GetProperty("BuildingOwnedByPlayer")?.GetValue(b)) == true;
                if (!owned) continue;
                var typeName = bt.GetField("businessTypeName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(b)?.ToString();
                var name = bt.GetProperty("BusinessName")?.GetValue(b)?.ToString();
                L($"  OWNED '{name}' type={typeName}");
                try
                {
                    var items = bt.GetMethod("GetAssignableItems")?.Invoke(b, null) as IEnumerable;
                    if (items != null)
                        foreach (var it in items)
                        {
                            var itt = it.GetType();
                            var iname = itt.GetField("itemName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(it)?.ToString();
                            var cached = itt.GetProperty("ItemCached")?.GetValue(it);
                            var suit = cached?.GetType().GetField("suitableSkills", BindingFlags.Public | BindingFlags.Instance)?.GetValue(cached) as IEnumerable;
                            L($"    station {iname}  suitableSkills=[{(suit == null ? "" : string.Join(",", suit.Cast<object>()))}]");
                        }
                }
                catch (Exception e) { L("    GetAssignableItems failed: " + e.Message); }
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private Type? FindType(string simpleName) =>
            _asms.SelectMany(SafeTypes).FirstOrDefault(t => t.Name == simpleName && (t.Namespace == null || !t.Namespace.StartsWith("System")));

        private static IEnumerable<Type> SafeTypes(Assembly a) { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }

        private void DumpMembers(Type t)
        {
            const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMembers(BF).OrderBy(m => m.MemberType.ToString()).ThenBy(m => m.Name))
            {
                if (m is MethodInfo mi && (mi.IsSpecialName)) continue;
                L("  " + Sig(m));
            }
        }

        private void DumpFields(Type t, Func<FieldInfo, bool> filter)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(filter))
                L($"  field {f.Name} : {f.FieldType.FullName}");
        }

        private static string Sig(MemberInfo m)
        {
            switch (m)
            {
                case MethodInfo mi:
                    return $"{(mi.IsStatic ? "static " : "")}{mi.ReturnType.Name} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";
                case FieldInfo fi:
                    return $"{(fi.IsStatic ? "static " : "")}field {fi.FieldType.Name} {fi.Name}";
                case PropertyInfo pi:
                    return $"prop {pi.PropertyType.Name} {pi.Name} {{{(pi.CanRead ? "get;" : "")}{(pi.CanWrite ? "set;" : "")}}}";
                case ConstructorInfo ci:
                    return $"ctor({string.Join(", ", ci.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";
                default: return m.MemberType + " " + m.Name;
            }
        }

        private static string DumpObj(object o, string[] fields)
        {
            var t = o.GetType();
            var sb = new StringBuilder(t.Name + " {");
            foreach (var fn in fields)
            {
                var f = t.GetField(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var p = t.GetProperty(fn, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object? v = null;
                try { v = f?.GetValue(o) ?? p?.GetValue(o); } catch { }
                sb.Append($" {fn}={v}");
            }
            return sb.Append(" }").ToString();
        }

        private void L(string s)
        {
            Logger?.Info(s);
            Debug.Log("[SMDUMP] " + s);
        }
    }
}
