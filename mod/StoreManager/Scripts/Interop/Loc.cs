#nullable enable

namespace StoreManager.Interop
{
    /// <summary>
    /// Thin wrapper over the game's localisation lookup. Keys live in Locales/*.json.
    /// PHASE0: resolve the game's string-table API (the example mods pass raw keys like
    /// "backalleydealer:description" straight into TextMessage / Contact, so there is one).
    /// Until resolved, returns the key so text is still traceable in a playtest.
    /// </summary>
    public static class Loc
    {
        public static string Get(string key) => key; // PHASE0: game string table

        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            try { return string.Format(template, args); }
            catch { return template; }
        }
    }
}
