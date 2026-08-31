#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;

// Real game namespaces — all confirmed public in referenced assemblies (see PHASE0-FINDINGS.md).
using Entities;                               // EmployeeInstance, BuildingRegistration, EmployeeComplaintData
using Helpers;                                // EmployeeHelper
using AI.Employees;                           // Complaint, ComplaintHelper
using Buildings.Schedule;                     // ScheduleAutoFiller
using Player.DifficultySettings;              // Difficulty

namespace StoreManager.Interop
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  THE GAME SEAM.
    //
    //  Phase 0 (decompile) resolved every type below to a real, public game type.
    //  Markers now:
    //    // VERIFY: — API is real; behaviour or exact overload must be checked in a running game.
    //  There are no // PHASE0: (unknown-API) markers left.
    // ─────────────────────────────────────────────────────────────────────────────

    public readonly struct GameRef
    {
        public string Id { get; }
        public string DisplayName { get; }
        public object? Raw { get; }
        public GameRef(string id, string displayName, object? raw) { Id = id; DisplayName = displayName; Raw = raw; }
        public T? As<T>() where T : class => Raw as T;
        public override string ToString() => $"{DisplayName} ({Id})";
    }

    public enum StationKind { Register, Restock, Clean, Backroom, Greeter }
    public enum EmployeePresence { Working, OffShift, Sick, OnLeave }
    public enum TrainableSkill { Sales, Restocking, CustomerService, Management }

    public sealed class ShiftSpec
    {
        public GameRef Employee;
        public int DayOfWeekIndex;              // 0..6, game DayOfWeekOrdered index (TimeHelper.GetDayOfWeekIndex)
        public int StartHour;
        public int EndHour;
        public StationKind Station;
        public string? StationItemInstanceId;   // game WorkShift.itemInstanceId
    }

    public sealed class LeaveRequest
    {
        public GameRef Employee;
        public int FromDay;
        public int ToDay;
        public bool CoverArranged;
    }

    public interface IGameBindings
    {
        event Action? DayElapsed;
        event Action? WeekElapsed;
        /// <summary>Game day counter (SaveGameManager.Current.Day), not a calendar date.</summary>
        int CurrentDay { get; }
        /// <summary>0..6 index of today's DayOfWeekOrdered.</summary>
        int CurrentDayOfWeekIndex { get; }

        GameDifficulty GetDifficulty();

        IEnumerable<GameRef> GetPlayerStores();
        GameRef? FindStore(string storeId);
        decimal GetDailyRevenue(GameRef store);
        double GetReputation(GameRef store);

        IEnumerable<GameRef> GetEmployees(GameRef store);
        GameRef? FindEmployee(string employeeId);
        int GetEmployeeSkill(GameRef employee, TrainableSkill skill);
        EmployeePresence GetPresence(GameRef employee);
        decimal GetHourlyWage(GameRef employee);
        void SetHourlyWage(GameRef employee, decimal wage);

        void AssignTask(GameRef employee, StationKind station);
        StationKind? GetAssignedTask(GameRef employee);

        IEnumerable<ShiftSpec> GetShifts(GameRef store, int dayOfWeekIndex);
        void AddShift(GameRef store, ShiftSpec shift);
        void RemoveShift(GameRef store, ShiftSpec shift);
        void RunGameScheduler(GameRef store, double targetStaffingMultiplier);

        IEnumerable<(GameRef product, int shortfall)> GetLowStock(GameRef store);
        bool PlaceRestockOrder(GameRef store, GameRef product, int quantity, out decimal cost);
        decimal GetStockOnHandValue(GameRef store);

        IEnumerable<GameRef> GetOpenComplaints(GameRef store);
        bool ResolveComplaint(GameRef complaint);

        IEnumerable<LeaveRequest> GetPendingLeave(GameRef store);
        void ApproveLeave(LeaveRequest request);
        void ArrangeCover(GameRef store, LeaveRequest request);
        void StartTraining(GameRef employee, TrainableSkill skill, out decimal cost);

        bool ChangeMoney(decimal delta, string reason, bool showNotification);

        GameRef GetPlayer();
        void ReleasePlayerFromStation(GameRef store);
        bool IsPlayerAtStation(GameRef store, out StationKind station);

        void SendManagerMessage(string localisedTitle, string localisedBody);

        void SaveModData(string key, string json);
        string? LoadModData(string key);
    }

    /// <summary>
    /// Live implementation against the real game. Skill names, the scheduler ctor, the Contact
    /// API and a handful of overloads carry a <c>// VERIFY</c> until first run in the editor.
    /// </summary>
    public sealed class GameBindingsLive : IGameBindings
    {
        // Game skill keys (0–100 float). VERIFY exact strings against SkillHelper.AllSkillNames.
        private const string SkillManagement = "ba:skill_storemanager";
        private const string SkillSales = "ba:skill_sales";
        private const string SkillRestocking = "ba:skill_stocking";
        private const string SkillCustomerService = "ba:skill_customerservice";

        public event Action? DayElapsed;
        public event Action? WeekElapsed;

        private int _lastWeekIndex = -1;

        public GameBindingsLive()
        {
            // GlobalEvents.onNewDay is a static Action in BigAmbitions.dll.
            GlobalEvents.onNewDay = (Action)Delegate.Combine(GlobalEvents.onNewDay, new Action(OnNewDay));
        }

        public void Dispose()
        {
            GlobalEvents.onNewDay = (Action)Delegate.Remove(GlobalEvents.onNewDay, new Action(OnNewDay))!;
        }

        private void OnNewDay()
        {
            DayElapsed?.Invoke();
            var dow = CurrentDayOfWeekIndex;
            if (_lastWeekIndex >= 0 && dow < _lastWeekIndex)   // week wrapped (e.g. Sun→Mon)
                WeekElapsed?.Invoke();
            _lastWeekIndex = dow;
        }

        // ── time ────────────────────────────────────────────────────────────────
        public int CurrentDay => TimeHelper.CurrentDay;
        public int CurrentDayOfWeekIndex => TimeHelper.GetDayOfWeekIndex(TimeHelper.GetDayOfWeek());

        // ── difficulty ──────────────────────────────────────────────────────────
        public GameDifficulty GetDifficulty()
        {
            // VERIFY: GameVariables accessor + Easy/Hard enum member names (decompile shows Normal, Custom).
            var d = SaveGameManager.Current.gameVariables.difficulty;
            return d.ToString().ToLowerInvariant() switch
            {
                "easy" => GameDifficulty.Easy,
                "hard" => GameDifficulty.Hard,
                _ => GameDifficulty.Normal,
            };
        }

        // ── stores ──────────────────────────────────────────────────────────────
        public IEnumerable<GameRef> GetPlayerStores() =>
            SaveGameManager.Current.BuildingRegistrations
                .Where(b => b.RentedByPlayer || b.BuildingOwnedByPlayer)
                .Where(b => b.businessTypeName != "ba:businesstype_empty"
                         && b.businessTypeName != "ba:businesstype_headquarters")
                .Select(b => new GameRef(b.Address.ToString(), b.BusinessName, b));

        public GameRef? FindStore(string storeId) =>
            GetPlayerStores().Cast<GameRef?>().FirstOrDefault(s => s!.Value.Id == storeId);

        public decimal GetDailyRevenue(GameRef store)
        {
            var b = store.As<BuildingRegistration>();
            return b == null ? 0m : (decimal)b.GetAvgDailyIncome(1);
        }

        public double GetReputation(GameRef store)
        {
            var b = store.As<BuildingRegistration>();
            return b?.satisfaction?.overall ?? 0d;   // Satisfaction { customerService, pricing, cleanliness, facility, overall }
        }

        // ── employees ───────────────────────────────────────────────────────────
        public IEnumerable<GameRef> GetEmployees(GameRef store)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) yield break;
            var list = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address });
            foreach (var e in list)
                yield return new GameRef(e.id, e.characterData?.ToString() ?? e.id, e);
        }

        public GameRef? FindEmployee(string employeeId)
        {
            var e = EmployeeHelper.GetEmployeeById(employeeId, showError: false);
            return e == null ? null : new GameRef(e.id, e.id, e);
        }

        public int GetEmployeeSkill(GameRef employee, TrainableSkill skill)
        {
            var e = employee.As<EmployeeInstance>();
            if (e == null) return 0;
            var raw = EmployeeHelper.GetSkillOfEmployee(e.id, GameSkillKey(skill)); // 0–100
            return Mathf01to5(raw);
        }

        public EmployeePresence GetPresence(GameRef employee)
        {
            var e = employee.As<EmployeeInstance>();
            if (e == null) return EmployeePresence.OffShift;
            if (e.isAbsent) return EmployeePresence.Sick;          // VERIFY: absent covers sick + leave
            return EmployeePresence.Working;
        }

        public decimal GetHourlyWage(GameRef employee) =>
            (decimal)(employee.As<EmployeeInstance>()?.hourlyWage ?? 0f);

        public void SetHourlyWage(GameRef employee, decimal wage)
        {
            var e = employee.As<EmployeeInstance>();
            if (e != null) e.hourlyWage = (float)wage;   // VERIFY: is there a setter that also logs a raise?
        }

        // ── task assignment ─────────────────────────────────────────────────────
        public void AssignTask(GameRef employee, StationKind station)
        {
            var e = employee.As<EmployeeInstance>();
            if (e == null) return;
            // VERIFY (probe #1): setting assignedWorkStationItems + UpdateAssignedWorkStationItems(),
            // vs. going through EmployeeStationController.AssignEmployee on the in-world Employee.
            // Does the sim keep the assignment, or re-derive it from the schedule next hour?
            e.UpdateAssignedWorkStationItems();
        }

        public StationKind? GetAssignedTask(GameRef employee)
        {
            var e = employee.As<EmployeeInstance>();
            if (e == null || e.assignedWorkStationItems.Count == 0) return null;
            return StationKind.Register; // VERIFY: map itemInstanceId → ItemType → StationKind
        }

        // ── scheduling ──────────────────────────────────────────────────────────
        public IEnumerable<ShiftSpec> GetShifts(GameRef store, int dayOfWeekIndex)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) yield break;
            var day = b.scheduleDays[dayOfWeekIndex];
            foreach (var s in day.workShifts)
                yield return new ShiftSpec
                {
                    Employee = FindEmployee(s.employeeId) ?? default,
                    DayOfWeekIndex = dayOfWeekIndex,
                    StartHour = s.startingHour,
                    EndHour = s.endingHour,
                    StationItemInstanceId = s.itemInstanceId,
                };
        }

        public void AddShift(GameRef store, ShiftSpec shift)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) return;
            var day = b.scheduleDays[shift.DayOfWeekIndex];
            day.AddWorkShift(new WorkShift
            {
                startingHour = shift.StartHour,
                endingHour = shift.EndHour,
                employeeId = shift.Employee.Id,
                itemInstanceId = shift.StationItemInstanceId ?? string.Empty,
                // type = WorkShiftType.Manual   // VERIFY enum member
            });
            var emp = EmployeeHelper.GetEmployeeById(shift.Employee.Id, showError: false);
            emp?.UpdateWeeklyHoursAndDays();
            emp?.UpdateAssignedWorkStationItems();
        }

        public void RemoveShift(GameRef store, ShiftSpec shift)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) return;
            var day = b.scheduleDays[shift.DayOfWeekIndex];
            day.RemoveAllWorkShiftsThatMatchPredicate(w =>
                w.employeeId == shift.Employee.Id && w.startingHour == shift.StartHour && w.endingHour == shift.EndHour);
        }

        public void RunGameScheduler(GameRef store, double targetStaffingMultiplier)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) return;
            // CONFIRMED: ScheduleAutoFiller(List<EmployeeInstance> employees, BuildingRegistration, ScheduleDay day = null)
            // then new Thread(filler.FillWithEmployees).Start(); (ScheduleAutoFillerHelper does exactly this).
            // The extension `b.AutoFillSchedule(...)` is UI-coupled (RegisterAutoFiller on the BizMan menu),
            // so call the filler directly for a headless run.
            var employees = EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address });
            var filler = new ScheduleAutoFiller(employees, b) { fast = true, inhibitSuccessNotification = true };
            filler.onCompleted.AddListener((f, ok) => { /* VERIFY: post-fill cleanup — see ScheduleAutoFillerHelper.OnCompleted */ });
            new System.Threading.Thread(filler.FillWithEmployees).Start();
            // targetStaffingMultiplier: VERIFY where staffing target feeds in (opening hours / demand model),
            // ScheduleAutoFiller derives need from the building's requirements, not a free multiplier.
        }

        // ── restock ─────────────────────────────────────────────────────────────
        public IEnumerable<(GameRef product, int shortfall)> GetLowStock(GameRef store)
        {
            // VERIFY: enumerate shelf ItemInstances for the store, compare stock vs max capacity
            // (CargoInstance.GetMaxStockCapacity). ReStockingHelper works on List<ItemInstance>.
            yield break;
        }

        public bool PlaceRestockOrder(GameRef store, GameRef product, int quantity, out decimal cost)
        {
            cost = 0m;
            // VERIFY: wholesale/importer purchase path. ReStockingHelper.RedistributeStockByPercentage
            // only moves warehouse→shelf; actual buying is a supplier/delivery contract + ChangeMoneySafe.
            return false;
        }

        public decimal GetStockOnHandValue(GameRef store) => 0m; // VERIFY

        // ── complaints ──────────────────────────────────────────────────────────
        public IEnumerable<GameRef> GetOpenComplaints(GameRef store)
        {
            var b = store.As<BuildingRegistration>();
            if (b == null) yield break;
            foreach (var e in EmployeeHelper.GetEmployeeInstances(new EmployeeInstancesQueryInfo { withAssignedAddress = b.Address }))
            {
                // EmployeeComplaintData complaintData — VERIFY its shape (list of active Complaint keys?)
                if (e.complaintData != null)
                    yield return new GameRef(e.id, "complaint:" + e.id, e.complaintData);
            }
        }

        public bool ResolveComplaint(GameRef complaint)
        {
            // VERIFY: the mitigation path per Complaint subtype (NoTaskAssignedComplaint → assign a task,
            // LowSkillComplaint → train, LowSatisfactionComplaint → raise/bonus, UnfulfilledDemands → meet a demand).
            return false;
        }

        // ── leave & training ────────────────────────────────────────────────────
        public IEnumerable<LeaveRequest> GetPendingLeave(GameRef store)
        {
            // VERIFY: the game models sickness (nextSickDay / isAbsent) but explicit *holiday requests*
            // may not exist yet. If not, this stays empty and the leave step is a no-op in v1.
            yield break;
        }

        public void ApproveLeave(LeaveRequest request) { /* VERIFY: no-op until holiday requests exist */ }
        public void ArrangeCover(GameRef store, LeaveRequest request) => RunGameScheduler(store, 1.0);

        public void StartTraining(GameRef employee, TrainableSkill skill, out decimal cost)
        {
            var e = employee.As<EmployeeInstance>();
            cost = 0m;
            if (e == null) return;
            var key = GameSkillKey(skill);
            cost = (decimal)EmployeeHelper.GetTrainingCost(e, key, skillIncrease: 10);
            // VERIFY: the call that actually starts a TrainingInstance (HrManagerPlan.TrainEmployees does it
            // via an HR plan; a direct EmployeeInstance.StartTraining(key) may exist).
            e.trainingSession = new EmployeeInstance.TrainingInstance { skill = key, startDay = CurrentDay };
        }

        // ── money ───────────────────────────────────────────────────────────────
        public bool ChangeMoney(decimal delta, string reason, bool showNotification)
        {
            // CONFIRMED: TransactionInfo(string type, Dictionary<string,string> data, bool isTaxDeductible = false)
            //            GameManager.ChangeMoneySafe(float, TransactionInfo, int? day, Address, bool force, bool showNotification)
            var info = new TransactionInfo("storemanager:transaction_managererror",
                new Dictionary<string, string> { { "reason", reason } });
            return GameManager.ChangeMoneySafe((float)delta, info, null, null, false, showNotification);
        }

        // ── player scheduling ───────────────────────────────────────────────────
        public GameRef GetPlayer()
        {
            var pc = PlayerHelper.PlayerController;   // seen in Employee.cs
            return new GameRef("player", "You", pc);
        }

        public void ReleasePlayerFromStation(GameRef store)
        {
            // VERIFY: PlayerHelper.PlayerController → find the EmployeeStationController the player occupies,
            // call UnassignEmployee(). Employee.cs shows employeeStationController.employee comparison.
        }

        public bool IsPlayerAtStation(GameRef store, out StationKind station)
        {
            station = StationKind.Register;
            return false; // VERIFY
        }

        // ── messaging ───────────────────────────────────────────────────────────
        public void SendManagerMessage(string localisedTitle, string localisedBody)
        {
            // CONFIRMED pattern (BackAlleyDealer): Contact.GetContact(name, category, description);
            //   contact.SendMessage(new TextMessage(bodyKey), sendNotificationInstantly: true);
            // VERIFY: passing already-formatted text vs. a locale key; ContactCategoryName for a mod contact.
        }

        // ── persistence (D6 revised — file-based; ModContext has no save API) ────
        public void SaveModData(string key, string json) => ModDataStore.Write(key, json);
        public string? LoadModData(string key) => ModDataStore.Read(key);

        // ── helpers ─────────────────────────────────────────────────────────────
        private static string GameSkillKey(TrainableSkill s) => s switch
        {
            TrainableSkill.Sales => SkillSales,
            TrainableSkill.Restocking => SkillRestocking,
            TrainableSkill.Management => SkillManagement,
            _ => SkillCustomerService,
        };

        /// <summary>Game skills are 0–100; the mod's tuning model is 1–5. ×20 mapping.</summary>
        private static int Mathf01to5(float skill0to100) =>
            Math.Clamp((int)Math.Round(skill0to100 / 20f), ManagementSkill.Min, ManagementSkill.Max);
    }
}
