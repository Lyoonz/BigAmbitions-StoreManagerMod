#nullable enable
using System;
using Localizor;   // HGPlugins.dll — LocalizorManager.GetLocalization(this string)

namespace StoreManager.Interop
{
    /// <summary>
    /// Resolve a Localizor key to text. The mod ships its keys in <c>Locales/en.json</c> /
    /// <c>nl.json</c> which the game merges; if a key is missing (or Localizor echoes it back),
    /// callers fall back to a supplied literal. The ModOptions panel hands keys to the game
    /// directly and doesn't need this — it's for the raw-uGUI HQ tab.
    /// </summary>
    public static class Loc
    {
        public static string T(string key)
        {
            try { return key.GetLocalization() ?? key; }
            catch { return key; }
        }

        public static string T(string key, string fallback)
        {
            var s = T(key);
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }
    }
}
