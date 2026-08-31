#nullable enable
using System;

namespace StoreManager.Domain
{
    /// <summary>
    /// A manager's competence, on the game's familiar 1–5 skill scale.
    /// Skill drives three things: how often mistakes fire, how big they are, and how many
    /// stores one manager can cover before quality drops (span of control).
    /// </summary>
    public readonly struct ManagementSkill
    {
        public const int Min = 1;
        public const int Max = 5;

        public int Value { get; }

        public ManagementSkill(int value)
        {
            Value = Math.Clamp(value, Min, Max);
        }

        /// <summary>
        /// Base probability that any given daily operation is botched, before difficulty scaling.
        /// skill 5 → 0.02, skill 3 → 0.12, skill 1 → 0.30. See MistakeModel for how it's applied.
        /// </summary>
        public float BaseMistakeChance => Value switch
        {
            5 => 0.02f,
            4 => 0.06f,
            3 => 0.12f,
            2 => 0.20f,
            _ => 0.30f,
        };

        /// <summary>
        /// Multiplier on the cost/impact of a mistake once it fires.
        /// A poor manager doesn't just err more often — the errors are worse.
        /// </summary>
        public float MistakeSeverity => Value switch
        {
            5 => 0.5f,
            4 => 0.7f,
            3 => 1.0f,
            2 => 1.4f,
            _ => 1.8f,
        };

        /// <summary>How many stores this manager can run well at once (Normal difficulty).</summary>
        public int BaseSpanOfControl => Value switch
        {
            5 => 3,
            4 => 2,
            _ => 1,
        };

        /// <summary>Tier label used in the design brief and the hiring UI.</summary>
        public string Tier => Value >= 4 ? "great" : Value == 3 ? "average" : "poor";

        public override string ToString() => $"skill {Value} ({Tier})";
    }
}
