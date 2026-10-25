using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// The fuel tank. It holds metres of riding (the tank's range, 6.67 km by default) in the bike's network record. The bikes
    /// burn Surtling cores (BikeDefinition.FuelItem, 1.33 km each). Fuel burns by distance driven, twice as fast with boost;
    /// standing still burns nothing. Empty, the engine dies. A new bike is built empty.
    /// Filling, only while the bike stands still: Shift+E pours cores from the player's bag, then from the saddlebags; a core
    /// used from the hotbar goes in too.
    /// </summary>
    public class BikeFuel : MonoBehaviour
    {
        private static readonly int s_fuelHash = "mm_fuel".GetStableHashCode();
        private ZNetView m_nview;
        private MotorcycleController m_bike;

        public static bool Enabled => MotorModPlugin.FuelEnabled == null || MotorModPlugin.FuelEnabled.Value;
        public static float Capacity => Mathf.Max(100f, MotorModPlugin.TankKm.Value * 1000f);
        public float Metres => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetFloat(s_fuelHash, 0f) : 0f;
        public float Fraction => Enabled ? Mathf.Clamp01(Metres / Capacity) : 1f;
        public bool Empty => Enabled && Metres <= 0.01f;
        public string Percent => $"{Mathf.CeilToInt(Fraction * 100f)}%";

        private BikeDefinition D => m_bike != null ? m_bike.m_definition : null;
        public string FuelItem => D != null ? D.FuelItem : null;

        /// <summary>The fuel's name as the game shows it ("Surtling core").</summary>
        public string FuelName
        {
            get
            {
                var prefab = FuelItem != null && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(FuelItem) : null;
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : "fuel";
            }
        }

        /// <summary>Hover lines for filling. Shown only while the bike stands still: callers check MotorcycleController.Standing.</summary>
        public string FillHint => $"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Fill tank ({FuelName})";
        public string HotbarHint => $"\n[<color=yellow><b>1-8</b></color>] Add {FuelName}";

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_bike = GetComponent<MotorcycleController>();
        }

        /// <summary>Metres one item of this fuel is worth in this bike, or 0 when the bike does not burn it.</summary>
        public float MetresPer(string prefabName) => D != null && prefabName == D.FuelItem ? D.FuelMetres : 0f;

        /// <summary>The owner burns fuel for the distance just driven. Crossing 20 % and 10 % warns the rider; empty kills the engine.</summary>
        public void Burn(float metres)
        {
            if (!Enabled || metres <= 0f || !m_nview.IsOwner()) return;
            float before = Metres, after = Mathf.Max(0f, before - metres);
            m_nview.GetZDO().Set(s_fuelHash, after);
            float cap = Capacity;
            if (before > 0.2f * cap && after <= 0.2f * cap) m_bike.RiderMessage(MessageHud.MessageType.Center, "Fuel low: 20 %");
            else if (before > 0.1f * cap && after <= 0.1f * cap) m_bike.RiderMessage(MessageHud.MessageType.Center, "Fuel very low: 10 %");
        }

        /// <summary>Shift+E: fuel from the player's bag first, then from the saddlebags, until the tank is full.</summary>
        public void Fill(Player player)
        {
            if (!CanFill(player)) return;
            string item = FuelItem;
            if (item == null) return;
            var storage = m_bike.Storage;
            int fromBag = Pour(player, player.GetInventory(), item);
            int fromBags = storage != null ? Pour(player, storage.GetInventory(), item) : 0;
            string name = FuelName.ToLowerInvariant();
            if (fromBag + fromBags == 0)
                player.Message(MessageHud.MessageType.Center, Metres >= Capacity - 1f ? "The tank is full" : $"No {name} in your bag or the saddlebags");
            else
                player.Message(MessageHud.MessageType.Center, $"Fuel {Percent} ({fromBag + fromBags} {name}{(fromBags > 0 ? $", {fromBags} from the saddlebags" : "")})");
        }

        /// <summary>A hotbar item used on the bike: the bike's fuel goes in until the tank is full.</summary>
        public bool UseItem(Player player, ItemDrop.ItemData item)
        {
            string prefab = item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (prefab == null || MetresPer(prefab) <= 0f) return false;
            if (!CanFill(player)) return true;
            int n = Pour(player, player.GetInventory(), prefab);
            player.Message(MessageHud.MessageType.Center, n == 0 ? "The tank is full" : $"Fuel {Percent} ({n} {Localization.instance.Localize(item.m_shared.m_name).ToLowerInvariant()})");
            return true;
        }

        private bool CanFill(Player player)
        {
            if (!Enabled) { player.Message(MessageHud.MessageType.Center, "Fuel is switched off on this world"); return false; }
            if (m_nview == null || !m_nview.IsValid()) return false;
            if (m_bike.HasRider) { player.Message(MessageHud.MessageType.Center, "Someone is riding it"); return false; }
            if (!m_bike.Standing) { player.Message(MessageHud.MessageType.Center, "Stop the bike before filling the tank"); return false; }
            if (m_bike.BagsOpen) { player.Message(MessageHud.MessageType.Center, "Someone has the saddlebags open"); return false; }
            m_nview.ClaimOwnership();          // nobody rides it: the filler keeps the tank (and the saddlebags) for this
            return true;
        }

        /// <summary>Moves items of one kind from an inventory into the tank, as many as it takes to fill it (the last one may overfill).</summary>
        private int Pour(Player player, Inventory inventory, string prefabName)
        {
            float per = MetresPer(prefabName), room = Capacity - Metres;
            if (per <= 0f || room < 1f || inventory == null) return 0;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return 0;
            string name = drop.m_itemData.m_shared.m_name;
            int n = Mathf.Min(inventory.CountItems(name), Mathf.CeilToInt(room / per));
            if (n <= 0) return 0;
            inventory.RemoveItem(name, n);
            m_nview.GetZDO().Set(s_fuelHash, Mathf.Min(Capacity, Metres + n * per));
            MotorModPlugin.Log.LogInfo($"{gameObject.name}: {player.GetPlayerName()} added {n} {prefabName}, fuel {Metres:0} m of {Capacity:0}");
            return n;
        }

        /// <summary>Console and test harness: set the tank (0..1 of full).</summary>
        public void SetFraction(float f)
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            if (!m_nview.IsOwner()) { if (m_bike.HasRider) return; m_nview.ClaimOwnership(); }   // never take a bike away from its rider
            m_nview.GetZDO().Set(s_fuelHash, Mathf.Clamp01(f) * Capacity);
        }
    }
}
