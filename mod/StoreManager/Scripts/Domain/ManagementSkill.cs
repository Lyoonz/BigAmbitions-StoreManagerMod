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
            Value = Num.Clamp(value, Min, Max);
        }

        /// <summary>
        /// Base probability that any given daily operation is botched, before difficulty scaling.
        /// skill 5 → 0.02, skill 3 → 0.12, skill 1 → 0.30. See MistakeModel for how it's applied.
        /// </summary>
        // Tuned against sim/BalanceSim (Normal ~ skill1 12% / skill3 4% / skill5 1% of weekly revenue).
        public float BaseMistakeChance => Value switch
        {
            5 => 0.015f,
            4 => 0.035f,
            3 => 0.070f,
            2 => 0.120f,
            _ => 0.190f,
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
            2 => 1.25f,
            _ => 1.5f,
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

        /// <summary>
        /// The game stores skills as a 0–100 float keyed by name (Phase 0: <c>EmployeeInstance.GetSkillValue</c>).
        /// The mod's 1–5 tuning scale maps ×20. HR Manager precedent: span of control also derives from
        /// the 0–100 value (<c>HrManagerSkillValue.CalculateMaxAssignableEmployees()</c>).
        /// </summary>
        public float ToGameScale() => Value * 20f;

        public static ManagementSkill FromGameScale(float skill0to100) =>
            new((int)System.Math.Round(skill0to100 / 20f));

        public override string ToString() => $"skill {Value} ({Tier})";
    }
}
