using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace MotorMod
{
    /// <summary>Entry point. Loads one asset bundle per bike from the plugin folder and registers each bike as a hammer piece.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class MotorModPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.shiva.valheim.motormod";
        public const string PluginName = "MotorMod";
        public const string PluginVersion = "0.8.0";

        internal static ManualLogSource Log;
        /// <summary>Detailed logging: the RideLog setting, and always in the test harness (which shares the settings file with normal play).</summary>
        internal static bool Verbose => (DebugLog != null && DebugLog.Value) || Harness.HarnessMode.Enabled;
        internal static MotorModPlugin Instance;

        // shared settings; per-bike ones live in each bike's config section
        internal static ConfigEntry<float> BarTurn, BarTurnFast;
        internal static ConfigEntry<float> SkullScale, SkullUp, SkullForward, SkullTilt, SkullTurn, SkullHeadShrink;
        internal static ConfigEntry<float> Coasting, ReverseSpeed, SteerResponse, EngineVolume, HeadlightIntensity, HeadlightRange, GripOffsetSide, GripOffsetUp, GripOffsetForward;
        internal static ConfigEntry<bool> DebugLog, UseFullStartup, HandsOnBars, Headlights;
        internal static ConfigEntry<float> GlowSize, GlowStrength, SitDepth, AnkleHeight;
        internal static ConfigEntry<string> LampMaterials;
        internal static ConfigEntry<float> MaxHealth, HitDamageScale, CrashSpeed, CrashDamage, FatalCrashSpeed, RamMinSpeed, RamDamage, ObjectMinSpeed, ObjectDamage;
        internal static ConfigEntry<bool> Glow, Sinking, Wake, DamageLook;
        internal static ConfigEntry<float> CruiseSinkRate, SinkSeconds, WakeSize;
        internal static ConfigEntry<bool> Trails, WheelieTimer;
        internal static ConfigEntry<float> TrailDensity, FireDamage;
        internal static ConfigEntry<KeyCode> WheelieKey;
        internal static ConfigEntry<bool> FuelEnabled;
        public static EffectList CrashBreakEffect;     // the fireball and bang when a crash breaks a bike (BikeHealth)
        internal static ConfigEntry<float> TankKm, BoostBurn;
        internal static GameObject SaddlebagPrefab;          // the leather saddlebag model (bundles/saddlebag_*)

        private Harmony _harmony;
        private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>();

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            if (System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-nomotormod"))
            {
                Log.LogInfo("-nomotormod given: MotorMod stays idle this session (no patches, no bikes)");
                return;
            }
            _harmony = new Harmony(PluginGuid);
            if (Harness.HarnessMode.Requested()) Harness.HarnessMode.Enable(this, _harmony);   // sandbox save folder must be set before the menu exists
            BindConfig();
            _harmony.PatchAll(typeof(MotorModPlugin).Assembly);
            var bagBundle = LoadBundle("saddlebag");
            SaddlebagPrefab = bagBundle != null ? bagBundle.LoadAsset<GameObject>("Saddlebag") : null;
            if (SaddlebagPrefab == null) Log.LogWarning("No saddlebag model: bikes have no saddlebags");
            // variants get their own copy of their base bike's model before the base is set up
            var models = new Dictionary<BikeDefinition, GameObject>();
            foreach (var bike in BikeDefinition.All)
            {
                var bundle = Bundle(bike.BundleKey);
                var asset = bundle != null ? bundle.LoadAsset<GameObject>(bike.BundlePrefab) : null;
                if (bundle != null && asset == null) Log.LogError($"Bundle {bike.BundleKey} has no prefab named {bike.BundlePrefab}");
                if (asset != null) models[bike] = bike.BaseKey == null ? asset : PrefabManager.Instance.CreateClonedPrefab(bike.PrefabName, asset);
            }
            foreach (var bike in BikeDefinition.All)
            {
                bike.BindConfig(Config);
                if (!models.TryGetValue(bike, out var prefab) || prefab == null) continue;
                var bundle = _bundles[bike.BundleKey];
                var icon = Tinted(bundle.LoadAsset<Sprite>(bike.IconAsset), bike.IconTint);
                BikePrefabSetup.Configure(prefab, bundle, bike, SaddlebagPrefab, icon);
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, new PieceConfig
                {
                    Name = bike.DisplayName, Description = bike.Description,
                    PieceTable = PieceTables.Hammer, Category = PieceCategories.Misc, CraftingStation = bike.CraftingStation,
                    Icon = icon, Requirements = bike.Requirements,
                }));
                bike.Prefab = prefab;
                Log.LogInfo($"Registered {bike.DisplayName} ({bike.PrefabName}) as a hammer piece");
            }
            BikeVanillaParts.Register();
            CommandManager.Instance.AddConsoleCommand(new MotorCommand());
            gameObject.AddComponent<Soundtrack.SoundtrackManager>();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded with {BikeDefinition.All.Count(b => b.Prefab != null)} bikes{(Harness.HarnessMode.Enabled ? " (harness mode)" : "")}");
        }

        private void BindConfig()
        {
            Coasting = Config.Bind("Ride", "Coasting", 1.2f, "Deceleration when the throttle is released, m/s^2.");
            ReverseSpeed = Config.Bind("Ride", "ReverseSpeed", 2.5f, "Max speed when pushing backwards, m/s.");
            SteerResponse = Config.Bind("Ride", "SteerResponse", 8f, "How fast steering input eases in and out (per second). Lower is smoother.");
            BarTurn = Config.Bind("Ride", "HandlebarTurn", 22f, "How far the handlebars, fork and front wheel turn at walking pace, degrees.");
            BarTurnFast = Config.Bind("Ride", "HandlebarTurnFast", 5f, "How far they turn at top speed, degrees (a fast bike turns by leaning).");
            DebugLog = Config.Bind("Debug", "RideLog", false, "Write ride details to the BepInEx log (throttle, steer and speed twice a second, hits, crashes): for bug reports.");
            EngineVolume = Config.Bind("Sound", "EngineVolume", 0.8f, "Engine loudness, 0 to 1.");
            UseFullStartup = Config.Bind("Sound", "UseFullStartup", false, "Play the long cold start instead of the short one.");
            Headlights = Config.Bind("Headlight", "Enabled", true, "Headlights shine while the engine runs.");
            HeadlightIntensity = Config.Bind("Headlight", "Intensity", 3f, "Headlight brightness.");
            HeadlightRange = Config.Bind("Headlight", "Range", 40f, "Headlight reach, metres.");
            Glow = Config.Bind("Headlight", "Glow", false, "The soft glowing disc at the lamp. Off: only the beam lights the road ahead.");
            GlowSize = Config.Bind("Headlight", "GlowSize", 0.2f, "Size of the soft glow at the lamp, metres.");
            GlowStrength = Config.Bind("Headlight", "GlowStrength", 0.55f, "Brightness of the glow, 0 to 1.");
            MaxHealth = Config.Bind("Durability", "MaxHealth", 3000f, "Bike health (the hammer shows it as the game's condition bar and repairs it near a forge). A troll club hit is about 70, a crash into a rock at 15 m/s about 300.");
            HitDamageScale = Config.Bind("Durability", "HitDamageScale", 1f, "Multiplier on damage from creatures and weapons.");
            CrashSpeed = Config.Bind("Durability", "CrashSpeed", 8f, "Impact speed into terrain or buildings (m/s, straight into the surface) below which crashes do no damage.");
            CrashDamage = Config.Bind("Durability", "CrashDamage", 100f, "Damage per m/s of impact speed above CrashSpeed (100: a head-on at plain top speed costs about a quarter of a 3000-health bike; see FatalCrashSpeed for the crash that breaks it outright).");
            FatalCrashSpeed = Config.Bind("Durability", "FatalCrashSpeed", 24f, "Impact speed (m/s, straight into the surface) from which a crash breaks the bike outright, whatever its condition, and it goes up in a fireball: a head-on at boost speed. A head-on at plain top speed is about 15.");
            DamageLook = Config.Bind("Durability", "DamageLook", true, "A damaged bike shows it: soot below 75 % health, heavy soot and engine smoke below 25 %, and a burst of smoke with a metal crunch at each step down.");
            RamMinSpeed = Config.Bind("Durability", "RamMinSpeed", 4f, "The bike's own speed into a creature (m/s) needed to hurt it. A creature walking into a parked or crawling bike takes nothing.");
            RamDamage = Config.Bind("Durability", "RamDamage", 4f, "Blunt damage to a rammed creature per m/s of the bike's speed into it (15 m/s = 60). Each creature is hurt at most once a second.");
            ObjectMinSpeed = Config.Bind("Durability", "ObjectMinSpeed", 6f, "Impact speed (m/s, straight into it) needed to damage a building or other breakable object.");
            ObjectDamage = Config.Bind("Durability", "ObjectDamage", 8f, "Blunt damage to a building or breakable object per m/s of impact speed (15 m/s = 120). Trees and rocks need an axe or pickaxe and shrug it off.");
            Sinking = Config.Bind("Water", "Sinking", true, "A bike on water stays up only at boost speed (Shift+W) or in a wheelie; otherwise it settles lower and lower, and swamped, the engine floods and the bike breaks apart.");
            SinkSeconds = Config.Bind("Water", "SinkSeconds", 1.5f, "Seconds from standing still on water to swamped (then the engine floods and 1.5 s later the bike breaks apart).");
            CruiseSinkRate = Config.Bind("Water", "CruiseSinkRate", 0.5f, "How fast a bike cruising without boost sinks, compared with standing still (0.5 = half as fast: about 3 s to swamped at full speed).");
            Wake = Config.Bind("Water", "Wake", true, "Foam wake, bow spray and a rooster tail while riding on water (looks only).");
            WakeSize = Config.Bind("Water", "WakeSize", 1f, "Size of the wake and spray (1 = normal).");
            Trails = Config.Bind("Trails", "Enabled", true, "The bikes leave a trail of fire while moving (orange, blue or green). It hurts enemies only, never players, tamed animals or buildings.");
            TrailDensity = Config.Bind("Trails", "Density", 1f, "How thick the trails are (1 = normal; they double at top speed).");
            FireDamage = Config.Bind("Trails", "FireDamage", 8f, "Fire damage to enemies in the flames (they also catch fire).");
            // fuel rules come from the server (or whoever hosts the world), so everyone rides by the same tank
            var serverRule = new ConfigurationManagerAttributes { IsAdminOnly = true };
            FuelEnabled = Config.Bind("Fuel", "Enabled", true, new ConfigDescription("Bikes need fuel: Surtling cores, 1.33 km each, by Shift+E or from the hotbar while the bike stands still. Off: they run forever.", null, serverRule));
            TankKm = Config.Bind("Fuel", "TankKm", 6.67f, new ConfigDescription("How far a full tank goes, kilometres (6.67: five Surtling cores).", new AcceptableValueRange<float>(1f, 200f), serverRule));
            BoostBurn = Config.Bind("Fuel", "BoostBurn", 2f, new ConfigDescription("Fuel use while boosting (Shift), against riding without it.", new AcceptableValueRange<float>(1f, 5f), serverRule));
            SkullScale = Config.Bind("FlamingSkull", "Scale", 1.0f, "Size of a worn flaming skull (1 = the skeleton trophy's skull).");
            SkullUp = Config.Bind("FlamingSkull", "Up", 0f, "Move a worn skull up (+) or down (-), metres.");
            SkullForward = Config.Bind("FlamingSkull", "Forward", 0f, "Move a worn skull forward (+) or back (-), metres.");
            SkullTilt = Config.Bind("FlamingSkull", "Tilt", 0f, "Tilt a worn skull nose-down (+) or nose-up (-), degrees.");
            SkullTurn = Config.Bind("FlamingSkull", "Turn", 0f, "Turn a worn skull, degrees.");
            SkullHeadShrink = Config.Bind("FlamingSkull", "HeadShrink", 0.6f, "How small the wearer's own head becomes inside the skull, so the face does not poke through (1 = as it is).");
            WheelieKey = Config.Bind("Wheelie", "Key", KeyCode.LeftControl, "Hold with throttle to lift the front; feather it to keep the wheelie up. Overshooting just sets the front down.");
            WheelieTimer = Config.Bind("Wheelie", "ShowTime", true, "After a wheelie of 2 s or more, show how long it lasted.");
            LampMaterials = Config.Bind("Headlight", "LampMaterials", "glass|lens|lamp|light|reflektor|headl|przod", "Materials matching this pattern light up (emission) while the engine runs.");
            HandsOnBars = Config.Bind("Rider", "HandsOnBars", true, "Pull the rider's hands onto the handlebar grips.");
            Soundtrack.SoundtrackManager.BindConfig(Config);
            SitDepth = Config.Bind("Rider", "SitDepth", 0.08f, "Height of the rider's hip joint above the seat surface when seated, metres (lower = sinks into the saddle).");
            AnkleHeight = Config.Bind("Rider", "AnkleHeight", 0.07f, "Height of the ankle joint above the footpeg when the sole rests on it, metres.");
            GripOffsetSide = Config.Bind("Rider", "GripOffsetSide", 0f, "Move every bike's grips outward (+) or inward (-), metres.");
            GripOffsetUp = Config.Bind("Rider", "GripOffsetUp", 0f, "Move every bike's grips up (+) or down (-), metres.");
            GripOffsetForward = Config.Bind("Rider", "GripOffsetForward", 0f, "Move every bike's grips forward (+) or back (-), metres.");
        }

        /// <summary>Bundles carry compiled shaders, so macOS needs its own (Metal). Windows and Linux use the Windows ones: a Linux
        /// dedicated server never draws anything, it only needs the bikes' shapes.</summary>
        private static string Platform() => Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor ? "osx" : "win";

        /// <summary>Each bundle is loaded once (a variant shares its base bike's).</summary>
        private AssetBundle Bundle(string key)
        {
            if (_bundles.TryGetValue(key, out var b)) return b;
            return _bundles[key] = LoadBundle(key);
        }

        /// <summary>A variant's icon: its base bike's, washed in the colour of its flames (read back from the GPU, so the bundle's
        /// texture need not be readable). As it is on a server without graphics.</summary>
        private static Sprite Tinted(Sprite icon, Color tint)
        {
            if (icon == null || tint.a <= 0f || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return icon;
            var src = icon.texture; var rect = icon.textureRect;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var was = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
            RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                px[i] = new Color(Mathf.Lerp(c.r, lum * tint.r * 1.25f, 0.6f), Mathf.Lerp(c.g, lum * tint.g * 1.25f, 0.6f), Mathf.Lerp(c.b, lum * tint.b * 1.25f, 0.6f), c.a);
            }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), icon.pixelsPerUnit);
        }

        /// <summary>Bundles are loose files next to the DLL: bundles/&lt;key&gt;_osx and bundles/&lt;key&gt;_win.</summary>
        private AssetBundle LoadBundle(string key)
        {
            var dir = Path.GetDirectoryName(Info.Location);
            var candidates = new[] { Path.Combine(dir, "bundles", key + "_" + Platform()), Path.Combine(dir, key + "_" + Platform()) };
            foreach (var path in candidates)
            {
                if (!File.Exists(path)) continue;
                var b = AssetBundle.LoadFromFile(path);
                Log.LogInfo($"{key}: {Path.GetFileName(path)} -> {(b ? "loaded" : "failed")}");
                if (b) return b;
            }
            Log.LogWarning($"{key}: no bundle file found (looked for bundles/{key}_{Platform()}); this bike is unavailable");
            return null;
        }
    }
}
