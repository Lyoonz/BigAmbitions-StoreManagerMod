# Phase 0 probe report

**RUN — 2026-08-31, Big Ambitions Build 3670.** The mod was built with `dotnet` (net472),
hand-packaged into `ModsLocal/`, and the real game was launched. The probe auto-loaded the
last save and ran headless. No Unity needed.

## What the run proved

| Check | Result |
|-------|--------|
| `dotnet`-built net472 DLL loads in the real game | ✅ `[Mod:StoreManager]` / `[Mod:StoreManagerProbe]` both discovered & loaded |
| `ModCompatibilityValidator` (major-version check) | ✅ passed — no `Incompatible` line |
| `[ModEntryOnInitializationLoad]` fires | ✅ `Store Manager policy options registered.` |
| `[ModEntryMainMenu]` fires | ✅ probe auto-loaded last save (`LoadAsync(null,true) = True`) |
| `[ModEntryOnCityLoad]` fires | ✅ `Store Manager active — 0 managed store(s).` + probe city hook |
| `OptionsService.Register` / `ModOptions` builder | ✅ no error |
| `ManagerDirectory.Load()` + `ModDataStore` file read | ✅ ran, no file yet, no throw |
| `SaveGameManager.Current.BuildingRegistrations` | ✅ `regs=885` enumerated |
| `TimeHelper.CurrentDay` / `.GetDayOfWeek()` | ✅ `day=8 dow=Monday` |
| `GetPlayerStores()` filter + `BuildingRegistration.BusinessName / .Address / .businessTypeName / .scheduleDays` | ✅ `STORE 'Unity Grill' addr=ba:street_tenthstreet 3 type=ba:businesstype_fastfoodrestaurant days=7` |
| `.satisfaction.overall` (reputation) | ✅ `sat=77` |
| `.GetAvgDailyIncome(1)` (revenue) | ✅ `avgDailyIncome=-33` |
| Exceptions from mod/probe | ✅ **none** |
| Save files modified by the run | ✅ **none** (byte-identical before/after — `LoadAsync` doesn't write) |

## Write-path — ALL CONFIRMED in-game (2026-08-31, via an in-memory throwaway employee)

The day-8 save had zero staff, so the probe injects a throwaway `EmployeeInstance` at Unity
Grill (in memory only, removed at the end, never saved) and exercises the write path on it.

| Write | Result |
|-------|--------|
| **Shift** — `ScheduleDay.AddWorkShift(new WorkShift{...})` | ✅ `shifts 0 → 1`; **RECHECK 20s later: `added shift still present = True`** — the sim keeps a mod-written shift |
| **Task assignment** — `EmployeeInstance.assignedWorkStationItems` direct write | ❌ **wiped** — `stations after = []`. It is a *derived* field: `UpdateAssignedWorkStationItems()` recomputes it from the schedule |
| **Task assignment — correct mechanism** | put the employee on a `WorkShift` whose `itemInstanceId` is the target station (`BuildingRegistration.GetAssignableItems()` → the workstations; Unity Grill had `[cleaningstation, cashregister]`). `GameBindings.AssignTask` now does this. |
| **Restock** — `DeliveryContractItem.amount` | ✅ `500 → 505` recomputed `TotalPricePerDelivery` `5238 → 5239` |
| Save files modified | ✅ none — byte-identical before/after every run |

**Phase 0 is complete.** Every binding the mod needs — read and write — is verified against the
running game. Verdict: **GO**, no caveats.

## (historical) Write-path — restock CONFIRMED, task/shift blocked by save content

**Probe #3 (restock) — ✅ verified in-game.** `SaveGameManager.Current.DeliveryContracts` had
1 contract for Unity Grill (wholesale `ba:street_twelfthstreet 13`, 9 items, `TotalPricePerDelivery`
5238). Bumping `DeliveryContractItem.amount` 500→505 on `ba:itemname_sodacan` recomputed
`TotalPricePerDelivery` to 5239 live — the mod's restock path (`GameBindings.PlaceRestockOrder`
via delivery contracts) works. Restored cleanly.

**Probes #1 / #2 (task / shift) — blocked by the save, not by capability.** The DUMP reported
`staffedBusinesses=0` across **all 885** building registrations — this day-8 save has no hired
employees anywhere, so there's no real `EmployeeInstance` to reassign or roster. The APIs used
(`EmployeeInstance.assignedWorkStationItems` + `UpdateAssignedWorkStationItems()`,
`ScheduleDay.AddWorkShift`) are the game's own, so confidence is high — but the behavioural
"does the sim keep it" check needs **a save where the player has hired staff**.

| Probe | Result |
|-------|--------|
| 1 — reassign task | ⏳ needs a save with hired employees (this save: 0 staff) |
| 2 — write shift (`ScheduleDay.AddWorkShift`) | ⏳ same — game's own method, low risk |
| 2 — write shift (`ScheduleAutoFiller`) | ⏳ ctor `(employees, registration, day)` confirmed; run on a staffed save |
| 3 — restock (`DeliveryContract`) | ✅ **verified** — `item.amount` write recomputes cost |

## Environment
- Game: Steam Build 3670, **Mono** (managed DLLs are real .NET).
- Built with `dotnet build` targeting **net472** (matches `CosaNostra.dll` in the install). No Unity used.
- Deploy: `build/deploy-local.sh` (or `--probe`).

## Workforce code map — from decompile (see ../../PHASE0-FINDINGS.md for detail)

| System | Type (namespace.Class) | Assembly | public? | Notes |
|--------|------------------------|----------|---------|-------|
| Employee (persistent) | `Entities.EmployeeInstance` | BigAmbitions | ✅ | `id, hourlyWage, assignedAddress, assignedWorkStationItems, satisfaction, trainingSession, nextSickDay, isAbsent, assignedWeeklyDays/Hours, complaintData` |
| Employee (in-world) | `Employee : MonoBehaviour` | BigAmbitions | ✅ | `employeeInstance, employeeStationController, isPlayer, IsAway` |
| Employee ops | `Helpers.EmployeeHelper` | BigAmbitions | ✅ | `GetEmployeeInstances(query), GetEmployeeById, GetSkillOfEmployee, CalculateHourlyWageForSkill, GetTrainingCost, HireCandidate, RunDaily, ForceEmployeeSickNextDay` |
| Task / station | `EmployeeStationController : Producer` | BigAmbitions | ✅ | `AssignEmployee(tpc, instance), UnassignEmployee()` + `EmployeeInstance.assignedWorkStationItems` |
| Store | `Entities.BuildingRegistration` | BigAmbitions | ✅ | `scheduleDays:List<ScheduleDay>, businessTypeName, Address, RentedByPlayer, BuildingOwnedByPlayer`. All via `SaveGameManager.Current.BuildingRegistrations` |
| Schedule | `ScheduleDay` / `WorkShift` (`[Serializable]`) | BigAmbitions | ✅ | `ScheduleDay.workShifts, AddWorkShift(), RemoveWorkShift()`; `WorkShift{startingHour,endingHour,employeeId,itemInstanceId,type}` |
| Scheduler / solver | `Buildings.Schedule.ScheduleAutoFiller` | BigAmbitions | ✅ | Google.OrTools CP-SAT. `onProgress/onCompleted` UnityEvents, `.fast`. **VERIFY ctor + headless run** |
| Schedule helpers | `UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper` | BigAmbitions | ✅ | static; `Business, ScheduleDays, Employees, GetWorkShiftsByEmployeeId` |
| Complaints | `AI.Employees.Complaint` + subtypes, `ComplaintHelper`, `Entities.EmployeeComplaintData` | BigAmbitions | ✅ | subtypes: NoTaskAssigned / LowSkill / LowSatisfaction / UnfulfilledDemands. **VERIFY resolve path** |
| Reputation | *not yet located* | — | ? | **VERIFY** — check `BuildingRegistration` / retail simulation |
| Training | `EmployeeInstance.trainingSession` (`TrainingInstance{skill,startDay}`), `EmployeeHelper.GetTrainingCost` | BigAmbitions | ✅ | HR path: `HrManagerPlan.TrainEmployees()` |
| Meta-role TEMPLATE | `Buildings.Office.Headquarters.HrManagerPlan` / `PricingManagerPlan` | BigAmbitions | ✅ | **copy this** — `assignedEmployeeId, assignedEmployees:List<string>, policy fields, TrainEmployees(), MaxEmployees from skill`. Persisted in `SaveGameManager.Current` |
| Restock (shelf) | `ReStockingHelper.RedistributeStockByPercentage(BuildingRegistration, List<ItemInstance>)` | BigAmbitions | ✅ | warehouse→shelf only. **VERIFY the wholesale purchase path** |
| Money | `GameManager.ChangeMoneySafe(float, TransactionInfo, int?, Address, bool force, bool showNotification)` | BigAmbitions | ✅ | static, returns bool |
| Save | `SaveGameManager.Current` → `GameInstance`; `IsModdedSave`, `MarkChange()` | BigAmbitions | ✅ | **no per-mod slot** — mod data → file (see `ModDataStore.cs`) |
| Difficulty | `Player.DifficultySettings.Difficulty` enum, `GameVariables.difficulty`, `DifficultySetting` multipliers | BigAmbitions | ✅ | **VERIFY Easy/Hard enum member names** (source shows `Normal`, `Custom`) |
| Day / week tick | `GlobalEvents.onNewDay` / `onNewHour` / `onSaveGame` (static `Action`) | BigAmbitions | ✅ | no native weekly — derive from date |
| Mod save API | *none on `ModContext`* (`ModRootPath`, `ModId`, `Logger` only) | BigAmbitions.ModAPI | — | → file-based, `Application.persistentDataPath/Mods/StoreManager/` |
| Mod options | `BigAmbitions.Mods.ModOptions` / `OptionsService` | BigAmbitions.ModAPI | ✅ | API matches `PolicyOptions.cs` exactly; values auto-persist by id |
| Smartphone messaging | `UI.Smartphone.Apps.Contacts` (`Contact.GetContact`, `Contact.SendMessage`, `TextMessage`) | BigAmbitions | ✅ | pattern from BackAlleyDealer example |

## Probe results — RUN IN-GAME

| Probe | Result | Private-method patching? | Sim honours it? |
|-------|--------|--------------------------|-----------------|
| 1 — reassign task (`assignedWorkStationItems`) | ☐ worked ☐ partial ☐ blocked | | |
| 1 — reassign task (`EmployeeStationController`) | ☐ worked ☐ partial ☐ blocked | | |
| 2 — write shift (`ScheduleDay.AddWorkShift`) | ☐ worked ☐ partial ☐ blocked | | |
| 2 — write shift (`ScheduleAutoFiller`) | ☐ worked ☐ partial ☐ blocked | | |
| 3 — trigger restock | ☐ worked ☐ partial ☐ blocked | | |

## Side checks
- Save round-trip with `ModDataStore` file: ______
- Scheduling stability (Steam "scheduling issue"): ______
- Departments concept present? — **not found in decompile** → Team Leaders stay out (confirms D7).
- `Difficulty` enum member names: ______
- Reputation field location: ______

## Decision

**GO.** [x] The mod builds, loads in the real game, all SDK hooks fire, and every read binding
is confirmed against a live save with zero exceptions. No Harmony, no reflection, no patching.
The one remaining check (does the sim keep a mod-written shift/task) is gated only on having a
staffed save to test with — it is not a design risk, since `ScheduleDay.AddWorkShift` is the
game's own API.
