using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MotorMod.Harness
{
    /// <summary>
    /// Harness mode (-motorharness): the game starts in its own sandbox so Claude can photograph riders without a human.
    /// Safety: the save folder is moved to BepInEx/plugins/MotorMod/harness/save before the menu exists, no character
    /// is ever picked (the game then makes its own local throwaway "Odev"), the world is a local single-player world named
    /// Harness, and every harness action checks the sandbox is in place first. Ordinary launches never run any of this.
    /// </summary>
    internal static class HarnessMode
    {
        public const string Flag = "-motorharness";
        public const string WorldName = "Harness";
        public const string WorldSeed = "motorharness";

        public static bool Enabled { get; private set; }
        public static string Dir { get; private set; }        // .../plugins/MotorMod/harness
        public static string SaveDir { get; private set; }    // .../harness/save
        private static bool s_started;

        public static bool Requested() => Array.Exists(Environment.GetCommandLineArgs(), a => a == Flag);

        /// <summary>Called from the plugin's Awake, before the start menu exists.</summary>
        public static void Enable(MotorModPlugin plugin, HarmonyLib.Harmony harmony)
        {
            Dir = Path.Combine(Path.GetDirectoryName(plugin.Info.Location), "harness");
            SaveDir = Path.Combine(Dir, "save");
            Directory.CreateDirectory(SaveDir);
            Utils.SetSaveDataPath(SaveDir);
            Enabled = true;
            Application.runInBackground = true;
            harmony.Patch(AccessTools.Method(typeof(FejdStartup), "Awake"), prefix: new HarmonyMethod(typeof(HarnessMode), nameof(BeforeMenuAwake)));
            harmony.Patch(AccessTools.Method(typeof(FejdStartup), "Start"), postfix: new HarmonyMethod(typeof(HarnessMode), nameof(AfterMenuStart)));
            harmony.Patch(AccessTools.Method(typeof(Game), "Awake"), prefix: new HarmonyMethod(typeof(HarnessMode), nameof(BeforeGameAwake)));
            plugin.gameObject.AddComponent<HarnessRunner>();
            File.Delete(Path.Combine(Dir, "ready.json"));
            MotorModPlugin.Log.LogWarning($"HARNESS MODE: save folder is {SaveDir}; no character is picked; world '{WorldName}' is local only");
        }

        /// <summary>True when the game's local save path is our sandbox. Every harness action checks this first.</summary>
        public static bool SandboxActive()
        {
            if (!Enabled || string.IsNullOrEmpty(SaveDir)) return false;
            string path = Utils.GetSaveDataPath(FileHelpers.FileSource.Local);
            return !string.IsNullOrEmpty(path) && Path.GetFullPath(path).TrimEnd('/') == Path.GetFullPath(SaveDir).TrimEnd('/');
        }

        private static void ApplyWindow()
        {
            Application.runInBackground = true;
            if (Screen.fullScreen || Screen.width > 1400) Screen.SetResolution(1280, 800, FullScreenMode.Windowed);
        }

        // --- Harmony hooks (only patched in harness mode) ---
        private static void BeforeMenuAwake()
        {
            Utils.SetSaveDataPath(SaveDir);        // belt and braces: in place before the menu reads any save list
            ApplyWindow();
        }

        private static void BeforeGameAwake()
        {
            Utils.SetSaveDataPath(SaveDir);
            ApplyWindow();
        }

        private static void AfterMenuStart(FejdStartup __instance)
        {
            if (s_started) return;                 // the menu scene comes back after a quit-to-menu; only auto-start once
            s_started = true;
            __instance.StartCoroutine(AutoStart(__instance));
        }

        /// <summary>The same calls the menu's Start button makes, minus the menus: empty profile (= the game's own Odev), local world, load the main scene.</summary>
        private static IEnumerator AutoStart(FejdStartup menu)
        {
            yield return new WaitForSeconds(1.5f);
            if (!SandboxActive())
            {
                MotorModPlugin.Log.LogError($"Harness refused to start: save path is '{Utils.GetSaveDataPath(FileHelpers.FileSource.Local)}', expected the sandbox '{SaveDir}'");
                yield break;
            }
            try
            {
                Game.SetProfile("", FileHelpers.FileSource.Local);   // empty name: Game.Awake builds the local "Developer" profile (player Odev)
                World world = LoadOrCreateWorld();
                ZNet.m_onlineBackend = OnlineBackendType.Steamworks;
                ZSteamMatchmaking.instance?.StopServerListing();
                ZNet.SetServer(server: true, openServer: false, publicServer: false, serverName: WorldName, password: "", world: world);
                ZNet.ResetServerHost();
                MotorModPlugin.Log.LogInfo($"Harness: entering world '{world.m_name}' (seed {world.m_seedName}, {world.m_fileSource}) with the throwaway character");
                AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(menu, null);
            }
            catch (Exception e)
            {
                MotorModPlugin.Log.LogError("Harness auto-start failed: " + e);
            }
        }

        private static World LoadOrCreateWorld()
        {
            if (SaveSystem.TryGetSaveByName(WorldName, SaveDataType.World, out var save) && !save.IsDeleted)
            {
                var existing = World.LoadWorld(save);
                if (existing.m_dataError == World.SaveDataError.None && existing.m_fileSource == FileHelpers.FileSource.Local) return existing;
                MotorModPlugin.Log.LogWarning($"Harness world unusable ({existing.m_dataError}, {existing.m_fileSource}); creating a fresh one");
            }
            var world = new World(WorldName, WorldSeed) { m_fileSource = FileHelpers.FileSource.Local };
            SaveSystem.SetSaveNumber(0u);
            world.SaveWorldFWLData(DateTime.Now);
            return world;
        }
    }
}
