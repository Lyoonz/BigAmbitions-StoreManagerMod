#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using UnityEngine;
using UnityEngine.InputSystem;

// ─────────────────────────────────────────────────────────────────────────────
//  PHASE 0 PROBE — throwaway. Not shipped. This is the go/no-go gate.
//
//  Goal: prove three writes are possible from an official-SDK mod, with no BepInEx:
//    F9  → reassign an employee's task            (PROBE 1)
//    F10 → add a shift to tomorrow's roster       (PROBE 2)
//    F11 → place a restock order                  (PROBE 3)
//    F8  → dump the workforce object graph to the log (discovery aid)
//
//  Fill each TODO with the real class name found by decompiling the game assemblies
//  (ILSpy/dnSpy over the DLLs the SDK imported). Then run in a loaded city, watch the
//  log, and fill in probe/StoreManagerProbe/REPORT.md.
// ─────────────────────────────────────────────────────────────────────────────

[assembly: RegisterModClass(typeof(StoreManagerProbe.ProbeInit))]

namespace StoreManagerProbe
{
    [ModEntryOnCityLoad]
    public sealed class ProbeInit : IModBigAmbitions
    {
        private GameObject? _host;
        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _host = new GameObject("StoreManagerProbe");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            var runner = _host.AddComponent<ProbeRunner>();
            runner.Logger = context.Logger;
            context.Logger.Info("Probe armed. F8 dump · F9 reassign · F10 shift · F11 restock.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            return Task.CompletedTask;
        }
    }

    public sealed class ProbeRunner : MonoBehaviour
    {
        // PHASE0: exact type of ModContext.Logger — likely IModLogger. Adjust if the SDK names it
        // differently; only .Info(string) is used here.
        public IModLogger? Logger;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F8].wasPressedThisFrame) SafeRun("DUMP", DumpWorkforceGraph);
            if (kb[Key.F9].wasPressedThisFrame) SafeRun("PROBE 1 reassign", ProbeReassignTask);
            if (kb[Key.F10].wasPressedThisFrame) SafeRun("PROBE 2 shift", ProbeWriteShift);
            if (kb[Key.F11].wasPressedThisFrame) SafeRun("PROBE 3 restock", ProbeRestock);
        }

        private void SafeRun(string label, Action action)
        {
            try
            {
                Log($"── {label} ──");
                action();
                Log($"{label}: completed without exception");
            }
            catch (Exception e)
            {
                Log($"{label}: FAILED — {e.GetType().Name}: {e.Message}");
            }
        }

        // ── discovery ───────────────────────────────────────────────────────────
        private void DumpWorkforceGraph()
        {
            // CONFIRMED reachable from the SDK examples:
            //   GameManager.Instance, GameManager.Instance.playerController
            //   SaveGameManager.Current  (BigAmbitions.SaveSystem.Legacy)
            //   BuildingManager.Instance, BuildingHelper.GetBuilding(new Address(street, number))
            //
            // TODO: from GameManager / SaveGameManager, walk to the player's businesses and log:
            //   - each store: id, display name, BusinessType, reputation, today's revenue
            //   - each employee: id, name, home store, assigned task/station, skill values, presence
            //   - the schedule object for a store + its shift entries for today/tomorrow
            //   - the low-stock list + the supplier-order method signature
            //   - whether GameManager (or DayNightCycle) exposes a day/week changed event
            Log("TODO: implement DumpWorkforceGraph against the real types (see comments).");
        }

        // ── PROBE 1 ─────────────────────────────────────────────────────────────
        private void ProbeReassignTask()
        {
            // TODO:
            //   var store = <player's first store>;
            //   var emp   = <first employee of store>;
            //   var before = emp.<assignedTaskField>;
            //   emp.<assignedTaskField> = <Restock task/enum/component>;
            //   Log($"task {before} -> {emp.<assignedTaskField>}");
            //   Then observe in-game: does the employee actually walk to restock? Does it persist a save?
            throw new NotImplementedException("fill with real employee + task-assignment type");
        }

        // ── PROBE 2 ─────────────────────────────────────────────────────────────
        private void ProbeWriteShift()
        {
            // TODO (two variants — try both):
            //   A) direct: store.<schedule>.<AddShift>(emp, tomorrow, 9, 13, Station.Register);
            //   B) solver: call the game's scheduler (Google.OrTools-backed) and let it fill shifts.
            //   Log the schedule UI backing list before/after; confirm the employee turns up tomorrow.
            throw new NotImplementedException("fill with real schedule/roster type");
        }

        // ── PROBE 3 ─────────────────────────────────────────────────────────────
        private void ProbeRestock()
        {
            // TODO:
            //   var order = <store>.<inventory>.<PlaceOrder>(<productId>, <qty>);
            //   or a supplier/delivery-contract call + GameManager.ChangeMoneySafe for the cash side
            //   (ChangeMoneySafe(amount, new TransactionInfo(LegacyRef.Transaction.*, data, taxDeductible), true)).
            //   Log cash before/after and watch for the delivery arriving.
            throw new NotImplementedException("fill with real restock/supplier-order type");
        }

        private void Log(string msg)
        {
            Logger?.Info(msg);
            Debug.Log($"[StoreManagerProbe] {msg}");
        }
    }
}
