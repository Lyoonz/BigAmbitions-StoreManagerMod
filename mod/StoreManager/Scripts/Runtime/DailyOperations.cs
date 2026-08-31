#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using StoreManager.Domain;
using StoreManager.Interop;

namespace StoreManager.Runtime
{
    /// <summary>
    /// The manager's daily work: the concrete steps, each one attempted through
    /// <see cref="IGameBindings"/>. Returns the set of operation kinds attempted so the
    /// <see cref="MistakeModel"/> only rolls for things that actually happened.
    /// </summary>
    public sealed class DailyOperations
    {
        private readonly IGameBindings _game;

        public DailyOperations(IGameBindings game) => _game = game;

        public IReadOnlyList<MistakeKind> Run(GameRef store, StoreManagerData data, ManagerRank controller)
        {
            var attempted = new List<MistakeKind>();
            if (controller == ManagerRank.Employee)
                return attempted; // unmanaged — nobody runs these steps

            Restock(store, data, attempted);
            Schedule(store, data, attempted);
            Leave(store, data, controller, attempted);
            Complaints(store, data, attempted);
            if (controller.CanArrangeTraining())
                Training(store, data, attempted);

            return attempted;
        }

        private void Restock(GameRef store, StoreManagerData data, List<MistakeKind> attempted)
        {
            var low = _game.GetLowStock(store).ToList();
            if (low.Count == 0) return;
            attempted.Add(MistakeKind.UnderOrdered);
            attempted.Add(MistakeKind.OverOrdered);

            decimal spentThisWeek = data.CurrentWeek.RestockSpend;
            foreach (var (product, shortfall) in low)
            {
                if (spentThisWeek >= data.Policy.WeeklyRestockBudgetCap)
                {
                    data.CurrentWeek.AttentionItems.Add($"restock budget cap hit at {store.DisplayName}");
                    break;
                }
                if (_game.PlaceRestockOrder(store, product, shortfall, out var cost))
                {
                    spentThisWeek += cost;
                    data.CurrentWeek.RestockSpend += cost;
                }
            }
        }

        private void Schedule(GameRef store, StoreManagerData data, List<MistakeKind> attempted)
        {
            attempted.Add(MistakeKind.Understaffed);
            attempted.Add(MistakeKind.Overstaffed);
            // D5: delegate to the game's own scheduler rather than composing shifts by hand.
            _game.RunGameScheduler(store, data.Policy.StaffingMultiplier);

            var tomorrow = _game.CurrentDate.AddDays(1);
            var shifts = _game.GetShifts(store, tomorrow).ToList();
            data.CurrentWeek.ShiftsTotal += Math.Max(shifts.Count, 1);
            data.CurrentWeek.ShiftsCovered += shifts.Count;
        }

        private void Leave(GameRef store, StoreManagerData data, ManagerRank controller, List<MistakeKind> attempted)
        {
            var pending = _game.GetPendingLeave(store).ToList();
            if (pending.Count == 0) return;
            attempted.Add(MistakeKind.UncoveredLeave);

            foreach (var request in pending)
            {
                if (data.Policy.LeaveApproval == LeaveApprovalMode.AskPlayer)
                {
                    data.CurrentWeek.AttentionItems.Add($"leave request from {request.Employee.DisplayName} awaits you");
                    continue;
                }
                _game.ApproveLeave(request);
                _game.ArrangeCover(store, request);
            }
        }

        private void Complaints(GameRef store, StoreManagerData data, List<MistakeKind> attempted)
        {
            var open = _game.GetOpenComplaints(store).ToList();
            if (open.Count == 0) return;
            attempted.Add(MistakeKind.ComplaintUnresolved);

            data.CurrentWeek.ComplaintsTotal += open.Count;
            foreach (var complaint in open)
                if (_game.ResolveComplaint(complaint))
                    data.CurrentWeek.ComplaintsResolved++;
        }

        private void Training(GameRef store, StoreManagerData data, List<MistakeKind> attempted)
        {
            if (data.Policy.WeeklyTrainingBudget <= 0) return;
            attempted.Add(MistakeKind.TrainingMisspent);

            var weakest = _game.GetEmployees(store)
                .Select(e => (emp: e, skill: _game.GetEmployeeSkill(e, TrainableSkill.CustomerService)))
                .Where(x => x.skill < 3)
                .OrderBy(x => x.skill)
                .FirstOrDefault();

            if (weakest.emp.Raw == null) return;
            _game.StartTraining(weakest.emp, TrainableSkill.CustomerService, out var cost);
            if (cost > data.Policy.WeeklyTrainingBudget)
                data.CurrentWeek.AttentionItems.Add($"training at {store.DisplayName} exceeded budget");
        }
    }
}
