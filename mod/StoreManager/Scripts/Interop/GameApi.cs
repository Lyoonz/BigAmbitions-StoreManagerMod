#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using Entities;                              // EmployeeInstance, BuildingRegistration, DeliveryContract, DeliveryHelper
using Helpers;                               // EmployeeHelper, BuildingHelper, TimeHelper
using Buildings.Office.Headquarters;         // *ManagerHelper dual-binding guards

namespace StoreManager.Interop
{
    /// <summary>
    /// The single seam onto Big Ambitions. Every member is against a type confirmed by the in-game
    /// reflection dump + the Build 3672 decompile. Risky spots are try/catch'd and log rather than
    /// throw — but note the mod's <i>event handlers</i> must also be guarded at their own frame
    /// (see Core/StoreManagerMod.cs), because the game invokes onNewDay/onSaveGame with a plain
    /// <c>?.Invoke()</c>.
    /// </summary>
    public static class GameApi
    {
        /// <summary>The custom skill a Store Manager carries (D15 — was <c>ba:skill_purchasingagent</c> under D10).</summary>
        public const string ManagerSkill = SkillRegistry.StoreManagerSkill;
        public const string HqBusinessType = "ba:businesstype_headquarters";

        /// <summary>
        /// v1: <c>sm:skill_storemanager</c> is not yet in the HQ desks' <c>suitableSkills</c>
        /// (deferred), so the game won't let the player put a Store Manager on an HQ desk shift.
        /// Until that injection ships, the plan is active whenever the manager is simply *assigned*
        /// to the HQ. Flip to true once desks accept the skill.
        /// </summary>
        public static readonly bool RequireHqShift = false;
        private const string EmptyBusinessType = "ba:businesstype_empty";

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
        public static bool IsWeeklyPlanningDay => CurrentDayOfWeek.Equals("Saturday", StringComparison.OrdinalIgnoreCase);

        // ── save / buildings ───────────────────────────────────────────────────
        private static GameInstance? Save => SaveGameManager.Current;

        private static IEnumerable<BuildingRegistration> PlayerBuildings =>
            (Save?.BuildingRegistrations ?? new List<BuildingRegistration>())
            .Where(b => b != null && b.Address != null && (b.RentedByPlayer || b.BuildingOwnedByPlayer));

        private static string A(BuildingRegistration b)
        {
            try { return b.Address?.ToString() ?? ""; } catch { return ""; }
        }

        public static List<Ref> GetHeadquarters() =>
            PlayerBuildings.Where(b => b.businessTypeName == HqBusinessType)
                           .Select(b => new Ref(A(b), b.BusinessName))
                           .Where(r => r.Address.Length > 0)
                           .ToList();

        public static List<Ref> GetSupervisableStores() =>
            PlayerBuildings.Where(b => b.businessTypeName != HqBusinessType
                                    && b.businessTypeName != EmptyBusinessType
                                    && (b.scheduleDays?.Count ?? 0) > 0)
                           .Select(b => new Ref(A(b), b.BusinessName))
                           .Where(r => r.Address.Length > 0)
                           .ToList();

        public static string StoreName(string address) => FindBuilding(address)?.BusinessName ?? address;

        public static bool StoreStillOwned(string address)
        {
            var b = FindBuilding(address);
            return b != null && (b.RentedByPlayer || b.BuildingOwnedByPlayer);
        }

        public static decimal GetStoreDailyRevenue(string address)
        {
            var b = FindBuilding(address);
            try { return b == null ? 0m : (decimal)b.GetAvgDailyIncome(3); } catch { return 0m; }
        }

        private static BuildingRegistration? FindBuilding(string address) =>
            PlayerBuildings.FirstOrDefault(b => A(b) == address);

        /// <summary>The live HQ <c>Address</c> object (game type), for code that must set an employee's address.</summary>
        public static object? HqAddressObject(string hqAddress) =>
            (Save?.BuildingRegistrations ?? new List<BuildingRegistration>())
            .FirstOrDefault(b => b != null && b.Address != null && b.Address.ToString() == hqAddress)?.Address;

        // ── employee subsystem ─────────────────────────────────────────────────
        /// <summary>True once the employee dictionary is populated — a gate before any destructive reconcile.</summary>
        public static bool EmployeeSubsystemReady()
        {
            try { return EmployeeHelper.GetEmployeeInstances().Count > 0; }
            catch { return false; }
        }

        public static List<EmpRef> GetManagerCandidates(string hqAddress)
        {
            var result = new List<EmpRef>();
            List<EmployeeInstance> all;
            try { all = EmployeeHelper.GetEmployeeInstances(); }
            catch { return result; }

            foreach (var e in all)
            {
                if (e == null) continue;
                if (e.assignedAddress == null || e.assignedAddress.ToString() != hqAddress) continue;   // cheap + silent
                if (!Try(() => e.HasSkill(ManagerSkill))) continue;                                      // HasSkill is silent; GetSkillValue logs a warning per miss
                result.Add(ToEmpRef(e));
            }
            return result;
        }

        /// <summary>Skill level 0–100 — guarded so it never triggers the game's "Skill not found" log spam.</summary>
        private static float SkillValue(EmployeeInstance e)
        {
            try { return e.HasSkill(ManagerSkill) ? e.GetSkillValue(ManagerSkill) : 0f; }
            catch { return 0f; }
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
            return e != null && Try(() => e.HasSkill(ManagerSkill));
        }

        public static string HqAddressOf(string employeeId)
        {
            var e = FindEmployee(employeeId);
            try { return e?.assignedAddress?.ToString() ?? ""; } catch { return ""; }
        }

        /// <summary>
        /// null = couldn't determine (game threw); true/false = a real answer. Callers must not
        /// treat "couldn't determine" as "not scheduled" (that would wrongly dormant a plan).
        /// </summary>
        public static bool? IsScheduledAtHq(string employeeId, string hqAddress)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return false;
            try
            {
                if (e.assignedAddress == null || e.assignedAddress.ToString() != hqAddress) return false;
                return e.IsAssignedToAnyWorkShift();
            }
            catch { return null; }
        }

        /// <summary>
        /// Whether the manager should be considered "on the job" for their plan. v1: assigned to
        /// the HQ is enough (<see cref="RequireHqShift"/>); later this also requires an HQ shift.
        /// null = couldn't determine (don't flip Dormant on that).
        /// </summary>
        public static bool? IsManagerOnDuty(string employeeId, string hqAddress)
        {
            if (RequireHqShift) return IsScheduledAtHq(employeeId, hqAddress);
            var e = FindEmployee(employeeId);
            if (e == null) return false;
            try
            {
                return e.assignedAddress != null && e.assignedAddress.ToString() == hqAddress;
            }
            catch { return null; }
        }

        public static bool IsBoundToVanillaPlan(string employeeId)
        {
            try { if (LogisticsManagerHelper.GetAssignedPlanForEmployee(employeeId) != null) return true; } catch { }
            try { if (HrManagerHelper.GetAssignedPlanForHrManager(employeeId) != null) return true; } catch { }
            try { if (PricingManagerHelper.GetAssignedPlanForPricingManager(employeeId) != null) return true; } catch { }
            try { if (PurchasingAgentHelper.GetAssignedPlanForPurchasingAgent(employeeId) != null) return true; } catch { }
            return false;
        }

        public static float GetManagerSkill(string employeeId)
        {
            var e = FindEmployee(employeeId);
            return e == null ? 0f : SkillValue(e);
        }

        /// <summary>
        /// Store cap from the manager's skill. No vanilla helper computes this for a purchasing
        /// agent (LogisticsManagerPlan.CalculateMaxDestinations is warehouse/logistics-specific),
        /// so this is the mod's own curve.
        /// </summary>
        public static int MaxStores(string hqAddress, string employeeId) =>
            Domain.StoreManagerPlan.MaxStoresForSkill(GetManagerSkill(employeeId));

        public static void OpenEmployeeCard(string employeeId)
        {
            var e = FindEmployee(employeeId);
            if (e == null) return;
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
            if (string.IsNullOrEmpty(employeeId)) return null;
            try { return EmployeeHelper.GetEmployeeById(employeeId, false); }
            catch { return null; }
        }

        private static EmpRef ToEmpRef(EmployeeInstance e)
        {
            bool sched = false;
            try { sched = e.IsAssignedToAnyWorkShift(); } catch { }
            return new EmpRef(e.id, e.characterData?.name ?? e.id, SkillValue(e), sched);
        }

        // ── persistence: GameInstance.modData primary, save-scoped file fallback (D13) ──
        private static string? _scopeCache;

        private static string SaveScope()
        {
            if (_scopeCache != null) return _scopeCache;
            var gi = Save;
            if (gi == null) return "";                       // unresolvable — caller skips the file write
            string cid = SafeGet(() => gi.characterId, "") ?? "";
            string sgn = SafeGet(() => gi.SaveGameName, "") ?? "";
            var scope = (cid + "_" + sgn).Trim('_');
            if (scope.Length == 0) return "";
            _scopeCache = scope;
            return scope;
        }

        public static void SaveModData(string key, string json)
        {
            if (string.IsNullOrEmpty(json)) return;          // never persist an empty document
            bool wroteDict = false;
            try
            {
                var md = ModDataDict();
                if (md != null) { md[key] = json; wroteDict = true; try { SaveGameManager.MarkChange(); } catch { } }
            }
            catch (Exception e) { Debug.LogError("[StoreManager] modData write failed: " + e.Message); }

            var scope = SaveScope();
            if (scope.Length > 0)
                try { ModDataStore.Write(scope + "__" + key, json); } catch { }

            if (!wroteDict) Debug.LogWarning("[StoreManager] GameInstance.modData unavailable — file sink only.");
        }

        /// <summary>modData is authoritative; the file is consulted only when the dict itself is unavailable.</summary>
        public static string? LoadModData(string key)
        {
            IDictionary<string, string>? md = null;
            try { md = ModDataDict(); }
            catch (Exception e) { Debug.LogError("[StoreManager] modData read failed: " + e.Message); }

            if (md != null)
                return md.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;

            var scope = SaveScope();
            if (scope.Length == 0) return null;
            try { return ModDataStore.Read(scope + "__" + key); } catch { return null; }
        }

        private static IDictionary<string, string>? ModDataDict()
        {
            var gi = Save;
            if (gi == null) return null;
            var f = gi.GetType().GetField("modData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(gi) as IDictionary<string, string>;
        }

        // ── events ──────────────────────────────────────────────────────────────
        public static void Subscribe(Action onNewDay, Action onNewHour, Action onSaveGame)
        {
            GlobalEvents.onNewDay = (Action)Delegate.Combine(GlobalEvents.onNewDay, onNewDay);
            GlobalEvents.onNewHour = (Action)Delegate.Combine(GlobalEvents.onNewHour, onNewHour);
            GlobalEvents.onSaveGame = (Action)Delegate.Combine(GlobalEvents.onSaveGame, onSaveGame);
        }

        public static void Unsubscribe(Action onNewDay, Action onNewHour, Action onSaveGame)
        {
            GlobalEvents.onNewDay = (Action)Delegate.Remove(GlobalEvents.onNewDay, onNewDay)!;
            GlobalEvents.onNewHour = (Action)Delegate.Remove(GlobalEvents.onNewHour, onNewHour)!;
            GlobalEvents.onSaveGame = (Action)Delegate.Remove(GlobalEvents.onSaveGame, onSaveGame)!;
        }

        // ── util ────────────────────────────────────────────────────────────────
        private static T SafeGet<T>(Func<T> f, T fallback)
        {
            try { return f(); } catch { return fallback; }
        }

        private static bool Try(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }
    }
}
