using HarmonyLib;

namespace MotorMod
{
    /// <summary>Valheim hides its F5 console unless the game was started with "-console". Our commands need it, so turn it on.</summary>
    [HarmonyPatch(typeof(Console), nameof(Console.IsConsoleEnabled))]
    internal static class ConsolePatch
    {
        private static void Postfix(ref bool __result) => __result = true;
    }
}
