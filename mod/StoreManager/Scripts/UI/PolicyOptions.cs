#nullable enable
using BAModAPI;
using BigAmbitions.Mods;
using StoreManager.Domain;

namespace StoreManager.UI
{
    /// <summary>
    /// The policy panel, built with the SDK's ModOptions builder (confirmed API — see
    /// ExampleOptionsLogic in the SDK). v1 is a single global profile (D4); per-store overrides
    /// come later if Phase 0 shows ModOptions can't be scoped.
    /// </summary>
    public sealed class PolicyOptions
    {
        private const string StaffingKey = "storemanager_staffing";
        private const string RestockCapKey = "storemanager_restock_cap";
        private const string TrainingBudgetKey = "storemanager_training_budget";
        private const string AutoLeaveKey = "storemanager_auto_leave";
        private const string TrackMarginKey = "storemanager_track_margin";

        private static readonly string[] StaffingChoices =
        {
            "storemanager_policy_staffing_lean",
            "storemanager_policy_staffing_normal",
            "storemanager_policy_staffing_generous",
        };

        private readonly StorePolicy _policy;
        private ModContext _context = null!;

        public PolicyOptions(StorePolicy policy) => _policy = policy;

        public void Register(ModContext context)
        {
            _context = context;

            var options = new ModOptions()
                .AddHeader("storemanager_options_header")
                // Interim hiring controls (real UI is later polish). Act on the store you're
                // standing in; same path as the StoreManager.Hire console command.
                .AddButton("storemanager_btn_hire_manager", () => Debugging.StoreManagerCommands.Hire("manager", 4))
                .AddButton("storemanager_btn_hire_assistant", () => Debugging.StoreManagerCommands.Hire("assistant", 3))
                .AddButton("storemanager_btn_status", Debugging.StoreManagerCommands.Status)
                .AddSplitter()
                .AddDropdown(StaffingKey, "storemanager_policy_staffing_label", StaffingChoices,
                    (int)_policy.Staffing, i => _policy.Staffing = (StaffingLevel)i)
                .AddSlider(RestockCapKey, "storemanager_policy_restock_cap_label", 0, 20000,
                    (int)_policy.WeeklyRestockBudgetCap,
                    v => _policy.WeeklyRestockBudgetCap = v, "storemanager_policy_restock_cap_value")
                .AddSlider(TrainingBudgetKey, "storemanager_policy_training_budget_label", 0, 5000,
                    (int)_policy.WeeklyTrainingBudget,
                    v => _policy.WeeklyTrainingBudget = v, "storemanager_policy_training_budget_value")
                .AddToggle(AutoLeaveKey, "storemanager_policy_leave_autoapprove_label",
                    _policy.LeaveApproval == LeaveApprovalMode.AutoApprove,
                    on => _policy.LeaveApproval = on ? LeaveApprovalMode.AutoApprove : LeaveApprovalMode.AskPlayer)
                .AddToggle(TrackMarginKey, "storemanager_policy_price_follow_margin_label",
                    _policy.Pricing == PricePolicy.TrackTargetMargin,
                    on => _policy.Pricing = on ? PricePolicy.TrackTargetMargin : PricePolicy.Hold)
                .AddSplitter();

            OptionsService.Register(context.ModId, options);
            context.Logger.Info("Store Manager policy options registered.");
        }

        public void Unregister()
        {
            OptionsService.RemoveModOptions(_context.ModId);
            _context.Logger.Info("Store Manager policy options unregistered.");
        }
    }
}
