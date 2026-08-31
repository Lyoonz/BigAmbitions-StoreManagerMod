#nullable enable
using System;
using System.Collections.Generic;
using StoreManager.Interop;

namespace StoreManager.PlayerScheduling
{
    /// <summary>
    /// Lets the player schedule themselves into a store roster like any worker (chat request #4).
    /// A player shift is advisory — skipping one just means the station falls back to whoever
    /// else is rostered, or goes unmanned.
    /// </summary>
    public sealed class PlayerShift
    {
        public string StoreId = string.Empty;
        public DateTime Date;
        public int StartHour;
        public int EndHour;
        public StationKind Station;

        public bool Covers(DateTime when, string storeId, StationKind station) =>
            storeId == StoreId
            && station == Station
            && when.Date == Date.Date
            && when.Hour >= StartHour
            && when.Hour < EndHour;
    }

    public sealed class PlayerScheduleBook
    {
        private readonly List<PlayerShift> _shifts = new();
        public IReadOnlyList<PlayerShift> Shifts => _shifts;

        public void Add(PlayerShift shift) => _shifts.Add(shift);
        public void Clear(DateTime date) => _shifts.RemoveAll(s => s.Date.Date == date.Date);

        public PlayerShift? ActiveAt(DateTime when, string storeId) =>
            _shifts.Find(s => s.Covers(when, storeId, s.Station));
    }
}
