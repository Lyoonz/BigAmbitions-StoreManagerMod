# Phase 0 probe report

Static decompile done (2026-08-31). **In-game probe run still pending** — needs Unity
2022.3.62f2 + a running save. Fill the "Result" columns after running F8–F11.

## Environment
- Game: Steam build, tally date 2026-08-29. **Mono** (managed DLLs are real .NET — fully decompilable).
- SDK: not yet cloned. Unity present on this machine is 6000.4.9f1 — **need 2022.3.62f2** for the SDK.

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
- [ ] **GO** — probes 1–3 honoured, no private-method patching. Reach already proven statically.
- [ ] **CONDITIONAL GO (D2)** — needs bundled Harmony for: ______
- [ ] **NO-GO / RE-SCOPE** — task assignment not honoured by the sim → numbers-only manager.

Static verdict so far: **leaning GO.** Every type is public in a referenced assembly; no
reflection or patching needed in `GameBindingsLive` yet. The only real risk is behavioural
(does the sim keep a mod-set task/shift), which only an in-game run settles.
