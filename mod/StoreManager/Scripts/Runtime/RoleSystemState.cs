#nullable enable
using System;
using StoreManager.Interop;
using StoreManager.Interop.Harmony;
using UnityEngine;

namespace StoreManager.Runtime
{
    public enum RoleSystem
    {
        /// <summary>Custom skill built, injected, Harmony patched — full function.</summary>
        Active,

        /// <summary>
        /// The custom-skill layer isn't safe this session (Harmony didn't patch, the game changed
        /// shape, or <c>BuildTagCache()</c> threw). Existing plans load read-only, the planner
        /// no-ops, no new hires, and <c>onSaveGame</c> re-skills any <c>sm:*</c> employee back to
        /// the vanilla fallback so the save never carries an orphan primary skill. The Options
        /// panel stays usable and explains the state.
        /// </summary>
        Disabled,
    }

    /// <summary>
    /// One ordered self-check at city load decides whether the role system runs. Build-agnostic:
    /// it verifies the concrete handles the mod needs (structural) rather than a version number,
    /// which the game exposes no reliable API for.
    /// </summary>
    public static class RoleSystemState
    {
        public static RoleSystem State { get; private set; } = RoleSystem.Disabled;
        public static string Reason { get; private set; } = "not initialised";
        public static bool IsActive => State == RoleSystem.Active;

        private static bool _warned;

        public static void Evaluate()
        {
            string reason;

            if (!SkillRegistry.StructuralCheck(out var structDetail))
                reason = "game shape changed: " + structDetail;
            else if (!SkillRegistry.SkillBuilt)
                reason = "custom skill could not be built (BuildTagCache failed)";
            else if (!HarmonyBootstrap.EnsurePatched())
                reason = "Harmony patch failed: " + (HarmonyBootstrap.LastError ?? "unknown");
            else if (!SkillRegistry.EnsureInjected())
                reason = "skill injection failed";
            else
                reason = "";

            if (reason.Length == 0)
            {
                State = RoleSystem.Active;
                Reason = "ok";
                Debug.Log("[StoreManager] role system ACTIVE (custom skill live).");
            }
            else
            {
                State = RoleSystem.Disabled;
                Reason = reason;
                Debug.LogWarning("[StoreManager] role system DISABLED — " + reason);
                if (!_warned)
                {
                    _warned = true;
                    Feedback.Toast(Feedback.Level.Warning, "storemanager_notify_role_disabled", null, "sm_role_disabled");
                    Feedback.Message("storemanager_msg_role_disabled", new() { { "reason", reason } });
                }
            }
        }

        /// <summary>Panel/console one-liner.</summary>
        public static string Summary() => State == RoleSystem.Active
            ? "Store Manager role: active"
            : "Store Manager role: DISABLED on this game build — supervision paused, no data lost (" + Reason + ")";
    }
}
