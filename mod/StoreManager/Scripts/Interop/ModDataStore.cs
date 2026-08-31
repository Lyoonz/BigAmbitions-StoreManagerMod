#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace StoreManager.Interop
{
    /// <summary>
    /// File-based mod persistence. Phase 0 confirmed <c>ModContext</c> exposes no save API and the
    /// game's <c>GameInstance</c> can't take a new field, so custom per-store data lives in a JSON
    /// file next to the game's own save data, namespaced by mod id.
    ///
    /// VERIFY: key the filename by the active save so two saves don't share manager assignments —
    /// e.g. <c>SaveGameManager.Current</c> should expose the save name / folder. Until confirmed,
    /// a single shared file is used (fine for a single-save playtester).
    /// </summary>
    public static class ModDataStore
    {
        private const string ModId = "StoreManager";

        private static string Dir
        {
            get
            {
                var d = Path.Combine(Application.persistentDataPath, "Mods", ModId);
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static string PathFor(string key) => Path.Combine(Dir, Sanitise(key) + ".json");

        public static void Write(string key, string json)
        {
            try { File.WriteAllText(PathFor(key), json); }
            catch (Exception e) { Debug.LogError($"[StoreManager] save failed ({key}): {e.Message}"); }
        }

        public static string? Read(string key)
        {
            try
            {
                var p = PathFor(key);
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[StoreManager] load failed ({key}): {e.Message}");
                return null;
            }
        }

        public static void Delete(string key)
        {
            try { var p = PathFor(key); if (File.Exists(p)) File.Delete(p); }
            catch { /* ignore */ }
        }

        private static string Sanitise(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key;
        }
    }
}
