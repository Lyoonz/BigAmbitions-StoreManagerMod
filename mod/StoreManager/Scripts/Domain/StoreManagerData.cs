#nullable enable
using System;
using System.Collections.Generic;

namespace StoreManager.Domain
{
    /// <summary>
    /// Per-store persisted state. One record per store that has a manager assigned.
    /// Serialized additively onto the game save (see D6); removed on mod unload.
    /// </summary>
    [Serializable]
    public sealed class StoreManagerData
    {
        /// <summary>Game-side business/building id this record belongs to. Format resolved in GameBindings.</summary>
        public string StoreId = string.Empty;

        /// <summary>Game-side employee id acting as Manager, or null.</summary>
        public string? ManagerEmployeeId;

        /// <summary>Game-side employee id acting as Assistant Manager, or null.</summary>
        public string? AssistantEmployeeId;

        public int ManagerSkill = 3;
        public int AssistantSkill = 3;

        public StorePolicy Policy = StorePolicy.Default();

        /// <summary>Deterministic RNG seed for this store's mistake rolls (stable across reloads).</summary>
        public int MistakeSeed = 0;

        /// <summary>Running week accumulators, reset when the digest is sent.</summary>
        public WeekTally CurrentWeek = new();

        public bool HasActiveLeadership => ManagerEmployeeId != null || AssistantEmployeeId != null;

        /// <summary>Who is actually running the store right now, given who is present/away.</summary>
        public ManagerRank EffectiveController(bool managerPresent, bool assistantPresent)
        {
            if (ManagerEmployeeId != null && managerPresent) return ManagerRank.Manager;
            if (AssistantEmployeeId != null && assistantPresent) return ManagerRank.AssistantManager;
            return ManagerRank.Employee; // unmanaged fallback
        }
    }

    [Serializable]
    public sealed class WeekTally
    {
        public decimal Revenue;
        public decimal RevenueLastWeek;
        public decimal RestockSpend;
        public int ShiftsCovered;
        public int ShiftsTotal;
        public int ComplaintsResolved;
        public int ComplaintsTotal;
        public List<string> AttentionItems = new();

        public void Reset()
        {
            RevenueLastWeek = Revenue;
            Revenue = 0m;
            RestockSpend = 0m;
            ShiftsCovered = 0;
            ShiftsTotal = 0;
            ComplaintsResolved = 0;
            ComplaintsTotal = 0;
            AttentionItems.Clear();
        }
    }
}
