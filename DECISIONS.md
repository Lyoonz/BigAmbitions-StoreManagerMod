# Decision log — Store Manager Mod

Decisions made while executing the plan of approach, with the evidence they rest on.
Mirrored into the design-brief artifact.


## D1 — Toolchain: the official SDK, not BepInEx

**Decision.** Build on the official modding SDK (`BAModAPI`, shipped in `BigAmbitions.ModAPI.dll`).

**Evidence.** The SDK's example-mod asmdef (`Example-Options.asmdef`) sets `"overrideReferences": true`
and lists **every** game DLL as a precompiled reference — including `BigAmbitions.dll`,
`BigAmbitions.Characters.dll`, `BigAmbitions.AI.dll`, `BigAmbitions.Neighborhoods.dll`,
`BigAmbitions.Legacy.dll`, `OdinSerializer.dll`, `Google.OrTools.dll`. The example mods call
game internals directly: `GameManager.ChangeMoneySafe(...)`, `SaveGameManager.Current`,
`saveGame.VehicleInstances`, `BuildingManager.Instance`, `BuildingHelper.GetBuilding(Address)`,
`ItemsGetter.AllItems`, and freely mutate live game-data objects (patch shelves, add to importer
settings) with a save/restore-on-unload pattern.

So a mod already has **full public reach** into the workforce/economy systems. BepInEx's value —
runtime access to game code — is already provided. BepInEx would cost us: the first-party loader,
`OptionsService` UI, smartphone messaging, `ModdingAPI` registration hooks, and in-game Mod Creator
distribution.

**Consequence.** `Interop/GameBindings.cs` is the single seam that touches game types. If Phase 0
finds a hook that direct access can't reach (subscribe to a per-tick/per-day event, intercept a
method mid-call), see D2 — not a switch to BepInEx.

## D2 — Patching, if needed: Harmony as a bundled dependency

**Decision.** If a runtime hook is unreachable by direct calls, ship `0Harmony.dll` as a flat
managed DLL in the mod's `Dependencies/` folder and patch from inside the SDK mod.

**Which build.** **Lib.Harmony 2.3.3** (`pardeike/Harmony`, from the `Lib.Harmony` NuGet package) —
the self-contained ~2.2 MB `0Harmony.dll` with MonoMod/Cecil ILRepacked in. **Not HarmonyX** (the
BepInEx fork, small `0Harmony.dll` + separate `MonoMod.*` / `Mono.Cecil.*`): the code binds to
`Harmony.Patch(MethodBase, HarmonyMethod × 5)` with the trailing `ilmanipulator` parameter and to
`Harmony.UnpatchSelf()`, both added in Lib.Harmony 2.3 and absent from HarmonyX 2.10.x. A build
shipped HarmonyX 2.10.2 by mistake — the load-bearing `PatchAll` skill patches still applied, but
`BizManTabPatch` / `RecruitmentPatch` / `HqCardPatch` threw `MissingMethodException` at setup, so
the HQ "Store Managers" tab, the agency skill entry and the HQ card counter silently never
appeared. Keep the bundled DLL in sync with *Lower Installation Fee*, which ships the same 2.3.3.

**Evidence.** `ModValidator` rule 12: "The Dependencies folder must be flat with only `.dll` files"
— third-party managed DLLs are an expected, validated case. Nothing in the validator restricts
gameplay manipulation or method patching.

**Consequence.** Still one mod, one loader, one distribution channel. Harmony is opt-in and isolated
to whichever `GameBindings` method needs it.

## D3 — Manager is an off-screen role, not a spawned NPC (v1)

**Decision.** A hired manager is a data role attached to a business/building. No character model,
pathfinding, or floor presence in v1.

**Evidence / rationale.** Matches the game's existing meta-roles (HR Manager, Logistics Manager,
Pricing Manager). Physical presence is high cost (Behavior Designer trees — `BehaviorDesigner.Runtime.dll`
is a game dep — plus nav) for low mechanical value. Revisit as polish.

## D4 — Digest & policy UI: smartphone messages + options panel (v1)

**Decision.** Weekly digest → smartphone messages via `Contact` / `TextMessage` (proven by the
BackAlleyDealer example). Policy knobs (restock budget cap, staffing level, leave approval mode,
training budget, price policy) → an `OptionsService` / `ModOptions` panel (`AddHeader` / `AddToggle`
/ `AddSlider` / `AddDropdown` — all shown in `ExampleOptionsLogic`). A bespoke native window is
deferred.

**Open sub-question for Phase 0.** Whether `ModOptions` can be scoped per-store or only globally.
If global-only, v1 ships one policy profile applied to every managed store; per-store overrides
come later via a small custom window.

## D5 — Scheduling: call the game's scheduler, don't reimplement

**Decision.** The manager's rostering delegates to the game's existing scheduling system rather
than computing shifts itself.

**Evidence.** `Google.OrTools.dll` (Google's constraint/optimization solver) is a shipped game
dependency — the game already models scheduling/logistics as an optimization problem. Reimplementing
would diverge from game behaviour and double the surface area.

**Consequence.** Phase 0 probe #2 ("write a roster entry") must locate that entry point in
`BigAmbitions.dll` / `BigAmbitions.AI.dll` and confirm a mod can invoke it with constraints
(who's available, target staffing level).

## D6 — Persistence: additive per-store blob on the game save

**Decision.** Persist a `StoreManagerData` record per managed store, keyed by mod id + store id,
written into the game save; removed cleanly on `OnUnloadAsync`.

**Evidence.** Game uses `OdinSerializer`; `SaveGameManager.Current` is reachable; the SDK's
save/restore-on-unload pattern is the established idiom.

**Open sub-question for Phase 0.** Exact mod-save API (a `ModContext` save hook vs. piggybacking
an existing container). Design assumes additive + reversible either way.

## D7 — Team Leaders / departments: out of scope for v1 and v2

**Decision.** No department (Frozen/Fresh/Produce) or Team Leader tier until the base mod ships
and the community asks.

**Evidence.** No department concept visible in the SDK surface or example mods; `BusinessType`
is the finest business-structure unit exposed. Building departments would be mod-invented from
scratch. One of the requesters flagged it as "a bit much" themselves.

## D8 — Project lives as a standalone repo

`C:\Source\BigAmbitions-StoreManagerMod\`, structured so `mod/StoreManager/` drops straight into
`<SDK clone>/Assets/Mods/StoreManager/`. In a separate standalone repo (unrelated to any other project).

## Wage ladder (placeholder numbers, for playtest tuning)

| Rank | Hourly | One-time hire fee (skill-scaled) |
|------|--------|----------------------------------|
| Employee | $16–18 | — |
| Team Leader | $20–23 | low |
| Assistant Manager | $24–27 | mid |
| Manager | $28–32 | high |

*(Superseded by D10/D11 — the manager is now a real hired employee on `ba:skill_purchasingagent`,
paid the game's own wage for that skill via `EmployeeHelper.CalculateHourlyWageForSkill`. The
ladder above is vestigial.)*

---

# v2 pivot — the manager becomes a real meta-role (2026-09-01)

The user rejected v1's off-screen data role. They want: hire via the office like an HR Manager,
appear in My Employees, schedule the manager, assign to N stores, set per-store limits,
auto-restock via delivery — all with visible feedback. Backed by a research + design workflow
and an in-game reflection dump. Full architecture: `docs/DESIGN-v2.md`. (design rationale is captured here and in `docs/DESIGN-v2.md`).

## D9 — Architecture: Option B (mod-owned supervision plan)

A parallel **mod-owned plan** that mimics `HrManagerPlan`/`LogisticsManagerPlan`, all state in
`GameInstance.modData`. **Reject** Option A (piggyback a real `LogisticsManagerPlan` — it
restocks from a *warehouse*, not a wholesale contract; its `GameInstance` list can't be safely
grown). **Reject** Option C (Harmony-inject a real plan type — dual serializers Newtonsoft
`TypeNameHandling.Auto` + OdinSerializer binary + `Player.SaveSystem.CompatibilityFixes.*` purge
passes → save-corruption risk).

## D10 — Manager skill: `ba:skill_purchasingagent`

A real "Purchasing Agent" skill. **Correction (v2 static review):** it *does* have an HQ tab
(`PurchasingAgentsPlanList`) — but that drives `ImportPartnership` (importer/warehouse) contracts,
**not** the store `DeliveryContract` the mod tunes, so there is no direct mechanism collision.
The dual-binding guard (`GameApi.IsBoundToVanillaPlan`) now checks all four:
`LogisticsManagerHelper` / `HrManagerHelper` / `PricingManagerHelper` /
`PurchasingAgentHelper.GetAssignedPlanForPurchasingAgent` — so the same employee can't run a mod
plan and a vanilla plan at once. **No custom skill** — `SkillHelper.GetData(name)` is called
unguarded in candidate generation / wage calc / the employee card, and `SkillHelper` doesn't
persist mod skills across reloads.

## D11 — v1 scope: the TRIMMED version (both adversarial reviews)

**In:** vanilla recruit→hire→schedule (requirements 2/3/4 for free); a `ModOption.SpawnUi` panel
+ console for assign-to-N-stores + per-store limits (5/6); a weekly delivery-contract planner
within budget (7); full visible feedback (1); reconcile-on-fire.
**Out (mostly permanently):** Harmony-injected HQ tab, custom skill, `MistakeModel` /
`ManagerRank` ladder / `DifficultyProfile` coupling, `ScheduleAutoFiller` roster top-up,
complaints/leave/training, price-policy writes, contract snapshot/restore beyond `enabled=false`,
3-day grace state machine, player self-scheduling.

## D12 — Restock runs on the WEEKLY Monday delivery cycle, not per-day

`DeliveryHelper`: `DeliveryDay` = Monday, delivery ~08:00, lock Sun ~20:00 → Mon ~08:00. The
planner runs **once per week** (trigger `onNewDay` when day-of-week is Saturday), computes next
Monday's per-store order within `WeeklyRestockBudgetCap`, respects `CanModifyContract`, no-ops
the rest of the week.

**Correction (v2 static review):** the per-line order target is `amountOrderedLastWeek` (real
units the game delivered last week) plus a capped gap top-up toward
`CountTotalResourcesInStock` vs `TargetDaysOfStock`. It must **never** be a function of the
line's own current `amount` (the first draft did `round(GetOrderAmount * TargetDays/7)` and wrote
it back → geometric compounding, since `DeliveryHelper.GetOrderAmount` just echoes `amount`).
The target now converges to steady-state weekly demand.

## D14 — Unreadable saved blob → read-only mode, never overwrite

If `GameInstance.modData["StoreManager.plans.v1"]` is present but won't parse (game/Odin update,
assembly rename, partial write), the directory enters **read-only mode**: it takes no actions,
writes nothing, shows a warning toast, and leaves the blob intact for a future version to
recover. It must never silently replace an unreadable-but-present blob with an empty document —
that would also destroy every `OriginalContract` snapshot (the only record of the player's real
pre-supervision delivery contracts).

## D13 — Persistence: `GameInstance.modData["StoreManager.plans.v1"]`

Confirmed `Dictionary<string,string>` and **already used by the CosaNostra mod** → proven to
round-trip the save. Plain JSON of `List<StoreManagerPlan>`. Write on `GlobalEvents.onSaveGame`,
read on `[ModEntryOnCityLoad]`. `persistentDataPath` file (`Interop/ModDataStore.cs`) stays as a
keyed fallback, wired from day one. On load: drop plans whose `ManagerEmployeeId` doesn't resolve;
set their contracts `enabled=false`.

## D3, D4, D5, D6 — amended by the v2 pivot

- **D3** (was: off-screen data role): the manager is now a real `Entities.EmployeeInstance`
  recruited through the vanilla flow, hired via `EmployeeHelper.HireCandidate`, in My Employees
  with a real wage and a player-set schedule. The mod adds only the supervision plan; never
  subclasses `EmployeeInstance`. Still no bespoke NPC body.
- **D4** (was: global ModOptions + phone digest): per-action `Notifications.Show` toast +
  weekly `Contact`/`TextMessage` thread + finance line items. Hire/assign/limits UI is a
  **per-store** `ModOption.SpawnUi` panel; global ModOptions keeps only defaults.
- **D5** (add): the manager is rostered by the player via the vanilla BizMan → HQ → Schedule
  tab. The mod plan is gated on `EmployeeInstance.IsAssignedToAnyWorkShift()` at the HQ and goes
  dormant when unscheduled.
- **D6** (was: per-store `StoreManagerData` file): now a plan-centric `List<StoreManagerPlan>`
  in `GameInstance.modData` (see D13). The `persistentDataPath` file is demoted to fallback.

## D15 — v3 pivot: two genuinely new native skills; D10 overridden

The user wants **two new hired roles** (`Filiaalmanager` / Store Manager + `Team Leader`), each
with its own job title, wage rung, and office hire flow — not a reused `ba:skill_purchasingagent`.
Plan in `docs/DESIGN-v3.md`.

**Phase A (first native release) = Store Manager only.** One custom skill
`sm:skill_storemanager`, built at runtime via `ScriptableObject.CreateInstance<SkillData>()`
(`secondarySkill = string.Empty`, non-null `associatedColorGradient`, `BuildTagCache()` in
try/catch), injected via a Harmony **Prefix on `SkillHelper.OnSkillDataLoaded(IList<SkillData>)`**
+ a `[ModEntryOnCityLoad]` backstop, with defense-in-depth Harmony **Postfix on both
`SkillHelper.GetData` overloads**. The v2 supervision loop is re-parented onto it
(`GameApi.ManagerSkill` → `sm:skill_storemanager`); drop `ContractSnapshot` / `OriginalContract`
/ `PendingRestore` (detach = `enabled=false` + toast). No HQ BizMan tab in v1 (highest-risk,
lowest-value — the Options panel already covers it). Kill-switch `RoleSystemState { Active,
DegradedPanelOnly, Dormant }` gated on build number `[3672, 3699]` + non-null Harmony handles.

**Skill placement — Option 1 (mod-skill PRIMARY), `skills[0] = sm:skill_storemanager`.**
The 2026-09-02 probe (`probe/StoreManagerProbe/SKILL-PROBE.md`) confirmed a folder-delete while
a manager is hired **NPEs vanilla at load** (`CalculateHourlyWageForSkill`,
`CompatibilityFixesEA03`) — i.e. can brick the save. User's call: *"if you use a mod you accept
your save can fail — don't weigh this too heavily."* So: primary skill (title shows
"Filiaalmanager"), **cheap** uninstall safety only — `OnUnloadAsync` re-skills every `sm:skill_*`
employee to `ba:skill_purchasingagent`, a `StoreManager.SafeRemove` console command does the same
on demand, and the readme documents "run SafeRemove before deleting the mod folder." **No**
per-save / per-day rewrite ceremony.

**Wage:** probe showed vanilla manager `baseHourlyWage = 30` with a ~0.5 multiplier on top;
base 24 landed at $14.79. Phase A uses `baseHourlyWage = 46` (→ ~$30 near skill 20), tunable.

**Deferred (post-v1, only after Phase A survives a game patch):** `sm:skill_teamleader` +
a 5-field `Department` record + flat pro-rata over-budget trim (drill-down inside the Store
Manager UI); the one HQ BizMan tab (Harmony Postfix on `BizManBusiness.SetUpTabs`); HQ desk
`suitableSkills` append; Assistant Manager as an `IsAssistant` flag on a Team Leader (never a
third skill); skill `icon28` PNG. Still open: do AI rivals own HQ registrations (probe's
ownership read failed) — gates the deferred `employeePrimarySkills` mutation only.
