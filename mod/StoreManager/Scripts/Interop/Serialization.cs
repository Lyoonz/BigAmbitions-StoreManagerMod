#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OdinSerializer;
using StoreManager.Domain;
using UnityEngine;

namespace StoreManager.Interop
{
    /// <summary>
    /// Serialises the plan list to/from JSON for <c>GameInstance.modData["StoreManager.plans.v1"]</c>.
    /// Uses OdinSerializer (a game dependency) with an explicit pinned policy so the mod's format
    /// never depends on game internals. All state on the persisted types is public fields with no
    /// <c>[Obsolete]</c> members (Odin's policy would silently skip those).
    ///
    /// <see cref="Serialize"/> never throws (it runs inside the game's unguarded onSaveGame). A parse
    /// failure in <see cref="Deserialize"/> is reported distinctly from "no data" via <see cref="Load"/>
    /// so the caller can refuse to overwrite an unreadable-but-present blob.
    /// </summary>
    public static class Serialization
    {
        [System.Serializable]
        private sealed class Envelope
        {
            public int Version = 1;
            public List<StoreManagerPlan> Plans = new();
        }

        private static string? _lastGood;

        private static SerializationContext SerContext() => new()
        {
            Config = new SerializationConfig
            {
                SerializationPolicy = SerializationPolicies.Everything,
            },
        };

        private static DeserializationContext DeContext() => new()
        {
            Config = new SerializationConfig
            {
                SerializationPolicy = SerializationPolicies.Everything,
                AllowDeserializeInvalidData = true,
            },
        };

        /// <summary>Never throws. Returns the last-known-good JSON on failure, or null if there is none.</summary>
        public static string? Serialize(IEnumerable<StoreManagerPlan> plans)
        {
            try
            {
                var env = new Envelope { Plans = plans.ToList() };
                var bytes = SerializationUtility.SerializeValue(env, DataFormat.JSON, SerContext());
                if (bytes == null || bytes.Length == 0) return _lastGood;
                var json = Encoding.UTF8.GetString(bytes);
                _lastGood = json;
                return json;
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] Serialize failed: " + e.Message);
                return _lastGood;
            }
        }

        public enum LoadStatus { Ok, Absent, Corrupt }

        public readonly struct LoadResult
        {
            public LoadStatus Status { get; }
            public List<StoreManagerPlan> Plans { get; }
            public LoadResult(LoadStatus s, List<StoreManagerPlan> p) { Status = s; Plans = p; }
        }

        public static LoadResult Load(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new LoadResult(LoadStatus.Absent, new List<StoreManagerPlan>());
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var env = SerializationUtility.DeserializeValue<Envelope>(bytes, DataFormat.JSON, DeContext());
                if (env == null || env.Plans == null)
                    return new LoadResult(LoadStatus.Corrupt, new List<StoreManagerPlan>());
                _lastGood = json;
                return new LoadResult(LoadStatus.Ok, env.Plans);
            }
            catch (Exception e)
            {
                Debug.LogError("[StoreManager] Deserialize failed (blob kept intact): " + e.Message);
                return new LoadResult(LoadStatus.Corrupt, new List<StoreManagerPlan>());
            }
        }
    }
}
