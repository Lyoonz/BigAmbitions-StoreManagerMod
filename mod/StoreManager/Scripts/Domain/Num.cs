#nullable enable

namespace StoreManager.Domain
{
    /// <summary>
    /// Tiny numeric helpers. <c>System.Math.Clamp</c> is .NET Standard 2.1 only; the mod builds
    /// against .NET Framework 4.7.2 reference assemblies (matching the SDK / Unity 2022.3), so
    /// this fills the gap without pulling UnityEngine into the pure domain layer.
    /// </summary>
    internal static class Num
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
