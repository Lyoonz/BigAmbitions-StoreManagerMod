#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Characters.Skills;
using BigAmbitions.Items;
using Entities;
using Helpers;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  v2 PROBE — runs headless on city load:
//   1. dumps the live save's HQ / stores / delivery contracts / purchasing agents
//   2. invokes StoreManager.Debugging.StoreManagerCommands.SelfTest() via reflection
//      (the mod DLL must also be deployed) — the end-to-end v2 loop check
//  Output: Player.log, prefix [SMPROBE]. Read-only except the mod's own SelfTest,
//  which is in-memory and self-cleaning. DO NOT SAVE after a run.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.V2ProbeInit))]
[assembly: RegisterModClass(typeof(StoreManagerProbe.V2ProbeAutoLoad))]

namespace StoreManagerProbe
{
    /// <summary>
    /// Opt-in test aid: at the main menu, if a file named "probe-autoload" sits next to the probe
    /// DLL, load the last save (like clicking Continue) so the city-load probe runs headless.
    /// Off by default — it can hijack the player's save choice. Delete the marker to disable.
    /// </summary>
    [ModEntryMainMenu]
    public sealed class V2ProbeAutoLoad : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext context)
        {
            var flag = System.IO.Path.Combine(context.ModRootPath ?? ".", "probe-autoload");
            if (!System.IO.File.Exists(flag)) return Task.CompletedTask;
            _host = new GameObject("SMV2AutoLoad");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<AutoLoadRunner>().Logger = context.Logger;
            return Task.CompletedTask;
        }
        public Task OnUnloadAsync() { if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; return Task.CompletedTask; }
    }

    public sealed class AutoLoadRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private void Start() => StartCoroutine(Go());
        private IEnumerator Go()
        {
            yield return new WaitForSeconds(6f);

            // Load the most-advanced save (highest in-game day) — that's the one with an HQ + stores.
            object? target = null;
            try
            {
                var helper = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(x => x.Name == "SaveGamePathHelper");
                var m = helper?.GetMethod("GetAllSaveGamesFromVersion",
                    BindingFlags.Public | BindingFlags.Static);
                if (m?.Invoke(null, new object?[] { null }) is IEnumerable list)
                {
                    int bestDay = -1;
                    foreach (var s in list)
                    {
                        var df = s.GetType().GetField("day", BindingFlags.Public | BindingFlags.Instance);
                        int day = df?.GetValue(s) is int i ? i : 0;
                        if (day > bestDay) { bestDay = day; target = s; }
                    }
                    Logger?.Info($"[SMPROBE] AutoLoad: chosen save day={bestDay}");
                }
            }
            catch (Exception e) { Logger?.Warn("[SMPROBE] AutoLoad: save enumeration failed: " + e.Message); }

            Task<bool>? t = null;
            try
            {
                t = target != null
                    ? (Task<bool>?)typeof(SaveGameManager).GetMethod("LoadAsync", BindingFlags.Public | BindingFlags.Static,
                          null, new[] { target.GetType(), typeof(bool) }, null)?.Invoke(null, new object[] { target, true })
                    : SaveGameManager.LoadAsync(null, true);
            }
            catch (Exception e) { Logger?.Error(e); yield break; }
            while (t != null && !t.IsCompleted) yield return null;
            Logger?.Info($"[SMPROBE] AutoLoad: completed = {t?.Result}");
        }
    }

    [ModEntryOnCityLoad]
    public sealed class V2ProbeInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();
        public Task OnLoadAsync(ModContext context)
        {
            _host = new GameObject("SMV2Probe");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<V2ProbeRunner>().Logger = context.Logger;
            return Task.CompletedTask;
        }
        public Task OnUnloadAsync() { if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; return Task.CompletedTask; }
    }

    public sealed class V2ProbeRunner : MonoBehaviour
    {
        public IModLogger? Logger;
        private void Start() => StartCoroutine(Run());

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(5f);
            try { DumpLiveSave(); } catch (Exception e) { L("dump threw: " + e); }
            yield return new WaitForSeconds(2f);
            try { InvokeSelfTest(); } catch (Exception e) { L("selftest invoke threw: " + e); }
        }

        private void DumpLiveSave()
        {
            var gi = SaveGameManager.Current;
            if (gi == null) { L("SaveGameManager.Current == null"); return; }

            L($"day={TimeHelper.CurrentDay} dow={TimeHelper.GetDayOfWeek()} regs={gi.BuildingRegistrations?.Count}");

            // modData
            try
            {
                var f = gi.GetType().GetField("modData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f?.GetValue(gi) is IDictionary md)
                    L($"modData: {md.Count} entries: [{string.Join(",", md.Keys.Cast<object>())}]");
            }
            catch (Exception e) { L("modData read failed: " + e.Message); }

            // delivery contracts
            try
            {
                var dcs = gi.DeliveryContracts;
                L($"deliveryContracts: {dcs?.Count ?? 0}");
                foreach (var c in dcs ?? new List<DeliveryContract>())
                {
                    int sum = c.items?.Where(i => i != null).Sum(i => i.amount) ?? 0;
                    L($"  CONTRACT biz={Addr(c.businessAddress)} wholesale={Addr(c.wholesaleAddress)} " +
                      $"enabled={c.enabled} repeating={c.repeatingOrder} nextDay={c.nextDeliveryDay} items={c.items?.Count} totalAmount={sum}");
                }
            }
            catch (Exception e) { L("contracts read failed: " + e.Message); }

            // player buildings: HQ + stores + workstations (fixes the earlier failed walk)
            foreach (var b in (gi.BuildingRegistrations ?? new List<BuildingRegistration>()))
            {
                if (b == null || !(b.RentedByPlayer || b.BuildingOwnedByPlayer)) continue;
                bool isHq = b.businessTypeName == "ba:businesstype_headquarters";
                var emps = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address });
                L($"OWNED {(isHq ? "HQ " : "")}'{b.BusinessName}' type={b.businessTypeName} emps={emps.Count} days={b.scheduleDays?.Count}");
                foreach (var e in emps)
                {
                    string skills = "";
                    try { skills = e.skills == null ? "" : string.Join(",", e.skills.Select(s => s?.name)); } catch { }
                    bool sched = false; try { sched = e.IsAssignedToAnyWorkShift(); } catch { }
                    L($"  EMP {e.characterData?.name} id={e.id} scheduled={sched} skills=[{skills}] purchasing={SafeSkill(e, "ba:skill_purchasingagent")}");
                }
                try
                {
                    var items = b.GetAssignableItems();
                    if (items != null)
                        foreach (var it in items)
                        {
                            var suit = it.ItemCached?.suitableSkills;
                            L($"  station {it.itemName}  suitableSkills=[{(suit == null ? "" : string.Join(",", suit))}]");
                        }
                }
                catch (Exception e) { L("  GetAssignableItems failed: " + e.Message); }
            }
        }

        private void InvokeSelfTest()
        {
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(x => x.FullName == "StoreManager.Debugging.StoreManagerCommands");
            if (t == null) { L("SelfTest: StoreManager mod not loaded (deploy it alongside the probe)"); return; }
            var m = t.GetMethod("SelfTest", Type.EmptyTypes) ?? t.GetMethod("SelfTest");
            if (m == null) { L("SelfTest: method not found"); return; }
            L("SelfTest: invoking — output on [StoreManager] lines");
            m.Invoke(null, null);
        }

        private static string SafeSkill(EmployeeInstance e, string k)
        { try { return e.HasSkill(k) ? e.GetSkillValue(k).ToString("0") : "-"; } catch { return "?"; } }

        private static string Addr(Address? a) { try { return a?.ToString() ?? "-"; } catch { return "-"; } }

        private void L(string s) { Logger?.Info(s); Debug.Log("[SMPROBE] " + s); }
    }
}
