#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StoreManager.Domain
{
    public enum StaffingLevel { Lean, Normal, Generous }

    public static class StaffingLevelExtensions
    {
        /// <summary>Multiplier on the store's demand-driven staffing need.</summary>
        public static double Multiplier(this StaffingLevel s) => s switch
        {
            StaffingLevel.Lean => 0.8,
            StaffingLevel.Generous => 1.25,
            _ => 1.0,
        };
    }

    /// <summary>
    /// One Store Manager supervision plan. The manager is a real hired game <c>EmployeeInstance</c>
    /// (skill <c>ba:skill_purchasingagent</c>) — this record only holds the mod's supervision layer:
    /// which stores the manager runs and the per-store limits. Persisted as JSON in
    /// <c>GameInstance.modData["StoreManager.plans.v1"]</c> (D13).
    /// </summary>
    [Serializable]
    public sealed class StoreManagerPlan
    {
        /// <summary>Game <c>EmployeeInstance.id</c> of the manager. The plan is keyed by this.</summary>
        public string ManagerEmployeeId = string.Empty;

        /// <summary>The HQ address (string form) the manager is assigned to and scheduled at.</summary>
        public string HqAddress = string.Empty;

        /// <summary>Stores this manager supervises. Capped by manager skill (see <see cref="MaxStores"/>).</summary>
        public List<StoreAssignment> Assignments = new();

        /// <summary>
        /// True when the manager is missing (fired/quit) or not currently scheduled at the HQ.
        /// A dormant plan takes no actions; its delivery contracts are left <c>enabled=false</c>.
        /// </summary>
        public bool Dormant;

        /// <summary>Rolling week accumulators for the digest, reset when the digest is sent.</summary>
        public WeekTally Week = new();

        public bool Supervises(string storeAddress) =>
            Assignments.Any(a => a.StoreAddress == storeAddress);

        public StoreAssignment? Find(string storeAddress) =>
            Assignments.FirstOrDefault(a => a.StoreAddress == storeAddress);

        /// <summary>
        /// Max stores this manager may supervise, from their 0–100 skill value.
        /// Mirrors the game's own <c>LogisticsManagerPlan.CalculateMaxDestinations</c> shape;
        /// <see cref="Interop.GameApi"/> uses the real game calc when available and falls back to this.
        /// </summary>
        public static int MaxStoresForSkill(float skill0to100) =>
            1 + (int)Math.Floor(Math.Max(0f, skill0to100) / 25f);   // 1 (0–24), 2 (25–49), 3 (50–74), 4 (75–99), 5 (100)
    }

    /// <summary>One supervised store + the limits the player set for it.</summary>
    [Serializable]
    public sealed class StoreAssignment
    {
        public string StoreAddress = string.Empty;
        public string StoreName = string.Empty;   // display only

        /// <summary>Hard cap on what the manager may spend restocking this store per delivery week.</summary>
        public decimal WeeklyRestockBudgetCap = 5000m;

        /// <summary>Target buffer: order enough that projected stock covers this many days of sales.</summary>
        public int TargetDaysOfStock = 10;

        public StaffingLevel Staffing = StaffingLevel.Normal;

        /// <summary>Spent restocking this store in the current delivery week (reset with the digest).</summary>
        public decimal SpentThisWeek;

        /// <summary>
        /// Captured state of the store's delivery contract at the moment it was assigned, so the
        /// mod can restore it verbatim on unassign / manager-fired. Empty = there was no contract.
        /// </summary>
        public ContractSnapshot? OriginalContract;

        public static StoreAssignment New(string address, string name, GlobalDefaults d) => new()
        {
            StoreAddress = address,
            StoreName = name,
            WeeklyRestockBudgetCap = d.WeeklyRestockBudgetCap,
            TargetDaysOfStock = d.TargetDaysOfStock,
            Staffing = d.Staffing,
        };
    }

    /// <summary>Verbatim snapshot of a game DeliveryContract, for restore-on-detach.</summary>
    [Serializable]
    public sealed class ContractSnapshot
    {
        public bool Enabled;
        public bool RepeatingOrder;
        public string WholesaleAddress = string.Empty;
        public List<ContractLine> Items = new();

        [Serializable]
        public sealed class ContractLine
        {
            public string ItemName = string.Empty;
            public int Amount;
        }
    }

    /// <summary>Values applied to each new <see cref="StoreAssignment"/>. Editable in the options panel.</summary>
    [Serializable]
    public sealed class GlobalDefaults
    {
        public decimal WeeklyRestockBudgetCap = 5000m;
        public int TargetDaysOfStock = 10;
        public StaffingLevel Staffing = StaffingLevel.Normal;

        public static GlobalDefaults Default() => new();
    }

    /// <summary>Rolling per-plan accumulators for the weekly digest.</summary>
    [Serializable]
    public sealed class WeekTally
    {
        public decimal RestockSpend;
        public int OrdersPlaced;
        public int StoresCovered;
        public int BudgetCapsHit;
        public List<string> AttentionItems = new();

        public void Reset()
        {
            RestockSpend = 0m;
            OrdersPlaced = 0;
            StoresCovered = 0;
            BudgetCapsHit = 0;
            AttentionItems.Clear();
        }
    }
}
