# Balance — Store Manager

`dotnet run --project sim/BalanceSim` runs the real domain code (`MistakeModel`,
`ManagementSkill`, `DifficultyProfile`) over 52 simulated weeks per cell so the numbers
can be tuned against data. **Final tuning still wants playtest confirmation** — this just
gets the constants into a defensible range instead of arbitrary.

## Current tuning (nominal store: $1,200/day revenue)

Manager-error cost as **% of weekly revenue**:

| difficulty | skill 1 (poor) | skill 3 (average) | skill 5 (great) |
|------------|----------------|-------------------|-----------------|
| Easy       | 4.6 %          | 1.2 %             | 0.1 %           |
| Normal     | 11.9 %         | 2.9 %             | 0.3 %           |
| Hard       | 25.2 %         | 6.2 %             | 0.6 %           |

"Net vs skill-1" (error reduction minus the extra wage) — does a better manager pay off on
error-reduction alone (the store actually running is upside on top)?

| difficulty | when it pays off |
|------------|------------------|
| Easy   | never on ROI — you hire for convenience, not savings |
| Normal | skill 3+ |
| Hard   | skill 2+ — you can't afford a bad manager |

## Design reading

- **Poor manager (1–2):** noticeable drag on Normal, dangerous on Hard, tolerable on Easy.
- **Average (3):** low single-digit % — fine for a hands-off store.
- **Great (4–5):** near-zero errors — "ignore the store for weeks" as the brief promised.
- The wage ladder ($28–32/h for a Manager) is set so on Hard a cheap manager is a real gamble
  and a good one is a serious payroll line — matching StuartArmour's intent.

## The knobs

- `ManagementSkill.BaseMistakeChance` / `MistakeSeverity` — per-skill.
- `MistakeModel.BaseCostFor` — per-mistake-kind cost as a fraction of daily revenue.
- `DifficultyProfile.For` — the Easy/Normal/Hard multipliers.

Change, `dotnet run --project sim/BalanceSim`, re-read the table.
