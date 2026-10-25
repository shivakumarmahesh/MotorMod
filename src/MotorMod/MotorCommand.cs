using System.Linq;
using Jotunn.Entities;
using UnityEngine;

namespace MotorMod
{
    /// <summary>Console (F5): "motor &lt;bike&gt;" spawns a bike in front of you; list, remove, removeall, repair, fuel, reload; music [open|rescan|status|skip].</summary>
    public class MotorCommand : ConsoleCommand
    {
        public override string Name => "motor";
        public override string Help => "motor <bike>|list|remove|removeall|repair|fuel [percent]|reload|music [open|rescan|status|skip] : spawn a bike (" + string.Join(", ", BikeDefinition.All.Select(b => b.Key)) + "), list them, remove the nearest / all within 50 m, repair or fill the tanks of bikes within 10 m, reload settings, or control the sea soundtrack";
        public override bool IsCheat => false;

        public override void Run(string[] args)
        {
            var arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (arg == "list") { Console.instance.Print("Bikes: " + string.Join(", ", BikeDefinition.All.Select(b => $"{b.Key} ({b.DisplayName})"))); return; }
            if (arg == "reload") { MotorModPlugin.Instance.Config.Reload(); Console.instance.Print("Motor settings reloaded"); return; }
            if (arg == "music")
            {
                var st = Soundtrack.SoundtrackManager.Instance;
                if (st == null) { Console.instance.Print("Soundtrack not running"); return; }
                string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                if (sub == "status") { Console.instance.Print(st.Describe()); return; }
                if (sub == "open" || sub == "folder")
                {
                    string folder = st.MusicFolderPath;
                    Console.instance.Print("Music folder: " + folder + "  (drop MP3, OGG or WAV files here, then: motor music rescan)");
                    if (sub == "open" && !string.IsNullOrEmpty(folder)) Application.OpenURL(new System.Uri(folder).AbsoluteUri);
                    return;
                }
                if (sub == "rescan") { st.Rescan(); Console.instance.Print("Looking at the music folder again; new songs are measured in the background (motor music status)"); return; }
                if (sub == "skip")
                {
                    if (!Terminal.m_cheat) { Console.instance.Print("motor music skip is a test tool: type devcommands first"); return; }
                    st.RequestSkip(); Console.instance.Print("Asked the host for the next song"); return;
                }
                st.Toggle(); Console.instance.Print("Soundtrack " + (Soundtrack.SoundtrackManager.Enabled.Value ? "on" : "off")); return;
            }
            if (arg == "harness")
            {
                if (!Harness.HarnessMode.Enabled || Harness.HarnessRunner.Instance == null) { Console.instance.Print("Harness is only available when the game was started with -motorharness"); return; }
                var req = new Harness.HarnessRequest { bikes = args.Skip(1).ToArray() };
                Harness.HarnessRunner.Instance.Trigger(req);
                Console.instance.Print("Harness run started: " + (req.bikes.Length == 0 ? "all bikes" : string.Join(", ", req.bikes)));
                return;
            }
            if (!Terminal.m_cheat)
            {   // spawning, repairing and removing are cheats, as the game's own spawn command is (on a server only admins can enable them)
                Console.instance.Print("motor " + (arg.Length > 0 ? arg : "<bike>") + " is a cheat: type devcommands first (on a server you must be an admin)");
                return;
            }
            var player = Player.m_localPlayer;
            if (player == null) { Console.instance.Print("No player"); return; }
            if (arg == "repair")
            {
                int n = 0;
                foreach (var hp in Object.FindObjectsByType<BikeHealth>(FindObjectsSortMode.None))
                    if (Vector3.Distance(hp.transform.position, player.transform.position) < 10f) { hp.Repair(); n++; }
                Console.instance.Print($"Repaired {n} bike(s) within 10 m"); return;
            }
            if (arg == "fuel")
            {
                float pct = 100f;
                if (args.Length > 1 && !float.TryParse(args[1].TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct)) { Console.instance.Print("motor fuel [percent], e.g. motor fuel 25"); return; }
                int n = 0;
                foreach (var fuel in Object.FindObjectsByType<BikeFuel>(FindObjectsSortMode.None))
                    if (Vector3.Distance(fuel.transform.position, player.transform.position) < 10f) { fuel.SetFraction(pct / 100f); n++; }
                Console.instance.Print($"Set the tank of {n} bike(s) within 10 m to {pct:0}%"); return;
            }
            if (arg == "remove" || arg == "removeall")
            {
                bool all = arg == "removeall"; int n = 0; MotorcycleController nearest = null; float best = 50f;
                foreach (var bike in Object.FindObjectsByType<MotorcycleController>(FindObjectsSortMode.None))
                {
                    float d = Vector3.Distance(bike.transform.position, player.transform.position);
                    if (all) { if (d < 50f) { bike.Remove(); n++; } }
                    else if (d < best) { best = d; nearest = bike; }
                }
                if (!all && nearest != null) { nearest.Remove(); n = 1; }
                Console.instance.Print($"Removed {n} bike(s)"); return;
            }
            var def = BikeDefinition.Find(arg);
            if (def == null) { Console.instance.Print("Unknown bike. " + Help); return; }
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(def.PrefabName) : null;
            if (prefab == null) { Console.instance.Print($"{def.DisplayName} is not registered (missing bundle?)"); return; }
            var fwd = player.transform.forward; fwd.y = 0f; fwd.Normalize();
            Object.Instantiate(prefab, player.transform.position + fwd * 3.5f + Vector3.up * 0.6f, Quaternion.LookRotation(fwd));
            Console.instance.Print($"Spawned a {def.DisplayName}");
        }
    }
}
