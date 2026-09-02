# v3 — native custom-skill roles (Filiaalmanager + Team Leader)

Supersedes v2's "reuse `ba:skill_purchasingagent`" (D10). The user wants two genuinely new
hired roles with their own job title, wage, and office hire flow. Full research +
two adversarial critiques: `docs/research/v3/`.

**D10 overridden. D9 / D12 / D13 / D14 stand.**

## The honest v1 (both critiques agree)

Ship **Store Manager only**, in isolation, and let it survive one real Big Ambitions patch
before doing Team Leader / the HQ tab. The v2 supervision loop already delivers the core value
(weekly supervised `DeliveryContract` restock) — v1 is just *re-parenting it onto one custom
skill* + making that skill safe.

### In v1
- **One custom skill** `sm:skill_storemanager`, built at runtime:
  `ScriptableObject.CreateInstance<SkillData>()` (`BigAmbitions.Characters.Skills.SkillData`),
  set: `skillName`, `name`, `baseHourlyWage` (~24, tuned in-game), `trainingCostMultiplier=1`,
  `secondarySkill=string.Empty` **(mandatory — kills the secondary-skill branch)**,
  `secondarySkillRange=new Vector2Int(5,20)`, `possibleDealbreakers=new List<string>()`,
  `associatedColorGradient=new Gradient()` with **both** colorKeys and alphaKeys set
  **(mandatory — `ScheduleHelper` calls `.EvaluateRandom()` unconditionally)**, `icon28=null`
  (tolerated). `BuildTagCache()` in try/catch → any throw = self-check failure → Dormant.
- **Injection: a Harmony *Prefix* on the public `SkillHelper.OnSkillDataLoaded(IList<SkillData>)`**
  that adds the mod SkillData to the incoming list — **no private-field reflection** (the
  stability critique's key change). `[ModEntryOnCityLoad]` backstop calls `OnSkillDataLoaded`
  again with `currentList + ours`. `Skills` is wiped+rebuilt on every save-load / new-game.
- **Defense-in-depth: Harmony Postfix on both `SkillHelper.GetData(string)` and `GetData(Skill)`**
  filling `__result` from a cached fallback when null. NB: this does **not** fully protect —
  `EmployeeInstance.HasAnySkillWithTag` does `x?.HasTag(tag) ?? true`, so a broken SkillData
  reads as *having every tag*. That's why `BuildTagCache()` must succeed (verified in-game).
- **Hiring: a direct mod action** calling
  `Helpers.RecruitmentHelper.GenerateCandidate("sm:skill_storemanager", 20f, hqAddress, null, 0f)`
  → the normal My Employees → Candidates → `HireCandidate` flow. **Do NOT mutate
  `BusinessType.employeePrimarySkills`** in v1 — it feeds AI staffing, `OfficeBusinessSimulator.First()`
  (throws if unmatched), rival defense. The Recruitment-Agency-dropdown integration
  (which needs that array) is a later opt-in, gated on ruling out AI-pool contamination.
- **Job title**: Localizor key == the skill id verbatim. `Locales/en.json` +`nl.json`:
  `"sm:skill_storemanager": "Store Manager"` (nl `"Filiaalmanager"`). **Unverified** whether the
  My Employees row template resolves the key or shows the literal id — in-game check #4.
- **Wage** = `baseHourlyWage * (pow(skillValue,1.05)/100+1) * salaryMult`. `baseHourlyWage` alone
  sets the rung. Tune against a runtime dump of vanilla baselines (`StoreManager.DumpSkill`).
- **The v2 supervision loop re-parented onto the skill** — `GameApi.ManagerSkill` flips from
  `ba:skill_purchasingagent` to `sm:skill_storemanager`. Everything else (`StoreManagerPlan` in
  `modData`, Saturday weekly pass, `DeliveryContract` restock within `WeeklyRestockBudgetCap`,
  feedback, read-only-on-corrupt) unchanged **except: drop `ContractSnapshot` / `OriginalContract`
  / `PendingRestore`** — detach = `enabled=false` + a toast (both critiques: cut it).
- **UI: the existing Options → Mods panel stays the only UI.** No HQ BizMan tab in v1.
- **Kill-switch** (part of v1, not deferred): one `RoleSystemState { Active, DegradedPanelOnly,
  Dormant }`. Ordered self-check at init — build number in `[3672, 3699]` (narrow!); every
  reflection/AccessTools handle non-null; probe SkillData `BuildTagCache()` + assert
  `HasTag(knownTag)` doesn't throw + a forced `OnSkillDataLoaded` re-run leaves `GetData`
  returning the probe; every `harmony.Patch()` returns non-null. First failure → lowest safe
  state, ONE toast + ONE phone message. Dormant = inject nothing, hire nothing, existing plans
  load read-only, planner no-ops, panel says "Store Manager role disabled on this game build —
  supervision paused, no data lost."
- **Uninstall safety** (part of v1): every hire persists
  `sm.employeeFallback.v1 : { employeeId → originalPrimarySkillName }` into `modData`. On
  `onSaveGame` **and** `onNewDay`, if `RoleSystemState != Active` or the skill didn't inject this
  session, re-skill any `sm:skill_*` employee back to the recorded fallback (or
  `ba:skill_purchasingagent`). A `StoreManager.SafeRemove` console command re-skills all mod
  employees, strips the role blobs, prints "safe to delete the mod folder now" — documented as
  THE uninstall procedure.

### RESOLVED (2026-09-02) — Option 1, mod-skill PRIMARY

Probe (`probe/StoreManagerProbe/SKILL-PROBE.md`) confirmed a folder-delete with a manager hired
NPEs vanilla at load (can brick the save). User's call: a mod save-risk is understood and
accepted; don't over-build around it. → **`skills[0] = sm:skill_storemanager`** (title shows
"Filiaalmanager"), **cheap** uninstall safety only (`OnUnloadAsync` re-skill +
`StoreManager.SafeRemove` command + documented procedure), no per-save/per-day rewrite.
`baseHourlyWage = 46`. `BuildTagCache()` + injection + hourly pass all verified safe. See D15.

### (historical) The one decision that needs the user — primary vs secondary skill

The common uninstall is **deleting the mod folder**, which never runs `OnUnloadAsync`. If a hired
Store Manager's **primary** skill is `sm:skill_storemanager` and the SkillData is then gone,
vanilla NPEs in wage calc / the employee card / `CompatibilityFixesEA03` / `ScheduleHelper` — the
employee is unfireable and **the save can be bricked**.

- **Option 1 — mod-skill primary** (`skills[0] = sm:skill_storemanager`): My Employees shows
  "Filiaalmanager" ✅ — exactly what the user asked. Folder-delete safety **depends on an in-game
  test** (does vanilla Build 3672 tolerate an unknown primary skill on load?). If it doesn't, we
  need the aggressive `onSaveGame`/`onNewDay` data-layer repair to be bulletproof.
- **Option 2 — mod-skill secondary** (`skills[0] = ba:skill_purchasingagent`,
  `skills[1] = sm:skill_storemanager`, plan driven off `HasSkill("sm:skill_storemanager")`):
  folder-delete is safe (primary is vanilla). But My Employees shows **"Purchasing Agent"** as
  the title — the "shows as Filiaalmanager" goal is lost.

**Resolve with the in-game test first** (below). If a mod-skill primary survives folder-delete,
go Option 1. If not, the user picks: accept the brick risk with heavy data-layer mitigation, or
accept "Purchasing Agent" as the shown title.

## Deferred (post-v1, only after Phase A survives a game patch)

- **Team Leader** (`sm:skill_teamleader`) + **departments** — a department is a mod-owned
  `{ Name, StoreAddress, List<string> ProductItemNames, string TeamLeaderEmployeeId, bool Dormant }`
  in the same `modData` blob (5 fields — cut `ShelfInstanceIds`, `Priority`). TL raises a weekly
  request per department from `DeliveryContractItem.amountOrderedLastWeek` (**never** the line's
  own current amount — the D12 compounding trap), Store Manager merges all requests for a store,
  trims **flat pro-rata** to `WeeklyRestockBudgetCap`, writes the store's **one** `DeliveryContract`.
  A store with no departments = one implicit whole-store department on the proven v2 `ComputeTarget`.
  Managed as a **drill-down inside the Store Manager UI**, not a separate tab.
- **HQ BizMan tab** — one Harmony Postfix on `BizManBusiness.SetUpTabs` (private void, global
  namespace): clone the live `PurchasingAgents` menu button, insert the id into private `_tabs`
  (`BizManBusiness.cs:166` kicks the user off a tab not in `_tabs`), `SetActive`. Runtime uGUI
  content. Gated + degrade-to-panel. Both critiques: highest fragility, lowest value — do it
  last, one tab only (Team Leader is a section inside it).
- **HQ desk `suitableSkills`** append (`ba:itemname_desktopcomputer`/`laptop`/`computer`) so the
  manager can take a real HQ shift → the plan gates on `IsAssignedToAnyWorkShift()`. v1 uses the
  `skipScheduleCheck` degrade (plan active whenever the manager is hired + `assignedAddress == HQ`).
- **Assistant Manager** — **not a third skill.** An `IsAssistant=true` flag on a Team Leader hire:
  +1 store cap, +1 TL capacity. Nothing else.
- **HQ "expansion"** — no game upgrade exists. Mod-invented capacity = live desk count
  (`GetAssignableItems().Count`); the player rents a bigger office / places more desks.
- Skill `icon28` PNG via `Sprite.Create`; Recruitment-Agency-dropdown integration; wage retune.

## Must verify in-game BEFORE coding Phase A (one probe run)

1. `SkillData.BuildTagCache()` on a runtime `CreateInstance<SkillData>()` — does it throw? Then
   does `HasTag(any TagRef.Skilltag value)` on that instance return false rather than throw? Then
   fake an employee holding only the mod skill and call `HasAnySkillWithTag(affectssecurity)` —
   no exception in the hourly security pass.
2. Does the `OnSkillDataLoaded` prefix apply + fire on new-game / load / city-change, and before
   `SaveGameCompatibilityFixes.ApplyCompatibilityFixes`?
3. **Delete the mod folder with 1 hired Store Manager on a test save — does the save still load
   fully vanilla?** Enumerate every NPE. This decides Option 1 vs 2.
4. My Employees row: `primarySkill.Arguments.skillName = "sm:skill_storemanager"` + the Locales
   key — does the row render "Store Manager" or the literal id?
5. `CalculateHourlyWageForSkill("sm:skill_storemanager", 20f)` output vs ~$30; dump vanilla
   `ba:skill_purchasingagent` / `ba:skill_customerservice` `baseHourlyWage` for calibration.
6. Does `ba:businesstype_headquarters` run `OfficeBusinessSimulator` / any per-hour sim that does
   `skills.First(x => employeePrimarySkills.Contains(x.name))` over scheduled staff? (If yes,
   appending to `employeePrimarySkills` becomes mandatory.)
7. Do AI rivals own `ba:businesstype_headquarters` registrations? (Contamination check for the
   deferred `employeePrimarySkills` mutation.)
