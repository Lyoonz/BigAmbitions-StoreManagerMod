# v3 skill probe — one headless run

Resolves the "must verify in-game BEFORE coding Phase A" list in `docs/DESIGN-v3.md`.
Deployed alongside the v2 mod. Runs itself ~6 s after a city loads. **Read-only** — one
temporary dict entry + one in-memory throwaway employee, both dropped before it finishes.

## How to run it

1. Launch Big Ambitions (normal — no `-console` needed).
2. **Load the day-125 save** (*Foundation Headquarters* + *The Signature Mart*) — or any save
   with an HQ and a shop that has a wholesale delivery contract.
3. Wait ~15 seconds on the city screen.
4. **Quit to desktop WITHOUT saving.** (A throwaway employee was briefly in memory.)
5. Tell me it's done — I read `AppData/LocalLow/Hovgaard Games/Big Ambitions/Player.log`
   (`[SKILLPROBE]` lines).

## What it answers

| # | Question | Decides |
|---|----------|---------|
| 1 | Does `SkillData.BuildTagCache()` throw on a runtime `CreateInstance<SkillData>()`? Does `HasTag()` then return false instead of throwing? | whether a runtime-built skill is viable at all |
| 2 | Does `SkillHelper.Skills` inject cleanly + does `GetData()` return it? | injection mechanism |
| 3 | **With the SkillData removed but an employee still holding `sm:skill_storemanager` as PRIMARY — do wage calc / `GetData(Skill)` / the EA03 compat fix NPE?** | **Option 1 vs Option 2** (mod-skill primary vs secondary) |
| 4 | `CalculateHourlyWageForSkill("sm:skill_storemanager", 20)` vs the ~$30 target + vanilla baselines dump | `baseHourlyWage` constant |
| 5 | `employee.HasAnySkillWithTag("affectssecurity")` in the hourly pass — throws? | hourly-tick safety |
| 6 | Do AI rivals own HQ registrations? | whether the deferred `employeePrimarySkills` mutation is safe |

## Result

_(paste run outcome here)_
