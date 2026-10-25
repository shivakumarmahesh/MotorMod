using System;
using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MotorMod
{
    /// <summary>
    /// The parts of the bikes made from the game's own objects, once those are loaded (the first time the main menu opens):
    /// the wreck saddlebag (the game's floating cargo crate wearing the leather bag), hit and break effects from the game's
    /// iron gate and the chest background for the saddlebags' window. Nothing of the game is copied into the mod's files.
    /// </summary>
    internal static class BikeVanillaParts
    {
        public const string WreckBagName = "MotorModSaddlebag";
        public static GameObject WreckBag;
        private static bool s_done;

        /// <summary>Clients see the game's prefabs at the main menu; a dedicated server, which has none, when its world scene starts.</summary>
        public static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += () => Finish(false);
            PrefabManager.OnPrefabsRegistered += () => Finish(true);
        }

        private static void Finish(bool sceneStarted)
        {
            if (s_done) return;                  // the prefabs keep what is done here for the whole session
            s_done = true;
            try { MakeWreckBag(); } catch (Exception e) { MotorModPlugin.Log.LogError("wreck saddlebag: " + e); }
            if (!sceneStarted) try { FlamingSkulls.Create(); } catch (Exception e) { MotorModPlugin.Log.LogError("flaming skulls: " + e); }   // items must exist before the game copies its item list
            if (sceneStarted && WreckBag != null && WreckBag.name == WreckBagName) PrefabManager.Instance.RegisterToZNetScene(WreckBag);   // too late for the normal registration
            var iron = PrefabManager.Instance.GetPrefab("iron_grate")?.GetComponent<WearNTear>();
            var chest = PrefabManager.Instance.GetPrefab("piece_chest_wood")?.GetComponent<Container>();
            // the bike's sounds and smoke, from the game's own parts: sparks and a clank on every hit, the Karve's smoke burst with a
            // metal crunch at each damage step and at the end, a fireball for a fatal crash (BikeCondition copies the smoke for a wrecked bike)
            var hit = Effects("vfx_HitSparks", "sfx_metalbars_hit");
            var step = Effects("vfx_Damaged_Karve", "sfx_metalbars_break");
            var destroyed = Effects("vfx_Damaged_Karve", "vfx_HitSparks", "sfx_metalbars_break");
            MotorModPlugin.CrashBreakEffect = Effects("fx_dynamite_explosion", "sfx_bombdynamite_explosion");
            foreach (var def in BikeDefinition.All)
            {
                if (def.Prefab == null) continue;
                var wear = def.Prefab.GetComponent<WearNTear>();
                if (wear != null)
                {
                    wear.m_hitEffect = hit; wear.m_switchEffect = step; wear.m_destroyedEffect = destroyed;
                    if (iron != null) { wear.m_hitNoise = iron.m_hitNoise; wear.m_destroyNoise = iron.m_destroyNoise; }
                }
                var bike = def.Prefab.GetComponent<MotorcycleController>();
                var bags = bike != null && bike.m_bags != null ? bike.m_bags.GetComponent<Container>() : null;
                if (bags != null) { bags.m_destroyedLootPrefab = WreckBag; if (chest != null) bags.m_bkg = chest.m_bkg; }
            }
            MotorModPlugin.Log.LogInfo($"Bikes finished with the game's parts: wreck bag {(WreckBag != null ? "ok" : "MISSING")}, iron noise {(iron != null ? "ok" : "MISSING")}, chest background {(chest != null ? "ok" : "MISSING")}, hit/step/destroyed effects {hit.m_effectPrefabs.Length}/{step.m_effectPrefabs.Length}/{destroyed.m_effectPrefabs.Length}, crash effect {MotorModPlugin.CrashBreakEffect.m_effectPrefabs.Length}");
        }

        /// <summary>An effect list of the game's own prefabs by name; a missing one is logged and left out.</summary>
        private static EffectList Effects(params string[] names)
        {
            var list = new List<EffectList.EffectData>();
            foreach (var n in names)
            {
                var p = PrefabManager.Instance.GetPrefab(n);
                if (p == null) { MotorModPlugin.Log.LogWarning($"effect {n} not found"); continue; }
                list.Add(new EffectList.EffectData { m_prefab = p });
            }
            return new EffectList { m_effectPrefabs = list.ToArray() };
        }

        /// <summary>
        /// The game's cargo crate (what a sunken boat's cargo comes up in: it floats, holds items, vanishes once emptied) with the
        /// leather saddlebag instead of the crate. A broken bike's saddlebags spill their contents into it. The bike's own materials
        /// never go in a bag: the game drops them loose where it broke, on the ground or down to the seabed.
        /// </summary>
        private static void MakeWreckBag()
        {
            var model = MotorModPlugin.SaddlebagPrefab;
            if (model == null) { MotorModPlugin.Log.LogWarning("No saddlebag model: wrecks drop the game's cargo crate"); WreckBag = PrefabManager.Instance.GetPrefab("CargoCrate"); return; }
            var bag = PrefabManager.Instance.CreateClonedPrefab(WreckBagName, "CargoCrate");
            if (bag == null) return;
            var old = new List<GameObject>();
            foreach (Transform child in bag.transform) old.Add(child.gameObject);
            foreach (var o in old) Object.DestroyImmediate(o);
            var lod = bag.GetComponent<LODGroup>(); if (lod != null) Object.DestroyImmediate(lod);
            float scale = 1.1f;
            Vector3 size = BikePrefabSetup.BagSize * scale;                       // depth, height, width
            var look = Object.Instantiate(model, bag.transform, false);
            look.name = "Bag"; look.transform.localScale = Vector3.one * scale;
            var c = BikePrefabSetup.BagBounds.center * scale;
            look.transform.localPosition = new Vector3(-c.x, size.y / 2f - c.y, -c.z);       // its middle over the crate's pivot, standing on it
            foreach (var t in look.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = bag.layer;
            var hit = new GameObject("collider") { layer = bag.layer };
            hit.transform.SetParent(bag.transform, false);
            var box = hit.AddComponent<BoxCollider>(); box.size = size; box.center = new Vector3(0f, size.y / 2f, 0f);
            var container = bag.GetComponent<Container>();
            container.m_name = "Saddlebag"; container.m_width = 2; container.m_height = 1;   // the saddlebags' two slots
            var floating = bag.GetComponent<Floating>();
            if (floating != null) floating.m_waterLevelOffset += 0.12f;                        // smaller than the crate: ride a little higher to stay in sight
            PrefabManager.Instance.AddPrefab(new CustomPrefab(bag, false));
            WreckBag = bag;
        }

    }
}
