#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using StoreManager.Domain;
using StoreManager.Interop;
using StoreManager.PlayerScheduling;
using StoreManager.Runtime;
using StoreManager.UI;

[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerInitMod))]
[assembly: RegisterModClass(typeof(StoreManager.Core.StoreManagerCityMod))]

namespace StoreManager.Core
{
    /// <summary>
    /// Init-load entry: the global policy profile and its options-menu panel. Lives for the
    /// whole session, independent of which city/save is loaded.
    /// </summary>
    [ModEntryOnInitializationLoad]
    public sealed class StoreManagerInitMod : IModBigAmbitions
    {
        public static StorePolicy GlobalPolicy { get; } = StorePolicy.Default();

        private PolicyOptions? _options;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _options = new PolicyOptions(GlobalPolicy);
            _options.Register(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            _options?.Unregister();
            _options = null;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// City-load entry: wires the manager directory to the live game and subscribes to the
    /// day/week ticks. Everything store-specific is torn down on unload for a clean uninstall.
    /// </summary>
    [ModEntryOnCityLoad]
    public sealed class StoreManagerCityMod : IModBigAmbitions
    {
        private IGameBindings? _game;
        private ManagerDirectory? _directory;
        private RegisterHandoff? _handoff;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            _game = new GameBindingsLive();
            _directory = new ManagerDirectory(_game);
            _handoff = new RegisterHandoff(_game);

            _directory.Load();

            _game.DayElapsed += OnDay;
            _game.WeekElapsed += OnWeek;

            context.Logger.Info($"Store Manager active — {_directory.Stores.Count} managed store(s).");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            if (_game != null)
            {
                _game.DayElapsed -= OnDay;
                _game.WeekElapsed -= OnWeek;
            }
            _directory?.Detach();
            _directory = null;
            _handoff = null;
            _game = null;
            return Task.CompletedTask;
        }

        private void OnDay() => _directory?.OnDayElapsed();
        private void OnWeek() => _directory?.OnWeekElapsed();

        /// <summary>Exposed for the (future) hiring UI and for the probe harness.</summary>
        public ManagerDirectory? Directory => _directory;
        public RegisterHandoff? Handoff => _handoff;
    }
}
