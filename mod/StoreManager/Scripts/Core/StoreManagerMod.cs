#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using StoreManager.Debugging;
using StoreManager.Domain;
using StoreManager.Interop;
using StoreManager.Runtime;
using StoreManager.UI;
using UnityEngine;

[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerInitMod))]
[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerCityMod))]

namespace StoreManager.Core
{
    /// <summary>
    /// Init-load entry: the global defaults + the Options → Mods panel. Session-wide.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public sealed class StoreManagerInitMod : IModBigAmbitions
    {
        /// <summary>Defaults applied to each new store assignment. Edited in the options panel.</summary>
        public static GlobalDefaults Defaults { get; } = GlobalDefaults.Default();

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            StoreManagerOptions.Register(context, Defaults);
            context.Logger.Info("Store Manager loaded (v2). Panel: Options → Mods. Console: StoreManager.*");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            StoreManagerOptions.Unregister();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// City-load entry: the supervision directory, wired to the game's day/save/job events.
    /// Torn down cleanly on unload (modData entry is left in place — re-adopted on reinstall).
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class StoreManagerCityMod : IModBigAmbitions
    {
        /// <summary>The live directory for the loaded save, or null between cities.</summary>
        public static ManagerDirectory? Active { get; private set; }

        private ManagerDirectory? _dir;
        private Action? _onDay, _onHour, _onSave;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            try
            {
                _dir = new ManagerDirectory(StoreManagerInitMod.Defaults);
                Active = _dir;
                Guard("Load", () => _dir!.Load());

                // The game invokes these with a plain ?.Invoke() (no InvokeSafely) — a throw here
                // would abort the day's tick or wedge the save subsystem. Guard every one.
                _onDay = () => Guard("onNewDay", () => _dir?.OnNewDay());
                _onHour = () => Guard("onNewHour", () => _dir?.OnNewHour());
                _onSave = () => Guard("onSaveGame", () => _dir?.Save());
                GameApi.Subscribe(_onDay, _onHour, _onSave);

                StoreManagerCommands.Register(_dir);
                StoreManagerOptions.Rebuild();

                context.Logger.Info($"Store Manager active — {_dir.Plans.Count} plan(s)" +
                                    (_dir.ReadOnly ? " (READ-ONLY — saved data unreadable)" : "") +
                                    ". Console: StoreManager.Managers / .Adopt / .Stores / .Assign / .Status / .PlanWeek");
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] city-load failed: " + e);
            }
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            try
            {
                if (_onDay != null && _onHour != null && _onSave != null)
                    GameApi.Unsubscribe(_onDay, _onHour, _onSave);
                StoreManagerCommands.Unregister();
                Guard("Detach", () => _dir?.Detach());
            }
            catch (Exception e) { Debug.LogError("[StoreManager] unload failed: " + e.Message); }
            _dir = null;
            Active = null;
            _onDay = _onHour = _onSave = null;
            return Task.CompletedTask;
        }

        private static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogError($"[StoreManager] {what} threw (swallowed): {e}"); }
        }
    }
}
