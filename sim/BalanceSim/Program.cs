using System;
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;

// Balance simulation for the Store Manager mod.
// Pure — runs the real MistakeModel / ManagementSkill / DifficultyProfile domain code over
// many simulated weeks so the placeholder numbers can be tuned against data, not just feel.
//
//   dotnet run --project sim/BalanceSim
//
// Nominal store: $1,200/day revenue, open 7 days. Each day the manager attempts the full
// operation set. We measure manager-error cost as a % of revenue, and whether the wage
// premium for a better manager pays for itself.

const decimal DailyRevenue = 1200m;
const int Weeks = 52;
const int Seed = 12345;

var ops = new[]
{
    MistakeKind.UnderOrdered, MistakeKind.OverOrdered,
    MistakeKind.Understaffed, MistakeKind.Overstaffed,
    MistakeKind.UncoveredLeave, MistakeKind.ComplaintUnresolved,
    MistakeKind.TrainingMisspent,
};

// Manager wage premium per week (40h) over a $17/h base employee, midpoint of the band.
decimal WeeklyWagePremium(int skill)
{
    var (min, max) = ManagerRank.Manager.WageBand();
    var t = (skill - ManagementSkill.Min) / (double)(ManagementSkill.Max - ManagementSkill.Min);
    var wage = min + (max - min) * t;
    return (decimal)((wage - 17.0) * 40.0);
}

Console.WriteLine($"Store Manager — balance sim  ({Weeks} weeks/cell, ${DailyRevenue}/day, seed {Seed})\n");

foreach (GameDifficulty diff in Enum.GetValues(typeof(GameDifficulty)))
{
    var profile = DifficultyProfile.For(diff);
    Console.WriteLine($"── {diff}  (mistake freq x{profile.MistakeFrequencyMultiplier}, severity x{profile.MistakeSeverityMultiplier}) ──");
    Console.WriteLine($"{"skill",-6}{"tier",-10}{"mistakes/wk",-13}{"err cost/wk",-13}{"% of revenue",-14}{"wage premium/wk",-16}{"net vs skill-1",-14}");

    decimal skill1Cost = 0m;
    for (int skill = 1; skill <= 5; skill++)
    {
        var s = new ManagementSkill(skill);
        var model = new MistakeModel(s, profile);
        var rng = new Random(Seed + skill * 7 + (int)diff * 101);

        int totalMistakes = 0;
        decimal totalCost = 0m;
        decimal worstWeek = 0m;

        for (int w = 0; w < Weeks; w++)
        {
            decimal weekCost = 0m;
            for (int d = 0; d < 7; d++)
                foreach (var m in model.RollDay(rng, ops, DailyRevenue))
                {
                    totalMistakes++;
                    weekCost += m.EstimatedCost;
                }
            totalCost += weekCost;
            if (weekCost > worstWeek) worstWeek = weekCost;
        }

        decimal mistakesPerWeek = (decimal)totalMistakes / Weeks;
        decimal costPerWeek = totalCost / Weeks;
        decimal weeklyRevenue = DailyRevenue * 7;
        decimal pct = costPerWeek / weeklyRevenue * 100m;
        decimal premium = WeeklyWagePremium(skill);
        if (skill == 1) skill1Cost = costPerWeek;
        decimal netVsSkill1 = (skill1Cost - costPerWeek) - premium; // errors saved minus extra wage

        Console.WriteLine($"{skill,-6}{s.Tier,-10}{mistakesPerWeek,-13:F1}{costPerWeek,-13:C0}{pct,-14:F1}{premium,-16:C0}{netVsSkill1,-14:C0}");
        _ = worstWeek;
    }
    Console.WriteLine();
}

Console.WriteLine("Read: 'net vs skill-1' > 0 means a better manager pays for the wage premium in");
Console.WriteLine("error reduction alone (upside from a store that actually runs is on top).");
Console.WriteLine("Tune the constants in ManagementSkill / MistakeModel / DifficultyProfile, re-run.");
