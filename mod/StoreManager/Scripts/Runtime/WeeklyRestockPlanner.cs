#nullable enable
using System.Collections.Generic;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// Runs once per delivery week (Saturday, D12): for every store a manager supervises, tune the
    /// store's repeating delivery contract toward its stock buffer, within the weekly budget cap.
    /// Accumulates into <see cref="StoreManagerPlan.Week"/> for the digest. The plan's WeekTally and
    /// each assignment's SpentThisWeek are reset by the caller before this runs.
    /// </summary>
    public sealed class WeeklyRestockPlanner
    {
        public void Run(StoreManagerPlan plan)
        {
            var w = plan.Week;
            foreach (var a in plan.Assignments)
            {
                var r = DeliveryContracts.PlanAndApply(a);   // sets a.SpentThisWeek to the standing weekly cost

                if (!r.ContractFound)
                {
                    w.AttentionItems.Add($"{a.StoreName}: {r.Note}");
                    continue;
                }
                if (!r.Modifiable) continue;   // Monday lock — skip, retry next week

                w.StoresCovered++;
                w.RestockSpend += a.SpentThisWeek;   // the recurring charge, whether or not amounts changed this week

                if (r.OrdersAdjusted > 0)
                {
                    w.OrdersPlaced += r.OrdersAdjusted;
                    Feedback.Toast(Feedback.Level.Info, "storemanager_notify_restocked",
                        new Dictionary<string, string>
                        {
                            { "store", a.StoreName },
                            { "count", r.OrdersAdjusted.ToString() },
                            { "cost", $"{r.ProjectedWeeklyCost:N0}" },
                        }, dedupeId: "sm_restock_" + a.StoreAddress);
                }

                if (r.BudgetCapHit)
                {
                    w.BudgetCapsHit++;
                    w.AttentionItems.Add($"{a.StoreName}: {r.Note ?? "restock budget cap reached"}");
                    Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_budget_cap",
                        new Dictionary<string, string> { { "store", a.StoreName } },
                        dedupeId: "sm_cap_" + a.StoreAddress);
                }
                else if (!string.IsNullOrEmpty(r.Note))
                {
                    w.AttentionItems.Add($"{a.StoreName}: {r.Note}");
                }
            }
        }
    }
}
