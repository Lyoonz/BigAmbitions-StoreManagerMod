#nullable enable
using System;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// Binds a manager to one store and runs its operating loop: each game day the controller
    /// performs the daily operations, then the mistake model applies skill/difficulty-scaled errors.
    /// </summary>
    public sealed class ManagedStore
    {
        private readonly IGameBindings _game;
        private readonly DailyOperations _ops;
        private readonly GameRef _store;
        private readonly StoreManagerData _data;
        private readonly Random _rng;

        public ManagedStore(IGameBindings game, GameRef store, StoreManagerData data)
        {
            _game = game;
            _store = store;
            _data = data;
            _ops = new DailyOperations(game);
            // One persistent stream seeded per store — advances every day, unlike a fresh-per-day Random.
            _rng = new Random(data.MistakeSeed == 0 ? store.Id.GetHashCode() : data.MistakeSeed);
        }

        public StoreManagerData Data => _data;
        public GameRef Store => _store;

        public void RunDay()
        {
            var difficulty = _game.GetDifficulty();
            var profile = DifficultyProfile.For(difficulty);

            var managerPresent = IsPresent(_data.ManagerEmployeeId);
            var assistantPresent = IsPresent(_data.AssistantEmployeeId);
            var controller = _data.EffectiveController(managerPresent, assistantPresent);

            if (controller == ManagerRank.Employee)
            {
                _data.CurrentWeek.AttentionItems.Add($"{_store.DisplayName} is unmanaged");
                return;
            }

            var skillValue = controller == ManagerRank.Manager ? _data.ManagerSkill : _data.AssistantSkill;
            var skill = new ManagementSkill(skillValue);
            var mistakes = new MistakeModel(skill, profile);

            var attempted = _ops.Run(_store, _data, controller);

            var dailyRevenue = _game.GetDailyRevenue(_store);
            _data.CurrentWeek.Revenue += dailyRevenue;

            // Cost model needs a positive revenue base — a loss-making store still has throughput.
            var costBase = Math.Max(200m, Math.Abs(dailyRevenue));
            foreach (var mistake in mistakes.RollDay(_rng, attempted, costBase))
                ApplyMistake(mistake);
        }

        public WeeklyDigest.Report CloseWeek()
        {
            var report = WeeklyDigest.Compose(_store, _data);
            _data.CurrentWeek.Reset();
            return report;
        }

        private void ApplyMistake(Mistake mistake)
        {
            // The cost lands as a money change with a reason; the attention list surfaces it in the digest.
            _game.ChangeMoney(-mistake.EstimatedCost, $"manager error: {mistake.Kind}", showNotification: false);
            _data.CurrentWeek.MistakeCount++;
            _data.CurrentWeek.MistakeCost += mistake.EstimatedCost;
            // distinct per (kind, cost) so the digest shows each, not one deduped line
            _data.CurrentWeek.AttentionItems.Add($"{mistake.LocaleKey}:{mistake.EstimatedCost:0}");
        }

        private bool IsPresent(string? employeeId)
        {
            if (employeeId == null) return false;
            var emp = _game.FindEmployee(employeeId);
            return emp.HasValue && _game.GetPresence(emp.Value) == EmployeePresence.Working;
        }
    }
}
