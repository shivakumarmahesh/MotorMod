using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;
using UnityEngine;

namespace MotorMod
{
    /// <summary>Everything that differs between bikes: assets, display text, survival recipe, ride stats, rider placement.</summary>
    public class BikeDefinition
    {
        public string Key;              // console name, config section, and the bundle's name for a bike with its own model
        public string PrefabName;       // the registered prefab (what a world saves); for a bike with its own model also the asset in its bundle
        public string DisplayName;
        public string Description;
        public string BaseKey;          // a variant (another name and trail on another bike's model): that bike's key
        public Color IconTint;          // a variant's hammer-menu icon is its base bike's, tinted with this (alpha 0: as it is)
        public string BundleKey => BaseKey ?? Key;
        public string BundlePrefab => BaseKey == null ? PrefabName : All.Find(d => d.Key == BaseKey).PrefabName;
        public string IconAsset => BundleKey + "_icon";
        public string CraftingStation = CraftingStations.Forge;
        public RequirementConfig[] Requirements;
        public string Pose = "attach_lox";
        public string Trail = "fire";                  // fire, fire_blue or fire_green (BikeTrail)
        public GameObject Prefab;                      // the registered prefab (set at load)
        public string FuelItem;              // the bike's fuel (item prefab)
        public float FuelMetres;             // metres per item of fuel
        public float DefaultBagForward = 0f, DefaultBagUp = 0f, DefaultBagScale = 0.85f;
        public float WheelieEase = 1f;       // >1 easier (slower), <1 harder

        // defaults per class; the config file can override every one of them per bike
        public float TopSpeed = 16f, Acceleration = 5f, BrakeForce = 9f, TurnRateSlow = 60f, TurnRateFast = 28f, LeanAngle = 22f, BoostMultiplier = 2f, Terrain = 0.5f;
        public float DefaultSeatForward = 0f, DefaultSeatUp = 0f, DefaultGripUp = 0f, DefaultGripSide = 0f, DefaultGripForward = 0f;
        public float Lean = 12f, LeanMax = 30f, DefaultFootForward = 0.55f, DefaultFootUp = 0.38f, DefaultFootSide = 0.32f;   // rider pose: torso lean (deg), cap on the automatic extra lean, footpeg position relative to the seat (m)

        public ConfigEntry<float> CfgTopSpeed, CfgAcceleration, CfgBrakeForce, CfgTurnRateSlow, CfgTurnRateFast, CfgLeanAngle, CfgBoostMultiplier, CfgTerrain;
        public ConfigEntry<float> SeatOffsetUp, SeatOffsetForward, SeatOffsetSide, Scale, GripOffsetUp, GripOffsetSide, GripOffsetForward;
        public ConfigEntry<string> CfgPose;
        public ConfigEntry<float> CfgWheelieEase;
        public ConfigEntry<float> CfgLean, CfgLeanMax, FootForward_, FootUp_, FootSide_;
        public ConfigEntry<float> BagForward, BagUp, BagScale;
        public ConfigEntry<float> FootForward => FootForward_; public ConfigEntry<float> FootUp => FootUp_; public ConfigEntry<float> FootSide => FootSide_;

        static RequirementConfig R(string item, int n) => new RequirementConfig(item, n, 0, true);

        public static readonly List<BikeDefinition> All = Roster();

        private static List<BikeDefinition> Roster()
        {
            var hel = new BikeDefinition
            {
                Key = "hel", PrefabName = "HelCycle", DisplayName = "Hel Cycle", Description = "Silver, bone and a skull that remembers being alive.",
                WheelieEase = 0.85f, TopSpeed = 18f, Acceleration = 6f, TurnRateSlow = 60f, TurnRateFast = 28f, Terrain = 0.3f,
                Lean = 0f, DefaultSeatForward = -0.1f, DefaultFootForward = 0.6f, DefaultFootUp = 0.45f, DefaultFootSide = 0.34f,
                FuelItem = "SurtlingCore", FuelMetres = 1334f,
                Requirements = new[] { R("Silver", 40), R("Iron", 10), R("BoneFragments", 50), R("SurtlingCore", 8), R("TrophySkeleton", 1), R("Chain", 4) },
            };
            // the Hel Cycle's twins: the same bike and ride, flames of another colour, another trophy in the recipe
            return new List<BikeDefinition>
            {
                hel,
                Variant(hel, "wraith", "WraithCycle", "Wraith Cycle", "The Hel Cycle's cold twin. Its flames burn blue, like the lights the wraiths carry through the swamp at night.",
                    "fire_blue", "TrophyWraith", new Color(0.55f, 0.75f, 1f, 1f)),
                Variant(hel, "draugr", "DraugrCycle", "Draugr Cycle", "The Hel Cycle risen from the swamp. Its flames burn green as rot.",
                    "fire_green", "TrophyDraugr", new Color(0.55f, 1f, 0.55f, 1f)),
            };
        }

        /// <summary>Another name and trail on a bike's model: every ride and rider setting copied, the trophy in the recipe swapped.</summary>
        private static BikeDefinition Variant(BikeDefinition b, string key, string prefab, string name, string description, string trail, string trophy, Color tint)
        {
            var v = (BikeDefinition)b.MemberwiseClone();
            v.Key = key; v.PrefabName = prefab; v.DisplayName = name; v.Description = description; v.Trail = trail; v.BaseKey = b.Key; v.IconTint = tint;
            v.Requirements = System.Array.ConvertAll(b.Requirements, r => r.Item == "TrophySkeleton" ? R(trophy, r.Amount) : R(r.Item, r.Amount));
            return v;
        }

        public static BikeDefinition Find(string nameOrKey)
        {
            if (string.IsNullOrEmpty(nameOrKey)) return null;
            var n = nameOrKey.ToLowerInvariant();
            return All.Find(d => d.Key == n) ?? All.Find(d => d.Key.StartsWith(n) || d.PrefabName.ToLowerInvariant().StartsWith(n) || d.DisplayName.ToLowerInvariant().StartsWith(n));
        }

        public static BikeDefinition ForObject(string gameObjectName) => All.Find(d => gameObjectName.StartsWith(d.PrefabName));

        public void BindConfig(ConfigFile config)
        {
            CfgTopSpeed = config.Bind(Key, "TopSpeed", TopSpeed, "Top speed, metres per second.");
            CfgAcceleration = config.Bind(Key, "Acceleration", Acceleration, "Throttle acceleration, m/s^2.");
            CfgBrakeForce = config.Bind(Key, "BrakeForce", BrakeForce, "Braking deceleration, m/s^2.");
            CfgTurnRateSlow = config.Bind(Key, "TurnRateSlow", TurnRateSlow, "Turn rate at walking pace, degrees per second.");
            CfgTurnRateFast = config.Bind(Key, "TurnRateFast", TurnRateFast, "Turn rate at top speed, degrees per second.");
            CfgLeanAngle = config.Bind(Key, "LeanAngle", LeanAngle, "Visual lean into turns at speed, degrees.");
            CfgBoostMultiplier = config.Bind(Key, "BoostMultiplier", BoostMultiplier, "Top speed multiplier while holding Run.");
            CfgTerrain = config.Bind(Key, "Terrain", Terrain, "0 to 1: how well the bike keeps speed and composure over bumps and slopes.");
            CfgWheelieEase = config.Bind(Key, "WheelieEase", WheelieEase, "Wheelie difficulty: above 1 slower and easier, below 1 quicker and harder.");
            CfgPose = config.Bind(Key, "Pose", Pose, "Rider animation while riding (attach_lox reaches forward, attach_chair sits upright).");
            CfgLean = config.Bind(Key, "RiderLean", Lean, "Torso lean toward the bars, degrees; more is added automatically when the grips are out of reach.");
            CfgLeanMax = config.Bind(Key, "RiderLeanMax", LeanMax, "Most extra lean the rider adds to reach the grips, degrees. Beyond that the arms stretch and the shoulders roll forward instead.");
            FootForward_ = config.Bind(Key, "FootForward", DefaultFootForward, "Footpegs ahead of the seat, metres.");
            FootUp_ = config.Bind(Key, "FootUp", DefaultFootUp, "Footpeg height above the ground, metres.");
            FootSide_ = config.Bind(Key, "FootSide", DefaultFootSide, "Footpegs out from the centre line, metres.");
            SeatOffsetUp = config.Bind(Key, "SeatOffsetUp", DefaultSeatUp, "Raise (+) or lower (-) the rider on the seat, metres. Re-mount after \"motor reload\".");
            SeatOffsetForward = config.Bind(Key, "SeatOffsetForward", DefaultSeatForward, "Move the rider forward (+) or back (-) on the seat, metres.");
            SeatOffsetSide = config.Bind(Key, "SeatOffsetSide", 0f, "Move the rider to the right (+) or left (-), metres.");
            Scale = config.Bind(Key, "Scale", 1f, "Extra size multiplier applied to the whole bike (1 = as modelled).");
            GripOffsetUp = config.Bind(Key, "GripOffsetUp", DefaultGripUp, "Move this bike's grips up (+) or down (-), metres. Hands follow.");
            GripOffsetSide = config.Bind(Key, "GripOffsetSide", DefaultGripSide, "Move this bike's grips outward (+) or inward (-), metres.");
            GripOffsetForward = config.Bind(Key, "GripOffsetForward", DefaultGripForward, "Move this bike's grips forward (+) or back (-), metres.");
            BagForward = config.Bind(Key, "SaddlebagForward", DefaultBagForward, "Move the saddlebags forward (+) or back (-) from the rear axle, metres. They sit just outside the bike at that spot.");
            BagUp = config.Bind(Key, "SaddlebagUp", DefaultBagUp, "Raise (+) or lower (-) the saddlebags, metres.");
            BagScale = config.Bind(Key, "SaddlebagScale", DefaultBagScale, "Size of the saddlebags (1 = 38 cm wide).");
        }
    }
}
