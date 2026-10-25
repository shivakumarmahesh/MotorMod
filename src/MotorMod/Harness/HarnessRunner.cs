using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MotorMod.Harness
{
    [Serializable]
    public class HarnessRequest
    {
        public string[] bikes;          // empty or ["all"] = every bike
        public string[] views;          // empty = right, left, front34, rear34
        public bool markers;            // red seat, green grips, blue feet, yellow headlight
        public float time = -1f;        // 0..1 time of day (0.5 = noon), -1 = leave the clock alone
        public string env = "";         // environment name to force, e.g. Clear; "" = leave it
        public int width = 1280, height = 800;
        public float fov = 40f;
        public float settle = 2f;       // seconds to wait after mounting
        public bool grass;              // keep the grass (default off so feet are visible)
        public bool probe;              // write <bike>_profile.txt: the bike mesh's top surface on a 5 cm grid (bike space), for placing seat, grips and pegs from data
        public float ride;              // seconds of full throttle before the pictures (0 = standing); the hand gap is then measured on two consecutive frames
        public int fps;                 // frame-rate cap during the run (0 = 60)
        public bool water;              // spawn the bike on open water instead of the land stage
        public bool music;              // run the soundtrack test sequence after mounting (needs water)
        public string[] fx;             // game effect prefabs to photograph one by one at the stage (bikes: ["none"] skips the bikes)
        public string prefabs;          // regex: write harness/prefabs.txt listing every spawnable prefab whose name matches, with its particle systems and lights
        public string icons;            // "yaw,pitch,fill,flames": re-render the flaming skull icons with these settings into harness/icon_<prefab>.png (and onto the items)
        public bool wheelie;            // after the ride: hold the wheelie key past the top, then feather it for 6 s; pictures at the high point, pitch trace in the log
        public string[] targets;        // creature prefabs placed beside the ride path with their AI off (land only); health and status effects are logged
        public bool census;             // at the first moving picture: log every particle system with live particles within 35 m (what is making that?)
        public string ram;              // creature prefab: (0) a live one walks up to the standing bike, (1) one standing right in front while the throttle is held 3 s, (2) one rammed at full throttle 16 m on
        public string wall;             // building piece prefab put across the lane 22 m ahead and hit at full speed (land only)
        public bool sink;               // on water: planes under boost, settles without it, lifts back, holds up in a wheelie; braking to a stop it swamps and breaks apart, the materials go down
        public string[] inspect;        // prefab names: log their WearNTear setup (new/worn/broken objects, effect lists) and material shaders
        public bool songs;              // decode every song in the music folder's score.tsv and compare lengths with the index (no bikes needed)
        public bool boost;              // hold Shift during the ride (on water: the bike planes instead of settling)
        public string refund;           // bike key: smashed and hammered bikes drop their recipe loose (a third from the console), no bag; never hammered while ridden
        public string fuel;             // bike key: built empty, Shift+E from bag and saddlebags, own fuel from the hotbar, runs dry, boost burns more (land)
        public string storage;          // bike key: two slots, found by the player's look, open with E, shut while ridden, spilled into the wreck bag
        public string[] gear;           // item prefabs the rider wears for the pictures (armour, cape, a weapon: sheathed on the back or hip)
        public string biome;            // stage in this biome instead of the usual one (Plains, AshLands, Mistlands, Mountain, BlackForest, Swamp, DeepNorth)
        public string condition;        // bike key: the damage look: clean when fresh, sooty and a smoke burst below 75 %, sootier and smoking below 25 %, clean again when repaired; pictures of each
        public string ui;               // bike key: screenshots of the game window: hover text, the hammer's condition bar (and its repair), the fuel gauge while riding, the materials on the ground once hammered
        public bool seadry;             // on water: run dry under way: the bike slows, swamps and breaks apart before anyone could fill it
        public bool steerOn;            // hold the steering at 'steer' (-1 full left .. 1 full right) before the pictures
        public float steer;
        public string skull = "";       // helmet prefab the rider wears for the pictures (e.g. HelmetHelSkull); "" = none
        public float fxEarly = 0.35f;   // first picture this soon after spawning (catches one-shot bursts); a second one at 1.6 s
        public string phase = "";       // unused (kept so older request files still parse)
        public bool pause;              // unused: pausing makes the rider pose run away (the pose is relative and the animator stops resetting bones)
        public bool quit;
    }

    [Serializable]
    public class HarnessDone
    {
        public bool ok;
        public float seconds;
        public string stage;
        public string[] pictures;
        public string[] errors;
        public string[] log;
    }

    /// <summary>
    /// Lives on the plugin object in harness mode. Once in the world it writes harness/ready.json, then watches
    /// harness/request.json once a second: for each requested bike it spawns it on a flat stage, mounts it the same
    /// way pressing E does, waits for the pose to settle, photographs it from the requested views with the mod's own
    /// camera and writes harness/done.json. Every run re-checks that the sandbox save folder is active.
    /// </summary>
    public partial class HarnessRunner : MonoBehaviour
    {
        public static HarnessRunner Instance;

        private static readonly string[] DefaultViews = { "right", "left", "front34", "rear34", "top", "front" };
        private static readonly FieldInfoCache s_cache = new FieldInfoCache();

        private float m_nextPoll, m_nextStatus;
        private int m_fpsOverride;
        private bool m_laneCleared;
        private bool m_running, m_readyWritten, m_refused, m_haveStage, m_levelled;
        private Vector3 m_stage, m_spawnAt, m_water;
        private bool m_haveWater;
        private readonly List<GameObject> m_markers = new List<GameObject>();

        private string RequestPath => Path.Combine(HarnessMode.Dir, "request.json");
        private string DonePath => Path.Combine(HarnessMode.Dir, "done.json");
        private string ReadyPath => Path.Combine(HarnessMode.Dir, "ready.json");
        private string StagePath => Path.Combine(HarnessMode.Dir, "stage.txt");

        private void Awake() { Instance = this; }

        private static RiderPose CurrentPose()
        {
            var p = Player.m_localPlayer; if (!p) return null;
            var anim = p.GetComponentInChildren<Animator>();
            return anim ? anim.GetComponent<RiderPose>() : null;
        }

        private void Update()
        {
            if (!HarnessMode.Enabled || m_refused) return;
            var cam = GameCamera.instance;
            if (cam) s_cache.MouseCapture.SetValue(cam, false);          // never grab the mouse
            if (Time.unscaledTime < m_nextPoll) return;
            m_nextPoll = Time.unscaledTime + 1f;
            Application.runInBackground = true;
            AudioListener.volume = 0f;
            Application.targetFrameRate = m_fpsOverride > 0 ? m_fpsOverride : (m_running ? 60 : 20);
            if (Time.unscaledTime >= m_nextStatus) { m_nextStatus = Time.unscaledTime + 5f; WriteStatus(); }
            if (!InWorld()) return;
            if (!Guard()) return;
            if (!m_readyWritten)
            {
                m_readyWritten = true;
                var p = Player.m_localPlayer;
                File.WriteAllText(ReadyPath, $"{{\"ok\":true,\"player\":\"{p.GetPlayerName()}\",\"world\":\"{ZNet.instance.GetWorldName()}\",\"save\":\"{HarnessMode.SaveDir}\"}}\n");
                MotorModPlugin.Log.LogInfo($"Harness ready: player {p.GetPlayerName()} in world {ZNet.instance.GetWorldName()}; watching {RequestPath}");
            }
            if (m_running || !File.Exists(RequestPath)) return;
            string json;
            try { json = File.ReadAllText(RequestPath); File.Delete(RequestPath); }
            catch (Exception e) { MotorModPlugin.Log.LogWarning("Harness: could not read request: " + e.Message); return; }
            HarnessRequest req;
            try { req = JsonUtility.FromJson<HarnessRequest>(json) ?? new HarnessRequest(); }
            catch (Exception e) { WriteDone(false, 0f, new List<string>(), new List<string> { "bad request.json: " + e.Message }, new List<string>()); return; }
            Trigger(req);
        }

        public void Trigger(HarnessRequest req)
        {
            if (req.quit) { MotorModPlugin.Log.LogInfo("Harness: quit requested"); Application.Quit(); return; }
            if (m_running) { MotorModPlugin.Log.LogWarning("Harness: a run is already in progress"); return; }
            StartCoroutine(Run(req));
        }

        /// <summary>harness/status.json every 5 s: why requests would be waiting (no player, dead, teleporting, area not loaded, busy).</summary>
        private void WriteStatus()
        {
            try
            {
                var p = Player.m_localPlayer;
                string s = p == null ? "{\"player\":false}" :
                    $"{{\"player\":true,\"dead\":{p.IsDead().ToString().ToLower()},\"teleporting\":{p.IsTeleporting().ToString().ToLower()},\"intro\":{p.InIntro().ToString().ToLower()}," +
                    $"\"areaReady\":{(ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(p.transform.position)).ToString().ToLower()},\"running\":{m_running.ToString().ToLower()},\"refused\":{m_refused.ToString().ToLower()}," +
                    $"\"pos\":\"{Fmt(p.transform.position)}\",\"health\":{p.GetHealth():0},\"time\":{Time.unscaledTime:0}}}";
                File.WriteAllText(Path.Combine(HarnessMode.Dir, "status.json"), s + "\n");
            }
            catch (Exception e) { MotorModPlugin.Log.LogWarning("Harness status: " + e.Message); }
        }

        private static bool InWorld()
        {
            var p = Player.m_localPlayer;
            // no IsAreaReady here: after some effect runs the game kept reporting the player's area as not loaded and requests waited
            // forever; every stage waits for its own area anyway
            return p && ZNetScene.instance && ZNet.instance && !p.IsTeleporting() && !p.InIntro() && !p.IsDead();
        }

        /// <summary>The sandbox must be the active save folder and this must be our own local Harness world, or nothing runs.</summary>
        private bool Guard()
        {
            string why = null;
            if (!HarnessMode.SandboxActive()) why = $"save path is '{Utils.GetSaveDataPath(FileHelpers.FileSource.Local)}', not the sandbox";
            else if (!ZNet.instance.IsServer()) why = "not hosting the world locally";
            else if (ZNet.IsOpenServer()) why = "the world is open to others";
            else if (ZNet.instance.GetWorldName() != HarnessMode.WorldName) why = $"world is '{ZNet.instance.GetWorldName()}', not '{HarnessMode.WorldName}'";
            if (why == null) return true;
            m_refused = true;
            MotorModPlugin.Log.LogError("HARNESS REFUSED TO RUN: " + why);
            File.WriteAllText(ReadyPath, "{\"ok\":false,\"error\":\"" + why.Replace("\"", "'") + "\"}\n");
            return false;
        }

        private IEnumerator Run(HarnessRequest req)
        {
            m_running = true;
            float t0 = Time.realtimeSinceStartup;
            var me = Player.m_localPlayer;
            if (me != null) { me.SetGodMode(true); me.SetGhostMode(true); me.SetHealth(me.GetMaxHealth()); }   // creatures ignore a ghost: photo sessions in hostile biomes   // attack effects hurt the throwaway character
            var pics = new List<string>(); var errors = new List<string>(); var log = new List<string>();
            MotorModPlugin.Instance.Config.Reload();
            log.Add("config reloaded");
            var clutter = ClutterSystem.instance;
            if (clutter) { clutter.m_amountScale = req.grass ? 1f : 0f; clutter.ClearAll(); }   // grass hides the feet; rebuilt empty
            // dressed on dry land first: the game refuses weapons and helmets to a swimmer, so a character left in the water by the
            // last run goes to the land stage, dresses, and then comes back to the water
            if (me != null && (req.gear != null && req.gear.Length > 0 || !string.IsNullOrEmpty(req.skull)))
            {
                if (me.IsSwimming()) yield return EnsureStage(req.biome, errors, log);
                WearGear(me, req.gear, log, errors); Wear(me, req.skull, log, errors);
            }
            if (req.water) yield return EnsureWaterStage(errors, log); else yield return EnsureStage(req.biome, errors, log);
            int strays = ClearEffects(req.water ? m_water : m_stage, 120f);
            if (strays > 0) log.Add($"removed {strays} stray effects and fires near the stage");
            var env = EnvMan.instance;
            if (env)
            {
                env.m_debugTimeOfDay = req.time >= 0f;
                if (req.time >= 0f) env.m_debugTime = Mathf.Clamp01(req.time);
                env.SetForceEnvironment(req.env ?? "");
            }
            if (!string.IsNullOrEmpty(req.prefabs)) ListPrefabs(req.prefabs, log, errors);
            if (req.inspect != null && req.inspect.Length > 0) Inspect(req.inspect, log, errors);
            if (!string.IsNullOrEmpty(req.icons)) RenderIcons(req.icons, pics, log, errors);
            if (req.songs && Soundtrack.SoundtrackManager.Instance != null) yield return Soundtrack.SoundtrackManager.Instance.CheckAllSongs(log, errors);
            if (!string.IsNullOrEmpty(req.refund)) yield return CheckRefund(req.refund, log, errors);
            if (!string.IsNullOrEmpty(req.fuel)) yield return CheckFuel(req.fuel, log, errors);
            if (!string.IsNullOrEmpty(req.storage)) yield return CheckStorage(req.storage, log, errors);
            if (!string.IsNullOrEmpty(req.ui)) yield return CheckUi(req.ui, pics, log, errors);
            if (!string.IsNullOrEmpty(req.condition)) yield return CheckCondition(req.condition, pics, log, errors);
            if (req.fx != null && req.fx.Length > 0) yield return ShootEffects(req, pics, errors, log);
            var keys = req.bikes == null || req.bikes.Length == 0 || req.bikes[0] == "all" ? BikeDefinition.All.Select(b => b.Key).ToArray()
                     : req.bikes[0] == "none" ? new string[0] : req.bikes;
            var views = req.views == null || req.views.Length == 0 ? DefaultViews : req.views;
            foreach (var key in keys)
            {
                var def = BikeDefinition.Find(key);
                if (def == null) { errors.Add($"unknown bike '{key}'"); continue; }
                yield return ShootBike(def, views, req, pics, errors, log);
            }
            WriteDone(errors.Count == 0, Time.realtimeSinceStartup - t0, pics, errors, log);
            m_running = false;
        }


        // ---------- stage ----------
        private string m_stageBiome = "";       // "" = the usual stage; a biome name = a flat spot found in that biome for this session

        private IEnumerator EnsureStage(string biome, List<string> errors, List<string> log)
        {
            var player = Player.m_localPlayer;
            biome = biome ?? "";
            if (!m_haveStage || biome != m_stageBiome)
            {
                if (biome.Length == 0)
                {
                    if (File.Exists(StagePath) && TryParseStage(File.ReadAllText(StagePath), out m_stage)) log.Add($"stage from file {Fmt(m_stage)}");
                    else { m_stage = FindFlatSpot(player.transform.position); File.WriteAllText(StagePath, Fmt(m_stage) + "\n"); log.Add($"stage found {Fmt(m_stage)}"); }
                }
                else
                {
                    if (!Enum.TryParse(biome, true, out Heightmap.Biome b)) { errors.Add($"unknown biome '{biome}'"); yield break; }
                    if (!FindBiomeSpot(b, out m_stage)) { errors.Add($"no flat {biome} spot with a clear lane found"); yield break; }
                    log.Add($"{b} stage found {Fmt(m_stage)}, {Vector3.Distance(player.transform.position, m_stage):0} m away");
                }
                m_stageBiome = biome; m_haveStage = true; m_levelled = false; m_laneCleared = false;
            }
            if (Vector3.Distance(player.transform.position, m_stage) > 25f)
            {
                float t = Time.time;
                while (!player.TeleportTo(m_stage + new Vector3(0f, 1f, 4f), Quaternion.identity, true) && Time.time - t < 10f) yield return new WaitForSeconds(0.5f);
                t = Time.time;
                while (player.IsTeleporting() && Time.time - t < 40f) yield return null;
                log.Add("teleported to the stage");
            }
            float t2 = Time.time;
            while (!ZNetScene.instance.IsAreaReady(m_stage) && Time.time - t2 < 90f) yield return null;
            if (!ZNetScene.instance.IsAreaReady(m_stage)) errors.Add("stage area never became ready");
            yield return new WaitForSeconds(0.5f);
            if (ZoneSystem.instance.GetGroundHeight(m_stage, out float h)) m_stage.y = h;
            if (!m_levelled)
            {
                int cleared = ClearArea(m_stage, 14f);
                if (m_stageBiome.Length > 0) { for (float x = 8f; x <= 50f; x += 8f) cleared += ClearArea(m_stage + Vector3.right * x, 8f); m_laneCleared = true; }   // a biome stage: the ride lane too (the usual stage keeps its boss stone)
                Level(m_stage);
                m_levelled = true;
                yield return new WaitForSeconds(2f);                     // the terrain collider rebuilds; spawning earlier tips the bike
                if (ZoneSystem.instance.GetGroundHeight(m_stage, out h)) m_stage.y = h;
                log.Add($"stage cleared ({cleared} objects) and levelled at {Fmt(m_stage)}");
            }
            m_spawnAt = m_stage + Vector3.up * 0.6f;
        }

        /// <summary>Open water at least 4 m deep, the nearest to the player; the bike spawns on its surface.</summary>
        private IEnumerator EnsureWaterStage(List<string> errors, List<string> log)
        {
            var player = Player.m_localPlayer;
            float level = ZoneSystem.instance.m_waterLevel;
            if (!m_haveWater)
            {
                var wg = WorldGenerator.instance; var from = player.transform.position;
                bool Deep(float x, float z, float depth) => wg.GetHeight(x, z) < level - depth;
                for (float r = 40f; r <= 3000f && !m_haveWater; r += 40f)
                    for (int i = 0; i < 32 && !m_haveWater; i++)
                    {
                        float a = i * Mathf.PI * 2f / 32f, x = from.x + Mathf.Cos(a) * r, z = from.z + Mathf.Sin(a) * r;
                        if (!Deep(x, z, 4f)) continue;
                        bool open = true;
                        for (int k = 0; k < 8 && open; k++) { float b = k * Mathf.PI / 4f; open = Deep(x + Mathf.Cos(b) * 12f, z + Mathf.Sin(b) * 12f, 3f); }
                        for (float d = 20f; d <= 500f && open; d += 20f) open = Deep(x + d, z, 3f) && Deep(x + d, z + 15f, 2f) && Deep(x + d, z - 15f, 2f);   // rides run along +x: 500 m of open water ahead
                        if (open) { m_water = new Vector3(x, level, z); m_haveWater = true; }
                    }
                if (!m_haveWater) { errors.Add("no open water within 3 km"); yield break; }
                log.Add($"water found {Fmt(m_water)}, {Vector3.Distance(from, m_water):0} m away");
            }
            if (Vector3.Distance(player.transform.position, m_water) > 25f)
            {
                float t = Time.time;
                while (!player.TeleportTo(m_water + new Vector3(0f, 1f, 4f), Quaternion.identity, true) && Time.time - t < 10f) yield return new WaitForSeconds(0.5f);
                t = Time.time;
                while (player.IsTeleporting() && Time.time - t < 40f) yield return null;
                log.Add("teleported to the water");
            }
            float t2 = Time.time;
            while (!ZNetScene.instance.IsAreaReady(m_water) && Time.time - t2 < 40f) yield return null;
            yield return new WaitForSeconds(1f);
            m_spawnAt = m_water + Vector3.up * 0.6f;
        }

        /// <summary>A flat, dry spot inside a biome with a flat 50 m lane along +x, the nearest to the world's centre (rings from 300 m out).</summary>
        private static bool FindBiomeSpot(Heightmap.Biome biome, out Vector3 spot)
        {
            var wg = WorldGenerator.instance; float water = ZoneSystem.instance.m_waterLevel; spot = Vector3.zero;
            for (float r = 300f; r <= 9800f; r += 150f)
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI * 2f / 48f, x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                    if (wg.GetBiome(x, z) != biome) continue;
                    float h = wg.GetHeight(x, z); if (h < water + 3f) continue;
                    bool ok = true;
                    for (int k = 0; k < 8 && ok; k++) { float b = k * Mathf.PI / 4f, hh = wg.GetHeight(x + Mathf.Cos(b) * 14f, z + Mathf.Sin(b) * 14f); ok = Mathf.Abs(hh - h) < 1.5f && hh > water + 2f; }
                    for (float d = 8f; d <= 50f && ok; d += 6f) { float hh = wg.GetHeight(x + d, z); ok = Mathf.Abs(hh - h) < 3f && hh > water + 2f; }   // the ride lane
                    for (int k = 0; k < 4 && ok; k++) { float b = k * Mathf.PI / 2f; ok = wg.GetBiome(x + Mathf.Cos(b) * 60f, z + Mathf.Sin(b) * 60f) == biome; }   // not a sliver at a border
                    if (!ok) continue;
                    spot = new Vector3(x, h, z); return true;
                }
            return false;
        }

        private static Vector3 FindFlatSpot(Vector3 from)
        {
            var wg = WorldGenerator.instance; float water = ZoneSystem.instance.m_waterLevel;
            Vector3 best = from; float bestScore = float.MaxValue;
            for (float r = 30f; r <= 500f; r += 10f)
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI * 2f / 24f, x = from.x + Mathf.Cos(a) * r, z = from.z + Mathf.Sin(a) * r;
                    if (wg.GetBiome(x, z) != Heightmap.Biome.Meadows) continue;
                    float h = wg.GetHeight(x, z);
                    if (h < water + 2f) continue;
                    float dev = 0f;
                    for (int k = 0; k < 8; k++) { float b = k * Mathf.PI / 4f; for (float d = 3f; d <= 9f; d += 3f) dev = Mathf.Max(dev, Mathf.Abs(wg.GetHeight(x + Mathf.Cos(b) * d, z + Mathf.Sin(b) * d) - h)); }
                    if (dev < 0.3f) return new Vector3(x, h, z);
                    float score = dev + r * 0.002f;
                    if (score < bestScore) { bestScore = score; best = new Vector3(x, h, z); }
                }
            return best;
        }

        /// <summary>Trees, rocks, bushes, pickables, creatures, dropped items and old bikes within the radius go; the player, terrain and zone objects stay.</summary>
        private static int ClearArea(Vector3 c, float r)
        {
            var doomed = new HashSet<GameObject>();
            foreach (var col in Physics.OverlapSphere(c, r))
            {
                GameObject root = null;
                Component comp = (Component)col.GetComponentInParent<TreeBase>() ?? (Component)col.GetComponentInParent<TreeLog>() ?? (Component)col.GetComponentInParent<Destructible>()
                    ?? (Component)col.GetComponentInParent<Pickable>() ?? (Component)col.GetComponentInParent<ItemDrop>() ?? (Component)col.GetComponentInParent<MotorcycleController>() ?? (Component)col.GetComponentInParent<MineRock5>();
                if (comp == null) { var ch = col.GetComponentInParent<Character>(); if (ch && !(ch is Player)) comp = ch; }
                if (comp == null) continue;
                root = comp.gameObject;
                var nv = root.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                doomed.Add(root);
            }
            foreach (var go in doomed) { var nv = go.GetComponent<ZNetView>(); nv.ClaimOwnership(); ZNetScene.instance.Destroy(go); }
            return doomed.Count;
        }

        private static void ClearDrops(Vector3 c, float r)
        {
            foreach (var d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (d == null || Vector3.Distance(d.transform.position, c) > r) continue;
                var nv = d.GetComponent<ZNetView>(); if (nv && nv.IsValid()) { nv.ClaimOwnership(); ZNetScene.instance.Destroy(d.gameObject); }
            }
        }

        /// <summary>Bikes left near the spawn point (a run that died half way, or one unloaded far out and back again).</summary>
        private static int ClearBikes(Vector3 c, float r)
        {
            int n = 0;
            foreach (var b in UnityEngine.Object.FindObjectsByType<MotorcycleController>(FindObjectsSortMode.None))
                if (b != null && (b.transform.position - c).sqrMagnitude < r * r && !b.HasRider) { b.Remove(); n++; }
            return n;
        }

        /// <summary>Effect leftovers near the stage (sandbox world only): saved copies of game effects from older test builds, and fires.</summary>
        private static int ClearEffects(Vector3 c, float r)
        {
            int n = 0;
            foreach (var nv in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
            {
                if (nv == null || !nv.IsValid() || nv.GetComponentInParent<Player>() != null) continue;
                string name = nv.gameObject.name;
                bool effect = name.StartsWith("fx_") || name.StartsWith("vfx_") || name.StartsWith("sfx_") || name.StartsWith("lightningAOE")
                              || nv.GetComponent<Fireplace>() != null || nv.GetComponentInChildren<SmokeSpawner>() != null;
                if (!effect || (nv.transform.position - c).sqrMagnitude > r * r) continue;
                nv.ClaimOwnership(); ZNetScene.instance.Destroy(nv.gameObject); n++;
            }
            return n;
        }

        /// <summary>The hoe's own terrain operation, nine times in a grid: level, smooth, paint dirt. Applies at once and the game saves it in the sandbox world.</summary>
        private static void Level(Vector3 c)
        {
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    var go = new GameObject("harness_level");
                    go.SetActive(false);
                    go.transform.position = new Vector3(c.x + ix * 3f, c.y, c.z + iz * 3f);
                    var op = go.AddComponent<TerrainOp>();
                    op.m_settings.m_level = true; op.m_settings.m_levelRadius = 3.5f; op.m_settings.m_levelOffset = 0f; op.m_settings.m_square = false;
                    op.m_settings.m_smooth = true; op.m_settings.m_smoothRadius = 5f; op.m_settings.m_smoothPower = 3f;
                    op.m_settings.m_paintCleared = true; op.m_settings.m_paintType = TerrainModifier.PaintType.Dirt; op.m_settings.m_paintRadius = 3.5f;
                    go.SetActive(true);   // Awake applies the operation and destroys the object
                }
        }

        // ---------- one bike ----------
        private IEnumerator ShootBike(BikeDefinition def, string[] views, HarnessRequest req, List<string> pics, List<string> errors, List<string> log)
        {
            var player = Player.m_localPlayer;
            var prefab = ZNetScene.instance.GetPrefab(def.PrefabName);
            if (prefab == null) { errors.Add($"{def.Key}: prefab {def.PrefabName} not registered"); yield break; }
            float t0 = Time.realtimeSinceStartup;
            // a long ride (the wheelie test) ends hundreds of metres out: come back first, or the next bike spawns outside the
            // loaded area and the game unloads it at once
            yield return req.water ? EnsureWaterStage(errors, log) : EnsureStage(req.biome, errors, log);
            int strays = ClearBikes(m_spawnAt, 25f);
            if (strays > 0) log.Add($"{def.Key}: removed {strays} stray bikes at the spawn point");
            if (req.ride > 0f && !req.water && !m_laneCleared)
            {   // the stage is cleared 14 m around; a ride runs on along +x, so clear a lane ahead once (sandbox world only)
                int n = 0; for (float x = 12f; x <= 60f; x += 8f) n += ClearArea(m_spawnAt + Vector3.right * x, 8f);
                m_laneCleared = true; log.Add($"ride lane cleared ({n} objects)");
                yield return new WaitForSeconds(0.5f);
            }
            var go = Instantiate(prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right));
            var bike = go.GetComponent<MotorcycleController>();
            if (bike == null) { errors.Add($"{def.Key}: no controller on the prefab"); ZNetScene.instance.Destroy(go); yield break; }
            if (req.water) bike.m_noSink = true;                        // held up on water (a standing bike swamps in seconds) until a test studies sinking
            yield return new WaitForSeconds(0.8f);                      // drop onto the ground
            if (bike == null) { errors.Add($"{def.Key}: the bike vanished right after spawning (outside the loaded area?)"); yield break; }
            if (bike.Fuel != null) bike.Fuel.SetFraction(1f);           // bikes are built empty; these runs are about riding (fuel has its own check)
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            bike.Interact(player, false, false);                        // same path as pressing E
            float t = Time.time;
            while (!ReferenceEquals(player.GetDoodadController(), bike) && Time.time - t < 4f) yield return null;
            bool mounted = ReferenceEquals(player.GetDoodadController(), bike);
            if (!mounted) errors.Add($"{def.Key}: mount did not happen within 4 s");
            player.SetLookDir(bike.transform.forward, 0f);                 // the head follows the look direction; look down the road
            BagReport(def, bike, log, errors);
            if (!string.IsNullOrEmpty(req.skull))
            {   // the game attaches the helmet's model on one of its next updates
                SkullHead sh = null; float ts = Time.time;
                while (sh == null && Time.time - ts < 3f) { yield return null; sh = player.GetComponentInChildren<SkullHead>(); }
                log.Add($"{def.Key}: skull {(sh != null ? $"worn, head bone scale {sh.HeadScale:0.00}, skull size {sh.transform.lossyScale.x:0.00}" : "NOT FOUND")}");
            }
            if (req.steerOn && mounted)
            {
                bike.m_forceSteer = req.steer;
                yield return new WaitForSeconds(1.2f);
                log.Add($"{def.Key}: steering held at {req.steer:+0.0;-0.0}: bars turned {bike.BarAngle:+0.0;-0.0} degrees");
            }
            yield return new WaitForSeconds(req.settle);
            if (req.music && mounted) yield return MusicTest(bike, player, log, errors);
            if (req.seadry && mounted && req.water) { yield return SeaDryTest(def, bike, player, log, errors); if (bike == null) yield break; }
            if (req.sink && mounted && req.water)
            {
                m_sinkShot = at =>
                {
                    var path = Path.Combine(HarnessMode.Dir, $"{def.Key}_sunk.png");
                    try { HarnessCamera.Shoot(path, at + new Vector3(2.4f, 1.3f, 2.4f), at, req.width, req.height, 45f); pics.Add(path); } catch (Exception e) { errors.Add("sunk spot shot: " + e.Message); }
                };
                yield return SinkTest(def, bike, player, log, errors);
                if (bike == null) yield break;                            // sunk and broken apart, as it should be
            }
            if (!string.IsNullOrEmpty(req.ram) && mounted && !req.water) yield return RamTest(def, bike, req.ram, log, errors);
            if (!string.IsNullOrEmpty(req.wall) && mounted && !req.water) { yield return WallTest(def, bike, req.wall, req.boost, pics, log, errors); if (bike == null) yield break; }
            var poseNow = CurrentPose();
            var targets = new List<Target>();
            if (req.targets != null && req.targets.Length > 0 && !req.water) SpawnTargets(bike, req.targets, targets, errors);
            if (targets.Count > 0) yield return new WaitForSeconds(0.5f);
            if (req.ride > 0f)
            {
                m_fpsOverride = req.fps > 0 ? req.fps : 60; Application.targetFrameRate = m_fpsOverride; QualitySettings.vSyncCount = 0;
                bike.m_forceThrottle = 1f;                                   // W held, applied after the player's own input
                bike.m_forceBoost = req.boost;                               // Shift held
                float t1 = Time.time; int frames = 0; var gaps = new List<string>();
                int trailShots = 0; float[] shotAt = { 0.62f, 0.72f, 0.82f, 0.92f };
                while (Time.time - t1 < req.ride)
                {
                    yield return null; frames++;
                    if (trailShots < shotAt.Length && Time.time - t1 > req.ride * shotAt[trailShots])
                    {   // pictures from behind and above while riding, for the trail and the lights (four moments)
                        trailShots++; if (poseNow) poseNow.Apply();
                        Vector3 bp = bike.transform.position, f = bike.transform.forward;
                        string path = Path.Combine(HarnessMode.Dir, $"{def.Key}_moving{(trailShots > 1 ? trailShots.ToString() : "")}.png");
                        try { HarnessCamera.Shoot(path, bp - f * 6f + Vector3.up * 3f + bike.transform.right * 1.5f, bp - f * 2f, req.width, req.height, 50f); pics.Add(path); } catch (Exception e) { errors.Add("moving shot: " + e.Message); }
                        string side = Path.Combine(HarnessMode.Dir, $"{def.Key}_movingside{(trailShots > 1 ? trailShots.ToString() : "")}.png");
                        try { HarnessCamera.Shoot(side, bp + bike.transform.right * 6.5f + Vector3.up * 1.1f - f * 1.5f, bp - f * 1.2f + Vector3.up * 0.3f, req.width, req.height, 45f); pics.Add(side); } catch (Exception e) { errors.Add("moving side shot: " + e.Message); }
                        foreach (var tag in new[] { "movingfront", "movingfrontl" })
                        {   // the front corners, 45 degrees off the nose
                            float sideSign = tag == "movingfront" ? 1f : -1f;
                            string fp = Path.Combine(HarnessMode.Dir, $"{def.Key}_{tag}{(trailShots > 1 ? trailShots.ToString() : "")}.png");
                            try { HarnessCamera.Shoot(fp, bp + (f + bike.transform.right * sideSign).normalized * 6.5f + Vector3.up * 1.4f, bp + Vector3.up * 0.5f, req.width, req.height, 45f); pics.Add(fp); } catch (Exception e) { errors.Add($"{def.Key}: {tag} shot {e.Message}"); }
                        }
                        if (trailShots == 1 && req.census) Census(bike.transform.position, 35f, def.Key, log);
                    }
                    if (frames > 20 && gaps.Count < 6 && poseNow) { poseNow.Apply(); gaps.Add(poseNow.Measure().Split('|')[1].Trim()); }
                    SampleTargets(targets, bike);
                }
                if (req.wheelie && mounted) yield return WheelieTest(def, bike, player, poseNow, req, pics, log, errors);
                float tEnd = Time.time;
                while (targets.Count > 0 && Time.time - tEnd < 3f) { yield return null; SampleTargets(targets, bike); }   // burning goes on for a while after the pass
                bike.m_forceThrottle = float.NaN; bike.m_forceBoost = false;
                log.Add($"{def.Key}: ride {req.ride:0.0} s at {frames / req.ride:0} fps, speed {bike.SyncedSpeed:0.0} m/s; hand gap on consecutive frames: {string.Join(" / ", gaps)}");
                m_fpsOverride = 0; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 1;
                yield return new WaitForSeconds(1f);
            }
            ReportTargets(def, targets, log);
            if (bike == null) { errors.Add($"{def.Key}: the bike is gone after the ride (rider attached {player.IsAttached()}, player at {Fmt(player.transform.position)})"); yield break; }
            if (poseNow) poseNow.Apply();                               // measure the posed rider, not this frame's raw animation
            log.Add($"{def.Key}: pose {(poseNow ? poseNow.Status() : "MISSING")}; attached {player.IsAttached()}; controller {(ReferenceEquals(player.GetDoodadController(), bike) ? "bike" : "other")}");
            if (poseNow) log.Add($"{def.Key}: measure {poseNow.Measure()} | {BarGaps(bike, poseNow)}");
            if (bike.m_gripLeft && bike.m_gripRight)
            {
                Vector3 gl = bike.transform.InverseTransformPoint(bike.m_gripLeft.position), gr = bike.transform.InverseTransformPoint(bike.m_gripRight.position);
                log.Add($"{def.Key}: grips L ({gl.x:0.000},{gl.y:0.000},{gl.z:0.000}) R ({gr.x:0.000},{gr.y:0.000},{gr.z:0.000}) asymmetry up {(gl.y - gr.y) * 100:+0.0} fwd {(gl.z - gr.z) * 100:+0.0} cm");
            }
            if (req.probe) { try { WriteProfile(def, bike); log.Add($"{def.Key}: profile written"); } catch (Exception e) { errors.Add($"{def.Key}: probe {e.Message}"); } }
            if (req.markers) AddMarkers(bike);
            yield return null;
            var seat = bike.m_seatMarker ? bike.m_seatMarker.position : bike.transform.position + Vector3.up * 0.9f;
            float size = BikeSize(bike);
            // every view in the same frame, right after the rider pose for this frame, so all pictures show one moment
            // (the coroutine runs between Update and LateUpdate; Apply() is once-per-frame safe and LateUpdate then skips)
            var pose = CurrentPose(); if (pose) pose.Apply();
            int skins = 0;
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {   // a manual Camera.Render() otherwise reuses stale skinning (the mesh is 'offscreen' for the game camera in batch mode)
                smr.updateWhenOffscreen = true; smr.forceMatrixRecalculationPerRender = true; skins++;
            }
            log.Add($"{def.Key}: {skins} skinned meshes forced to re-skin per render");
            foreach (var view in views)
            {
                if (!ViewPose(view, bike, seat, size, out var pos, out var look)) { errors.Add($"{def.Key}: unknown view '{view}'"); continue; }
                string path = Path.Combine(HarnessMode.Dir, $"{def.Key}_{view}.png");
                try { HarnessCamera.Shoot(path, pos, look, req.width, req.height, req.fov); pics.Add(path); }
                catch (Exception e) { errors.Add($"{def.Key}/{view}: {e.Message}"); }
            }
            yield return null;
            RemoveMarkers();
            if (mounted) player.StopDoodadControl();                    // same as pressing E again
            yield return new WaitForSeconds(0.3f);
            bike.Remove();
            yield return new WaitForSeconds(0.3f);
            log.Add($"{def.Key}: {(mounted ? "mounted" : "NOT mounted")}, {views.Length} views, {Time.realtimeSinceStartup - t0:0.0} s, size {size:0.0} m");
        }

        private class Target { public Character c; public string name; public float hp0, minHp, closest = 999f; public Vector2 spot; public readonly HashSet<string> effects = new HashSet<string>(); }

        // (sideways, ahead) in metres from where the bike starts: two close to the line, one far off it, one further on
        private static readonly Vector2[] s_targetSpots = { new Vector2(-1.3f, 8f), new Vector2(2.2f, 9.5f), new Vector2(-4.5f, 12f), new Vector2(1.3f, 14f) };

        /// <summary>Creatures beside the ride path, AI off so they stand still and never attack.</summary>
        private static void SpawnTargets(MotorcycleController bike, string[] names, List<Target> into, List<string> errors)
        {
            for (int i = 0; i < names.Length && i < s_targetSpots.Length; i++)
            {
                var prefab = ZNetScene.instance.GetPrefab(names[i]);
                if (prefab == null || prefab.GetComponent<Character>() == null) { errors.Add($"target '{names[i]}': no such creature"); continue; }
                Vector3 p = bike.transform.position + bike.transform.right * s_targetSpots[i].x + bike.transform.forward * s_targetSpots[i].y;
                if (ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h + 0.05f;
                var go = Instantiate(prefab, p, Quaternion.LookRotation(-bike.transform.right));
                foreach (var ai in go.GetComponentsInChildren<BaseAI>(true)) ai.enabled = false;
                var c = go.GetComponent<Character>();
                into.Add(new Target { c = c, name = names[i], hp0 = c.GetMaxHealth(), minHp = c.GetMaxHealth(), spot = s_targetSpots[i] });
            }
        }

        private static void SampleTargets(List<Target> targets, MotorcycleController bike)
        {
            Vector3 wheel = bike.m_wheelRear ? bike.m_wheelRear.position : bike.transform.position;
            foreach (var t in targets)
            {
                if (t.c == null) continue;
                Vector3 d = t.c.transform.position - wheel; d.y = 0f; t.closest = Mathf.Min(t.closest, d.magnitude);
                t.minHp = Mathf.Min(t.minHp, t.c.GetHealth());
                var se = t.c.GetSEMan(); if (se == null) continue;
                foreach (var s in se.GetStatusEffects()) if (s != null) t.effects.Add(s.name);
            }
        }

        private static void ReportTargets(BikeDefinition def, List<Target> targets, List<string> log)
        {
            foreach (var t in targets)
            {
                bool gone = t.c == null || t.c.IsDead();
                log.Add($"{def.Key}: target {t.name} at {t.spot.x:+0.0;-0.0} m side {t.spot.y:0} m ahead: rear wheel passed {t.closest:0.0} m away; health {t.hp0:0} -> {(gone ? "dead" : t.c.GetHealth().ToString("0"))} (lowest {t.minHp:0}); effects seen [{string.Join(", ", t.effects)}]");
                if (t.c != null) { var nv = t.c.GetComponent<ZNetView>(); if (nv && nv.IsValid()) { nv.ClaimOwnership(); ZNetScene.instance.Destroy(t.c.gameObject); } }
            }
        }

        /// <summary>
        /// The wheelie with the throttle still held: (1) hold the key until the front goes past the top and is set back down,
        /// (2) feather it (hold below 28 degrees, let go above 34) for 6 s. Pictures from the side and the rear at the high point.
        /// Checks: the time to go over the top, the pitch range while feathering, the timer's best, health unchanged, still riding.
        /// </summary>
        private IEnumerator WheelieTest(BikeDefinition def, MotorcycleController bike, Player player, RiderPose pose, HarnessRequest req, List<string> pics, List<string> log, List<string> errors)
        {
            var health = bike.GetComponent<BikeHealth>();
            float hp0 = health ? health.Health : -1f, speed0 = bike.SyncedSpeed;
            // the crouch key is also the wheelie key: a crouch press while riding must not toggle the game's crouch
            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, true, false, false);
            bool crouched = Traverse.Create(player).Field("m_crouchToggled").GetValue<bool>();
            log.Add($"{def.Key}: crouch press while riding -> crouch toggled {crouched}");
            if (crouched) errors.Add($"{def.Key}: the crouch key toggled crouch while riding");
            bike.m_forceWheelie = 1;
            float t0 = Time.time, peak = 0f, overAt = -1f, nextTrace = 0f; bool shot = false;
            var trace = new List<string>();
            while (Time.time - t0 < 5f)
            {
                yield return null;
                float p = bike.Pitch; peak = Mathf.Max(peak, p);
                if (Time.time >= nextTrace) { nextTrace = Time.time + 0.2f; trace.Add($"{p:0}"); }
                if (!shot && p > 38f)
                {
                    shot = true; if (pose) pose.Apply();
                    var seat = bike.m_seatMarker ? bike.m_seatMarker.position : bike.transform.position + Vector3.up * 0.9f;
                    float size = BikeSize(bike);
                    foreach (var view in new[] { "right", "rear34", "front34", "front34l" })
                    {
                        if (!ViewPose(view, bike, seat, size, out var pos, out var look)) continue;
                        string path = Path.Combine(HarnessMode.Dir, $"{def.Key}_wheelie_{view}.png");
                        try { HarnessCamera.Shoot(path, pos, look, req.width, req.height, req.fov); pics.Add(path); } catch (Exception e) { errors.Add($"wheelie shot: {e.Message}"); }
                    }
                    if (pose) log.Add($"{def.Key}: wheelie at {p:0} deg, rider {pose.Measure()}");
                }
                if (overAt < 0f && p >= 61.5f) overAt = Time.time - t0;
                if (overAt >= 0f && Time.time - t0 > overAt + 1.2f) break;
                if (!ReferenceEquals(player.GetDoodadController(), bike)) { errors.Add($"{def.Key}: rider came off {Time.time - t0:0.00} s into the held wheelie at {p:0} deg"); break; }
            }
            log.Add($"{def.Key}: wheelie held from {speed0:0.0} m/s: over the top after {(overAt < 0f ? "never" : overAt.ToString("0.00") + " s")}, peak {peak:0} deg; pitch every 0.2 s: {string.Join(" ", trace)}");
            bike.m_forceWheelie = 0;
            float tw = Time.time; while (bike.Pitch > 1f && Time.time - tw < 4f) yield return null;
            yield return new WaitForSeconds(0.4f);
            float best0 = MotorcycleController.BestWheelie;
            bike.m_forceWheelie = 1;
            float tf = Time.time, lo = 99f, hi = 0f, up = 0f;
            while (Time.time - tf < 6f)
            {
                yield return null;
                float p = bike.Pitch;
                if (p < 28f) bike.m_forceWheelie = 1; else if (p > 34f) bike.m_forceWheelie = 0;
                if (Time.time - tf > 1.5f) { lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p); }
                if (p > 8f) up += Time.deltaTime;
                if (!ReferenceEquals(player.GetDoodadController(), bike)) { errors.Add($"{def.Key}: rider came off {Time.time - tf:0.00} s into feathering at {p:0} deg"); break; }
            }
            bike.m_forceWheelie = 0;
            tw = Time.time; while (bike.Pitch > 1f && Time.time - tw < 4f) yield return null;
            yield return new WaitForSeconds(0.3f);
            bool riding = ReferenceEquals(player.GetDoodadController(), bike);
            log.Add($"{def.Key}: wheelie feathered 6 s: pitch {lo:0}-{hi:0} deg after the first 1.5 s, front up {up:0.0} s; timer best {best0:0.0} -> {MotorcycleController.BestWheelie:0.0} s; health {hp0:0} -> {(health ? health.Health : -1f):0}; still riding {riding}; speed {bike.SyncedSpeed:0.0} m/s");
            if (!riding) errors.Add($"{def.Key}: the wheelie threw the rider off");
            if (health && health.Health < hp0 - 0.5f) errors.Add($"{def.Key}: the wheelie cost {hp0 - health.Health:0} health");
            bike.m_forceWheelie = -1;
        }

        /// <summary>Colliders within 2 m ahead of the bike's front (not the bike, the rider or the ground): what is it pushing on?</summary>
        private static string WhatIsAhead(MotorcycleController bike)
        {
            Vector3 c = bike.transform.position + bike.transform.forward * 2.2f + Vector3.up * 0.6f;
            var names = new List<string>();
            foreach (var col in Physics.OverlapBox(c, new Vector3(0.8f, 0.6f, 1.2f), bike.transform.rotation))
            {
                if (col.transform.IsChildOf(bike.transform) || col.GetComponentInParent<Player>() != null) continue;
                if (col.GetComponentInParent<Heightmap>() != null) continue;
                var root = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform.root;
                names.Add($"{root.name}/{col.name} ({LayerMask.LayerToName(col.gameObject.layer)}) at {Vector3.Distance(col.ClosestPoint(c), bike.transform.position):0.0} m");
            }
            return names.Count == 0 ? "nothing" : string.Join("; ", names.Distinct());
        }

        /// <summary>Puts the bike (and so its rider) somewhere else, at rest.</summary>
        private static void MoveBike(MotorcycleController bike, Vector3 pos)
        {
            var rb = bike.GetComponent<Rigidbody>();
            bike.transform.position = pos;
            if (rb) { rb.position = pos; rb.linearVelocity = Vector3.zero; }
            Physics.SyncTransforms();
        }

        /// <summary>A creature that stands still (AI off) at p, on the ground.</summary>
        private static Character SpawnStill(GameObject prefab, Vector3 p, Vector3 faceTowards)
        {
            if (ZoneSystem.instance.GetGroundHeight(p, out float h)) p.y = h + 0.05f;
            Vector3 look = faceTowards - p; look.y = 0f;
            var go = Instantiate(prefab, p, look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look) : Quaternion.identity);
            foreach (var ai in go.GetComponentsInChildren<BaseAI>(true)) ai.enabled = false;
            return go.GetComponent<Character>();
        }

        private static void RemoveObject(Component c)
        {
            if (c == null) return;
            var nv = c.GetComponent<ZNetView>();
            if (nv && nv.IsValid()) { nv.ClaimOwnership(); ZNetScene.instance.Destroy(c.gameObject); }
        }

        /// <summary>
        /// Contact damage: (1) a creature standing right in front of the front wheel while the throttle is held for 3 s (a mob
        /// half a metre ahead used to melt at walking pace); (2) a full-speed ram on one 16 m down the lane.
        /// </summary>
        private IEnumerator RamTest(BikeDefinition def, MotorcycleController bike, string creature, List<string> log, List<string> errors)
        {
            var prefab = ZNetScene.instance.GetPrefab(creature);
            if (prefab == null || prefab.GetComponent<Character>() == null) { errors.Add($"ram: no creature '{creature}'"); yield break; }
            var health = bike.GetComponent<BikeHealth>();
            float front = bike.m_wheelFront ? Vector3.Dot(bike.m_wheelFront.position - bike.transform.position, bike.transform.forward) + bike.m_wheelRadius : 1.5f;
            // 0. a live one walking up to the bike, which stands still: under the old rule its own steps counted as rams
            var live = SpawnStill(prefab, bike.transform.position + bike.transform.forward * (front + 1.5f), bike.transform.position);
            foreach (var ai in live.GetComponentsInChildren<BaseAI>(true)) ai.enabled = true;
            float liveHp = live.GetHealth(), liveLast = liveHp; int liveHits = 0; float tl = Time.time;
            while (Time.time - tl < 4f && live != null && !live.IsDead()) { yield return null; float hp = live.GetHealth(); if (hp < liveLast - 0.01f) { liveHits++; liveLast = hp; } }
            log.Add($"{def.Key}: ram 0, a live {creature} at the standing bike for 4 s: {liveHits} hits from the bike, health {liveHp:0} -> {(live == null || live.IsDead() ? "dead" : live.GetHealth().ToString("0"))}");
            if (liveHits > 0) errors.Add($"{def.Key}: a creature walking into the standing bike got hurt");
            RemoveObject(live);
            yield return new WaitForSeconds(0.5f);
            var c = SpawnStill(prefab, bike.transform.position + bike.transform.forward * (front + 0.6f), bike.transform.position);
            yield return new WaitForSeconds(0.6f);
            float hp0 = c.GetHealth(), last = hp0, top = 0f; int hits = 0;
            bike.m_forceThrottle = 1f;
            float t0 = Time.time;
            while (Time.time - t0 < 3f && c != null && !c.IsDead())
            {
                yield return null;
                float hp = c.GetHealth(); if (hp < last - 0.01f) { hits++; last = hp; }
                top = Mathf.Max(top, bike.Speed);
            }
            log.Add($"{def.Key}: ram 1, {creature} standing {0.6f:0.0} m ahead of the front wheel, throttle held 3 s: {hits} hits, health {hp0:0} -> {(c == null || c.IsDead() ? "dead" : c.GetHealth().ToString("0"))}, bike speed up to {top:0.0} m/s");
            RemoveObject(c);
            bike.m_forceThrottle = -1f; yield return new WaitForSeconds(1.2f);
            bike.m_forceThrottle = 0f; yield return new WaitForSeconds(0.6f);
            Vector3 was = bike.transform.position;
            MoveBike(bike, m_spawnAt);                                     // back to the start: the lane ends at the Elder's stone
            yield return new WaitForSeconds(1f);
            Vector3 flat = bike.transform.forward; flat.y = 0f; flat.Normalize();
            Vector3 at = bike.transform.position + flat * 16f;              // the Elder's stone stands about 26 m down the lane
            log.Add($"{def.Key}: bike moved from {Fmt(was)} to {Fmt(m_spawnAt)}, now at {Fmt(bike.transform.position)} facing {Fmt(bike.transform.forward)}; ram 2 target at {Fmt(at)}");
            c = SpawnStill(prefab, at, bike.transform.position);
            yield return new WaitForSeconds(0.6f);
            hp0 = c.GetHealth(); last = hp0; hits = 0; top = 0f;
            float bike0 = health ? health.Health : -1f, hitSpeed = 0f;
            bike.m_forceThrottle = 1f; t0 = Time.time;
            while (Time.time - t0 < 5f)
            {
                yield return null;
                top = Mathf.Max(top, bike.Speed);
                if (c != null && !c.IsDead()) { float hp = c.GetHealth(); if (hp < last - 0.01f) { hits++; last = hp; if (hitSpeed == 0f) hitSpeed = top; } }
                if (Vector3.Dot(bike.transform.position - at, bike.transform.forward) > 3f) break;
            }
            log.Add($"{def.Key}: ram 2, {creature} 16 m down the lane at full throttle: {hits} hits (at {hitSpeed:0.0} m/s), health {hp0:0} -> {(c == null || c.IsDead() ? "dead" : c.GetHealth().ToString("0"))}; bike {bike0:0} -> {(health ? health.Health : -1f):0}");
            if (hits == 0) errors.Add($"{def.Key}: the full-speed ram did not hurt the {creature}");
            log.Add($"{def.Key}: after ram 2 the bike is at {Fmt(bike.transform.position)}, the {creature} at {(c != null ? Fmt(c.transform.position) : "gone")}; ahead: {WhatIsAhead(bike)}");
            RemoveObject(c);
            bike.m_forceThrottle = -1f; yield return new WaitForSeconds(1.5f);
            bike.m_forceThrottle = float.NaN;
        }

        /// <summary>Crash into a building piece across the lane at full speed: the piece loses health, the bike stops against it.
        /// With Shift held (--boost) the bike gets the acceleration to be at boost speed by the wall, so the crash breaks it and it
        /// goes up in a fireball: photographed, and the recipe must lie loose on the ground.</summary>
        private IEnumerator WallTest(BikeDefinition def, MotorcycleController bike, string piece, bool boost, List<string> pics, List<string> log, List<string> errors)
        {
            var prefab = ZNetScene.instance.GetPrefab(piece);
            if (prefab == null || prefab.GetComponent<WearNTear>() == null) { errors.Add($"wall: no building piece '{piece}'"); yield break; }
            Vector3 p = bike.transform.position + bike.transform.forward * 22f;
            if (ZoneSystem.instance.GetGroundHeight(p, out float gh)) p.y = gh;
            var go = Instantiate(prefab, p, Quaternion.LookRotation(bike.transform.forward));      // its face across the lane
            yield return new WaitForSeconds(1.2f);
            var w = go != null ? go.GetComponent<WearNTear>() : null;
            if (w == null) { errors.Add($"wall: the {piece} fell down before the test"); yield break; }
            var health = bike.GetComponent<BikeHealth>();
            float max = w.m_health, before = w.GetHealthPercentage() * max, bike0 = health ? health.Health : -1f, top = 0f;
            float accel0 = def.CfgAcceleration.Value;
            if (boost)
            {
                Traverse.Create(bike.GetComponent<Piece>()).Field("m_creator").SetValue(Game.instance.GetPlayerProfile().GetPlayerID());   // as if built: the whole recipe drops
                def.CfgAcceleration.Value = 30f;                                                   // 22 m is not enough run-up for boost speed otherwise
            }
            var drops0 = DropsNow();
            bike.m_forceThrottle = 1f; bike.m_forceBoost = boost; float t0 = Time.time, hitAt = -1f, brokeAt = -1f; Vector3 last = bike.transform.position;
            while (Time.time - t0 < 6f)
            {
                yield return null;
                if (bike == null) { brokeAt = Time.time - t0; break; }
                last = bike.transform.position; top = Mathf.Max(top, bike.Speed);
                if (hitAt < 0f && (w == null || w.GetHealthPercentage() * max < before - 0.01f)) hitAt = Time.time;
                if (hitAt > 0f && Time.time - hitAt > 1.5f) break;             // keep pushing a moment: no more hits, the bike stays stopped
            }
            def.CfgAcceleration.Value = accel0;
            float after = w != null ? w.GetHealthPercentage() * max : 0f;
            if (bike == null)
            {
                int fireballs = UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Count(v => v != null && v.gameObject.name.StartsWith("fx_dynamite_explosion") && Vector3.Distance(v.transform.position, last) < 10f);
                Vector3 cam = last + new Vector3(-4f, 2.2f, 6f);
                for (int i = 0; i < 2; i++)
                {
                    var path = Path.Combine(HarnessMode.Dir, $"{def.Key}_crash{i}.png");
                    try { HarnessCamera.Shoot(path, cam, last + Vector3.up * 0.8f, 1280, 800, 50f); pics.Add(path); } catch (Exception e) { errors.Add("crash shot: " + e.Message); }
                    yield return new WaitForSeconds(0.25f);
                }
                yield return new WaitForSeconds(2.5f);
                var loose = LooseItems(last, 15f, drops0);
                int recipe = def.Requirements.Sum(r => r.Amount);
                log.Add($"{def.Key}: wall, {piece} ({max:0} health) 22 m ahead {(boost ? "with boost" : "at full throttle")}: hit at {top:0.0} m/s and broke apart at {brokeAt:0.0} s; wall {before:0} -> {(w == null ? "destroyed" : after.ToString("0"))}; fireballs {fireballs}; {loose.items} items (recipe {recipe}) loose ({loose.what})");
                if (!boost) errors.Add($"{def.Key}: the crash without boost broke the bike");
                if (fireballs < 1) errors.Add($"{def.Key}: the fatal crash made no fireball");
                if (loose.items != recipe) errors.Add($"{def.Key}: {loose.items} of {recipe} items came back from the crash");
                if (w != null) RemoveObject(w);
                yield break;
            }
            log.Add($"{def.Key}: wall, {piece} ({max:0} health) 22 m ahead {(boost ? "with boost" : "at full throttle")}: hit at {top:0.0} m/s; wall {before:0} -> {(w == null ? "destroyed" : after.ToString("0"))}; bike {bike0:0} -> {(health ? health.Health : -1f):0}; bike speed while pushing on {bike.Speed:0.0} m/s");
            if (w != null && after >= before - 0.01f) errors.Add($"{def.Key}: the crash did not damage the {piece}");
            if (boost) errors.Add($"{def.Key}: the crash at boost speed did not break the bike ({(health ? health.Health : -1f):0} health left)");
            bike.m_forceThrottle = -1f; bike.m_forceBoost = false; yield return new WaitForSeconds(1f);
            bike.m_forceThrottle = float.NaN;
            if (w != null) RemoveObject(w);
        }

        /// <summary>
        /// Sinking on water: up on the plane under boost it stays dry; cruising without boost it settles; boost lifts it back;
        /// a wheelie holds it up without boost; braking to a stop it swamps within seconds, the engine floods and the bike
        /// breaks apart: the rider ends up swimming and the materials go down to the seabed, loose, with no bag.
        /// </summary>
        private System.Action<Vector3> m_sinkShot;     // set by ShootBike: photographs the spot where the bike went down

        private IEnumerator SinkTest(BikeDefinition def, MotorcycleController bike, Player player, List<string> log, List<string> errors)
        {
            var health = bike.GetComponent<BikeHealth>();
            float hp0 = health ? health.Health : -1f;
            Traverse.Create(bike.GetComponent<Piece>()).Field("m_creator").SetValue(Game.instance.GetPlayerProfile().GetPlayerID());   // as if built: the whole recipe drops
            // the rule: it stays up only at boost speed (Shift+W) or in a wheelie; cruising it settles, stopped it swamps in seconds
            IEnumerator Phase(string name, float seconds, float throttle, bool boost, bool wheelie, List<string> into)
            {
                bike.m_forceThrottle = throttle; bike.m_forceBoost = boost; bike.m_forceWheelie = wheelie ? 1 : -1;
                float t0 = Time.time, from = bike.SinkDepth, next = 0f;
                var trace = new List<string>();
                while (bike != null && Time.time - t0 < seconds)
                {
                    yield return null;
                    if (bike == null) break;
                    if (wheelie) bike.m_forceWheelie = bike.Pitch < 22f ? 1 : bike.Pitch > 30f ? 0 : bike.m_forceWheelie;   // feathered
                    if (Time.time >= next) { next = Time.time + 0.5f; trace.Add($"{bike.SinkDepth:0.00}@{bike.Speed:0}"); }
                }
                into.Add($"{name}: {from:0.00} -> {(bike != null ? bike.SinkDepth : 9.99f):0.00} m ({string.Join(" ", trace)}, depth@speed every 0.5 s)");
            }
            var phases = new List<string>();
            bike.m_noSink = true;                                        // held up while it gets up to speed: a standing start on deep water is fatal by design
            yield return Phase("Shift+W, held up", 4f, 1f, true, false, phases);
            bike.m_noSink = false;
            yield return Phase("Shift+W", 3f, 1f, true, false, phases);
            float afterBoost = bike != null ? bike.SinkDepth : 9.99f;
            if (bike != null) yield return Phase("W only", 1f, 1f, false, false, phases);
            float afterCruise = bike != null ? bike.SinkDepth : 9.99f;
            if (bike != null) yield return Phase("Shift+W again", 2f, 1f, true, false, phases);
            float afterLift = bike != null ? bike.SinkDepth : 9.99f;
            if (bike != null) yield return Phase("wheelie, W only", 3f, 1f, false, true, phases);
            if (bike == null) { errors.Add($"{def.Key}: sank during the planing phases ({string.Join("; ", phases)})"); yield break; }
            float afterWheelie = bike.SinkDepth;
            bike.m_forceBoost = false; bike.m_forceWheelie = -1;
            log.Add($"{def.Key}: sink 1: {string.Join("; ", phases)}; health {hp0:0} -> {(health ? health.Health : -1f):0}");
            if (afterBoost > 0.05f) errors.Add($"{def.Key}: at boost speed it did not stay up ({afterBoost:0.00} m)");
            if (afterCruise < afterBoost + 0.15f) errors.Add($"{def.Key}: cruising without boost did not sink");
            if (afterLift > 0.05f) errors.Add($"{def.Key}: boost did not lift it back up ({afterLift:0.00} m)");
            if (afterWheelie > 0.1f) errors.Add($"{def.Key}: it sank during the wheelie ({afterWheelie:0.00} m)");
            // brake to a stop: it swamps while slowing, the engine floods, and the bike breaks apart
            var before = DropsNow();
            bike.m_forceThrottle = -1f;
            float t1 = Time.time, swampAt = -1f, brokeAt = -1f, stoppedAt = -1f, nextDown = 0f; Vector3 last = bike.transform.position; var down = new List<string>();
            while (Time.time - t1 < 20f)
            {
                yield return null;
                if (bike == null) { brokeAt = Time.time - t1; break; }
                last = bike.transform.position;
                if (stoppedAt < 0f && Mathf.Abs(bike.Speed) < 0.5f) { stoppedAt = Time.time - t1; bike.m_forceThrottle = 0f; }
                if (swampAt < 0f && bike.Swamped) swampAt = Time.time - t1;
                if (Time.time >= nextDown) { nextDown = Time.time + 0.5f; down.Add($"{bike.SinkDepth:0.00}@{bike.Speed:0}"); }
            }
            bool gone = bike == null;
            yield return new WaitForSeconds(4f);                         // the materials settle on the bottom
            var loose = LooseItems(last, 15f, before);
            var (bags, items, _) = WreckBags(last, 15f);
            int recipe = def.Requirements.Sum(r => r.Amount);
            log.Add($"{def.Key}: sink 2, braking from cruise: stopped at {stoppedAt:0.0} s, swamped at {swampAt:0.0} s, {(gone ? $"broke apart at {brokeAt:0.0} s" : "STILL THERE after 20 s")} (depth@speed every 0.5 s: {string.Join(" ", down)}); rider attached {player.IsAttached()}, on a bike {player.GetDoodadController() != null}, swimming {player.IsSwimming()}; {loose.items} items (recipe {recipe}) in {loose.piles} piles ({loose.what}), {loose.underWater:0.0} m under the surface on average; {bags} wreck bag(s) holding {items}");
            if (!gone) errors.Add($"{def.Key}: the bike never sank");
            if (player.GetDoodadController() != null) errors.Add($"{def.Key}: the rider still holds the controls after the bike broke");
            if (gone && loose.items != recipe) errors.Add($"{def.Key}: {loose.items} of {recipe} items went down with the sunk bike");
            if (bags > 0) errors.Add($"{def.Key}: a wreck bag came up for the bike's materials");
            if (m_sinkShot != null) m_sinkShot(last);
        }

        /// <summary>The soundtrack on water, single player: starts, survives a short hop off the bike, pauses after the grace
        /// period, resumes the same song, goes quiet on the mute toggle, moves on with skip. Each step is logged with numbers.</summary>
        private IEnumerator MusicTest(MotorcycleController bike, Player player, List<string> log, List<string> errors)
        {
            var st = Soundtrack.SoundtrackManager.Instance;
            if (st == null) { errors.Add("music: no soundtrack component"); yield break; }
            IEnumerator Remount()
            {
                if (Vector3.Distance(player.transform.position, bike.transform.position) > 2.5f)
                    PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
                bike.Interact(player, false, false);
                float tm = Time.time;
                while (!ReferenceEquals(player.GetDoodadController(), bike) && Time.time - tm < 4f) yield return null;
                if (!ReferenceEquals(player.GetDoodadController(), bike)) errors.Add("music: could not get back on the bike");
            }
            log.Add($"music: bike on water {bike.IsOnWater}, uid {ZNet.GetUID()}");
            float t = Time.time;
            while (!st.Audible && Time.time - t < 20f) yield return null;
            log.Add($"music: audible {st.Audible} after {Time.time - t:0.0} s. {st.Describe()}");
            if (!st.Audible) { errors.Add("music never became audible on water"); yield break; }
            yield return new WaitForSeconds(2.5f);
            string song = st.CurrentSong; float time1 = st.SourceTime, t1 = Time.time;
            log.Add($"music: playing '{song}' at {time1:0.0} s, level {st.Level:0.00}, volume {st.SourceVolume:0.000}");

            player.StopDoodadControl();
            yield return new WaitForSeconds(3f);
            float fadeOut = Soundtrack.SoundtrackManager.FadeOutSeconds.Value, expectLevel = 1f - 3f / fadeOut;
            log.Add($"music: 3 s off the bike: level {st.Level:0.00} (a {fadeOut:0} s fade-out gives about {expectLevel:0.00}), audible {st.Audible}, host paused {st.HostPaused}");
            if (st.Level > expectLevel + 0.06f || st.Level < expectLevel - 0.1f) errors.Add($"music: off the bike the music should fade over {fadeOut:0} s; the level is {st.Level:0.00} after 3 s");
            yield return Remount();
            yield return new WaitForSeconds(2.5f);
            float expected = time1 + (Time.time - t1);
            log.Add($"music: back on within the grace: '{st.CurrentSong}' at {st.SourceTime:0.0} s (expected {expected:0.0}), level {st.Level:0.00}");
            if (st.CurrentSong != song) errors.Add("music: a new song after a short hop");
            if (Mathf.Abs(st.SourceTime - expected) > 1f) errors.Add("music: the song did not continue from where it was");

            // the playlist pauses once everyone has been off the water for the grace period: shortened to 4 s for the test
            float grace = Soundtrack.SoundtrackManager.GraceSeconds.Value; Soundtrack.SoundtrackManager.GraceSeconds.Value = 4f;
            player.StopDoodadControl();
            yield return new WaitForSeconds(9f);
            float pausedAt = st.HostPausedOffset;
            log.Add($"music: 9 s off the bike with a 4 s grace: host paused {st.HostPaused} at {pausedAt:0.0} s, audible {st.Audible}");
            if (!st.HostPaused) errors.Add("music: playlist did not pause after the grace period");
            Soundtrack.SoundtrackManager.GraceSeconds.Value = grace;
            yield return Remount();
            yield return new WaitForSeconds(3f);
            log.Add($"music: back on after the pause: '{st.CurrentSong}' at {st.SourceTime:0.0} s (paused at {pausedAt:0.0}), host paused {st.HostPaused}, level {st.Level:0.00}");
            if (st.CurrentSong != song) errors.Add("music: a different song after the pause");
            if (st.HostPaused || st.SourceTime < pausedAt || st.SourceTime > pausedAt + 4f) errors.Add("music: did not resume where it paused");

            st.Toggle();
            yield return new WaitForSeconds(2f);
            log.Add($"music: muted: level {st.Level:0.00}, audible {st.Audible}");
            if (st.Audible) errors.Add("music: still audible after the mute key");
            st.Toggle();
            yield return new WaitForSeconds(2.5f);
            log.Add($"music: unmuted: level {st.Level:0.00}, audible {st.Audible}");
            if (!st.Audible) errors.Add("music: silent after unmuting");

            st.RequestSkip();
            t = Time.time;
            while ((st.CurrentSong == song || st.ClipSong != st.CurrentSong || !st.Audible) && Time.time - t < 8f) yield return null;
            log.Add($"music: skip: '{song}' -> '{st.CurrentSong}' after {Time.time - t:0.0} s, audible {st.Audible}. {st.Describe()}");
            if (st.CurrentSong == song || st.ClipSong != st.CurrentSong || !st.Audible) errors.Add("music: skip did not start another song");
        }

        private static void ListPrefabs(string pattern, List<string> log, List<string> errors)
        {
            System.Text.RegularExpressions.Regex rx;
            try { rx = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch (Exception e) { errors.Add("prefabs: bad pattern: " + e.Message); return; }
            var all = new List<GameObject>();
            var zs = ZNetScene.instance;
            all.AddRange(zs.m_prefabs); all.AddRange(zs.m_nonNetViewPrefabs);
            var lines = all.Where(p => p != null && rx.IsMatch(p.name)).Select(p => p.name).Distinct().OrderBy(n => n)
                .Select(n => { var p = zs.GetPrefab(n); return $"{n}\tparticles {p.GetComponentsInChildren<ParticleSystem>(true).Length}\tlights {p.GetComponentsInChildren<Light>(true).Length}\taudio {p.GetComponentsInChildren<AudioSource>(true).Length}\tnetview {(p.GetComponent<ZNetView>() ? "yes" : "no")}"
                    + string.Concat(p.GetComponentsInChildren<Renderer>(true).Select(r => $"\n    {r.GetType().Name} {PathOf(r.transform, p.transform)}{PsInfo(r)}: " + string.Join("; ", r.sharedMaterials.Where(m => m).Select(m => $"{m.name} [{(m.shader ? m.shader.name : "?")}] tex {(m.HasProperty("_MainTex") && m.mainTexture ? m.mainTexture.name : "-")}")))); }).ToList();
            File.WriteAllText(Path.Combine(HarnessMode.Dir, "prefabs.txt"), string.Join("\n", lines) + "\n");
            log.Add($"prefabs: {lines.Count} of {all.Count} match '{pattern}' (harness/prefabs.txt)");
        }

        /// <summary>How a prefab is built: its WearNTear (health, new/worn/broken/wet objects, hit/switch/destroyed effects with their
        /// particle systems and sounds) and its renderers' materials (shader and the properties a MaterialPropertyBlock could drive).</summary>
        private static void Inspect(string[] names, List<string> log, List<string> errors)
        {
            string[] props = { "_Color", "_MainTex", "_MetallicGlossMap", "_Metallic", "_Glossiness", "_GlossMapScale", "_BumpMap", "_EmissionColor", "_Cutoff" };
            foreach (var name in names)
            {
                var p = ZNetScene.instance.GetPrefab(name) ?? Jotunn.Managers.PrefabManager.Instance.GetPrefab(name);
                if (p == null) { errors.Add($"inspect {name}: no such prefab"); continue; }
                var w = p.GetComponent<WearNTear>();
                if (w != null)
                {
                    string Obj(GameObject g) => g == null ? "-" : $"{PathOf(g.transform, p.transform)} ({g.GetComponentsInChildren<Renderer>(true).Length} renderers, {g.GetComponentsInChildren<ParticleSystem>(true).Length} particle systems)";
                    log.Add($"inspect {name}: WearNTear health {w.m_health}, material {w.m_materialType}; new {Obj(w.m_new)}; worn {Obj(w.m_worn)}; broken {Obj(w.m_broken)}; wet {Obj(w.m_wet)}; hitNoise {w.m_hitNoise}, destroyNoise {w.m_destroyNoise}");
                    log.Add($"    hit: {Fx(w.m_hitEffect)}");
                    log.Add($"    switch: {Fx(w.m_switchEffect)}");
                    log.Add($"    destroyed: {Fx(w.m_destroyedEffect)}");
                }
                else log.Add($"inspect {name}: no WearNTear");
                foreach (var r in p.GetComponentsInChildren<Renderer>(true).Take(16))
                    log.Add($"    renderer {PathOf(r.transform, p.transform)}: " + string.Join("; ", r.sharedMaterials.Where(m => m).Select(m => $"{m.name} [{(m.shader ? m.shader.name : "?")}] " + string.Join(",", props.Where(m.HasProperty)))));
            }
        }

        private static string Fx(EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) return "none";
            return string.Join(" | ", list.m_effectPrefabs.Where(e => e != null && e.m_prefab != null).Select(e =>
                $"{e.m_prefab.name} (ps {string.Join("/", e.m_prefab.GetComponentsInChildren<ParticleSystem>(true).Select(ps => ps.name))}; audio {string.Join("/", e.m_prefab.GetComponentsInChildren<AudioSource>(true).Select(a => a.clip ? a.clip.name : "?"))}; attach {e.m_attach}, scale {e.m_scale})"));
        }

        /// <summary>Every particle system with live particles near p: its object path, distance, particle count and material.</summary>
        private static void Census(Vector3 p, float r, string key, List<string> log)
        {
            var rows = new List<string>();
            foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps == null || ps.particleCount == 0) continue;
                float d = Vector3.Distance(ps.transform.position, p);
                if (d > r) continue;
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                rows.Add($"{PathOf(ps.transform, null)} at {d:0.0} m: {ps.particleCount} particles, {(pr && pr.sharedMaterial ? pr.sharedMaterial.name : "-")}, {ps.main.simulationSpace}");
            }
            log.Add($"{key}: census of live particle systems within {r:0} m ({rows.Count}):");
            foreach (var row in rows.OrderBy(x => x)) log.Add("    " + row);
        }

        private static string PathOf(Transform t, Transform root) { var parts = new List<string>(); for (; t != null && t != root; t = t.parent) parts.Add(t.name); parts.Reverse(); return parts.Count == 0 ? root.name : string.Join("/", parts); }

        private static string PsInfo(Renderer r)
        {
            var ps = r.GetComponent<ParticleSystem>(); if (ps == null) return "";
            var m = ps.main; var e = ps.emission;
            return $" [life {m.startLifetime.constantMin:0.##}-{m.startLifetime.constantMax:0.##} ({m.startLifetime.mode}) size {m.startSize.constantMin:0.##}-{m.startSize.constantMax:0.##} speed {m.startSpeed.constantMax:0.##} space {m.simulationSpace} rate {e.rateOverTime.constantMax:0.#}/s {e.rateOverDistance.constantMax:0.#}/m shape {(ps.shape.enabled ? ps.shape.shapeType.ToString() : "off")} colour {m.startColor.color}]";
        }

        /// <summary>Spawns each named game effect at the stage, re-shapes particles meant for a body (mesh emitters) into a small
        /// sphere so they show on their own, waits for them to bloom and takes one picture each.</summary>
        private IEnumerator ShootEffects(HarnessRequest req, List<string> pics, List<string> errors, List<string> log)
        {
            Vector3 at = m_spawnAt + Vector3.up * 0.2f;
            try { HarnessCamera.Shoot(Path.Combine(HarnessMode.Dir, "fx__background.png"), at + new Vector3(2.2f, 1.1f, 2.2f), at + Vector3.up * 0.5f, req.width, req.height, 45f); } catch { }
            foreach (var name in req.fx)
            {
                var prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null) { errors.Add($"fx {name}: no such prefab"); continue; }
                var go = Instantiate(prefab, at, Quaternion.identity);
                int systems = 0;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = ps.shape;
                    // body effects (burning, frost) emit from the character's mesh, assigned at runtime: empty here, so use a small sphere.
                    // Effects that bring their own mesh (a weapon's blade) keep it.
                    bool noMesh = (shape.shapeType == ParticleSystemShapeType.MeshRenderer && shape.meshRenderer == null)
                               || (shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer && shape.skinnedMeshRenderer == null)
                               || (shape.shapeType == ParticleSystemShapeType.Mesh && shape.mesh == null);
                    if (shape.enabled && noMesh) { shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.35f; }
                    var main = ps.main; main.loop = true;
                    ps.Play(); systems++;
                }
                Vector3 camPos = at + new Vector3(2.2f, 1.1f, 2.2f), look = at + Vector3.up * 0.5f;
                yield return new WaitForSeconds(Mathf.Max(0.05f, req.fxEarly));
                string early = Path.Combine(HarnessMode.Dir, $"fx_{name}_early.png"), late = Path.Combine(HarnessMode.Dir, $"fx_{name}.png");
                try { HarnessCamera.Shoot(early, camPos, look, req.width, req.height, 45f); pics.Add(early); } catch (Exception e) { errors.Add($"fx {name}: {e.Message}"); }
                yield return new WaitForSeconds(Mathf.Max(0f, 1.6f - req.fxEarly));
                try { HarnessCamera.Shoot(late, camPos, look, req.width, req.height, 45f); pics.Add(late); } catch (Exception e) { errors.Add($"fx {name}: {e.Message}"); }
                log.Add($"fx {name}: {systems} particle systems, lights {go.GetComponentsInChildren<Light>(true).Length}");
                var nv = go.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid()) { nv.ClaimOwnership(); ZNetScene.instance.Destroy(go); } else Destroy(go);
                yield return new WaitForSeconds(0.3f);
            }
        }

        private static void PlacePlayer(Player p, Vector3 pos, Quaternion rot)
        {
            var rb = p.GetComponent<Rigidbody>();
            p.transform.SetPositionAndRotation(pos, rot);
            if (rb) { rb.position = pos; rb.rotation = rot; rb.linearVelocity = Vector3.zero; }
            Physics.SyncTransforms();
        }

        private static float BikeSize(MotorcycleController bike)
        {
            var rs = (bike.m_visual ? bike.m_visual : bike.transform).GetComponentsInChildren<Renderer>()
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();   // not the trail's particles or arcs
            if (rs.Length == 0) return 3f;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Max(b.size.x, b.size.z, 2.5f);
        }

        /// <summary>Camera placements relative to the bike: distance scales with the bike's length so every bike fills the frame the same way.</summary>
        private static bool ViewPose(string view, MotorcycleController bike, Vector3 seat, float size, out Vector3 pos, out Vector3 look)
        {
            Vector3 f = bike.transform.forward, r = bike.transform.right, u = Vector3.up;
            float d = size * 1.05f + 0.6f;
            look = seat + u * 0.1f; pos = seat;
            switch (view)
            {
                case "right": pos = seat + r * d + u * 0.45f; return true;
                case "left": pos = seat - r * d + u * 0.45f; return true;
                case "front34": pos = seat + (f + r).normalized * d + u * 0.9f; return true;      // 45 degrees off the nose: never straight into the face and the headlight
                case "front34l": pos = seat + (f - r).normalized * d + u * 0.9f; return true;
                case "rear34": pos = seat + (-f * 0.75f - r * 0.65f).normalized * d + u * 1.8f; return true;
                case "front": pos = seat + f * d + u * 0.6f; return true;
                case "rear": pos = seat - f * d + u * 0.8f; return true;
                case "top": pos = seat + u * (d * 1.7f) + f * 0.01f; look = seat; return true;
                case "hands": pos = seat + (f * 0.9f + r * 0.4f).normalized * (d * 0.55f) + u * 1.0f; look = seat + f * 0.5f + u * 0.5f; return true;
                case "feet": pos = seat + r * (d * 0.6f) - u * 0.1f; look = seat - u * 0.5f; return true;
                case "bags":
                {   // the right saddlebag from behind and outside, close
                    var bag = bike.m_bags != null ? bike.m_bags.Find("BagRight") : null; Vector3 b = bag != null ? bag.position : seat - f * 1f;
                    pos = b + (r * 0.9f - f * 0.9f).normalized * 1.6f + u * 0.5f; look = b; return true;
                }
                case "bagsleft":
                {
                    var bag = bike.m_bags != null ? bike.m_bags.Find("BagLeft") : null; Vector3 b = bag != null ? bag.position : seat - f * 1f;
                    pos = b + (-r * 0.9f - f * 0.6f).normalized * 1.6f + u * 0.5f; look = b; return true;
                }
                case "riderhead":
                case "riderheadside":
                case "riderheadback":
                {   // the rider's head close (the flaming skulls)
                    var p = Player.m_localPlayer; Vector3 h = p != null ? p.GetHeadPoint() : seat + u * 0.8f;
                    Vector3 dir = view == "riderhead" ? (f * 0.85f + r * 0.5f) : view == "riderheadside" ? r : (-f * 0.8f + r * 0.6f);
                    pos = h + dir.normalized * 1.1f + u * 0.12f; look = h; return true;
                }
                default: return false;
            }
        }

        /// <summary>Top surface of the bike mesh (highest vertex per 5 cm cell, bike space x/z, metres) as a text grid, plus the raw cell list.</summary>
        private static void WriteProfile(BikeDefinition def, MotorcycleController bike)
        {
            var root = bike.transform; const float cell = 0.05f;
            var top = new Dictionary<(int, int), float>();
            foreach (var mf in (bike.m_visual ? bike.m_visual : root).GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
                var verts = mesh.vertices; var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    var k = (Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.z / cell));
                    if (!top.TryGetValue(k, out float y) || p.y > y) top[k] = p.y;
                }
            }
            if (top.Count == 0) throw new Exception("no readable mesh");
            int x0 = top.Keys.Min(k => k.Item1), x1 = top.Keys.Max(k => k.Item1), z0 = top.Keys.Min(k => k.Item2), z1 = top.Keys.Max(k => k.Item2);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# {def.Key}: highest mesh point per {cell * 100:0} cm cell, bike space; rows = z (forward, top row is the front), columns = x (left to right); values in cm, '.' = nothing");
            sb.Append("z\\x ");
            for (int x = x0; x <= x1; x++) sb.Append($"{x * cell * 100,5:0}");
            sb.AppendLine();
            for (int z = z1; z >= z0; z--)
            {
                sb.Append($"{z * cell * 100,5:0}");
                for (int x = x0; x <= x1; x++) sb.Append(top.TryGetValue((x, z), out float y) ? $"{y * 100,5:0}" : "    .");
                sb.AppendLine();
            }
            var seat = root.InverseTransformPoint(bike.m_seatMarker ? bike.m_seatMarker.position : root.position);
            var gl = bike.m_gripLeft ? root.InverseTransformPoint(bike.m_gripLeft.position) : Vector3.zero; var gr = bike.m_gripRight ? root.InverseTransformPoint(bike.m_gripRight.position) : Vector3.zero;
            var fl = bike.m_footLeft ? root.InverseTransformPoint(bike.m_footLeft.position) : Vector3.zero;
            sb.AppendLine($"# markers (x,y,z cm): seat ({seat.x * 100:0},{seat.y * 100:0},{seat.z * 100:0}) gripL ({gl.x * 100:0},{gl.y * 100:0},{gl.z * 100:0}) gripR ({gr.x * 100:0},{gr.y * 100:0},{gr.z * 100:0}) footL ({fl.x * 100:0},{fl.y * 100:0},{fl.z * 100:0})");
            File.WriteAllText(Path.Combine(HarnessMode.Dir, $"{def.Key}_profile.txt"), sb.ToString());
        }

        // ---------- markers ----------
        private void AddMarkers(MotorcycleController bike)
        {
            Material baseMat = null;
            foreach (var r in bike.GetComponentsInChildren<Renderer>()) { if (r.sharedMaterial) { baseMat = r.sharedMaterial; break; } }
            void Ball(Transform t, Color c, float s)
            {
                if (!t) return;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col) Destroy(col);
                go.transform.position = t.position; go.transform.localScale = Vector3.one * s;
                var mr = go.GetComponent<MeshRenderer>();
                if (baseMat) { var m = new Material(baseMat); m.color = c; if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c); mr.sharedMaterial = m; }
                else mr.material.color = c;
                m_markers.Add(go);
            }
            Ball(bike.m_seatMarker, Color.red, 0.12f);
            Ball(bike.m_gripLeft, Color.green, 0.08f); Ball(bike.m_gripRight, Color.green, 0.08f);
            Ball(bike.m_footLeft, Color.blue, 0.08f); Ball(bike.m_footRight, Color.blue, 0.08f);
            Ball(bike.m_headlight, Color.yellow, 0.08f);
        }

        private void RemoveMarkers() { foreach (var m in m_markers) if (m) Destroy(m); m_markers.Clear(); }

        // ---------- files ----------
        private void WriteDone(bool ok, float seconds, List<string> pics, List<string> errors, List<string> log)
        {
            var d = new HarnessDone { ok = ok, seconds = seconds, stage = m_haveStage ? Fmt(m_stage) : "", pictures = pics.ToArray(), errors = errors.ToArray(), log = log.ToArray() };
            File.WriteAllText(DonePath, JsonUtility.ToJson(d, true) + "\n");
            MotorModPlugin.Log.LogInfo($"Harness done: ok={ok} pictures={pics.Count} errors={errors.Count} in {seconds:0.0} s");
            foreach (var e in errors) MotorModPlugin.Log.LogWarning("Harness: " + e);
        }

        private static string Fmt(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00} {2:0.00}", v.x, v.y, v.z);

        private static bool TryParseStage(string s, out Vector3 v)
        {
            v = Vector3.zero;
            var p = s.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return false;
            if (!float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.x)) return false;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.y)) return false;
            return float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v.z);
        }

        private class FieldInfoCache
        {
            public readonly System.Reflection.FieldInfo MouseCapture = AccessTools.Field(typeof(GameCamera), "m_mouseCapture");
        }
    }
}
