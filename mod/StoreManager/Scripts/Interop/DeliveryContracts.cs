#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Entities;
using Helpers;
using StoreManager.Domain;

namespace StoreManager.Interop
{
    /// <summary>
    /// Drives a store's repeating wholesale <see cref="DeliveryContract"/> — requirement 7
    /// ("keeps stores stocked via delivery"). Only tunes contracts the player already created.
    /// The target for each line is <b>real weekly throughput</b> (<c>amountOrderedLastWeek</c>) plus
    /// a one-time gap top-up toward the stock buffer — it never reads the line's own amount back,
    /// so it converges instead of compounding. All edits gated by <c>DeliveryHelper.CanModifyContract</c>.
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

        public static string Describe(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return "(no contract)";
            int items = c.items?.Count ?? 0;
            int sum = c.items?.Where(i => i != null).Sum(i => i.amount) ?? 0;
            var price = Price(c);
            return $"enabled={c.enabled} repeating={c.repeatingOrder} nextDay={c.nextDeliveryDay} " +
                   $"items={items} totalAmount={sum} costPerDelivery={(price.HasValue ? price.Value.ToString("N0") : "?")} " +
                   $"canModify={CanModifyNow(storeAddress)}";
        }

        public static bool CanModifyNow(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return false;
            try { return DeliveryHelper.CanModifyContract(c.nextDeliveryDay); } catch { return false; }
        }

        // ── snapshot / restore ──────────────────────────────────────────────────
        public static ContractSnapshot? Snapshot(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return null;
            return new ContractSnapshot
            {
                Enabled = c.enabled,
                RepeatingOrder = c.repeatingOrder,
                Items = (c.items ?? new List<DeliveryContractItem>())
                        .Where(i => i != null)
                        .Select(i => new ContractSnapshot.ContractLine { ItemName = i.itemName, Amount = i.amount })
                        .ToList(),
            };
        }

        /// <returns>true if the change was applied (false = the delivery lock is active; caller must retry later).</returns>
        public static bool Restore(string storeAddress, ContractSnapshot? snap)
        {
            var c = Get(storeAddress);
            if (c == null) return true;          // nothing to restore — treat as done
            if (snap == null) return Disable(storeAddress);
            try
            {
                if (!DeliveryHelper.CanModifyContract(c.nextDeliveryDay)) return false;
                bool wasEnabled = c.enabled;
                c.enabled = snap.Enabled;
                c.repeatingOrder = snap.RepeatingOrder;
                foreach (var line in snap.Items)
                {
                    var it = c.items?.FirstOrDefault(x => x != null && x.itemName == line.ItemName);
                    if (it != null) it.amount = line.Amount;
                }
                if (snap.Enabled && !wasEnabled) TryUpdateNextDeliveryDay(c);
                return true;
            }
            catch (Exception e) { Debug.LogError("[StoreManager] contract restore failed: " + e.Message); return true; }
        }

        /// <returns>true if applied (false = locked, retry later).</returns>
        public static bool Disable(string storeAddress)
        {
            var c = Get(storeAddress);
            if (c == null) return true;
            try
            {
                if (!DeliveryHelper.CanModifyContract(c.nextDeliveryDay)) return false;
                c.enabled = false;
                return true;
            }
            catch (Exception e) { Debug.LogError("[StoreManager] contract disable failed: " + e.Message); return true; }
        }

        // ── the weekly plan ────────────────────────────────────────────────────
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

                var storeReg = FindReg(a.StoreAddress);
                var items = (c.items ?? new List<DeliveryContractItem>()).Where(i => i != null).ToList();
                if (items.Count == 0) { r.Note = "contract has no items"; return r; }

                // snapshot amounts so we can revert if the plan doesn't fit the budget
                var before = items.ToDictionary(i => i, i => i.amount);
                int daysTarget = Math.Min(60, Math.Max(1, a.TargetDaysOfStock));

                foreach (var it in items)
                {
                    int target = ComputeTarget(it, storeReg, daysTarget);
                    if (target != it.amount) { it.amount = target; r.OrdersAdjusted++; }
                }

                bool wasEnabled = c.enabled;
                c.enabled = true;
                c.repeatingOrder = true;

                var price = Price(c);
                if (price == null)
                {
                    // can't value the contract (wholesale unresolved) — don't gamble on an uncapped order
                    foreach (var kv in before) kv.Key.amount = kv.Value;
                    c.enabled = wasEnabled;
                    r.OrdersAdjusted = 0;
                    r.Note = "couldn't price this contract's wholesaler — left it unchanged this week";
                    return r;
                }

                r.ProjectedWeeklyCost = (decimal)price.Value;

                if (r.ProjectedWeeklyCost > a.WeeklyRestockBudgetCap && a.WeeklyRestockBudgetCap > 0)
                {
                    double scale = (double)a.WeeklyRestockBudgetCap / Math.Max(1f, price.Value);
                    foreach (var it in items)
                        it.amount = Math.Max(0, (int)Math.Floor(before[it] * scale));   // scale from the ORIGINAL, not the inflated target

                    if (items.All(i => i.amount == 0))
                    {
                        // nothing fits — revert and pause rather than ship an empty enabled contract
                        foreach (var kv in before) kv.Key.amount = kv.Value;
                        c.enabled = wasEnabled;
                        r.OrdersAdjusted = 0;
                        r.BudgetCapHit = true;
                        r.Note = "weekly budget too low to cover even a minimal order — contract left as-is";
                        return r;
                    }

                    r.BudgetCapHit = true;
                    r.ProjectedWeeklyCost = (decimal)(Price(c) ?? 0f);
                    r.Note = "order trimmed to fit the weekly budget cap";
                }

                if (!wasEnabled) TryUpdateNextDeliveryDay(c);
                a.SpentThisWeek = r.ProjectedWeeklyCost;   // the standing weekly charge, for the digest
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] PlanAndApply failed for " + a.StoreAddress + ": " + e);
                r.Note = "error planning this store's delivery (logged)";
            }
            return r;
        }

        // ── helpers ────────────────────────────────────────────────────────────
        /// <summary>
        /// Weekly order = last week's real throughput + a capped top-up toward the stock buffer.
        /// Never a function of the line's own current <c>amount</c>, so it converges, never compounds.
        /// </summary>
        private static int ComputeTarget(DeliveryContractItem it, BuildingRegistration? storeReg, int targetDays)
        {
            int weeklyDemand = it.amountOrderedLastWeek > 0 ? it.amountOrderedLastWeek : Math.Max(1, it.amount);

            int onHand = 0;
            try
            {
                if (storeReg != null)
                    onHand = BuildingHelper.CountTotalResourcesInStock(storeReg, it.itemName, false, true, true);
            }
            catch { onHand = -1; }   // unknown

            if (onHand < 0)
                return Math.Max(1, weeklyDemand);   // just maintain throughput

            int targetOnHand = (int)Math.Round(weeklyDemand / 7.0 * targetDays);
            int gap = Math.Max(0, targetOnHand - onHand);
            // fill at most one extra week of demand per delivery so the buffer builds over a few weeks
            int topUp = Math.Min(gap, weeklyDemand);
            return Math.Max(1, weeklyDemand + topUp);
        }

        private static void TryUpdateNextDeliveryDay(DeliveryContract c)
        {
            try
            {
                var m = c.GetType().GetMethod("UpdateNextDeliveryDay", Type.EmptyTypes);
                m?.Invoke(c, null);
            }
            catch (Exception e) { Debug.LogWarning("[StoreManager] UpdateNextDeliveryDay failed: " + e.Message); }
        }

        private static BuildingRegistration? FindReg(string address)
        {
            return SaveGameManager.Current?.BuildingRegistrations?
                .FirstOrDefault(b => b != null && b.Address != null && b.Address.ToString() == address);
        }

        private static float? Price(DeliveryContract c)
        {
            try { return c.TotalPricePerDelivery; }
            catch { return null; }
        }

        private static string AddrString(Address? a)
        {
            try { return a?.ToString() ?? ""; } catch { return ""; }
        }
    }
}
