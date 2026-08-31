#nullable enable

namespace StoreManager.Domain
{
    public enum StaffingLevel
    {
        Lean,
        Normal,
        Generous,
    }

    public enum PricePolicy
    {
        /// <summary>Leave prices exactly where the player set them.</summary>
        Hold,
        /// <summary>Nudge prices to track <see cref="StorePolicy.TargetMarginPct"/>.</summary>
        TrackTargetMargin,
    }

    public enum LeaveApprovalMode
    {
        /// <summary>Manager approves within policy and arranges cover.</summary>
        AutoApprove,
        /// <summary>Every leave request is escalated to the player.</summary>
        AskPlayer,
    }

    /// <summary>
    /// The knobs the player sets once per managed store (or globally — see D4).
    /// The manager acts strictly within these; anything outside comes back as a request.
    /// </summary>
    public sealed class StorePolicy
    {
        public decimal WeeklyRestockBudgetCap { get; set; } = 5000m;
        public decimal WeeklyTrainingBudget { get; set; } = 0m;
        public StaffingLevel Staffing { get; set; } = StaffingLevel.Normal;
        public PricePolicy Pricing { get; set; } = PricePolicy.Hold;
        public double TargetMarginPct { get; set; } = 35d;
        public LeaveApprovalMode LeaveApproval { get; set; } = LeaveApprovalMode.AskPlayer;

        public static StorePolicy Default() => new();

        /// <summary>Target headcount multiplier applied to the store's baseline demand-driven need.</summary>
        public double StaffingMultiplier => Staffing switch
        {
            StaffingLevel.Lean => 0.8,
            StaffingLevel.Generous => 1.25,
            _ => 1.0,
        };
    }
}
