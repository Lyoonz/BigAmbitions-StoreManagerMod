#nullable enable
using System;
using System.Collections.Generic;

namespace StoreManager.Domain
{
    public enum MistakeKind
    {
        UnderOrdered,
        OverOrdered,
        Understaffed,
        Overstaffed,
        UncoveredLeave,
        ComplaintUnresolved,
        TrainingMisspent,
    }

    public readonly struct Mistake
    {
        public MistakeKind Kind { get; }
        public string LocaleKey { get; }
        public decimal EstimatedCost { get; }

        public Mistake(MistakeKind kind, string localeKey, decimal estimatedCost)
        {
            Kind = kind;
            LocaleKey = localeKey;
            EstimatedCost = estimatedCost;
        }
    }

    /// <summary>
    /// Decides which mistakes a manager makes on a given operating day, from skill + difficulty.
    /// Pure and deterministic given the supplied RNG — the caller owns the seed so results are
    /// reproducible across a save reload.
    /// </summary>
    public sealed class MistakeModel
    {
        private readonly ManagementSkill _skill;
        private readonly DifficultyProfile _difficulty;

        public MistakeModel(ManagementSkill skill, DifficultyProfile difficulty)
        {
            _skill = skill;
            _difficulty = difficulty;
        }

        public float EffectiveMistakeChance =>
            Math.Clamp(_skill.BaseMistakeChance * _difficulty.MistakeFrequencyMultiplier, 0f, 0.95f);

        public float EffectiveSeverity =>
            _skill.MistakeSeverity * _difficulty.MistakeSeverityMultiplier;

        /// <summary>
        /// Roll each operation the manager performed today. <paramref name="operations"/> is the set
        /// of operation kinds actually attempted (no leave request today → no UncoveredLeave roll).
        /// </summary>
        public IReadOnlyList<Mistake> RollDay(Random rng, IEnumerable<MistakeKind> operations, decimal storeDailyRevenue)
        {
            var result = new List<Mistake>();
            foreach (var kind in operations)
            {
                if (rng.NextDouble() >= EffectiveMistakeChance)
                    continue;

                var baseCost = BaseCostFor(kind, storeDailyRevenue);
                var cost = Math.Round(baseCost * (decimal)EffectiveSeverity, 2);
                result.Add(new Mistake(kind, LocaleKeyFor(kind), cost));
            }
            return result;
        }

        private static decimal BaseCostFor(MistakeKind kind, decimal dailyRevenue) => kind switch
        {
            MistakeKind.UnderOrdered => dailyRevenue * 0.15m,   // lost sales
            MistakeKind.OverOrdered => dailyRevenue * 0.10m,    // cash tied up / spoilage
            MistakeKind.Understaffed => dailyRevenue * 0.12m,   // queues, walkouts
            MistakeKind.Overstaffed => dailyRevenue * 0.05m,    // wasted wages
            MistakeKind.UncoveredLeave => dailyRevenue * 0.20m,
            MistakeKind.ComplaintUnresolved => dailyRevenue * 0.08m, // reputation drag
            MistakeKind.TrainingMisspent => 250m,
            _ => 0m,
        };

        private static string LocaleKeyFor(MistakeKind kind) => kind switch
        {
            MistakeKind.UnderOrdered => "storemanager_mistake_understocked",
            MistakeKind.OverOrdered => "storemanager_mistake_overstocked",
            MistakeKind.Understaffed => "storemanager_mistake_understaffed",
            MistakeKind.Overstaffed => "storemanager_mistake_understaffed",
            MistakeKind.UncoveredLeave => "storemanager_mistake_uncovered_leave",
            MistakeKind.ComplaintUnresolved => "storemanager_mistake_complaint_unresolved",
            MistakeKind.TrainingMisspent => "storemanager_mistake_training_misspent",
            _ => "storemanager_mistake_understocked",
        };
    }
}
