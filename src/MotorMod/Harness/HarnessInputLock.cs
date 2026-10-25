using HarmonyLib;

namespace MotorMod.Harness
{
    /// <summary>
    /// In harness mode the test character ignores the keyboard and mouse. You keep working while the harness window is
    /// open, and the window can hold the keyboard focus: a stray Space or E would make the test rider jump or get off the
    /// bike. Both of the game's input
    /// gates are closed: the player's own (use key, hotbar) and the controller's (move, jump, attack, dodge).
    /// </summary>
    [HarmonyPatch]
    internal static class HarnessInputLock
    {
        [HarmonyPatch(typeof(Player), "TakeInput"), HarmonyPrefix]
        private static bool PlayerTakeInput(ref bool __result)
        {
            if (!HarnessMode.Enabled) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(PlayerController), "TakeInput"), HarmonyPrefix]
        private static bool ControllerTakeInput(ref bool __result)
        {
            if (!HarnessMode.Enabled) return true;
            __result = false;
            return false;
        }
    }
}
