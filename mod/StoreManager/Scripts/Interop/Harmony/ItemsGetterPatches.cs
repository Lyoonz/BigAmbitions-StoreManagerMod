#nullable enable
using System;
using HarmonyLib;
using UnityEngine;
using BigAmbitions.Items;

namespace StoreManager.Interop.Harmony
{
    /// <summary>
    /// Re-applies the HQ-desk <c>suitableSkills</c> append every time the game reloads its item
    /// definitions (save-load / new-game), so a Store Manager stays schedulable at an office desk.
    /// Postfix only — never touches the game's own load path, and swallows everything.
    /// </summary>
    public static class ItemsGetterPatches
    {
        public static bool Patched { get; private set; }

        [HarmonyPatch(typeof(ItemsGetter), nameof(ItemsGetter.OnItemsLoaded))]
        private static class OnItemsLoaded_Patch
        {
            private static void Postfix()
            {
                try
                {
                    Patched = true;
                    HqDeskAccess.EnsureDesksAcceptManager();
                }
                catch (Exception e) { Debug.LogError("[StoreManager] OnItemsLoaded postfix swallowed: " + e); }
            }
        }
    }
}
