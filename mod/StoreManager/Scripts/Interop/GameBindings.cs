#nullable enable
using System;
using System.Collections.Generic;
using StoreManager.Domain;

namespace StoreManager.Interop
{
    // ─────────────────────────────────────────────────────────────────────────────
    //  THE GAME SEAM.
    //
    //  Every line of this file that touches a real game type is marked `// PHASE0:`.
    //  Phase 0 (see the runbook) resolves each one against the decompiled assemblies,
    //  then GameBindingsLive below stops throwing.
    //
    //  Nothing else in this mod references a game namespace directly — keep it that way
    //  so a game patch that moves a type is a one-file fix.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Opaque handle to a game object. <see cref="Raw"/> holds the real instance.</summary>
    public readonly struct GameRef
    {
        public string Id { get; }
        public string DisplayName { get; }
        public object? Raw { get; }

        public GameRef(string id, string displayName, object? raw)
        {
            Id = id;
            DisplayName = displayName;
            Raw = raw;
        }

        public override string ToString() => $"{DisplayName} ({Id})";
    }

    public enum StationKind { Register, Restock, Clean, Backroom, Greeter }

    public enum EmployeePresence { Working, OffShift, Sick, OnLeave }

    public enum TrainableSkill { Sales, Restocking, CustomerService, Management }

    public sealed class ShiftSpec
    {
        public GameRef Employee;
        public DateTime Date;
        public int StartHour;
        public int EndHour;
        public StationKind Station;
    }

    public sealed class LeaveRequest
    {
        public GameRef Employee;
        public DateTime From;
        public DateTime To;
        public bool CoverArranged;
    }

    /// <summary>The contract the mod needs from Big Ambitions. Implemented by <see cref="GameBindingsLive"/>.</summary>
    public interface IGameBindings
    {
        // ── time ────────────────────────────────────────────────────────────────
        /// PHASE0: find the day/week tick source. Candidates: DayNightCycle.dll,
        /// a GameManager time event, or SeasonManager. Wire it to raise these.
        event Action? DayElapsed;
        event Action? WeekElapsed;
        DateTime CurrentDate { get; }

        // ── difficulty ──────────────────────────────────────────────────────────
        /// PHASE0: BigAmbitions.dll — GameSettings / difficulty enum. Map to GameDifficulty.
        GameDifficulty GetDifficulty();

        // ── stores ──────────────────────────────────────────────────────────────
        /// PHASE0: player-owned businesses. Likely BuildingManager / a "PlayerBusinesses"
        /// collection in BigAmbitions.dll. Example mods use BuildingHelper.GetBuilding(Address).
        IEnumerable<GameRef> GetPlayerStores();
        GameRef? FindStore(string storeId);
        decimal GetDailyRevenue(GameRef store);
        double GetReputation(GameRef store);

        // ── employees ───────────────────────────────────────────────────────────
        /// PHASE0: BigAmbitions.Characters.dll — the employee/staff entity. Needs id,
        /// display name, assigned home store, current task, skill values, presence state.
        IEnumerable<GameRef> GetEmployees(GameRef store);
        GameRef? FindEmployee(string employeeId);
        int GetEmployeeSkill(GameRef employee, TrainableSkill skill);
        EmployeePresence GetPresence(GameRef employee);
        decimal GetHourlyWage(GameRef employee);
        void SetHourlyWage(GameRef employee, decimal wage);

        // ── PROBE 1: task assignment ────────────────────────────────────────────
        /// PHASE0: how an employee is bound to a station. Enum field? Assignment object?
        /// If it's a Behavior Designer tree variable (BehaviorDesigner.Runtime.dll),
        /// find the higher-level task layer instead of poking the tree.
        void AssignTask(GameRef employee, StationKind station);
        StationKind? GetAssignedTask(GameRef employee);

        // ── PROBE 2: scheduling ─────────────────────────────────────────────────
        /// PHASE0: the roster model behind the schedule UI. Shift = {employee, day, block, station}.
        IEnumerable<ShiftSpec> GetShifts(GameRef store, DateTime date);
        void AddShift(GameRef store, ShiftSpec shift);
        void RemoveShift(GameRef store, ShiftSpec shift);

        /// PHASE0 (D5): the game already links Google.OrTools.dll — it very likely has a
        /// scheduler/solver. Prefer calling it over building shifts by hand. Pass the target
        /// staffing multiplier and let the game solve availability.
        void RunGameScheduler(GameRef store, double targetStaffingMultiplier);

        // ── PROBE 3: restock ────────────────────────────────────────────────────
        /// PHASE0: the supplier-order path. Example mods show delivery contracts
        /// (ContractItemsForSaleService) and GameManager.ChangeMoneySafe for the cash side.
        IEnumerable<(GameRef product, int shortfall)> GetLowStock(GameRef store);
        bool PlaceRestockOrder(GameRef store, GameRef product, int quantity, out decimal cost);
        decimal GetStockOnHandValue(GameRef store);

        // ── complaints ──────────────────────────────────────────────────────────
        /// PHASE0: BigAmbitions.dll — customer complaint entity + resolve path.
        IEnumerable<GameRef> GetOpenComplaints(GameRef store);
        bool ResolveComplaint(GameRef complaint);

        // ── leave & training ────────────────────────────────────────────────────
        IEnumerable<LeaveRequest> GetPendingLeave(GameRef store);
        void ApproveLeave(LeaveRequest request);
        void ArrangeCover(GameRef store, LeaveRequest request);
        /// PHASE0: the HR Manager's auto-training path is the closest analogue — reuse it.
        void StartTraining(GameRef employee, TrainableSkill skill, out decimal cost);

        // ── money ───────────────────────────────────────────────────────────────
        /// CONFIRMED shape: GameManager.ChangeMoneySafe(amount, TransactionInfo, showNotification).
        bool ChangeMoney(decimal delta, string reason, bool showNotification);

        // ── player scheduling ───────────────────────────────────────────────────
        /// PHASE0: the player character. GameManager.Instance.playerController is confirmed to exist.
        GameRef GetPlayer();
        /// Release the player from whatever station they're manning (register-handoff fix).
        void ReleasePlayerFromStation(GameRef store);
        bool IsPlayerAtStation(GameRef store, out StationKind station);

        // ── messaging (digest) ──────────────────────────────────────────────────
        /// CONFIRMED shape: Contact.GetContact(name, category, description); contact.SendMessage(new TextMessage(key)).
        void SendManagerMessage(string localisedTitle, string localisedBody);

        // ── persistence ─────────────────────────────────────────────────────────
        /// PHASE0: mod-save API. Either a ModContext hook or piggyback an OdinSerializer container.
        void SaveModData(string key, string json);
        string? LoadModData(string key);
    }

    /// <summary>
    /// Live implementation. Each member is a one-liner once its PHASE0 note is resolved.
    /// Until then it throws so nothing silently no-ops in a playtest.
    /// </summary>
    public sealed class GameBindingsLive : IGameBindings
    {
        private static NotImplementedException Todo(string what) =>
            new($"PHASE0 unresolved: {what}. See Scripts/Interop/GameBindings.cs and the Phase 0 runbook.");

        public event Action? DayElapsed;
        public event Action? WeekElapsed;
        public DateTime CurrentDate => throw Todo("time source");

        public GameDifficulty GetDifficulty() => throw Todo("GameSettings difficulty");

        public IEnumerable<GameRef> GetPlayerStores() => throw Todo("player-owned businesses");
        public GameRef? FindStore(string storeId) => throw Todo("store lookup by id");
        public decimal GetDailyRevenue(GameRef store) => throw Todo("store daily revenue");
        public double GetReputation(GameRef store) => throw Todo("store reputation");

        public IEnumerable<GameRef> GetEmployees(GameRef store) => throw Todo("store employees");
        public GameRef? FindEmployee(string employeeId) => throw Todo("employee lookup by id");
        public int GetEmployeeSkill(GameRef employee, TrainableSkill skill) => throw Todo("employee skill read");
        public EmployeePresence GetPresence(GameRef employee) => throw Todo("employee presence state");
        public decimal GetHourlyWage(GameRef employee) => throw Todo("employee wage read");
        public void SetHourlyWage(GameRef employee, decimal wage) => throw Todo("employee wage write");

        public void AssignTask(GameRef employee, StationKind station) => throw Todo("PROBE 1 — task assignment");
        public StationKind? GetAssignedTask(GameRef employee) => throw Todo("assigned task read");

        public IEnumerable<ShiftSpec> GetShifts(GameRef store, DateTime date) => throw Todo("roster read");
        public void AddShift(GameRef store, ShiftSpec shift) => throw Todo("PROBE 2 — add shift");
        public void RemoveShift(GameRef store, ShiftSpec shift) => throw Todo("remove shift");
        public void RunGameScheduler(GameRef store, double targetStaffingMultiplier) => throw Todo("D5 — game scheduler entry point");

        public IEnumerable<(GameRef product, int shortfall)> GetLowStock(GameRef store) => throw Todo("low-stock read");
        public bool PlaceRestockOrder(GameRef store, GameRef product, int quantity, out decimal cost) => throw Todo("PROBE 3 — restock order");
        public decimal GetStockOnHandValue(GameRef store) => throw Todo("stock value read");

        public IEnumerable<GameRef> GetOpenComplaints(GameRef store) => throw Todo("open complaints read");
        public bool ResolveComplaint(GameRef complaint) => throw Todo("resolve complaint");

        public IEnumerable<LeaveRequest> GetPendingLeave(GameRef store) => throw Todo("pending leave read");
        public void ApproveLeave(LeaveRequest request) => throw Todo("approve leave");
        public void ArrangeCover(GameRef store, LeaveRequest request) => throw Todo("arrange cover");
        public void StartTraining(GameRef employee, TrainableSkill skill, out decimal cost) => throw Todo("start training");

        public bool ChangeMoney(decimal delta, string reason, bool showNotification) => throw Todo("GameManager.ChangeMoneySafe wrap");

        public GameRef GetPlayer() => throw Todo("player controller ref");
        public void ReleasePlayerFromStation(GameRef store) => throw Todo("release player from station");
        public bool IsPlayerAtStation(GameRef store, out StationKind station) => throw Todo("player-at-station check");

        public void SendManagerMessage(string localisedTitle, string localisedBody) => throw Todo("Contact.SendMessage wrap");

        public void SaveModData(string key, string json) => throw Todo("mod-save write");
        public string? LoadModData(string key) => throw Todo("mod-save read");

        internal void RaiseDayElapsed() => DayElapsed?.Invoke();
        internal void RaiseWeekElapsed() => WeekElapsed?.Invoke();
    }
}
