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
        private Action? _onDay, _onSave, _onJob;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            try
            {
                _dir = new ManagerDirectory(StoreManagerInitMod.Defaults);
                Active = _dir;
                _dir.Load();

                _onDay = () => _dir?.OnNewDay();
                _onSave = () => _dir?.Save();
                _onJob = () => _dir?.OnJobChange();
                GameApi.Subscribe(_onDay, _onSave, _onJob);

                StoreManagerCommands.Register(_dir);
                StoreManagerOptions.Rebuild();

                context.Logger.Info($"Store Manager active — {_dir.Plans.Count} plan(s). " +
                                    "Console: StoreManager.Managers / .Adopt / .Stores / .Assign / .Status / .PlanWeek");
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
                if (_onDay != null && _onSave != null && _onJob != null)
                    GameApi.Unsubscribe(_onDay, _onSave, _onJob);
                StoreManagerCommands.Unregister();
                _dir?.Detach();
            }
            catch (Exception e) { Debug.LogError("[StoreManager] unload failed: " + e.Message); }
            _dir = null;
            Active = null;
            _onDay = _onSave = _onJob = null;
            return Task.CompletedTask;
        }
    }
}
