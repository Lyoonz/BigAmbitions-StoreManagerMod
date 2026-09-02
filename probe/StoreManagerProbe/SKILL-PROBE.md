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

## Result — RUN 2026-09-02, Build 3672

| # | Finding | Verdict |
|---|---------|---------|
| 1 | `built SkillData OK` · `BuildTagCache method: TaggedScriptableObject.BuildTagCache` · `BuildTagCache(): ok, no throw` · all four `HasTag(...)` returned `False` cleanly | ✅ **runtime-built skill is viable** — tagless = the safe default (no forced full-time / job demands / hours-per-week) |
| 2 | `injected into Skills dict` · `GetData("sm:skill_storemanager") after inject = the SkillData` | ✅ injection + lookup work |
| 3 | **SkillData removed, employee keeps `sm:skill_storemanager` as PRIMARY:** `GetData(string)`→null (safe), `GetData(Skill)`→null (safe), but **`CalculateHourlyWageForSkill(string)` → NPE @ EmployeeHelper.cs:361**, **`CalculateHourlyWageForSkill(Skill)` → NPE @ EmployeeHelper.cs:353**, **`CompatibilityFixesEA03.EnsureAllFullTimeEmployeesHaveFullTimeDemand` → NPE**. `HasAnySkillWithTag` → returns `True` (null-safe, semantically wrong). | ❌ **mod-skill as PRIMARY bricks a folder-deleted save.** Wage calc + the EA03 load-time compat fix both NPE on a missing primary skill. |
| 4 | base `24` → `wage(sm,20) = 14.79`, `wage(sm,50) = 19.30`. Vanilla `baseHourlyWage`: purchasingagent / hrmanager / logisticsmanager = **30**, customerservice = 16. A ~0.5 salary multiplier is applied on top. | ⚠️ **base 24 is too low** (below the $16 cashier). Need base ≈ **45–48** to land the Filiaalmanager near $30. Pure constant. |
| 5 | `employee.HasAnySkillWithTag("affectssecurity") = False` (skill present) | ✅ hourly security pass is safe |
| 6 | `HQ registrations: player=0 rival/AI=0` — probe's ownership reflection failed (day-125 save *does* have a player HQ), so **AI-HQ ownership unresolved**. `OfficeBusinessSimulator present: True` (behaviour not probed). | ⏳ still open — only matters for the **deferred** `employeePrimarySkills` mutation; v1 uses direct `GenerateCandidate`, so not Phase-A-blocking |

### Decision forced by #3

- **Option 1 — mod-skill primary** (`skills[0] = sm:skill_storemanager`): title shows "Filiaalmanager", but a folder-delete while a manager is hired **can brick the save** (load-time compat-fix NPE). Only safe with a bulletproof `onSaveGame`/`onNewDay` repair that re-skills every `sm:skill_*` employee to `ba:skill_purchasingagent` *before every serialize* — and even then, deleting the folder mid-session before a save is unprotected.
- **Option 2 — mod-skill secondary** (`skills[0] = ba:skill_purchasingagent`, `skills[1] = sm:skill_storemanager`, plan driven off `HasSkill("sm:skill_storemanager")`): folder-delete is completely safe (primary is vanilla). Title shows **"Purchasing Agent"**. A later Harmony *display* shim on the My Employees row can relabel it without touching data.

**Recommendation was Option 2. User chose Option 1** (2026-09-02): a mod save-risk is understood
and accepted, don't over-build around it. → `skills[0] = sm:skill_storemanager`, cheap uninstall
safety only (`OnUnloadAsync` re-skill + `StoreManager.SafeRemove` + documented procedure). See D15.

