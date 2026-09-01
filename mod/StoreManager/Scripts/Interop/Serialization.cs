#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OdinSerializer;
using StoreManager.Domain;

namespace StoreManager.Interop
{
    /// <summary>
    /// Serialises the plan list to/from JSON for <c>GameInstance.modData["StoreManager.plans.v1"]</c>.
    /// Uses OdinSerializer (a game dependency) with a private permissive config — never the game's
    /// own save policy — so decimals, nested lists and the snapshot structs round-trip.
    /// NB: no <c>[Obsolete]</c> members on <see cref="StoreManagerPlan"/>/<see cref="StoreAssignment"/>
    /// ever — Odin's policy silently skips them (see DECISIONS.md / critique 2).
    /// </summary>
    public static class Serialization
    {
        [System.Serializable]
        private sealed class Envelope
        {
            public int Version = 1;
            public List<StoreManagerPlan> Plans = new();
        }

        public static string Serialize(IEnumerable<StoreManagerPlan> plans)
        {
            var env = new Envelope { Plans = plans.ToList() };
            var bytes = SerializationUtility.SerializeValue(env, DataFormat.JSON);
            return Encoding.UTF8.GetString(bytes);
        }

        public static List<StoreManagerPlan> Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<StoreManagerPlan>();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                var env = SerializationUtility.DeserializeValue<Envelope>(bytes, DataFormat.JSON);
                return env?.Plans ?? new List<StoreManagerPlan>();
            }
            catch
            {
                return new List<StoreManagerPlan>();
            }
        }
    }
}
