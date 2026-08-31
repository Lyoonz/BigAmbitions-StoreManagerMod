#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OdinSerializer;
using StoreManager.Domain;

namespace StoreManager.Interop
{
    /// <summary>
    /// Uses OdinSerializer — already a game dependency (OdinSerializer.dll in the asmdef) and
    /// the serializer the game's own save system uses, so decimals, nested lists and structs
    /// round-trip without a custom DTO.
    /// PHASE0: confirm the namespace is `OdinSerializer` (vs `Sirenix.Serialization` in some builds).
    /// </summary>
    public static class Serialization
    {
        [System.Serializable]
        private sealed class Envelope
        {
            public List<StoreManagerData> Items = new();
        }

        public static string SerializeList(IEnumerable<StoreManagerData> items)
        {
            var envelope = new Envelope { Items = items.ToList() };
            var bytes = SerializationUtility.SerializeValue(envelope, DataFormat.JSON);
            return Encoding.UTF8.GetString(bytes);
        }

        public static IEnumerable<StoreManagerData> DeserializeList(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Enumerable.Empty<StoreManagerData>();

            var bytes = Encoding.UTF8.GetBytes(json);
            var envelope = SerializationUtility.DeserializeValue<Envelope>(bytes, DataFormat.JSON);
            return envelope?.Items ?? new List<StoreManagerData>();
        }
    }
}
