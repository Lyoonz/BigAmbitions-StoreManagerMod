#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// Owns the set of managed stores: hiring/assignment, wages, quit checks, and the
    /// per-day / per-week fan-out. One instance per city session.
    /// </summary>
    public sealed class ManagerDirectory
    {
        private readonly IGameBindings _game;
        private readonly Dictionary<string, ManagedStore> _stores = new();
        private const string SaveKey = "StoreManager.stores.v1";

        public ManagerDirectory(IGameBindings game) => _game = game;

        public IReadOnlyCollection<ManagedStore> Stores => _stores.Values;

        // ── lifecycle ───────────────────────────────────────────────────────────
        public void Load()
        {
            _stores.Clear();
            var json = _game.LoadModData(SaveKey);
            if (string.IsNullOrEmpty(json)) return;

            foreach (var record in Serialization.DeserializeList(json!))
            {
                var store = _game.FindStore(record.StoreId);
                if (store.HasValue)
                    _stores[record.StoreId] = new ManagedStore(_game, store.Value, record);
            }
        }

        public void Save()
        {
            var json = Serialization.SerializeList(_stores.Values.Select(s => s.Data));
            _game.SaveModData(SaveKey, json);
        }

        /// <summary>Called on OnUnloadAsync — persist, then drop everything so uninstall is clean.</summary>
        public void Detach()
        {
            Save();
            _stores.Clear();
        }

        // ── hiring ──────────────────────────────────────────────────────────────
        public ManagedStore AssignManager(GameRef store, GameRef employee, ManagementSkill skill, ManagerRank rank)
        {
            if (rank is not (ManagerRank.Manager or ManagerRank.AssistantManager))
                throw new ArgumentException("Only Manager / AssistantManager can be assigned to a store.", nameof(rank));

            var data = GetOrCreate(store);
            if (rank == ManagerRank.Manager)
            {
                data.ManagerEmployeeId = employee.Id;
                data.ManagerSkill = skill.Value;
            }
            else
            {
                data.AssistantEmployeeId = employee.Id;
                data.AssistantSkill = skill.Value;
            }

            ApplyWage(employee, rank, skill);
            Save();
            return _stores[store.Id];
        }

        public void RemoveLeadership(GameRef store, ManagerRank rank)
        {
            if (!_stores.TryGetValue(store.Id, out var managed)) return;
            if (rank == ManagerRank.Manager) managed.Data.ManagerEmployeeId = null;
            else managed.Data.AssistantEmployeeId = null;

            if (!managed.Data.HasActiveLeadership)
                _stores.Remove(store.Id);
            Save();
        }

        private void ApplyWage(GameRef employee, ManagerRank rank, ManagementSkill skill)
        {
            var (min, max) = rank.WageBand();
            var t = (skill.Value - ManagementSkill.Min) / (double)(ManagementSkill.Max - ManagementSkill.Min);
            var band = min + (max - min) * t;
            var difficulty = DifficultyProfile.For(_game.GetDifficulty());
            _game.SetHourlyWage(employee, (decimal)(band * difficulty.WageMultiplier));
        }

        // ── ticks ───────────────────────────────────────────────────────────────
        public void OnDayElapsed()
        {
            foreach (var store in _stores.Values.ToList())
            {
                store.RunDay();
                CheckQuit(store);
            }
            Save();
        }

        public void OnWeekElapsed()
        {
            foreach (var store in _stores.Values)
            {
                var report = store.CloseWeek();
                WeeklyDigest.Send(_game, report);
            }
            Save();
        }

        private void CheckQuit(ManagedStore store)
        {
            var difficulty = _game.GetDifficulty();
            var profile = DifficultyProfile.For(difficulty);
            // "overworked" = this manager runs more stores than their span of control allows.
            var skill = new ManagementSkill(store.Data.ManagerSkill);
            var span = Math.Max(1, skill.BaseSpanOfControl + profile.SpanOfControlDelta);
            var runningCount = _stores.Values.Count(s => s.Data.ManagerEmployeeId == store.Data.ManagerEmployeeId);
            var overworked = runningCount > span;

            if (profile.WouldQuit(difficulty, mistreated: false, overworkedOrUnderpaid: overworked)
                && store.Data.ManagerEmployeeId != null)
            {
                var emp = _game.FindEmployee(store.Data.ManagerEmployeeId);
                store.Data.ManagerEmployeeId = null;
                _game.SendManagerMessage(
                    Loc.Get("storemanager_options_header"),
                    Loc.Format("storemanager_manager_quit",
                        emp?.DisplayName ?? "your manager", store.Store.DisplayName));
            }
        }

        private StoreManagerData GetOrCreate(GameRef store)
        {
            if (_stores.TryGetValue(store.Id, out var existing))
                return existing.Data;

            var data = new StoreManagerData
            {
                StoreId = store.Id,
                MistakeSeed = store.Id.GetHashCode(),
            };
            _stores[store.Id] = new ManagedStore(_game, store, data);
            return data;
        }
    }
}
