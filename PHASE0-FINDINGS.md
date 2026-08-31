# Phase 0 findings — the real game seam

Resolved by decompiling the shipped assemblies (Mono build, real .NET — `ilspycmd` over
`Big Ambitions_Data/Managed/*.dll`). Build version tally date 2026-08-29.

**The whole mod compiles clean (0 warnings, 0 errors) against the real game + Unity assemblies**
via `build/CompileCheck.csproj` — domain, runtime, UI, interop, player-scheduling, and the probe.
So every type/method/field referenced below is real and correctly used.

**In-game verification still outstanding** (needs Unity 2022.3.62f2 + a running save): whether
the sim *honours* a mod-written task/shift the same frame; the exact staffing input to
`ScheduleAutoFiller`; the wholesale purchase path; a few field names behind `// VERIFY`.

## Assemblies & namespaces

| Concern | Namespace / type | Assembly |
|---|---|---|
| Mod API | `BAModAPI` (`IModBigAmbitions`, `ModContext`, `IModLogger`, entry attrs, `ModEnumHash`) | `BigAmbitions.ModAPI.dll` |
| Mod options UI | `BigAmbitions.Mods` (`ModOptions`, `OptionsService`, `*Option`) | `BigAmbitions.ModAPI.dll` |
| Mod events | `BAModAPI.ModEvents` (`onModsLoaded`, `onModsUnloaded`) | `BigAmbitions.ModAPI.dll` |
| Employee (persistent) | `Entities.EmployeeInstance` | `BigAmbitions.dll` |
| Employee (in-world) | `Employee : MonoBehaviour` | `BigAmbitions.dll` |
| Employee ops | `Helpers.EmployeeHelper` | `BigAmbitions.dll` |
| Station / task | `EmployeeStationController : Producer` | `BigAmbitions.dll` |
| Store | `Entities.BuildingRegistration` | `BigAmbitions.dll` |
| Schedule | `ScheduleDay`, `WorkShift` (`[Serializable]`), `OpeningHourSlot` | `BigAmbitions.dll` |
| Auto-scheduler | `Buildings.Schedule.ScheduleAutoFiller` (Google.OrTools CP-SAT) | `BigAmbitions.dll` |
| Schedule helpers | `UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper` / `WorkShiftHelper` | `BigAmbitions.dll` |
| Complaints | `AI.Employees.Complaint` (+ `NoTaskAssignedComplaint`, `LowSkillComplaint`, `LowSatisfactionComplaint`, `UnfulfilledDemandsComplaint`), `AI.Employees.ComplaintHelper`, `Entities.EmployeeComplaintData` | `BigAmbitions.dll` |
| Skills | `BigAmbitions.Characters.Skills` (`Skill`, `SkillData`, `SkillHelper`) | `BigAmbitions.Characters.dll` |
| HR Manager (pattern to copy) | `Entities.HRManager`, `Buildings.Office.Headquarters.HrManagerPlan` / `HrManagerHelper` | `BigAmbitions.dll` |
| Restock (shelf refill) | `ReStockingHelper.RedistributeStockByPercentage(BuildingRegistration, List<ItemInstance>)` | `BigAmbitions.dll` |
| Money | `GameManager.ChangeMoneySafe(float, TransactionInfo, int?, Address, bool force, bool showNotification)` | `BigAmbitions.dll` |
| Save | `SaveGameManager` (`Current` → `GameInstance`, `IsModdedSave`, `MarkChange()`) | `BigAmbitions.dll` |
| Difficulty | `Player.DifficultySettings.Difficulty` enum, `GameVariables.difficulty`, `DifficultySetting` (multipliers incl. `employeeHourlySalaryMultiplier`, `marketPriceMultiplier`) | `BigAmbitions.dll` |
| Time / event bus | `GlobalEvents.onNewDay` / `onNewHour` / `onSaveGame` / `onJobChange` / `onEnterBuilding` / `onExitBuilding` (static `Action`s); `GameEvent.Invoke("ba:gameevent_*")` | `BigAmbitions.dll` |
| Localisation | `Localizor` namespace | `BigAmbitions.dll` (+ ext) |
| Smartphone messaging | `UI.Smartphone.Apps.Contacts` (`Contact.GetContact`, `Contact.SendMessage`, `TextMessage`) | `BigAmbitions.dll` |
| Time helpers | `BigAmbitions.DayNightCycle` (`Timestamp`, `TimeHelper.Now()`) | `BigAmbitions.dll` |

## Confirmed field/method shapes

### `Entities.EmployeeInstance`  (`[Serializable]`)
```
string id;                    float hourlyWage;
Address assignedAddress;       List<string> assignedWorkStationItems;   // station item-instance ids
float satisfaction;            TrainingInstance trainingSession;        // { string skill; int startDay; }
int nextSickDay;  bool isAbsent;  bool isReplaced;  bool poached;
List<DayOfWeekOrdered> assignedWeeklyDays;   int assignedWeeklyHours;
int workedHoursToday;  int workedHoursThisWeek;  int workedDays;  int dayHired;
CharacterData characterData;  EmployeeComplaintData complaintData;  CandidateInfo candidateInfo;
// methods: GetPrimarySkill(), UpdateWeeklyHoursAndDays(), UpdateAssignedWorkStationItems(), UpdateSatisfaction()
```

### `Helpers.EmployeeHelper`  (static)
```
List<EmployeeInstance> GetEmployeeInstances(EmployeeInstancesQueryInfo{ withAssignedAddress = addr })
EmployeeInstance       GetEmployeeById(string id, bool showError = true)
float  GetSkillOfEmployee(string employeeId, string skillName)
float  CalculateHourlyWageForSkill(string skillName, float skillValue)
float  GetTrainingCost(EmployeeInstance e, string skillName, int skillIncrease)
void   HireCandidate(EmployeeInstance candidate)
EmployeeInstance CreateAIEmployeeInstance(string primarySkillName)
EmployeeInstance GetEmployeeAtStationAndHour(BuildingRegistration reg, string stationId, int hour = -1)
bool   IsEmployeeStationEmployedAtHour(BuildingRegistration reg, string stationId, int hour)
void   UnassignEmployeeFromAllWorkshifts(EmployeeInstance e)
void   ForceEmployeeSickNextDay(EmployeeInstance e);   int GetNextSickDay(EmployeeInstance e)
void   RunDaily();  void RunHourly();  void WorkDaily();  void PayDailyWages()
```

### `Entities.BuildingRegistration`
```
List<ScheduleDay> scheduleDays;      // 7, indexed [DayOfWeekOrdered - 1]
string businessTypeName;             Address Address;
```

### `ScheduleDay`  (`[Serializable]`)
```
DayOfWeekOrdered day;  bool isOpen;
List<OpeningHourSlot> openingHourSlots;   List<WorkShift> workShifts;
void AddWorkShift(WorkShift);  void RemoveWorkShift(WorkShift);  void ClearWorkShifts();
void RemoveAllWorkShiftsThatMatchPredicate(Predicate<WorkShift>);
bool IsOpenAtHour(int);
```

### `WorkShift`  (`[Serializable]`)
```
int startingHour;  int endingHour;  string employeeId;  string itemInstanceId;  WorkShiftType type;
```

### `Buildings.Schedule.ScheduleAutoFiller`   ← D5, the OR-Tools scheduler
```
ctor(List<EmployeeInstance> employees, BuildingRegistration registration, ScheduleDay day = null)
void FillWithEmployees()          // run on a background thread (ScheduleAutoFillerHelper does `new Thread(...).Start()`)
UnityEvent<ScheduleAutoFiller,float> onProgress;   UnityEvent<ScheduleAutoFiller,bool> onCompleted;
bool fast;  bool inhibitSuccessNotification;  List<EmployeeInstance> Employees / UnassignedEmployees;
// CpModel/CpSolver from Google.OrTools.Sat, 5s/run, 3 workers.
// Helper: `registration.AutoFillSchedule(...)` — same thing but UI-coupled; call the filler directly instead.
```

### `Entities.BuildingRegistration` (more)
```
string BusinessName;                          Satisfaction satisfaction;   // { customerService, pricing, cleanliness, facility, overall } ints
List<float> dailyIncomes;   float GetAvgDailyIncome(int days);   float GetAvgWeeklyIncome();
bool RentedByPlayer;  bool BuildingOwnedByPlayer;
```

### time — `Helpers.TimeHelper`  (no DateTime; the game uses int day)
```
int CurrentDay  (SaveGameManager.Current.Day);  int CurrentHour;  float CurrentMinute;
DayOfWeekOrdered GetDayOfWeek();  int GetDayOfWeekIndex(DayOfWeekOrdered);  DayOfWeekOrdered GetNextDayOfWeek();
```

### money — `TransactionInfo`
```
new TransactionInfo(string type, Dictionary<string,string> data, bool isTaxDeductible = false)   // type is a "ba:transaction_*" string
GameManager.ChangeMoneySafe(float amount, TransactionInfo, int? day = null, Address = null, bool force = false, bool showNotification = false) → bool
```

## Decisions this locks / changes

- **D6 persistence — REVISED.** `ModContext` exposes **no save API** (only `ModRootPath`,
  `ModId`, `Logger`). Options values *do* auto-persist by id (game renders `IPersistableOption`).
  For per-store `StoreManagerData`: write JSON to `Application.persistentDataPath/StoreManager/`
  keyed by save name, save on `GlobalEvents.onSaveGame`, load on `[ModEntryOnCityLoad]`.
  `Serialization.cs` keeps using OdinSerializer for the blob; only the *sink* changes from a
  hypothetical `ModContext` hook to a file. `GameBindingsLive.SaveModData/LoadModData` now do file IO.
- **D4 digest — CONFIRMED.** `Contact` + `TextMessage` in `UI.Smartphone.Apps.Contacts`, exactly
  as the BackAlleyDealer SDK example uses. Policy panel via `ModOptions` — API matches
  `PolicyOptions.cs` byte-for-byte.
- **D5 scheduling — CONFIRMED PRESENT.** `ScheduleAutoFiller` exists and is OR-Tools CP-SAT.
  `GameBindings.RunGameScheduler` targets it. Exact ctor + a mod-safe invocation path is the
  one remaining in-game check.
- **Difficulty — CONFIRMED.** `Difficulty` enum + `GameVariables` multipliers. `DifficultyProfile`
  in the domain layer maps onto `Difficulty.{Easy,Normal,Hard}` (confirm the Easy/Hard member
  names in-game — enum shows `Normal`, `Custom`).
- **Task assignment — types found, behaviour unverified.** `EmployeeStationController.AssignEmployee`
  / `UnassignEmployee` + `EmployeeInstance.assignedWorkStationItems`. Probe #1 still must show the
  sim doesn't immediately re-derive the assignment.
- **No BepInEx needed — CONFIRMED.** Every type above is `public` in a referenced assembly.
  No `throw Todo` in `GameBindingsLive` needs reflection or patching so far.
