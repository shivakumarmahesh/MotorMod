using HarmonyLib;
using UnityEngine;

namespace MotorMod
{
    /// <summary>A bike's saddlebags are the game's chest; these patches make them part of the bike.</summary>
    internal static class Saddlebags
    {
        public static MotorcycleController BikeOf(Container c) => c != null && c.m_rootObjectOverride != null ? c.m_rootObjectOverride.GetComponent<MotorcycleController>() : null;

        /// <summary>Shift+E fills the tank here too; the bags stay shut while someone rides the bike (opening them hands the
        /// bike's network record to whoever opens them, which would take it from the rider).</summary>
        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Open
        {
            private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                var bike = BikeOf(__instance);
                if (bike == null || hold) return true;
                var player = character as Player;
                if (alt && player != null && bike.Fuel != null && BikeFuel.Enabled) { bike.Fuel.Fill(player); __result = true; return false; }
                if (bike.HasRider) { character.Message(MessageHud.MessageType.Center, "Someone is riding it"); __result = true; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class Hover
        {
            private static void Postfix(Container __instance, ref string __result)
            {
                var bike = BikeOf(__instance);
                if (bike == null || bike.Fuel == null || !BikeFuel.Enabled) return;
                __result += Localization.instance.Localize($"\nFuel {bike.Fuel.Percent}" + (bike.Standing ? bike.Fuel.FillHint : ""));
            }
        }
    }

    /// <summary>The hammer never takes a bike apart from under its rider.</summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class NoRemovingRiddenBikes
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (!__result) return;
            var bike = __instance.GetComponent<MotorcycleController>();
            if (bike != null && bike.HasRider) __result = false;
        }
    }

    /// <summary>The HitDamageScale setting: hits from creatures and weapons on a bike (its own crashes are counted elsewhere).</summary>
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class BikeHitScale
    {
        private static void Prefix(WearNTear __instance, HitData hit)
        {
            if (hit == null || __instance.GetComponent<BikeHealth>() == null) return;
            float k = MotorModPlugin.HitDamageScale.Value;
            if (!Mathf.Approximately(k, 1f)) hit.m_damage.Modify(Mathf.Max(0f, k));
        }
    }

    /// <summary>
    /// While riding, the game's mount panel (the one a saddled lox shows) becomes the bike's fuel gauge: the bike's name and
    /// icon, and the stamina bar as the tank. Its health bar stays hidden: the bike's condition is read with the hammer.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateMount")]
    internal static class FuelGauge
    {
        private static bool s_healthHidden;

        private static void Postfix(Hud __instance, Player player)
        {
            var bike = player != null ? player.GetDoodadController() as MotorcycleController : null;
            bool show = bike != null && bike.Fuel != null && BikeFuel.Enabled;
            if (show != s_healthHidden)
            {
                s_healthHidden = show;
                foreach (var go in new[] { __instance.m_mountHealthBarSlow?.gameObject, __instance.m_mountHealthBarFast?.gameObject, __instance.m_mountHealthText?.gameObject })
                    if (go != null) go.SetActive(!show);
            }
            if (!show) return;
            __instance.m_mountPanel.SetActive(true);
            var piece = bike.GetComponent<Piece>();
            if (piece != null && __instance.m_mountIcon != null) __instance.m_mountIcon.overrideSprite = piece.m_icon;
            __instance.m_mountStaminaBar.SetValue(bike.Fuel.Fraction);
            __instance.m_mountStaminaText.text = "Fuel " + bike.Fuel.Percent;
            __instance.m_mountNameText.text = bike.m_hoverText;
        }
    }
}
