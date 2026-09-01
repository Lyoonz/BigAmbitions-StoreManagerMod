#nullable enable
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// The weekly report the player gets as a phone message + a summary toast (requirement 1).
    /// </summary>
    public static class WeeklyDigest
    {
        public sealed class Report
        {
            public string ManagerName = string.Empty;
            public int StoresSupervised;
            public int StoresCovered;
            public int OrdersPlaced;
            public decimal RestockSpend;
            public int BudgetCapsHit;
            public List<string> Attention = new();
            public bool NeedsAttention => Attention.Count > 0;
        }

        public static Report Compose(StoreManagerPlan plan)
        {
            var w = plan.Week;
            return new Report
            {
                ManagerName = GameApi.FindManager(plan.ManagerEmployeeId)?.Name ?? "Your Store Manager",
                StoresSupervised = plan.Assignments.Count,
                StoresCovered = w.StoresCovered,
                OrdersPlaced = w.OrdersPlaced,
                RestockSpend = w.RestockSpend,
                BudgetCapsHit = w.BudgetCapsHit,
                Attention = w.AttentionItems.Distinct().ToList(),
            };
        }

        public static void Send(Report r)
        {
            var title = $"Weekly report — {r.ManagerName}";
            var body = string.Join("\n", new[]
            {
                $"Stores supervised: {r.StoresSupervised}  (restocked this week: {r.StoresCovered})",
                $"Delivery orders adjusted: {r.OrdersPlaced}",
                $"Restock spend: ${r.RestockSpend:N0}",
                r.BudgetCapsHit > 0 ? $"Budget caps hit: {r.BudgetCapsHit}" : null,
                r.NeedsAttention ? "Needs your attention:\n  - " + string.Join("\n  - ", r.Attention)
                                 : "Nothing needs your attention this week.",
            }.Where(l => l != null));

            Feedback.DigestMessage(title, body);
            Feedback.Toast(
                r.NeedsAttention ? Feedback.Level.Warning : Feedback.Level.Info,
                "storemanager_notify_weekly_digest",
                new Dictionary<string, string>
                {
                    { "manager", r.ManagerName },
                    { "stores", r.StoresCovered.ToString() },
                    { "spend", $"{r.RestockSpend:N0}" },
                },
                dedupeId: "sm_digest_" + r.ManagerName);
        }
    }
}
