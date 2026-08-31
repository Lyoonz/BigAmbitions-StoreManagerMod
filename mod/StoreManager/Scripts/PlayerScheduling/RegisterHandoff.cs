#nullable enable
using StoreManager.Interop;

namespace StoreManager.PlayerScheduling
{
    /// <summary>
    /// Fixes StuartArmour's bug (chat #10): when a scheduled employee clocks in for a station
    /// the player is currently manning, the player is released from it — free to leave the store.
    ///
    /// Hooked to the employee "clock in" / shift-start event (PHASE0: find that event on the
    /// employee or schedule system) and also re-checked whenever the player is assigned to a station.
    /// </summary>
    public sealed class RegisterHandoff
    {
        private readonly IGameBindings _game;

        public RegisterHandoff(IGameBindings game) => _game = game;

        /// <summary>Call when an employee starts a shift at <paramref name="store"/>.</summary>
        public void OnEmployeeClockIn(GameRef store, GameRef employee)
        {
            if (!_game.IsPlayerAtStation(store, out var playerStation))
                return;

            var empStation = _game.GetAssignedTask(employee);
            if (empStation == playerStation)
                _game.ReleasePlayerFromStation(store);
        }
    }
}
