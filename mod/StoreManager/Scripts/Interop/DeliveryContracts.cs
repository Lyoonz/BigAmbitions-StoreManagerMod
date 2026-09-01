#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Entities;
using Helpers;
using StoreManager.Domain;

namespace StoreManager.Interop
{
    /// <summary>
    /// Drives a store's repeating wholesale <see cref="DeliveryContract"/> — the mechanism behind
    /// requirement 7 ("keeps stores stocked via delivery"). v1 only tunes contracts the player
    /// already created (creating one from scratch, incl. picking a wholesale source, is Phase 2).
    /// All edits are gated by <c>DeliveryHelper.CanModifyContract</c> and the Monday lock window (D12).
    /// </summary>
    public static class DeliveryContracts
    {
        public sealed class PlanResult
        {
            public bool ContractFound;
            public bool Modifiable;
            public int OrdersAdjusted;
            public decimal ProjectedWeeklyCost;
            public bool BudgetCapHit;
            public string? Note;
        }

        private static IEnumerable<DeliveryContract> All =>
            SaveGameManager.Current?.DeliveryContracts?.Where(c => c != null) ?? Enumerable.Empty<DeliveryContract>();

        public static DeliveryContract? Get(string storeAddress) =>
            All.FirstOrDefault(c => AddrString(c.businessAddress) == storeAddress);

        public static bool HasContract(string storeAddress) => Get(storeAddress) != null;

        public static bool CanModifyNow(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return false;
            try { return DeliveryHelper.CanModifyContract(c.nextDeliveryDay); } catch { return false; }
        }

        // ── snapshot / restore (restore-on-detach) ──────────────────────────────
        public static ContractSnapshot? Snapshot(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return null;
            return new ContractSnapshot
            {
                Enabled = c.enabled,
                RepeatingOrder = c.repeatingOrder,
                WholesaleAddress = AddrString(c.wholesaleAddress),
                Items = (c.items ?? new List<DeliveryContractItem>())
                        .Where(i => i != null)
                        .Select(i => new ContractSnapshot.ContractLine { ItemName = i.itemName, Amount = i.amount })
                        .ToList(),
            };
        }

        public static void Restore(string storeAddress, ContractSnapshot? snap)
        {
            var c = Get(storeAddress);
            if (c == null || snap == null) return;
            try
            {
                if (!DeliveryHelper.CanModifyContract(c.nextDeliveryDay)) return;
                c.enabled = snap.Enabled;
                c.repeatingOrder = snap.RepeatingOrder;
                foreach (var line in snap.Items)
                {
                    var it = c.items?.FirstOrDefault(x => x != null && x.itemName == line.ItemName);
                    if (it != null) it.amount = line.Amount;
                }
            }
            catch (Exception e) { Debug.LogError("[StoreManager] contract restore failed: " + e.Message); }
        }

        public static void Disable(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return;
            try
            {
                if (!DeliveryHelper.CanModifyContract(c.nextDeliveryDay)) return;
                c.enabled = false;
            }
            catch (Exception e) { Debug.LogError("[StoreManager] contract disable failed: " + e.Message); }
        }

        // ── the weekly plan ────────────────────────────────────────────────────
        /// <summary>
        /// Tune the store's contract so projected stock covers <c>TargetDaysOfStock</c>, capped by
        /// <c>WeeklyRestockBudgetCap - already spent this week</c>. Enables + repeats the contract.
        /// </summary>
        public static PlanResult PlanAndApply(StoreAssignment a)
        {
            var r = new PlanResult();
            var c = Get(a.StoreAddress);
            if (c == null) { r.Note = "no delivery contract — set one up in the store's BizMan Deliveries tab"; return r; }
            r.ContractFound = true;

            try
            {
                if (!DeliveryHelper.CanModifyContract(c.nextDeliveryDay))
                {
                    r.Note = "delivery locked until Monday — will plan next week";
                    return r;
                }
                r.Modifiable = true;

                var wholesaleReg = ResolveWholesale(c);
                decimal budgetLeft = a.WeeklyRestockBudgetCap - a.SpentThisWeek;
                if (budgetLeft <= 0) { r.BudgetCapHit = true; r.Note = "weekly budget already spent"; return r; }

                // target multiplier vs a 7-day baseline
                double dayFactor = Math.Max(1, a.TargetDaysOfStock) / 7.0;

                var priceBefore = SafeTotal(c);
                foreach (var it in (c.items ?? new List<DeliveryContractItem>()).Where(i => i != null))
                {
                    int target = ComputeTarget(it, wholesaleReg, dayFactor);
                    if (target != it.amount)
                    {
                        it.amount = target;
                        r.OrdersAdjusted++;
                    }
                }

                c.enabled = true;
                c.repeatingOrder = true;

                var projected = SafeTotal(c);
                r.ProjectedWeeklyCost = (decimal)projected;

                if ((decimal)projected > budgetLeft)
                {
                    // scale every line down proportionally to fit the budget
                    double scale = budgetLeft <= 0 ? 0 : (double)budgetLeft / Math.Max(1f, projected);
                    foreach (var it in (c.items ?? new List<DeliveryContractItem>()).Where(i => i != null))
                        it.amount = Math.Max(0, (int)Math.Floor(it.amount * scale));
                    r.BudgetCapHit = true;
                    r.ProjectedWeeklyCost = (decimal)SafeTotal(c);
                    r.Note = "order trimmed to fit the weekly budget cap";
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] PlanAndApply failed for " + a.StoreAddress + ": " + e);
                r.Note = "error planning this store's delivery (logged)";
            }
            return r;
        }

        // ── helpers ────────────────────────────────────────────────────────────
        private static int ComputeTarget(DeliveryContractItem it, BuildingRegistration? wholesaleReg, double dayFactor)
        {
            // Prefer the game's own "how much to order" when we can supply its args.
            try
            {
                var item = it.ItemCached;
                if (item != null && wholesaleReg != null)
                {
                    int gameAmt = DeliveryHelper.GetOrderAmount(it, item, wholesaleReg);
                    if (gameAmt > 0) return Math.Max(1, (int)Math.Round(gameAmt * dayFactor));
                }
            }
            catch { /* fall through */ }

            // Fallback: last week's order is a reasonable weekly-demand proxy.
            int baseline = it.amountOrderedLastWeek > 0 ? it.amountOrderedLastWeek
                         : it.amount > 0 ? it.amount
                         : 10;
            return Math.Max(1, (int)Math.Round(baseline * dayFactor));
        }

        private static BuildingRegistration? ResolveWholesale(DeliveryContract c)
        {
            try
            {
                if (c.wholesaleAddress != null)
                    return BuildingHelper.GetBuildingRegistration(c.wholesaleAddress);
            }
            catch { }
            try { return BuildingHelper.FindClosestWholesaleStore(c.businessAddress); }
            catch { return null; }
        }

        private static float SafeTotal(DeliveryContract c)
        {
            try
            {
                var p = c.GetType().GetProperty("TotalPricePerDelivery", BindingFlags.Public | BindingFlags.Instance);
                if (p?.GetValue(c) is float f) return f;
            }
            catch { }
            return 0f;
        }

        private static string AddrString(Address? a)
        {
            try { return a?.ToString() ?? ""; } catch { return ""; }
        }
    }
}
