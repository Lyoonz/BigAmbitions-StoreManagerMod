#nullable enable

namespace StoreManager.Domain
{
    /// <summary>
    /// The leadership ladder this mod adds on top of the base employee.
    /// Order matters: each rank costs more and (except TeamLeader) has wider authority.
    /// </summary>
    public enum ManagerRank
    {
        /// <summary>Base game employee. One assigned task, no decisions. Listed for the wage ladder only.</summary>
        Employee = 0,

        /// <summary>Owns one store department. Raises stock requests, oversees section staff. v2+ (see D7).</summary>
        TeamLeader = 1,

        /// <summary>Full store control except training and hiring. Runs the store when the Manager is off.</summary>
        AssistantManager = 2,

        /// <summary>Full authority over one store, within the player's <see cref="StorePolicy"/>.</summary>
        Manager = 3,
    }

    public static class ManagerRankExtensions
    {
        public static bool CanArrangeTraining(this ManagerRank rank) => rank == ManagerRank.Manager;

        public static bool CanHireAndFire(this ManagerRank rank) => rank == ManagerRank.Manager;

        public static bool CanRunWholeStore(this ManagerRank rank) =>
            rank is ManagerRank.Manager or ManagerRank.AssistantManager;

        /// <summary>Placeholder wage band, USD/hour. Tuned in playtest — see DECISIONS.md.</summary>
        public static (int min, int max) WageBand(this ManagerRank rank) => rank switch
        {
            ManagerRank.Employee => (16, 18),
            ManagerRank.TeamLeader => (20, 23),
            ManagerRank.AssistantManager => (24, 27),
            ManagerRank.Manager => (28, 32),
            _ => (16, 18),
        };

        public static string LocaleKey(this ManagerRank rank) => rank switch
        {
            ManagerRank.TeamLeader => "storemanager_rank_teamleader",
            ManagerRank.AssistantManager => "storemanager_rank_assistant",
            ManagerRank.Manager => "storemanager_rank_manager",
            _ => "storemanager_rank_employee",
        };
    }
}
