#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using StoreManager.Debugging;
using StoreManager.Domain;
using StoreManager.Interop;
using StoreManager.Interop.Harmony;
using StoreManager.Runtime;
using StoreManager.UI;
using UnityEngine;

[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerInitMod))]
[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerCityMod))]

namespace StoreManager.Core
{
    /// <summary>
    /// Init-load entry (session-wide, fires once): Harmony patching + the custom skill build, plus
    /// the global defaults and the Options → Mods panel.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public sealed class StoreManagerInitMod : IModBigAmbitions
    {
        /// <summary>Defaults applied to each new store assignment. Edited in the options panel.</summary>
        public static GlobalDefaults Defaults { get; } = GlobalDefaults.Default();

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            try
            {
                // Load-bearing (D15): the OnSkillDataLoaded prefix must be in place before any save
                // that holds an sm:skill_* employee is deserialized, or the load-time compat fixes NPE.
                bool patched = HarmonyBootstrap.EnsurePatched();
                _ = SkillRegistry.Skill;   // build the SkillData now so BuildTagCache runs early

                StoreManagerOptions.Register(context, Defaults);
                context.Logger.Info($"Store Manager loaded (v3). Harmony patched={patched}. Panel: Options → Mods.");
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] init-load failed (mod will be inert): " + e);
            }
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            try { StoreManagerOptions.Unregister(); } catch (Exception e) { Debug.LogWarning("[StoreManager] options unregister: " + e.Message); }
            try { HarmonyBootstrap.Unpatch(); } catch (Exception e) { Debug.LogWarning("[StoreManager] unpatch: " + e.Message); }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// City-load entry: re-inject the skill (backstop), run the role-system self-check, then bring
    /// up the supervision directory wired to the game's day/save events. Torn down cleanly on
    /// unload — the modData entry is left in place. No re-skilling here (this fires on every city
    /// exit, not just uninstall); uninstall safety is the SafeRemove command + the degraded
    /// onSaveGame guard.
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
                SkillRegistry.EnsureInjected();      // backstop — the Harmony prefix is primary
                HqDeskAccess.EnsureDesksAcceptManager();   // backstop — the ItemsGetter postfix is primary
                RoleSystemState.Evaluate();

                _dir = new ManagerDirectory(StoreManagerInitMod.Defaults);
                Active = _dir;
                Guard("Load", () => _dir!.Load());

                // The game invokes these with a plain ?.Invoke() (no InvokeSafely) — a throw here
                // would abort the day's tick or wedge the save subsystem. Guard every one.
                _onDay = () => Guard("onNewDay", () => _dir?.OnNewDay());
                _onHour = () => Guard("onNewHour", () => _dir?.OnNewHour());
                _onSave = () => Guard("onSaveGame", OnSaveGame);
                GameApi.Subscribe(_onDay, _onHour, _onSave);

                StoreManagerCommands.Register(_dir);
                StoreManagerOptions.Rebuild();

                context.Logger.Info($"Store Manager active — {_dir.Plans.Count} plan(s)" +
                                    (_dir.ReadOnly ? " (READ-ONLY — saved data unreadable)" : "") +
                                    $" — {RoleSystemState.Summary()}");
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] city-load failed: " + e);
            }
            return Task.CompletedTask;
        }

        private void OnSaveGame()
        {
            // A degraded session must not leave an orphan primary skill in the save file.
            if (!RoleSystemState.IsActive)
            {
                try
                {
                    int n = RoleEmployees.ReskillAllToVanilla();
                    if (n > 0) Debug.LogWarning($"[StoreManager] role disabled — re-skilled {n} manager(s) to vanilla before save.");
                }
                catch (Exception e) { Debug.LogError("[StoreManager] pre-save re-skill failed: " + e.Message); }
            }
            _dir?.Save();
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
