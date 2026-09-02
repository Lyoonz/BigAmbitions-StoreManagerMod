# Store Manager Mod — v2 architecture (the "real meta-role" rebuild)

This supersedes the v1 lightweight design. v1 (off-screen data role, console/button hire) is
built and works but the user rejected the UX. v2 makes the manager a **real hired employee**
managed through the game's own UI, with a mod supervision layer on top.

API names below are from the **in-game reflection dump**
from an in-game reflection dump — the decompile is partly obfuscated, the dump is not.

---

## The seven user requirements → how v2 meets them

| # | Requirement | v1 v2 mechanism |
|---|-------------|-------------------|
| 1 | Everything has visible in-game feedback | `UI.Notification.Notifications.Show(NotificationType, key, data, secs, dupId, onClick, sound, track)` toasts per action; a weekly phone thread via `LogisticsManagerPlan.GetDeliveryAlertContact()` **or** `Entities.Contact.GetContact(name, ContactCategoryName.ImportsAndGoods, desc)` + `contact.SendMessage(new TextMessage(key, data))`; `TransactionInfo("storemanager:transaction_*")` line items on every `GameManager.ChangeMoneySafe`; `EmployeeHelper.OnClickShowEmployee(emp)` deep-link. |
| 2 | Hire **via the office**, like HR/Logistics/Pricing Manager | The manager is recruited through the **vanilla Recruitment Agency** on `ba:skill_purchasingagent` (a real "Purchasing Agent" job). No mod hire screen in v1 — the player uses the game's own recruitment flow. `Helpers.RecruitmentHelper.GenerateCandidate("ba:skill_purchasingagent", skillValue, hqAddress, demands, secondSkillValue)` is available if a mod "Recruit" button is wanted later. |
| 3 | Appears in **My Employees**, behaves like a normal hire | Automatic — `EmployeeHelper.HireCandidate(candidate)` runs the full vanilla hire (adds to `EmployeeInstances`, `dayHired`, `ba:gameevent_employeehired`, badge). It's a plain `EmployeeInstance` with `ba:skill_purchasingagent`. The mod never subclasses `EmployeeInstance`. |
| 4 | **Schedule** the manager like any employee | Vanilla BizMan → HQ → Schedule tab. `ScheduleHelper.AddWorkShift(hour, workstationId, employeeId)` if the mod ever needs to; gate is `EmployeeInstance.IsAssignedToAnyWorkShift()` + `assignedAddress == HQ`. The mod plan is **dormant until the manager holds an HQ shift**. |
| 5 | **Assign to N stores** (multi-store) | Mod-owned `StoreManagerPlan.SupervisedStores`. Cap = `LogisticsManagerPlan.CalculateMaxDestinations(hqAddress, employeeId)` (reuse the game's own skill→cap calc) **or** `1 + floor(skill/25)`. Eligible stores: `BuildingHelper.GetPlayerBuildingRegistrations(filter, sort)` where retail + not already supervised. |
| 6 | **Per-store limits** | Mod data — `StoreAssignment { Address Store; decimal WeeklyRestockBudgetCap; StaffingLevel Staffing; int TargetDaysOfStock; }`. Global defaults seed new assignments. Enforced in the mod's own weekly planner. |
| 7 | Keeps stores **stocked via delivery** | `SaveGameManager.Current.DeliveryContracts` — get-or-create the contract for `businessAddress == store` (`BuildingHelper.FindClosestWholesaleStore(store)` picks the wholesale source), set `enabled = repeatingOrder = true`, tune `items[].amount` toward target using `DeliveryHelper.GetOrderAmount(item, itemObj, wholesaleReg)` within the weekly budget cap. Guard with `DeliveryHelper.CanModifyContract(contract.nextDeliveryDay)`. |

---

## Locked decisions (also in `../DECISIONS.md` as D9–D13)

- **D9 — architecture: Option B.** A **mod-owned supervision plan** that mimics
  `HrManagerPlan`/`LogisticsManagerPlan`, with all state in `GameInstance.modData`. **Reject**
  Option A (piggyback a real `LogisticsManagerPlan` — it restocks from a *warehouse* via
  `LogisticsManagerPlanDestination.Deliver(Warehouse,...)`, not a wholesale contract, and its
  list can't be safely grown). **Reject** Option C (Harmony-inject a real plan type into
  `GameInstance` — dual serializers Newtonsoft `TypeNameHandling.Auto` + OdinSerializer binary,
  plus `Player.SaveSystem.CompatibilityFixes.*` purge passes → save-corruption risk).

- **D10 — manager skill: `ba:skill_purchasingagent`.** Confirmed by the dump to be a real skill
  with **no HQ manager plan/tab** (unlike hrmanager/logisticsmanager/pricingmanager/headhunter),
  so no dual-binding hazard. Guard anyway: refuse to adopt an employee for whom
  `LogisticsManagerHelper.GetAssignedPlanForEmployee(id) != null` (or the hr/pricing equivalents).
  **No custom skill** — the dump confirms `SkillHelper.GetData(name)` is called unguarded in
  candidate generation, wage calc, the employee card; an unregistered skill NPEs, and
  `SkillHelper` doesn't persist mod skills across reloads.

- **D11 — v1 scope is the TRIMMED version** both adversarial reviews recommended. **In:**
  vanilla recruit→hire→schedule; a `ModOption.SpawnUi` panel + console for assign-to-N-stores +
  per-store limits; a weekly delivery-contract planner within budget; full visible feedback;
  reconcile when the manager is fired. **Out of v1 (and mostly permanently):** Harmony-injected
  HQ tab, custom skill, `MistakeModel`/`ManagerRank` ladder/`DifficultyProfile` coupling,
  `ScheduleAutoFiller` roster top-up, complaints/leave/training handling, price-policy writes,
  contract snapshot/restore beyond `enabled=false`, 3-day grace state machine, player
  self-scheduling.

- **D12 — restock runs on the WEEKLY Monday delivery cycle**, not per-day. `DeliveryHelper`:
  `DeliveryDay` (Monday), `DeliveryHour` (~8), lock `LockPeriodStartingDay`/`Hour` (Sun ~20:00
  → Mon ~08:00). The planner runs **once per week** (trigger on `GlobalEvents.onNewDay` when
  day-of-week is Saturday, or `onNewHour` guarded), computes next Monday's order per store within
  `WeeklyRestockBudgetCap`, respects `CanModifyContract`, no-ops the rest of the week.

- **D13 — persistence: `GameInstance.modData["StoreManager.plans.v1"]`.** Confirmed
  `Dictionary<string,string>` and **already used by the CosaNostra mod** (`modData: 1 entries:
  hekwereld.cosanostra` in the dump) → proven to round-trip the save. Plain JSON (Newtonsoft is
  a game dep) of `List<StoreManagerPlan>`. Write on `GlobalEvents.onSaveGame`, read on
  `[ModEntryOnCityLoad]`. Keep the `persistentDataPath` file (`Interop/ModDataStore.cs`) as a
  keyed fallback, wired from day one. On load: drop any plan whose `ManagerEmployeeId` doesn't
  resolve via `EmployeeHelper.GetEmployeeById`, and set its contracts `enabled = false`.

---

## Confirmed API (from the reflection dump — real names)

```
GameInstance.modData : Dictionary<string,string>            // persistence sink, proven (CosaNostra uses it)

GlobalEvents (static Action): onNewDay, onNewHour, onSaveGame, onJobChange, onGameUnloaded,
             onBuildingRegistrationChange:Action<Address>, onEnterBuilding/onExitBuilding:Action<Address>

Entities.DeliveryHelper (static):
  DayOfWeekOrdered DeliveryDay;  int DeliveryHour;  DayOfWeekOrdered LockPeriodStartingDay;  int LockPeriodStartingHour
  bool CanModifyContract(int contractDeliveryDay)
  int  GetNextDeliveryDay()
  int  GetOrderAmount(DeliveryContractItem deliveryItem, Item item, BuildingRegistration wholesaleRegistration)
  bool IsLockPeriod();  bool IsRegularDeliveryHour()
  void ShowCantModifyContractNotification()

Helpers.BuildingHelper (static):
  BuildingRegistration FindClosestWholesaleStore(Address address)
  BuildingRegistration GetBuildingRegistration(Address address)
  List<BuildingRegistration> GetPlayerBuildingRegistrations(BuildingRegistrationFilterDelegate, BuildingRegistrationSortDelegate)
  int  CountTotalResourcesInStock(BuildingRegistration, string itemName, bool includeProducers, bool includePallets, bool includeBoxItemInstances)
  ScheduleDay GetTodaySchedule(BuildingRegistration)
  Address ParseAddressString(string)

Helpers.RecruitmentHelper.GenerateCandidate(string skillName, float skillValue, Address assignedAddress, List demands, float secondSkillValue) : EmployeeInstance

Helpers.EmployeeHelper (static):
  void HireCandidate(EmployeeInstance candidate)
  void OnClickShowEmployee(EmployeeInstance)                 // opens the employee card (feedback)
  EmployeeInstance GetEmployeeById(string employeeId, bool showError)
  List<EmployeeInstance> GetEmployeeInstances()
  List<EmployeeInstance> GetEmployeeInstances(EmployeeInstancesQueryInfo, List listToFill)
  float CalculateHourlyWageForSkill(string skillName, float skillValue)
  float GetSkillOfEmployee(string employeeId, string skillName)
  void  UnassignEmployeeFromAllWorkshifts(EmployeeInstance)

Entities.EmployeeInstance:
  bool HasSkill(string skill);  float GetSkillValue(string skillName);  string GetPrimarySkill()
  bool IsAssignedToAnyWorkShift();  bool IsAssignedToSpecificWorkShift(WorkShiftType);  bool IsAssignedToAnyBusiness()
  WorkShift GetWorkShiftAssignedInThisMoment(bool specificShiftType, WorkShiftType)
  void UnAssignWork();  void UpdateAssignedWorkStationItems()
  fields: Address assignedAddress; List assignedWorkStationItems; string assignedHrManagerPlanId;
          List assignedWeeklyDays; int assignedWeeklyHours

UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper (static):
  void AddWorkShift(int hour, string workstationId, string employeeId)
  void RemoveWorkShift(string workstationId, string employeeId, int startingHour)
  void EditWorkShift(...);  void MoveWorkShift(WorkShift, int newStart, int newEnd, string newWorkstationId)
  bool HasSkillForWorkstation(string workstationId, string employeeId)
  bool IsEmployeeAvailable(string workstationId, string employeeId, int hour)
  void FetchEmployees(Address);  void FetchWorkstations()
  List GetWorkShiftsByEmployeeId(string);  List GetWorkShiftsByWorkstationId(string)
  ScheduleDay GetScheduleDay(int dayOfWeekOrderedIndex)
  void UpdateHQPlans(BizManBusiness business)               // vanilla HQ-plan reconcile on schedule change
  prop bool IsHeadquarters

Buildings.Office.Headquarters.LogisticsManagerPlan (reusable helpers):
  static int CalculateMaxDestinations(Address address, string employeeId)   // skill -> store cap
  static Contact GetDeliveryAlertContact()                                  // ready-made phone contact
  static string DeliveryAlertContactId / DeliveryAlertContactDescription
Buildings.Office.Headquarters.LogisticsManagerHelper.GetAssignedPlanForEmployee(string employeeId) : LogisticsManagerPlan
Buildings.Office.Headquarters.HrManagerHelper.CalculateMaxAssignableEmployees(float skill) : int
Buildings.Office.Headquarters.PricingManagerPlan.GetSupervisedStores() : List<BuildingRegistration>   // pattern reference
Buildings.Office.Headquarters.PricingManagerHelper.IsManageableBusiness(BuildingRegistration) : bool

UI.Notification.Notifications.Show(NotificationType {Info,Success,Warning,Error}, string headerKey,
    Dictionary<string,string> data=null, float secs=4, string dupId=null, Action onClick=null, bool sound=true, bool track=true)
Entities.Contact.GetContact(string name, ContactCategoryName {Business,Employees,Finance,ImportsAndGoods,General,Rivals,FurnitureAndEquipment}, string description, Address=null, ...)
Entities.Contact.SendMessage(TextMessage, bool notify=true, bool sendNotificationInstantly=false)
new Entities.TextMessage(string messageKey, Dictionary<string,string> data=null, ...)
```

### Verified in-game 2026-09-01 (v2 probe, day-8 save, no HQ)
- ✅ v2 mod loads clean against Build 3672: `Store Manager loaded (v2)` + `active — 0 plan(s)`, no exceptions.
- ✅ `GameInstance.modData` is `Dictionary<string,string>`, reachable, empty on a fresh save.
- ✅ `SaveGameManager.Current.DeliveryContracts` → `DeliveryContract { businessAddress, wholesaleAddress,
  enabled, repeatingOrder, nextDeliveryDay, items[] }`; a store had a 9-item contract (disabled).
- ✅ `BuildingRegistration.GetAssignableItems()` → each `ItemInstance.ItemCached.suitableSkills`,
  e.g. `ba:itemname_cashregister` → `[ba:skill_customerservice]`, `ba:itemname_cleaningstation` → `[ba:skill_cleaning]`.
- ✅ `GameApi` / `DeliveryContracts` read paths all work, zero exceptions.

### Verified in-game 2026-09-02 — day-125 HQ save (`Foundation Headquarters`), `StoreManager.SelfTest`
- ✅ **HQ desks accept the manager skill.** `ba:itemname_desktopcomputer` / `laptop` / `computer`
  at the HQ list `ba:skill_purchasingagent` in `suitableSkills` — a Purchasing Agent can be
  scheduled there. (Open question 1: **yes**.)
- ✅ `GameApi.GetManagerCandidates(hq)` found the real skill-100 Purchasing Agent, flagged scheduled.
- ✅ **Dual-binding guard fires:** that agent was already on a vanilla Purchasing Agent plan →
  `AdoptManager` blocked with the right message. `PurchasingAgentHelper.GetAssignedPlanForPurchasingAgent`
  works.
- ✅ **Full loop, clean:** inject throwaway agent → adopt → assign `The Signature Mart` (supermarket,
  8-item contract) → weekly pass:
  - BEFORE `enabled=False repeating=False total=4000 cost=$4,155`
  - AFTER  `enabled=True repeating=True total=7830 cost=$7,681` — enabled, set repeating, tuned all
    8 lines once (not runaway), within the $100k test cap, `capsHit=0`, `attention=[]`.
  - `week tally spend=$7,681` — matches the standing `TotalPricePerDelivery` (budget accounting fix works).
- ✅ **Restore is exact:** after `DropManager`, contract back to `enabled=False repeating=False
  total=4000` — byte-identical to BEFORE. Snapshot + restore correct.
- ✅ Zero exceptions. `modData` stayed at 0 entries (`SuppressSave` — the throwaway plan never persisted).
- ✅ **No save file touched** — every `.hsg` byte-identical before/after (only an unrelated game
  auto-recover `.jpg` thumbnail appeared).

### Still to verify (not blocking — needs a real multi-week playthrough)
- The convergent `ComputeTarget` math over several in-game weeks: does the buffer settle where
  expected, and does `amountOrderedLastWeek` behave as the demand proxy once deliveries run?
- `GameInstance.modData` round-trip of a multi-KB plan blob through both save paths (CosaNostra
  proves the mechanism for a small value; a real adopt + save + reload confirms it for ours).
- The manager scheduled through the **vanilla** BizMan → HQ → Schedule tab (SelfTest uses
  `skipScheduleCheck`); confirm `IsAssignedToAnyWorkShift()` at the HQ gates the plan as intended.

---

## Phased plan

### Phase 1 — real, visible, hireable (no Harmony, no bundle, pure ModAPI)
File-level tasks in `06_DESIGN.json` → `phases[0].tasks`, **trimmed per the critiques**:
- `Interop/GameBindings.cs` — rewrite: `GetHeadquarters()`, `IsScheduledAtHq(empId)`,
  `GetPurchasingAgentsAtHq()`, `GetSupervisableStores()`, `ReadModData/WriteModData` vs
  `SaveGameManager.Current.modData` + `ModDataStore` fallback, dual-binding guard. Remove shelf-redistribution restock.
- `Interop/Feedback.cs` — NEW: `Notify(type,key,data,onClick)` + `Thread(key,data)` wrappers.
- `Interop/DeliveryContracts.cs` — NEW: `GetOrCreateContract(store)`, `SetTargets`, `Enable/Disable`, guarded by `CanModifyContract`.
- `Domain/StoreManagerPlan.cs` + `Domain/StoreAssignment.cs` — NEW (replace `StoreManagerData.cs`).
- `Domain/StoreManagerData.cs` **delete**; move `WeekTally` into the new file.
- `Domain/ManagerRank.cs` — collapse to a single Manager role + wage helper.
- `Domain/ManagementSkill.cs` — re-base on live 0–100 `GetSkillValue`.
- `Domain/MistakeModel.cs`, `DifficultyProfile.cs`, `Num.cs` — **keep on disk but unwire from v1** (re-introduce later once the loop is proven fun).
- `Interop/Serialization.cs` — Envelope wraps `List<StoreManagerPlan>`; consider plain Newtonsoft over Odin (simpler, game dep).
- `Runtime/ManagerDirectory.cs` — rewrite: keyed by manager `EmployeeInstance.id`; `Load()` from modData; `Reconcile()` each tick + on `onJobChange`; cap-enforced `AssignStore/UnassignStore`; on detach set contracts `enabled=false` + notify.
- `Runtime/ManagedStore.cs` → `Runtime/ManagedPlan.cs` — one manager, N assignments; **weekly** planner (D12), not daily.
- `Runtime/DailyOperations.cs` — rewrite `Restock()` to drive the repeating `DeliveryContract` within `WeeklyRestockBudgetCap`; **drop** `Schedule()`/`Complaints()`/`Leave()` from v1.
- `Runtime/WeeklyDigest.cs` — keep; also raise a `Feedback.Notify` summary + send the Contact thread.
- `UI/PolicyOptions.cs` — global defaults + register a custom `StoreManagerPanel` via `AddCustom`.
- `UI/StoreManagerPanel.cs` — NEW: `ModOption` subclass, `SpawnUi(Transform)` builds pick-manager + toggle-stores (capped) + per-store cap/staffing/target-days.
- `Debug/StoreManagerCommands.cs` — update: `.Assign <storeId>`, `.Unassign <storeId>`, `.SetCap <storeId> <amt>`, `.Status`, `.RunWeek`, `.SelfTest`. Drop `.Recruit`/`.Hire` (vanilla flow).
- `Core/StoreManagerMod.cs` — wire `onNewDay`(weekly guard)/`onSaveGame`/`onJobChange`; flush modData on save + unload; HQ discovery on city load.
- `Locales/en.json` + `nl.json` — notification/message/transaction/panel keys; drop rank keys.
- **In-game test**: recruit a Purchasing Agent from the Recruitment Agency → hire → schedule at HQ → open the panel → assign a store + set a cap → advance a week → confirm the `DeliveryContract` was tuned, toasts fired, phone digest arrived, `modData` survived save/reload, and firing the manager disables the contracts + notifies.

### Phase 2 — multi-store hardening + limits UX polish
Multi-store cap + per-store budget accounting proven; the panel becomes comfortable to use;
optional: the "Store Managers" BizMan HQ tab is a **research spike only, not committed** — the
critiques rate it highest-risk/lowest-value (4–5 Harmony patches on private `BizManBusiness`
members + prefab hierarchy). If pursued: throwaway prototype against the current build, gate
behind a game-build-number check, fall back to the panel on any unrecognised version.

### Phase 3 — deferred / only if playtest asks for it
Custom skill (needs full unguarded-`GetData`-site audit + Harmony guard), price-policy writes
(`PricingManagerPlan.ApplyManualPrice` pattern), training budgets, `MistakeModel` re-wire,
`ScheduleAutoFiller` roster top-up, player self-scheduling + register-handoff bug, manager-quits
on Hard, Headhunter integration, AssetBundle for a prefab-perfect panel.
