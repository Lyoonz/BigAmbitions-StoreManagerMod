#nullable enable

namespace StoreManager.Domain
{
    /// <summary>Maps onto the game's difficulty setting. Resolved in GameBindings (PHASE0).</summary>
    public enum GameDifficulty
    {
        Easy,
        Normal,
        Hard,
    }

    /// <summary>
    /// How difficulty bends the manager numbers. See the design brief §06.
    /// </summary>
    public readonly struct DifficultyProfile
    {
        public float MistakeFrequencyMultiplier { get; }
        public float MistakeSeverityMultiplier { get; }
        public int SpanOfControlDelta { get; }
        public float WageMultiplier { get; }
        public bool ManagersCanQuit { get; }

        private DifficultyProfile(float freq, float sev, int spanDelta, float wage, bool canQuit)
        {
            MistakeFrequencyMultiplier = freq;
            MistakeSeverityMultiplier = sev;
            SpanOfControlDelta = spanDelta;
            WageMultiplier = wage;
            ManagersCanQuit = canQuit;
        }

        public static DifficultyProfile For(GameDifficulty difficulty) => difficulty switch
        {
            GameDifficulty.Easy => new DifficultyProfile(0.5f, 0.75f, +1, 0.9f, canQuit: false),
            GameDifficulty.Hard => new DifficultyProfile(1.5f, 1.35f, -1, 1.15f, canQuit: true),
            _ => new DifficultyProfile(1.0f, 1.0f, 0, 1.0f, canQuit: true),
        };

        /// <summary>On Normal a manager only quits if genuinely mistreated; on Hard, also if overworked/underpaid.</summary>
        public bool WouldQuit(GameDifficulty difficulty, bool mistreated, bool overworkedOrUnderpaid)
        {
            if (!ManagersCanQuit) return false;
            return difficulty == GameDifficulty.Hard
                ? mistreated || overworkedOrUnderpaid
                : mistreated;
        }
    }
}
