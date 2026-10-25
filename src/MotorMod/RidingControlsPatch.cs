using HarmonyLib;

namespace MotorMod
{
    /// <summary>
    /// While a player rides one of our bikes, the crouch key (Left Ctrl, which is also the wheelie key) must not toggle the
    /// game's crouch: the rider would get off crouched and sneaking, and the crouch animation could bend the riding pose.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class RidingControlsPatch
    {
        private static void Prefix(Player __instance, ref bool crouch)
        {
            if (crouch && __instance.GetDoodadController() is MotorcycleController) crouch = false;
        }
    }
}
