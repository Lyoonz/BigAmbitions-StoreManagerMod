#nullable enable
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// Builds the weekly report a player gets as a smartphone message (D4).
    /// Localisation keys resolve game-side; this only assembles the arguments.
    /// </summary>
    public static class WeeklyDigest
    {
        public sealed class Report
        {
            public string StoreName = string.Empty;
            public decimal Revenue;
            public decimal RevenueDelta;
            public decimal RestockSpend;
            public int ShiftsCovered;
            public int ShiftsTotal;
            public int ComplaintsResolved;
            public int ComplaintsTotal;
            public int MistakeCount;
            public decimal MistakeCost;
            public List<string> AttentionItems = new();

            public bool NeedsAttention => AttentionItems.Count > 0;
        }

        public static Report Compose(GameRef store, StoreManagerData data)
        {
            var w = data.CurrentWeek;
            return new Report
            {
                StoreName = store.DisplayName,
                Revenue = w.Revenue,
                RevenueDelta = w.Revenue - w.RevenueLastWeek,
                RestockSpend = w.RestockSpend,
                ShiftsCovered = w.ShiftsCovered,
                ShiftsTotal = w.ShiftsTotal,
                ComplaintsResolved = w.ComplaintsResolved,
                ComplaintsTotal = w.ComplaintsTotal,
                MistakeCount = w.MistakeCount,
                MistakeCost = w.MistakeCost,
                AttentionItems = w.AttentionItems.Distinct().ToList(),
            };
        }

        public static void Send(IGameBindings game, Report report)
        {
            var title = Loc.Format("storemanager_digest_title", report.StoreName);

            var lines = new List<string>
            {
                Loc.Format("storemanager_digest_line_revenue", Money(report.Revenue), Delta(report.RevenueDelta)),
                Loc.Format("storemanager_digest_line_restock", Money(report.RestockSpend)),
                Loc.Format("storemanager_digest_line_shifts", report.ShiftsCovered, report.ShiftsTotal),
                Loc.Format("storemanager_digest_line_complaints", report.ComplaintsResolved, report.ComplaintsTotal),
            };

            lines.Add(report.NeedsAttention
                ? Loc.Format("storemanager_digest_line_attention", string.Join("; ", report.AttentionItems))
                : Loc.Get("storemanager_digest_all_good"));

            game.SendManagerMessage(title, string.Join("\n", lines));
        }

        private static string Money(decimal v) => v.ToString("N0");
        private static string Delta(decimal v) => (v >= 0 ? "+" : "-") + System.Math.Abs(v).ToString("N0");
    }
}
