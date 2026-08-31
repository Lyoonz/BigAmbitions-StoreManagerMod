#nullable enable
using System.Collections.Generic;
using StoreManager.Interop;

namespace StoreManager.PlayerScheduling
{
    /// <summary>
    /// Lets the player schedule themselves into a store roster like any worker (chat request #4).
    /// A player shift is advisory — skipping one just means the station falls back to whoever
    /// else is rostered, or goes unmanned. Works in game time terms: DayOfWeekOrdered index + hour.
    /// </summary>
    public sealed class PlayerShift
    {
        public string StoreId = string.Empty;
        public int DayOfWeekIndex;   // 0..6
        public int StartHour;
        public int EndHour;
        public StationKind Station;

        public bool Covers(string storeId, int dayOfWeekIndex, int hour, StationKind station) =>
            storeId == StoreId
            && station == Station
            && dayOfWeekIndex == DayOfWeekIndex
            && hour >= StartHour
            && hour < EndHour;
    }

    public sealed class PlayerScheduleBook
    {
        private readonly List<PlayerShift> _shifts = new();
        public IReadOnlyList<PlayerShift> Shifts => _shifts;

        public void Add(PlayerShift shift) => _shifts.Add(shift);
        public void ClearDay(int dayOfWeekIndex) => _shifts.RemoveAll(s => s.DayOfWeekIndex == dayOfWeekIndex);

        public PlayerShift? ActiveAt(string storeId, int dayOfWeekIndex, int hour) =>
            _shifts.Find(s => s.Covers(storeId, dayOfWeekIndex, hour, s.Station));
    }
}
