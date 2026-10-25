using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MotorMod.Harness
{
    /// <summary>Checks for fuel, saddlebags and the game's durability on bikes (0.4.0).</summary>
    public partial class HarnessRunner
    {
        private static int CountItems(Inventory inv, string prefab)
        {
            var p = ObjectDB.instance.GetItemPrefab(prefab); if (p == null || inv == null) return 0;
            return inv.CountItems(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
        }

        /// <summary>Wreck saddlebags near a point: how many, and how many items (stacks counted) are in them.</summary>
        private static (int bags, int items, List<Container> list) WreckBags(Vector3 c, float r)
        {
            var list = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(k => k != null && k.gameObject.name.StartsWith(BikeVanillaParts.WreckBagName) && Vector3.Distance(k.transform.position, c) < r).ToList();
            return (list.Count, list.Sum(k => k.GetInventory().GetAllItems().Sum(i => i.m_stack)), list);
        }

        private static void ClearWreckBags(Vector3 c, float r)
        {
            foreach (var k in WreckBags(c, r).list) RemoveObject(k);
        }

        private IEnumerator Mount(MotorcycleController bike, Player player)
        {
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            bike.Interact(player, false, false);
            float t = Time.time;
            while (!ReferenceEquals(player.GetDoodadController(), bike) && Time.time - t < 4f) yield return null;
        }

        /// <summary>The harness character puts on armour, a cape and a weapon by prefab name; the weapon is sheathed (on the back or hip).</summary>
        private static void WearGear(Player p, string[] prefabs, List<string> log, List<string> errors)
        {
            if (prefabs == null || prefabs.Length == 0) return;
            var inv = p.GetInventory(); var worn = new List<string>();
            foreach (var prefab in prefabs)
            {
                var go = ObjectDB.instance.GetItemPrefab(prefab);
                if (go == null) { errors.Add($"no item {prefab}"); continue; }
                var item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);
                if (item == null) { inv.AddItem(go, 1); item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab); }
                var hiddenR = Traverse.Create(p).Field("m_hiddenRightItem").GetValue<ItemDrop.ItemData>(); var hiddenL = Traverse.Create(p).Field("m_hiddenLeftItem").GetValue<ItemDrop.ItemData>();
                bool sheathed = item != null && (ReferenceEquals(item, hiddenR) || ReferenceEquals(item, hiddenL));   // a weapon already on the back or hip stays there
                if (item == null || (!item.m_equipped && !sheathed && !p.EquipItem(item))) { errors.Add($"could not equip {prefab}"); continue; }
                worn.Add(prefab + (sheathed ? " (sheathed)" : ""));
            }
            p.HideHandItems();                                           // the weapon goes on the back (two-handed) or the hip; the hands are free for the bars
            log.Add($"gear: {string.Join(", ", worn)}");
        }

        /// <summary>The harness character puts on a helmet item by prefab name (taken off again with "").</summary>
        private static void Wear(Player p, string prefab, List<string> log, List<string> errors)
        {
            var inv = p.GetInventory();
            if (!string.IsNullOrEmpty(prefab) && inv.GetEquippedItems().Any(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet && i.m_dropPrefab != null && i.m_dropPrefab.name == prefab)) { log.Add($"wearing {prefab}: still on"); return; }
            foreach (var it in inv.GetEquippedItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet).ToList()) p.UnequipItem(it);
            if (string.IsNullOrEmpty(prefab)) return;
            // long hair and a full beard, so the pictures show whether the skull hides them
            Traverse.Create(p).Field("m_hairItem").SetValue("Hair3"); Traverse.Create(p).Field("m_beardItem").SetValue("Beard5");
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) { errors.Add($"no item {prefab}"); return; }
            var item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);
            if (item == null) { inv.AddItem(go, 1); item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab); }
            bool ok = item != null && p.EquipItem(item);
            log.Add($"wearing {prefab}: {(ok ? "yes" : "NO")}");
            if (!ok) errors.Add($"could not wear {prefab}");
        }

        /// <summary>Points the game camera (it follows the player's mouse-look angles, from behind and above) at a point: the
        /// crosshair, and so the player's hover and the hammer, land on it. A few frames, as the camera moves with the angles.</summary>
        private static IEnumerator Aim(Player p, Vector3 target)
        {
            for (int i = 0; i < 45; i++)
            {
                var cam = GameCamera.instance != null ? GameCamera.instance.transform.position : p.transform.position + Vector3.up * 1.6f;
                Vector3 dir = (target - cam).normalized;
                var flat = new Vector3(dir.x, 0f, dir.z);
                Traverse.Create(p).Field("m_lookYaw").SetValue(Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat : p.transform.forward));
                Traverse.Create(p).Field("m_lookPitch").SetValue(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg);
                p.SetLookDir(dir, 0f);
                yield return null;
            }
        }

        private IEnumerator Dismount(Player player)
        {
            if (player.GetDoodadController() != null) player.StopDoodadControl();
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>
        /// Refunds, the game's way: a bike smashed by a player's weapon and one taken down with the hammer both drop the whole
        /// recipe loose on the ground when someone built it (a third when it came from the console); no bag appears for the
        /// bike's own materials. The hammer refuses a bike someone is riding.
        /// </summary>
        private IEnumerator CheckRefund(string key, List<string> log, List<string> errors)
        {
            var def = BikeDefinition.Find(key); var player = Player.m_localPlayer;
            if (def == null || def.Prefab == null || player == null) { errors.Add($"refund: no bike '{key}'"); yield break; }
            var pickup = Traverse.Create(typeof(Player)).Field("m_enableAutoPickup");
            bool pickupWas = pickup.GetValue<bool>(); pickup.SetValue(false);
            long me = Game.instance.GetPlayerProfile().GetPlayerID();
            int recipe = def.Requirements.Sum(r => r.Amount), third = def.Requirements.Sum(r => Mathf.Max(1, r.Amount / 3));
            var cases = new[] { ("built, smashed with a weapon", me, recipe, true), ("built, hammer", me, recipe, false), ("console-spawned, smashed", 0L, third, true) };
            foreach (var (name, creator, expected, smash) in cases)
            {
                ClearBikes(m_spawnAt, 25f); ClearDrops(m_spawnAt, 15f); ClearWreckBags(m_spawnAt, 20f);
                yield return new WaitForSeconds(0.5f);
                var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
                yield return new WaitForSeconds(0.8f);
                Traverse.Create(bike.GetComponent<Piece>()).Field("m_creator").SetValue(creator);
                var wear = bike.GetComponent<WearNTear>();
                Vector3 at = bike.transform.position;
                int hits = 0;
                if (smash)
                    while (bike != null && hits < 30)
                    {
                        var hit = new HitData(); hit.m_damage.m_slash = 400f; hit.m_point = at + Vector3.up; hit.m_dir = Vector3.right; hit.m_attacker = player.GetZDOID();
                        wear.Damage(hit); hits++;
                        yield return new WaitForSeconds(0.25f);
                    }
                else wear.Remove();                                   // what the hammer's remove does once its checks pass
                yield return new WaitForSeconds(1.5f);
                var (bags, items, _) = WreckBags(at, 15f);
                int loose = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(d => d != null && Vector3.Distance(d.transform.position, at) < 15f).Sum(d => d.m_itemData.m_stack);
                bool gone = bike == null;
                log.Add($"refund: {def.DisplayName} {name}: {(gone ? "broke" : "STILL THERE")}{(smash ? $" after {hits} hits of 400" : "")}; {loose} items loose on the ground (expected {expected}), {bags} wreck bag(s) holding {items}");
                if (!gone) { errors.Add($"refund: {name}: the bike did not break"); bike.Remove(); }
                if (loose != expected) errors.Add($"refund: {name}: {loose} items lie loose, expected {expected}");
                if (bags > 0) errors.Add($"refund: {name}: a wreck bag appeared for the bike's materials");
            }
            // a ridden bike: the hammer may not take it
            {
                ClearBikes(m_spawnAt, 25f); ClearWreckBags(m_spawnAt, 20f);
                var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
                yield return new WaitForSeconds(0.8f);
                var piece = bike.GetComponent<Piece>();
                bool parked = piece.CanBeRemoved();
                yield return Mount(bike, player);
                bool ridden = piece.CanBeRemoved();
                yield return Dismount(player);
                log.Add($"refund: hammer allowed on a parked bike {parked}, on a ridden one {ridden}");
                if (!parked || ridden) errors.Add("refund: the hammer rule for ridden bikes is wrong");
                bike.Remove();
            }
            ClearDrops(m_spawnAt, 15f); ClearWreckBags(m_spawnAt, 20f);
            pickup.SetValue(pickupWas);
        }

        /// <summary>
        /// Fuel on land: built empty (the engine will not start), Shift+E pours cores from the bag and then from the saddlebags,
        /// a core goes in from the hotbar, coal is refused, a full tank takes nothing, a rolling bike takes nothing (and offers
        /// no fill prompt) until it stands still, a tank of 15 m runs dry after about 15 m, and boost burns BoostBurn times as
        /// much per metre. The mount panel shows the tank while riding.
        /// </summary>
        private IEnumerator CheckFuel(string key, List<string> log, List<string> errors)
        {
            var def = BikeDefinition.Find(key); var player = Player.m_localPlayer;
            if (def == null || def.Prefab == null || player == null) { errors.Add($"fuel: no bike '{key}'"); yield break; }
            var pickup = Traverse.Create(typeof(Player)).Field("m_enableAutoPickup");
            bool pickupWas = pickup.GetValue<bool>(); pickup.SetValue(false);
            var inv = player.GetInventory();
            ClearBikes(m_spawnAt, 25f); ClearDrops(m_spawnAt, 15f);
            if (!m_laneCleared) { for (float x = 12f; x <= 60f; x += 8f) ClearArea(m_spawnAt + Vector3.right * x, 8f); m_laneCleared = true; }
            yield return new WaitForSeconds(0.5f);
            var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
            yield return new WaitForSeconds(0.8f);
            var fuel = bike.Fuel; float cap = BikeFuel.Capacity;
            foreach (var name in new[] { def.FuelItem, "Coal" })                  // the throwaway character may still carry some from earlier runs
                if (!string.IsNullOrEmpty(name)) { int n = CountItems(inv, name); if (n > 0) inv.RemoveItem(ObjectDB.instance.GetItemPrefab(name).GetComponent<ItemDrop>().m_itemData.m_shared.m_name, n); }
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            log.Add($"fuel: {def.DisplayName} built with {fuel.Metres:0} m in a {cap:0} m tank; hover: {bike.GetHoverText().Replace("\n", " | ")}");
            if (fuel.Metres > 0.5f) errors.Add("fuel: a new bike is not empty");

            // empty: sit on it, nothing happens
            yield return Mount(bike, player);
            Vector3 p0 = bike.transform.position; bike.m_forceThrottle = 1f;
            yield return new WaitForSeconds(2f);
            float moved = Vector3.Distance(bike.transform.position, p0);
            var hud = Hud.instance;
            string panel = hud != null ? $"panel {(hud.m_mountPanel.activeSelf ? "shown" : "HIDDEN")}, '{hud.m_mountNameText.text}', '{hud.m_mountStaminaText.text}', health bar {(hud.m_mountHealthText.gameObject.activeSelf ? "SHOWN" : "hidden")}" : "no hud";
            log.Add($"fuel: empty tank, throttle 2 s: engine {(bike.EngineOn ? "ON" : "off")}, moved {moved:0.00} m; mount {panel}");
            if (bike.EngineOn || moved > 0.5f) errors.Add("fuel: an empty bike drove");
            if (hud != null && (!hud.m_mountPanel.activeSelf || !hud.m_mountStaminaText.text.StartsWith("Fuel"))) errors.Add("fuel: the mount panel does not show the tank");
            bike.m_forceThrottle = float.NaN;
            yield return Dismount(player);

            // Shift+E: cores from the bag
            string core = def.FuelItem; float per = def.FuelMetres;
            string Name(string prefab) => ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            inv.AddItem(ObjectDB.instance.GetItemPrefab(core), 2);
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            bike.Interact(player, false, true);
            yield return new WaitForSeconds(0.2f);
            float afterBag = fuel.Metres;
            log.Add($"fuel: Shift+E with 2 {core} in the bag: {afterBag:0} m, {CountItems(inv, core)} left");
            if (Mathf.Abs(afterBag - Mathf.Min(cap, 2 * per)) > 1f || CountItems(inv, core) != 0) errors.Add("fuel: cores from the bag went wrong");

            // Shift+E: a core from the saddlebags when the bag has none
            var bags = bike.Storage;
            if (bags == null) errors.Add("fuel: no saddlebags");
            else
            {
                bags.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(core), 1);
                yield return null;
                bike.Interact(player, false, true);
                yield return new WaitForSeconds(0.2f);
                log.Add($"fuel: Shift+E with 1 {core} in the saddlebags only: {fuel.Metres:0} m, {CountItems(bags.GetInventory(), core)} left in them");
                if (Mathf.Abs(fuel.Metres - Mathf.Min(cap, afterBag + per)) > 1f) errors.Add("fuel: a core from the saddlebags went wrong");
            }

            // a core from the hotbar; wood and coal are not fuel
            {
                float before = fuel.Metres;
                inv.AddItem(ObjectDB.instance.GetItemPrefab(core), 1);
                var item = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == core);
                bool used = item != null && bike.UseItem(player, item);
                yield return new WaitForSeconds(0.2f);
                log.Add($"fuel: one {core} from the hotbar: used {used}, {before:0} -> {fuel.Metres:0} m");
                if (!used || Mathf.Abs(fuel.Metres - Mathf.Min(cap, before + per)) > 1f) errors.Add($"fuel: {core} from the hotbar went wrong");
                inv.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 1);
                var wood = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Wood");
                if (wood != null && bike.UseItem(player, wood)) errors.Add("fuel: wood was taken as fuel");
                inv.RemoveItem(wood);
                fuel.SetFraction(0.5f);
                inv.AddItem(ObjectDB.instance.GetItemPrefab("Coal"), 2);
                float half = fuel.Metres;
                bike.Interact(player, false, true);
                var lump = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Coal");
                bool tookCoal = lump != null && bike.UseItem(player, lump);
                log.Add($"fuel: 2 coal in the bag at half a tank: Shift+E left {CountItems(inv, "Coal")}, hotbar use taken {tookCoal}, fuel {half:0} -> {fuel.Metres:0} m");
                if (CountItems(inv, "Coal") != 2 || tookCoal || fuel.Metres > half + 0.5f) errors.Add("fuel: coal was taken as fuel");
                inv.RemoveItem(Name("Coal"), 2);
            }

            // a full tank takes nothing
            fuel.SetFraction(1f);
            inv.AddItem(ObjectDB.instance.GetItemPrefab(core), 1);
            bike.Interact(player, false, true);
            yield return new WaitForSeconds(0.2f);
            log.Add($"fuel: full tank, Shift+E: {CountItems(inv, core)} {core} still in the bag");
            if (CountItems(inv, core) != 1) errors.Add("fuel: a full tank took a core");
            inv.RemoveItem(Name(core), 1);

            // a rolling bike takes nothing and offers no fill prompt; stopped again, it fills
            fuel.SetFraction(0.5f);
            MoveBike(bike, m_spawnAt);
            yield return Mount(bike, player);
            bike.m_forceThrottle = 1f;
            yield return new WaitForSeconds(1.0f);
            bike.m_forceThrottle = float.NaN;
            yield return Dismount(player);
            float rolling = Mathf.Abs(bike.Speed), beforeRolling = fuel.Metres;
            inv.AddItem(ObjectDB.instance.GetItemPrefab(core), 1);
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            string hoverRolling = bike.GetHoverText().Replace("\n", " | ");
            bike.Interact(player, false, true);
            bool tookRolling = fuel.Metres > beforeRolling + 0.5f;
            log.Add($"fuel: hopped off at {rolling:0.0} m/s: hover '{hoverRolling}'; Shift+E {(tookRolling ? "FILLED" : "refused")}");
            if (rolling < 1f) errors.Add($"fuel: the bike was hardly rolling ({rolling:0.0} m/s) for the moving check");
            if (hoverRolling.Contains("Fill tank")) errors.Add("fuel: a rolling bike offers to fill the tank");
            if (tookRolling) errors.Add("fuel: a rolling bike took fuel");
            float tStop = Time.time;
            while (!bike.Standing && Time.time - tStop < 12f) yield return null;
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            string hoverStill = bike.GetHoverText().Replace("\n", " | ");
            bike.Interact(player, false, true);
            bool tookStill = fuel.Metres > beforeRolling + 0.5f;
            log.Add($"fuel: at rest {Time.time - tStop:0.0} s later ({Mathf.Abs(bike.Speed):0.0} m/s): hover '{hoverStill}'; Shift+E {(tookStill ? "filled" : "REFUSED")}");
            if (!bike.Standing) errors.Add("fuel: the bike never came to rest for the standing check");
            if (!hoverStill.Contains("Fill tank") || !tookStill) errors.Add("fuel: a standing bike refused fuel after rolling");

            // 15 m in the tank: it runs dry after about 15 m
            fuel.SetFraction(15f / cap);
            MoveBike(bike, m_spawnAt);
            yield return Mount(bike, player);
            p0 = bike.transform.position; bike.m_forceThrottle = 1f;
            float t0 = Time.time, dryAt = -1f; Vector3 dryPos = p0;
            while (Time.time - t0 < 12f)
            {
                yield return null;
                if (dryAt < 0f && !bike.EngineOn) { dryAt = Time.time - t0; dryPos = bike.transform.position; }
                if (dryAt >= 0f && Time.time - t0 > dryAt + 2f) break;
            }
            float dryDist = Vector3.Distance(Flat(dryPos), Flat(p0)), rolled = Vector3.Distance(Flat(bike.transform.position), Flat(dryPos));
            log.Add($"fuel: 15 m of fuel: engine died after {dryAt:0.0} s and {dryDist:0.0} m, then rolled {rolled:0.0} m; fuel {fuel.Metres:0.0} m");
            if (dryAt < 0f) errors.Add($"fuel: the engine never died (stuck after {Vector3.Distance(Flat(bike.transform.position), Flat(p0)):0.0} m?)");
            else if (dryDist < 12f || dryDist > 19f) errors.Add($"fuel: 15 m of fuel lasted {dryDist:0.0} m");
            bike.m_forceThrottle = float.NaN;
            yield return Dismount(player);

            // boost burns 1.5 times as much per metre: two short runs from the start line (the cleared lane is 60 m)
            fuel.SetFraction(1f);
            float Rate(float burnt, float metres) => metres > 1f ? burnt / metres : 0f;
            float plain = 0f, boosted = 0f;
            for (int run = 0; run < 2; run++)
            {
                MoveBike(bike, m_spawnAt);
                if (!ReferenceEquals(player.GetDoodadController(), bike)) yield return Mount(bike, player);
                bike.m_forceThrottle = 1f; bike.m_forceBoost = run == 1;
                yield return new WaitForSeconds(1.2f);               // up to speed
                float f1 = fuel.Metres; Vector3 q1 = bike.transform.position;
                yield return new WaitForSeconds(1.0f);
                float rate = Rate(f1 - fuel.Metres, Vector3.Distance(Flat(bike.transform.position), Flat(q1)));
                if (run == 0) plain = rate; else boosted = rate;
                bike.m_forceThrottle = -1f; bike.m_forceBoost = false;  // brake
                yield return new WaitForSeconds(1.5f);
            }
            bike.m_forceThrottle = float.NaN;
            log.Add($"fuel: burn per metre driven: {plain:0.00} plain, {boosted:0.00} with boost (x{(plain > 0f ? boosted / plain : 0f):0.00})");
            if (plain < 0.8f || plain > 1.25f) errors.Add($"fuel: plain burn {plain:0.00} per metre");
            if (plain > 0f && Mathf.Abs(boosted / plain - MotorModPlugin.BoostBurn.Value) > 0.25f) errors.Add($"fuel: boost burns x{boosted / plain:0.00}");
            yield return Dismount(player);
            bike.Remove();
            ClearDrops(m_spawnAt, 15f);
            pickup.SetValue(pickupWas);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The flaming skull icons rendered again with "yaw,pitch,fill,flames" (empty fields keep the value): 256 px PNGs beside the pictures, and the items get the 128 px ones.</summary>
        private static void RenderIcons(string spec, List<string> pics, List<string> log, List<string> errors)
        {
            var v = spec.Split(',');
            try
            {
                float F(int i, float keep) => v.Length > i && v[i].Trim() != "" ? float.Parse(v[i], System.Globalization.CultureInfo.InvariantCulture) : keep;
                SkullIcon.Yaw = F(0, SkullIcon.Yaw); SkullIcon.Pitch = F(1, SkullIcon.Pitch); SkullIcon.Fill = F(2, SkullIcon.Fill); SkullIcon.Flames = F(3, SkullIcon.Flames);
            }
            catch (System.Exception e) { errors.Add("icons: bad settings: " + e.Message); return; }
            int n = 0;
            foreach (var name in FlamingSkulls.Prefabs)
            {
                var item = Jotunn.Managers.PrefabManager.Instance.GetPrefab(name);
                var big = item != null ? SkullIcon.Render(item, 256) : null;
                if (big == null) { errors.Add($"icons: {name} did not render"); continue; }
                string path = System.IO.Path.Combine(HarnessMode.Dir, $"icon_{name}.png");
                System.IO.File.WriteAllBytes(path, big.texture.EncodeToPNG()); pics.Add(path);
                var small = SkullIcon.Render(item, 128);
                if (small != null) item.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons = new[] { small };
                n++;
            }
            log.Add($"icons: yaw {SkullIcon.Yaw} pitch {SkullIcon.Pitch} fill {SkullIcon.Fill} flames {SkullIcon.Flames} s: {n} rendered");
        }

        /// <summary>How far each hand's knuckle is from the surface of the steering part (bars, grips, fork; not the front wheel), cm.</summary>
        private static string BarGaps(MotorcycleController bike, RiderPose pose)
        {
            if (bike.SteerPart == null || pose == null) return "bar gap n/a";
            var tris = new List<Vector3>();
            foreach (var mf in bike.SteerPart.GetComponentsInChildren<MeshFilter>())
            {
                if (bike.m_wheelFront && mf.transform.IsChildOf(bike.m_wheelFront)) continue;
                var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
                var v = mesh.vertices; var ix = mesh.triangles; var m = mf.transform.localToWorldMatrix;
                foreach (int i in ix) tris.Add(m.MultiplyPoint3x4(v[i]));
            }
            string Gap(Transform palm)
            {
                if (!palm || tris.Count == 0) return "n/a";
                float best = float.MaxValue; Vector3 at = palm.position;
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    Vector3 q = ClosestOnTriangle(palm.position, tris[i], tris[i + 1], tris[i + 2]); float d = (q - palm.position).sqrMagnitude;
                    if (d < best) { best = d; at = q; }
                }
                Vector3 g = (bike.BikeFrame != null ? bike.BikeFrame : bike.transform).InverseTransformVector(at - palm.position) * 100f;
                return $"{Mathf.Sqrt(best) * 100:0.0} (surface at side {g.x:+0.0;-0.0} up {g.y:+0.0;-0.0} fwd {g.z:+0.0;-0.0})";
            }
            return $"knuckle to bar surface cm: L {Gap(pose.LeftPalm)} R {Gap(pose.RightPalm)}";
        }

        /// <summary>The point of triangle abc nearest to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
            float den = 1f / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }

        /// <summary>
        /// The saddlebags: two slots, the player's look finds them (their trigger boxes), E opens them, they stay shut while
        /// someone rides, and a broken bike spills them into the wreck saddlebag.
        /// </summary>
        private IEnumerator CheckStorage(string key, List<string> log, List<string> errors)
        {
            var def = BikeDefinition.Find(key); var player = Player.m_localPlayer;
            if (def == null || def.Prefab == null || player == null) { errors.Add($"storage: no bike '{key}'"); yield break; }
            ClearBikes(m_spawnAt, 25f); ClearWreckBags(m_spawnAt, 20f);
            yield return new WaitForSeconds(0.5f);
            var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
            yield return new WaitForSeconds(0.8f);
            var bags = bike.Storage;
            if (bags == null) { errors.Add("storage: no saddlebags"); bike.Remove(); yield break; }
            var inv = bags.GetInventory();
            log.Add($"storage: {def.DisplayName} saddlebags {inv.GetWidth()}x{inv.GetHeight()}, raycasts hit triggers: {Physics.queriesHitTriggers}");
            if (inv.GetWidth() * inv.GetHeight() != 2) errors.Add("storage: not two slots");
            // the player's look: from beside the bike at eye height straight at the right bag, through the game's interact layers
            var box = bike.m_bags.GetComponents<BoxCollider>()[1];
            Vector3 target = box.bounds.center, eye = target + bike.transform.right * 1.6f + Vector3.up * 0.4f;
            int mask = LayerMask.GetMask("item", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "character_net", "terrain", "vehicle", "character_ghost");
            var hits = Physics.RaycastAll(eye, (target - eye).normalized, 4f, mask).OrderBy(h => h.distance).ToArray();
            string first = hits.Length > 0 ? hits[0].collider.gameObject.name : "nothing";
            var hover = hits.Length > 0 ? hits[0].collider.GetComponent<Hoverable>() : null;
            log.Add($"storage: looking at the right bag hits {first}; hover text: {(hover != null ? hover.GetHoverText().Replace("\n", " | ") : "-")}");
            if (!(hover is Container)) errors.Add("storage: looking at the bag does not find the saddlebags");
            // E opens them
            PlacePlayer(player, bike.transform.position + bike.transform.right * 1.4f, Quaternion.LookRotation(-bike.transform.right));
            yield return null;
            bags.Interact(player, false, false);
            yield return new WaitForSeconds(0.6f);
            bool open = InventoryGui.instance != null && InventoryGui.instance.IsContainerOpen();
            log.Add($"storage: E on the bags: open {open}");
            if (!open) errors.Add("storage: E did not open the saddlebags");
            if (InventoryGui.instance != null) InventoryGui.instance.Hide();
            yield return new WaitForSeconds(0.5f);
            // shut while ridden
            yield return Mount(bike, player);
            var rider = player;
            bags.Interact(rider, false, false);
            yield return new WaitForSeconds(0.5f);
            bool openRidden = InventoryGui.instance != null && InventoryGui.instance.IsContainerOpen();
            log.Add($"storage: bags while ridden: open {openRidden}");
            if (openRidden) errors.Add("storage: the bags opened under a rider");
            if (InventoryGui.instance != null) InventoryGui.instance.Hide();
            yield return Dismount(player);
            // a broken bike spills them into the wreck bag
            inv.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 7);
            Vector3 at = bike.transform.position;
            bike.GetComponent<WearNTear>().Remove();
            yield return new WaitForSeconds(1.5f);
            var wreck = WreckBags(at, 15f);
            int wood = wreck.list.Sum(k => CountItems(k.GetInventory(), "Wood"));
            log.Add($"storage: broken with 7 wood in the bags: {wreck.bags} wreck bag(s), {wood} wood in them");
            if (wood != 7) errors.Add("storage: the saddlebags' contents were lost");
            ClearWreckBags(m_spawnAt, 20f); ClearDrops(m_spawnAt, 15f);
        }

        /// <summary>
        /// What a player sees, as screenshots of the game window (HUD included): the hover text on a bike with fuel in it, the
        /// hammer's condition bar on a damaged bike, the mount panel as the fuel gauge while riding, and the materials lying on the
        /// ground once the hammer takes it down.
        /// </summary>
        private IEnumerator CheckUi(string key, List<string> pics, List<string> log, List<string> errors)
        {
            var def = BikeDefinition.Find(key); var player = Player.m_localPlayer;
            if (def == null || def.Prefab == null || player == null) { errors.Add($"ui: no bike '{key}'"); yield break; }
            ClearBikes(m_spawnAt, 25f); ClearWreckBags(m_spawnAt, 20f);
            yield return new WaitForSeconds(0.5f);
            var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
            yield return new WaitForSeconds(0.8f);
            Traverse.Create(bike.GetComponent<Piece>()).Field("m_creator").SetValue(Game.instance.GetPlayerProfile().GetPlayerID());
            bike.Fuel.SetFraction(0.64f);
            var wear = bike.GetComponent<WearNTear>();
            var hit = new HitData(); hit.m_damage.m_blunt = 0.4f * bike.GetComponent<BikeHealth>().Max; hit.m_point = bike.transform.position + Vector3.up;
            wear.Damage(hit);                                         // down to 60 %: the bar shows it
            // stand off the bike's side, looking at its middle
            Vector3 seat = bike.transform.position + Vector3.up * 0.55f;
            Vector3 stand = seat + bike.transform.right * 1.7f; stand.y = bike.transform.position.y;
            PlacePlayer(player, stand, Quaternion.LookRotation(-bike.transform.right));
            yield return new WaitForSeconds(1.0f);
            yield return Aim(player, seat);
            yield return new WaitForSeconds(0.3f);
            string Shot(string name)
            {
                var path = System.IO.Path.Combine(HarnessMode.Dir, $"{def.Key}_ui_{name}.png");
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                ScreenCapture.CaptureScreenshot(path); pics.Add(path); return path;
            }
            Shot("hover");
            log.Add($"ui: hovering {(player.GetHoverObject() != null ? player.GetHoverObject().name : "nothing")}; bike health {bike.GetComponent<BikeHealth>().Fraction * 100f:0}%, fuel {bike.Fuel.Percent}");
            yield return new WaitForSeconds(0.5f);
            // the hammer: the game's condition bar and highlight
            var inv = player.GetInventory();
            if (CountItems(inv, "Hammer") == 0) inv.AddItem(ObjectDB.instance.GetItemPrefab("Hammer"), 1);
            var hammer = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Hammer");
            if (hammer != null) player.EquipItem(hammer);
            yield return new WaitForSeconds(1.0f);
            yield return Aim(player, seat);
            yield return new WaitForSeconds(0.4f);
            var hud = Hud.instance;
            bool bar = hud != null && hud.m_pieceHealthRoot != null && hud.m_pieceHealthRoot.gameObject.activeInHierarchy;
            Shot("hammer");
            log.Add($"ui: hammer in hand: in place mode {player.InPlaceMode()}, hovering piece {(player.GetHoveringPiece() != null ? player.GetHoveringPiece().name : "none")}, condition bar shown {bar}");
            if (!bar) errors.Add("ui: the hammer shows no condition bar on the bike");
            yield return new WaitForSeconds(0.5f);
            // the hammer's repair tool
            float before = bike.GetComponent<BikeHealth>().Fraction;
            bool repaired = wear.Repair();
            yield return new WaitForSeconds(0.5f);
            log.Add($"ui: the hammer's repair: {(repaired ? "repaired" : "REFUSED")}, health {before * 100f:0}% -> {bike.GetComponent<BikeHealth>().Fraction * 100f:0}%");
            if (bike.GetComponent<BikeHealth>().Fraction < 0.999f) errors.Add("ui: the hammer's repair did not repair the bike");
            if (hammer != null) player.UnequipItem(hammer);
            yield return new WaitForSeconds(0.5f);
            // riding: the mount panel as the fuel gauge
            yield return Mount(bike, player);
            player.SetLookDir(bike.transform.forward, 0f);
            yield return new WaitForSeconds(1.5f);
            Shot("riding");
            yield return Dismount(player);
            // the materials on the ground once the hammer takes it down (no bag for them)
            var pickup = Traverse.Create(typeof(Player)).Field("m_enableAutoPickup");
            bool pickupWas = pickup.GetValue<bool>(); pickup.SetValue(false);
            Vector3 at = bike.transform.position;
            var drops0 = DropsNow();
            wear.Remove();
            yield return new WaitForSeconds(2.5f);
            var loose = LooseItems(at, 15f, drops0);
            int bags = WreckBags(at, 15f).bags;
            log.Add($"ui: hammered: {loose.items} items in {loose.piles} piles on the ground ({loose.what}), {bags} wreck bag(s)");
            if (bags > 0) errors.Add("ui: a wreck bag appeared for the bike's materials");
            {
                var path = System.IO.Path.Combine(HarnessMode.Dir, $"{def.Key}_wreck_land.png");
                try { HarnessCamera.Shoot(path, at + new Vector3(2.4f, 1.8f, 2.4f), at + Vector3.up * 0.2f, 1280, 800, 45f); pics.Add(path); } catch (System.Exception e) { errors.Add("ui: wreck shot " + e.Message); }
                PlacePlayer(player, at + new Vector3(2.2f, 0f, 0f), Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.6f);
                yield return Aim(player, at + Vector3.up * 0.2f);
                yield return new WaitForSeconds(0.4f);
                Shot("wreck_hover");
            }
            ClearDrops(m_spawnAt, 15f); ClearWreckBags(m_spawnAt, 20f);
            pickup.SetValue(pickupWas);
        }

        /// <summary>
        /// Running dry at sea: up on the plane the tank empties, the throttle dies, the bike slows, settles, swamps and breaks
        /// apart before anyone could fill it (filling needs a standing bike, and a standing bike swamps within SinkSeconds).
        /// The rider ends up swimming and the materials go down, loose, with no bag.
        /// </summary>
        private IEnumerator SeaDryTest(BikeDefinition def, MotorcycleController bike, Player player, List<string> log, List<string> errors)
        {
            Traverse.Create(bike.GetComponent<Piece>()).Field("m_creator").SetValue(Game.instance.GetPlayerProfile().GetPlayerID());   // as if built: the whole recipe drops
            bike.m_noSink = true;                                      // held up until it planes: a standing start on deep water is fatal by design
            bike.m_forceThrottle = 1f; bike.m_forceBoost = true;
            yield return new WaitForSeconds(3f);
            bike.m_noSink = false;
            yield return new WaitForSeconds(1f);
            float planing = bike.SinkDepth, speedPlaning = Mathf.Abs(bike.Speed);
            var before = DropsNow();
            bike.Fuel.SetFraction(30f / BikeFuel.Capacity);
            float t0 = Time.time;
            while (bike != null && bike.EngineOn && Time.time - t0 < 15f) yield return null;
            float dryAfter = Time.time - t0, speedDry = bike != null ? Mathf.Abs(bike.Speed) : 0f;
            Vector3 at = bike != null ? bike.transform.position : player.transform.position;
            float swampAt = -1f, brokeAt = -1f, next = 0f; var depth = new List<string>();
            while (bike != null && Time.time - t0 - dryAfter < 20f)
            {
                yield return null;
                if (bike == null) { brokeAt = Time.time - t0 - dryAfter; break; }
                at = bike.transform.position;
                if (swampAt < 0f && bike.Swamped) swampAt = Time.time - t0 - dryAfter;
                if (Time.time >= next) { next = Time.time + 0.5f; depth.Add($"{bike.SinkDepth:0.00}@{bike.Speed:0.0}"); }
            }
            bool gone = bike == null;
            yield return new WaitForSeconds(4f);                      // the materials settle on the bottom
            var loose = LooseItems(at, 15f, before);
            int bags = WreckBags(at, 15f).bags, recipe = def.Requirements.Sum(r => r.Amount);
            log.Add($"{def.Key}: dry at sea: planing at {speedPlaning:0.0} m/s, {planing:0.00} m deep; engine died after {dryAfter:0.0} s at {speedDry:0.0} m/s; swamped {swampAt:0.0} s later, {(gone ? $"broke apart at {brokeAt:0.0} s" : "STILL AFLOAT after 20 s")} (depth@speed every 0.5 s: {string.Join(" ", depth)}); rider swimming {player.IsSwimming()}, holds the controls {player.GetDoodadController() != null}; {loose.items} items (recipe {recipe}) in {loose.piles} piles near the spot ({loose.what}), {loose.underWater:0.0} m under the surface on average, {bags} wreck bag(s)");
            if (planing > 0.05f) errors.Add($"{def.Key}: it settled while planing under boost ({planing:0.00} m)");
            if (!gone) errors.Add($"{def.Key}: the bike that ran dry at sea did not sink");
            if (player.GetDoodadController() != null) errors.Add($"{def.Key}: the rider still holds the controls after the bike broke");
            if (gone && loose.items != recipe) errors.Add($"{def.Key}: {loose.items} of {recipe} items went down");
            if (bags > 0) errors.Add($"{def.Key}: a wreck bag came up for the bike's materials");
        }

        /// <summary>
        /// The damage look: a fresh bike is clean; below 75 % it is sooty and the owner lets off a smoke burst with a crunch; below
        /// 25 % it is sootier still and smokes from the engine; repaired, it is clean again. A picture of each state.
        /// </summary>
        private IEnumerator CheckCondition(string key, List<string> pics, List<string> log, List<string> errors)
        {
            var def = BikeDefinition.Find(key); var player = Player.m_localPlayer;
            if (def == null || def.Prefab == null || player == null) { errors.Add($"condition: no bike '{key}'"); yield break; }
            ClearBikes(m_spawnAt, 25f);
            yield return new WaitForSeconds(0.5f);
            var bike = Instantiate(def.Prefab, m_spawnAt, Quaternion.LookRotation(Vector3.right)).GetComponent<MotorcycleController>();
            yield return new WaitForSeconds(0.8f);
            var cond = bike.GetComponent<BikeCondition>(); var health = bike.GetComponent<BikeHealth>(); var wear = bike.GetComponent<WearNTear>();
            if (cond == null || health == null || wear == null) { errors.Add("condition: the bike lacks BikeCondition, BikeHealth or WearNTear"); bike.Remove(); yield break; }
            PlacePlayer(player, bike.transform.position - bike.transform.right * 3f, Quaternion.LookRotation(bike.transform.right));   // out of the pictures
            float Tint()
            {
                var r = bike.m_visual != null ? bike.m_visual.GetComponent<MeshRenderer>() : null;
                if (r == null || r.sharedMaterials.Length == 0 || r.sharedMaterials[0] == null) return -1f;
                var b = new MaterialPropertyBlock(); r.GetPropertyBlock(b, 0);
                return b.isEmpty ? 1f : b.GetColor("_Color").r / Mathf.Max(0.01f, r.sharedMaterials[0].GetColor("_Color").r);
            }
            int Bursts() => UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Count(v => v != null && v.gameObject.name.StartsWith("vfx_Damaged_Karve") && Vector3.Distance(v.transform.position, bike.transform.position) < 6f);
            void Shot(string name)
            {
                var at = bike.transform.position; var path = System.IO.Path.Combine(HarnessMode.Dir, $"{def.Key}_cond_{name}.png");
                try { HarnessCamera.Shoot(path, at + new Vector3(2.6f, 1.3f, 2.6f), at + Vector3.up * 0.6f, 1280, 800, 40f); pics.Add(path); } catch (System.Exception e) { errors.Add("condition: shot " + e.Message); }
            }
            IEnumerator Damage(float amount) { var hit = new HitData(); hit.m_damage.m_blunt = amount; hit.m_point = bike.transform.position + Vector3.up; wear.Damage(hit); yield return new WaitForSeconds(0.35f); }
            log.Add($"condition: fresh: state {cond.Current}, tint {Tint():0.00}, smoking {cond.Smoking}; hover: {bike.GetHoverText().Replace("\n", " | ")}");
            if (cond.Current != BikeCondition.State.Fresh || Tint() < 0.99f) errors.Add("condition: a fresh bike is not clean");
            Shot("fresh");
            yield return Damage(0.4f * health.Max);                      // down to 60 %: worn
            int bursts = Bursts(); float tint = Tint();
            log.Add($"condition: at {health.Fraction * 100f:0}%: state {cond.Current}, tint {tint:0.00}, smoke bursts near {bursts}, smoking {cond.Smoking}");
            if (cond.Current != BikeCondition.State.Worn) errors.Add($"condition: 60 % is {cond.Current}, expected Worn");
            if (tint > 0.8f || tint < 0.6f) errors.Add($"condition: worn tint {tint:0.00}");
            if (bursts < 1) errors.Add("condition: no smoke burst at the step to worn");
            yield return new WaitForSeconds(1.5f);
            Shot("worn");
            yield return Damage(0.4f * health.Max);                      // down to 20 %: wrecked
            bursts = Bursts(); tint = Tint();
            yield return new WaitForSeconds(2.5f);
            int smokes = cond.SmokeParticles;
            log.Add($"condition: at {health.Fraction * 100f:0}%: state {cond.Current}, tint {tint:0.00}, smoke bursts near {bursts}, smoking {cond.Smoking}, {smokes} smoke particles over the engine after 2.5 s; hover: {bike.GetHoverText().Replace("\n", " | ")}");
            if (cond.Current != BikeCondition.State.Wrecked) errors.Add($"condition: 20 % is {cond.Current}, expected Wrecked");
            if (tint > 0.55f || tint < 0.35f) errors.Add($"condition: wrecked tint {tint:0.00}");
            if (bursts < 1) errors.Add("condition: no smoke burst at the step to wrecked");
            if (smokes < 3) errors.Add($"condition: a wrecked bike hardly smokes ({smokes} particles)");
            Shot("wrecked");
            health.Repair();
            yield return new WaitForSeconds(0.5f);
            log.Add($"condition: repaired: state {cond.Current}, tint {Tint():0.00}, smoking {cond.Smoking}");
            if (cond.Current != BikeCondition.State.Fresh || Tint() < 0.99f || cond.Smoking) errors.Add("condition: a repaired bike is not clean again");
            bike.Remove();
        }

        /// <summary>Every loose item drop in the loaded world right now, to tell a bike's drops from whatever already lay there.</summary>
        private static HashSet<int> DropsNow() => new HashSet<int>(UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Select(d => d.GetInstanceID()));

        /// <summary>Loose item drops near a point (flat distance) that were not there before: piles, items (stacks counted), how far under the
        /// water surface they lie on average, and what they are.</summary>
        private static (int piles, int items, float underWater, string what) LooseItems(Vector3 c, float r, HashSet<int> before)
        {
            var drops = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                .Where(d => d != null && (before == null || !before.Contains(d.GetInstanceID())) && Vector3.Distance(Flat(d.transform.position), Flat(c)) < r).ToList();
            float under = drops.Count > 0 ? drops.Average(d => Floating.GetLiquidLevel(d.transform.position, 1f, LiquidType.Water) - d.transform.position.y) : 0f;
            string what = string.Join(", ", drops.GroupBy(d => d.m_itemData.m_shared.m_name).Select(g => $"{Localization.instance.Localize(g.Key)} x{g.Sum(d => d.m_itemData.m_stack)}"));
            return (drops.Count, drops.Sum(d => d.m_itemData.m_stack), under, what);
        }

        /// <summary>Where the bags went, and whether any part of the bike pokes into a bag (vertices inside its box).</summary>
        private static void BagReport(BikeDefinition def, MotorcycleController bike, List<string> log, List<string> errors)
        {
            if (bike.m_bags == null) { errors.Add($"{def.Key}: no saddlebags"); return; }
            var boxes = bike.m_bags.GetComponents<BoxCollider>();
            var at = bike.BagsAt;
            var inside = new List<string>();
            foreach (var b in boxes)
            {
                var c = b.center; var s = b.size * 0.5f - Vector3.one * 0.01f;   // in the bike's frame at rest, like the measured points
                int n = BikePrefabSetup.PointsInside(bike, new Bounds(c, s * 2f));
                inside.Add(n.ToString());
                if (n > 0) errors.Add($"{def.Key}: {n} vertices of the bike inside a saddlebag at {c}");
            }
            Vector3 axle = bike.m_wheelRear != null ? bike.transform.InverseTransformPoint(bike.m_wheelRear.position) : Vector3.zero;
            log.Add($"{def.Key}: saddlebags back face {at.x:0.000} m out, centre {at.y:0.000} m up, {at.z - axle.z:+0.000;-0.000} m from the rear axle; bike vertices inside them: {string.Join("/", inside)}");
        }
    }
}
