using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MotorMod
{
    /// <summary>Adds the game-side components to a bundle prefab (visuals, markers, light) so it becomes a networked vehicle and a buildable piece.</summary>
    internal static class BikePrefabSetup
    {
        /// <summary>The saddlebag model's size at scale 1: depth (x, out from the bike), height (y), width (z, along the bike).</summary>
        public static Vector3 BagSize;
        /// <summary>The model's box in its own space: its back is at x = 0, and Unity's import mirrors x, so its front may face -x.</summary>
        public static Bounds BagBounds;

        public static void Configure(GameObject prefab, AssetBundle bundle, BikeDefinition def, GameObject saddlebag, Sprite icon)
        {
            int layer = LayerMask.NameToLayer("vehicle"); if (layer < 0) layer = LayerMask.NameToLayer("piece");
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            if (def.Scale.Value != 1f) prefab.transform.localScale = Vector3.one * def.Scale.Value;

            var body = prefab.GetComponent<Rigidbody>() ?? prefab.AddComponent<Rigidbody>();
            body.mass = 320f; body.linearDamping = 0.05f; body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            var nview = prefab.GetComponent<ZNetView>() ?? prefab.AddComponent<ZNetView>();
            nview.m_persistent = true; nview.m_distant = false; nview.m_type = ZDO.ObjectType.Default;
            var sync = prefab.GetComponent<ZSyncTransform>() ?? prefab.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = true;

            var bike = prefab.GetComponent<MotorcycleController>() ?? prefab.AddComponent<MotorcycleController>();
            bike.m_attachPoint = FindDeep(prefab.transform, "SeatAttach");
            bike.m_visual = prefab.transform.Find("Visual");
            bike.m_wheelFront = FindDeep(prefab.transform, "WheelFront");
            bike.m_wheelRear = FindDeep(prefab.transform, "WheelRear");
            bike.m_gripLeft = FindDeep(prefab.transform, "GripLeft");
            bike.m_gripRight = FindDeep(prefab.transform, "GripRight");
            bike.m_headlight = FindDeep(prefab.transform, "Headlight");
            bike.m_hoverText = def.DisplayName;
            bike.m_definition = def;

            var piece = prefab.GetComponent<Piece>() ?? prefab.AddComponent<Piece>();
            piece.m_name = def.DisplayName; piece.m_description = def.Description; piece.m_icon = icon;
            piece.m_groundOnly = false; piece.m_allowedInDungeons = false; piece.m_clipGround = false; piece.m_noInWater = false; piece.m_canBeRemoved = true;

            // the game's own durability, set up like a boat's or cart's: no rain or support wear (it moves and leaves the ground),
            // metal, so it neither burns nor crumbles under Deep North snow; ash only wears it down to a tenth. Its hit and break
            // effects come from the game's iron pieces once those are loaded (BikeVanillaParts).
            var wear = prefab.GetComponent<WearNTear>() ?? prefab.AddComponent<WearNTear>();
            wear.m_health = MotorModPlugin.MaxHealth.Value;
            wear.m_noRoofWear = false; wear.m_noSupportWear = false; wear.m_supports = false; wear.m_staticPosition = false;
            wear.m_burnable = false; wear.m_snowDamageImmune = true; wear.m_ashDamageResist = true; wear.m_triggerPrivateArea = true;
            wear.m_autoCreateFragments = false; wear.m_materialType = WearNTear.MaterialType.Iron;

            if (prefab.GetComponent<BikeHealth>() == null) prefab.AddComponent<BikeHealth>();
            if (prefab.GetComponent<BikeFuel>() == null) prefab.AddComponent<BikeFuel>();
            if (prefab.GetComponent<BikeCondition>() == null) prefab.AddComponent<BikeCondition>();
            if (saddlebag != null) AddSaddlebags(prefab, bike, nview, saddlebag, layer);
            if (!string.IsNullOrEmpty(def.Trail)) { var trail = prefab.GetComponent<BikeTrail>() ?? prefab.AddComponent<BikeTrail>(); trail.m_kind = def.Trail; }
            if (prefab.GetComponent<BikeWake>() == null) prefab.AddComponent<BikeWake>();
            var audio = prefab.GetComponent<EngineAudio>() ?? prefab.AddComponent<EngineAudio>();
            audio.m_startupShort = bundle.LoadAsset<AudioClip>("startup_short"); audio.m_startupFull = bundle.LoadAsset<AudioClip>("startup_full");
            audio.m_idle = bundle.LoadAsset<AudioClip>("idle_loop"); audio.m_cruiseLow = bundle.LoadAsset<AudioClip>("cruise_low"); audio.m_cruiseHigh = bundle.LoadAsset<AudioClip>("cruise_high"); audio.m_cruiseFast = bundle.LoadAsset<AudioClip>("cruise_fast");
            audio.m_accel = bundle.LoadAsset<AudioClip>("accel"); audio.m_revBlips = bundle.LoadAsset<AudioClip>("rev_blips"); audio.m_shutoff = bundle.LoadAsset<AudioClip>("shutoff");

            var light = bike.m_headlight ? bike.m_headlight.GetComponent<Light>() : null;
            MotorModPlugin.Log.LogInfo($"{def.DisplayName} configured: seat={(bike.m_attachPoint ? "ok" : "MISSING")} wheels={(bike.m_wheelFront ? "F" : "-")}{(bike.m_wheelRear ? "R" : "-")} grips={(bike.m_gripLeft && bike.m_gripRight ? "ok" : "MISSING")} headlight={(light ? "ok" : "MISSING")} idle={(audio.m_idle ? audio.m_idle.length.ToString("0.0") : "MISSING")}s");
        }

        /// <summary>
        /// Two leather saddlebags (the game's chest behind them: 2 slots, one per bag) at the back of the bike. They hang under the
        /// visible bike so they lean and lift with it, in a frame that matches the bike's at rest; MotorcycleController.PlaceBags
        /// puts them just outside the bike. Their colliders are triggers: they catch the player's look, not the ground.
        /// </summary>
        private static void AddSaddlebags(GameObject prefab, MotorcycleController bike, ZNetView nview, GameObject saddlebag, int layer)
        {
            if (bike.m_visual == null) return;
            if (BagSize == Vector3.zero) { BagBounds = MeasureBag(saddlebag); BagSize = BagBounds.size; }
            var bags = new GameObject("Saddlebags") { layer = layer };
            bags.transform.SetParent(bike.m_visual, false);
            bags.transform.SetPositionAndRotation(prefab.transform.position, prefab.transform.rotation);
            for (int i = 0; i < 2; i++)
            {
                var bag = Object.Instantiate(saddlebag, bags.transform, false);
                bag.name = i == 0 ? "BagLeft" : "BagRight";
                foreach (var t in bag.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (var c in bag.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c, true);
                var box = bags.AddComponent<BoxCollider>(); box.isTrigger = true;
            }
            var container = bags.AddComponent<Container>();
            container.m_name = "Saddlebags"; container.m_width = 2; container.m_height = 1;
            container.m_privacy = Container.PrivacySetting.Public; container.m_checkGuardStone = false; container.m_autoDestroyEmpty = false;
            container.m_rootObjectOverride = nview;
            bike.m_bags = bags.transform;
        }

        /// <summary>The bag model's box from its mesh: out from its back (x), height (y), width (z).</summary>
        private static Bounds MeasureBag(GameObject saddlebag)
        {
            var b = new Bounds(); bool any = false;
            foreach (var mf in saddlebag.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = saddlebag.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            MotorModPlugin.Log.LogInfo($"Saddlebag model: {b.size.x:0.00} deep, {b.size.y:0.00} high, {b.size.z:0.00} wide (x {b.min.x:0.00} to {b.max.x:0.00})");
            return any ? b : new Bounds();
        }

        private static readonly Dictionary<string, Vector3[]> s_bikePoints = new Dictionary<string, Vector3[]>();

        /// <summary>
        /// How far the bike reaches sideways (largest |x|, bike space) within a box along it (z0..z1) and in height (y0..y1):
        /// the vertices of its body and wheels, without the bags or the headlight parts. Measured once per model.
        /// </summary>
        public static float HalfWidthAt(MotorcycleController bike, float z0, float z1, float y0, float y1)
        {
            string key = bike.gameObject.name.Replace("(Clone)", "");
            if (!s_bikePoints.TryGetValue(key, out var pts))
            {
                var list = new List<Vector3>();
                var toBike = bike.transform.worldToLocalMatrix;
                foreach (var mf in bike.m_visual.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    if ((bike.m_bags != null && mf.transform.IsChildOf(bike.m_bags)) || (bike.m_headlight != null && mf.transform.IsChildOf(bike.m_headlight))) continue;
                    var m = toBike * mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices) list.Add(m.MultiplyPoint3x4(v));
                }
                s_bikePoints[key] = pts = list.ToArray();
            }
            float w = 0f;
            foreach (var p in pts) if (p.z >= z0 && p.z <= z1 && p.y >= y0 && p.y <= y1 && Mathf.Abs(p.x) > w) w = Mathf.Abs(p.x);
            return w;
        }

        /// <summary>Test harness: how many of the bike's own vertices (bike space, at rest) lie inside a box.</summary>
        public static int PointsInside(MotorcycleController bike, Bounds box)
        {
            HalfWidthAt(bike, 0f, 0f, 0f, 0f);                       // make sure the points are measured
            return s_bikePoints.TryGetValue(bike.gameObject.name.Replace("(Clone)", ""), out var pts) ? pts.Count(box.Contains) : 0;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
