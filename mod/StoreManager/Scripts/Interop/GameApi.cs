#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using Entities;                              // EmployeeInstance, BuildingRegistration, DeliveryContract, DeliveryHelper, Contact, TextMessage
using Helpers;                               // EmployeeHelper, BuildingHelper, TimeHelper
using Buildings.Office.Headquarters;         // LogisticsManagerHelper, LogisticsManagerPlan, HrManagerHelper, PricingManagerHelper

namespace StoreManager.Interop
{
    /// <summary>
    /// The single seam onto Big Ambitions. Every call here is against a type confirmed by the
    /// in-game reflection dump (docs/research/reflection-dump-2026-09-01.txt). Risky spots are
    /// wrapped in try/catch and log rather than throw, so a game patch degrades the mod to
    /// "dormant + player told" instead of crashing a save.
    /// </summary>
    public static class GameApi
    {
        public const string ManagerSkill = "ba:skill_purchasingagent";   // D10
        public const string HqBusinessType = "ba:businesstype_headquarters";
        private const string EmptyBusinessType = "ba:businesstype_empty";

        // ── lightweight handles the mod passes around (game types don't leak past this file) ──
        public readonly struct Ref
        {
            public string Address { get; }
            public string Name { get; }
            public Ref(string address, string name) { Address = address; Name = name; }
            public override string ToString() => $"{Name} <{Address}>";
        }

        public readonly struct EmpRef
        {
            public string Id { get; }
            public string Name { get; }
            public float Skill { get; }
            public bool Scheduled { get; }
            public EmpRef(string id, string name, float skill, bool scheduled) { Id = id; Name = name; Skill = skill; Scheduled = scheduled; }
            public override string ToString() => $"{Name} ({Skill:0} skill{(Scheduled ? ", scheduled" : ", NOT scheduled")})";
        }

        // ── time ────────────────────────────────────────────────────────────────
        public static int CurrentDay => SafeGet(() => TimeHelper.CurrentDay, 0);
        public static string CurrentDayOfWeek => SafeGet(() => TimeHelper.GetDayOfWeek().ToString(), "Monday");

        /// <summary>The weekly restock planner runs once per week, before the Monday delivery lock (D12).</summary>
        public static bool IsWeeklyPlanningDay => CurrentDayOfWeek.Equals("Saturday", StringComparison.OrdinalIgnoreCase);

        // ── headquarters + stores ───────────────────────────────────────────────
        private static GameInstance? Save => SaveGameManager.Current;

        private static IEnumerable<BuildingRegistration> PlayerBuildings =>
            Save?.BuildingRegistrations?.Where(b => b != null && (b.RentedByPlayer || b.BuildingOwnedByPlayer))
            ?? Enumerable.Empty<BuildingRegistration>();

        public static List<Ref> GetHeadquarters() =>
            PlayerBuildings.Where(b => b.businessTypeName == HqBusinessType)
                           .Select(b => new Ref(b.Address.ToString(), b.BusinessName))
                           .ToList();

        /// <summary>Retail/revenue businesses the player owns that could be supervised.</summary>
        public static List<Ref> GetSupervisableStores() =>
            PlayerBuildings.Where(b => b.businessTypeName != HqBusinessType
                                    && b.businessTypeName != EmptyBusinessType
                                    && (b.scheduleDays?.Count ?? 0) > 0)
                           .Select(b => new Ref(b.Address.ToString(), b.BusinessName))
                           .ToList();

        public static string StoreName(string address) => FindBuilding(address)?.BusinessName ?? address;

        public static bool StoreStillOwned(string address)
        {
            var b = FindBuilding(address);
            return b != null && (b.RentedByPlayer || b.BuildingOwnedByPlayer);
        }

        public static decimal GetStoreWeeklyRevenue(string address)
        {
            var b = FindBuilding(address);
            return b == null ? 0m : (decimal)Math.Max(0f, b.GetAvgDailyIncome(7) * 7f);
        }

        public static decimal GetStoreDailyRevenue(string address)
        {
            var b = FindBuilding(address);
            return b == null ? 0m : (decimal)b.GetAvgDailyIncome(3);
        }

        private static BuildingRegistration? FindBuilding(string address)
        {
            try { return BuildingHelper.GetBuildingRegistration(BuildingHelper.ParseAddressString(address)); }
            catch { return PlayerBuildings.FirstOrDefault(b => b.Address.ToString() == address); }
        }

        // ── managers (employees) ────────────────────────────────────────────────
        /// <summary>Employees at this HQ carrying the manager skill — candidates to run stores.</summary>
        public static List<EmpRef> GetManagerCandidates(string hqAddress)
        {
            var result = new List<EmpRef>();
            EmployeeInstance[] all;
            try { all = EmployeeHelper.GetEmployeeInstances().ToArray(); }
            catch { return result; }

            foreach (var e in all)
            {
                if (e == null) continue;
                bool hasSkill;
                try { hasSkill = e.HasSkill(ManagerSkill); } catch { hasSkill = false; }
                if (!hasSkill) continue;
                if (e.assignedAddress == null || e.assignedAddress.ToString() != hqAddress) continue;
                result.Add(ToEmpRef(e));
            }
            return result;
        }

        public static EmpRef? FindManager(string employeeId)
        {
            var e = FindEmployee(employeeId);
            return e == null ? (EmpRef?)null : ToEmpRef(e);
        }

        public static bool EmployeeExists(string employeeId) => FindEmployee(employeeId) != null;

        public static bool HasManagerSkill(string employeeId)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return false;
            try { return e.HasSkill(ManagerSkill); } catch { return false; }
        }

        public static string HqAddressOf(string employeeId) =>
            FindEmployee(employeeId)?.assignedAddress?.ToString() ?? string.Empty;

        public static bool IsScheduledAtHq(string employeeId, string hqAddress)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return false;
            if (e.assignedAddress == null || e.assignedAddress.ToString() != hqAddress) return false;
            try { return e.IsAssignedToAnyWorkShift(); } catch { return false; }
        }

        /// <summary>Dual-binding guard — already bound to a vanilla HQ manager plan?</summary>
        public static bool IsBoundToVanillaPlan(string employeeId)
        {
            try { if (LogisticsManagerHelper.GetAssignedPlanForEmployee(employeeId) != null) return true; } catch { }
            try { if (HrManagerHelper.GetAssignedPlanForHrManager(employeeId) != null) return true; } catch { }
            try { if (PricingManagerHelper.GetAssignedPlanForPricingManager(employeeId) != null) return true; } catch { }
            return false;
        }

        public static float GetManagerSkill(string employeeId)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return 0f;
            try { return e.GetSkillValue(ManagerSkill); } catch { return 0f; }
        }

        /// <summary>Max stores this manager may supervise — game calc if callable, else the domain fallback.</summary>
        public static int MaxStores(string hqAddress, string employeeId)
        {
            try
            {
                var m = typeof(LogisticsManagerPlan).GetMethod("CalculateMaxDestinations",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Address), typeof(string) }, null);
                if (m != null)
                {
                    var addr = BuildingHelper.ParseAddressString(hqAddress);
                    var v = m.Invoke(null, new object[] { addr, employeeId });
                    if (v is int i && i > 0) return i;
                }
            }
            catch { /* fall through */ }
            return Domain.StoreManagerPlan.MaxStoresForSkill(GetManagerSkill(employeeId));
        }

        public static void OpenEmployeeCard(string employeeId)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return;
            // OnClickShowEmployee is private static in this build — reach it by reflection (best-effort).
            try
            {
                var m = typeof(EmployeeHelper).GetMethod("OnClickShowEmployee",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                m?.Invoke(null, new object[] { e });
            }
            catch { }
        }

        private static EmployeeInstance? FindEmployee(string employeeId)
        {
            try { return EmployeeHelper.GetEmployeeById(employeeId, false); }
            catch { return null; }
        }

        private static EmpRef ToEmpRef(EmployeeInstance e)
        {
            float skill = 0f; bool sched = false;
            try { skill = e.GetSkillValue(ManagerSkill); } catch { }
            try { sched = e.IsAssignedToAnyWorkShift(); } catch { }
            return new EmpRef(e.id, e.characterData?.name ?? e.id, skill, sched);
        }

        // ── money ───────────────────────────────────────────────────────────────
        public static bool ChangeMoney(decimal amount, string reasonKey, string? storeAddress = null)
        {
            try
            {
                var info = new TransactionInfo("storemanager:transaction_restock",
                    new Dictionary<string, string> { { "reason", reasonKey } });
                Address? addr = null;
                if (storeAddress != null) { try { addr = BuildingHelper.ParseAddressString(storeAddress); } catch { } }
                return GameManager.ChangeMoneySafe((float)amount, info, null, addr, false, false);
            }
            catch (Exception e) { Debug.LogError("[StoreManager] ChangeMoney failed: " + e.Message); return false; }
        }

        // ── persistence: GameInstance.modData primary, file fallback (D13) ───────
        public static void SaveModData(string key, string json)
        {
            bool wrote = false;
            try
            {
                var md = ModDataDict();
                if (md != null) { md[key] = json; wrote = true; try { SaveGameManager.MarkChange(); } catch { } }
            }
            catch (Exception e) { Debug.LogError("[StoreManager] modData write failed: " + e.Message); }
            try { ModDataStore.Write(SaveScopedKey(key), json); } catch { }
            if (!wrote) Debug.LogWarning("[StoreManager] modData unavailable — file sink only.");
        }

        public static string? LoadModData(string key)
        {
            try
            {
                var md = ModDataDict();
                if (md != null && md.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
            }
            catch (Exception e) { Debug.LogError("[StoreManager] modData read failed: " + e.Message); }
            try { return ModDataStore.Read(SaveScopedKey(key)); } catch { return null; }
        }

        private static IDictionary<string, string>? ModDataDict()
        {
            var gi = Save;
            if (gi == null) return null;
            var f = gi.GetType().GetField("modData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(gi) as IDictionary<string, string>;
        }

        private static string SaveScopedKey(string key)
        {
            string scope = "default";
            try
            {
                var cd = Save?.charactersData;
                if (cd != null && cd.Count > 0) scope = cd[0].name ?? "default";
            }
            catch { }
            return scope + "__" + key;
        }

        // ── events ──────────────────────────────────────────────────────────────
        public static void Subscribe(Action onNewDay, Action onSaveGame, Action onJobChange)
        {
            GlobalEvents.onNewDay = (Action)Delegate.Combine(GlobalEvents.onNewDay, onNewDay);
            GlobalEvents.onSaveGame = (Action)Delegate.Combine(GlobalEvents.onSaveGame, onSaveGame);
            GlobalEvents.onJobChange = (Action)Delegate.Combine(GlobalEvents.onJobChange, onJobChange);
        }

        public static void Unsubscribe(Action onNewDay, Action onSaveGame, Action onJobChange)
        {
            GlobalEvents.onNewDay = (Action)Delegate.Remove(GlobalEvents.onNewDay, onNewDay)!;
            GlobalEvents.onSaveGame = (Action)Delegate.Remove(GlobalEvents.onSaveGame, onSaveGame)!;
            GlobalEvents.onJobChange = (Action)Delegate.Remove(GlobalEvents.onJobChange, onJobChange)!;
        }

        private static T SafeGet<T>(Func<T> f, T fallback)
        {
            try { return f(); } catch { return fallback; }
        }
    }
}
