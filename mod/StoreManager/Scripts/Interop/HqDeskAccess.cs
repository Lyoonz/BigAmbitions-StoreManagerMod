#nullable enable
using System;
using UnityEngine;
using BigAmbitions.Items;

namespace StoreManager.Interop
{
    /// <summary>
    /// Lets a Store Manager take an HQ desk shift by appending <c>sm:skill_storemanager</c> to the
    /// <c>suitableSkills</c> of the three office-desk items — exactly how the vanilla manager skills
    /// already sit on those desks (probe-confirmed). <c>Item</c> is a <c>TaggedScriptableObject</c>
    /// reloaded from Addressables on each save-load via <c>ItemsGetter.OnItemsLoaded</c>, so this is
    /// re-applied from a Harmony postfix on that method plus a <c>[ModEntryOnCityLoad]</c> backstop.
    ///
    /// Idempotent and self-contained: it only ever <i>adds</i> one string, and only to the desk
    /// items. Nothing to undo on uninstall — a stale entry in a reloaded array is harmless (the
    /// game just won't find a skill by that name), and the array is rebuilt from Addressables anyway.
    /// </summary>
    public static class HqDeskAccess
    {
        private static readonly string[] DeskItems =
        {
            "ba:itemname_desktopcomputer", "ba:itemname_laptop", "ba:itemname_computer",
        };

        public static int Applied { get; private set; }

        /// <summary>Append the skill to each desk item's suitableSkills if not already there.</summary>
        public static void EnsureDesksAcceptManager()
        {
            int applied = 0;
            foreach (var itemName in DeskItems)
            {
                try
                {
                    var item = ItemsGetter.GetByName(itemName, true);
                    if (item == null) continue;

                    var ss = item.suitableSkills ?? Array.Empty<string>();
                    if (Array.IndexOf(ss, SkillRegistry.StoreManagerSkill) >= 0) { applied++; continue; }

                    var next = new string[ss.Length + 1];
                    Array.Copy(ss, next, ss.Length);
                    next[ss.Length] = SkillRegistry.StoreManagerSkill;
                    item.suitableSkills = next;
                    applied++;
                }
                catch (Exception e) { Debug.LogWarning($"[StoreManager] desk append failed for {itemName}: {e.Message}"); }
            }
            Applied = applied;
            if (applied > 0) Debug.Log($"[StoreManager] HQ desks now accept the Store Manager skill ({applied}/{DeskItems.Length}).");
        }

        public static bool AllDesksReady()
        {
            try
            {
                foreach (var itemName in DeskItems)
                {
                    var item = ItemsGetter.GetByName(itemName, true);
                    if (item?.suitableSkills == null) return false;
                    if (Array.IndexOf(item.suitableSkills, SkillRegistry.StoreManagerSkill) < 0) return false;
                }
                return true;
            }
            catch { return false; }
        }
    }
}
