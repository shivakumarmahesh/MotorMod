using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MotorMod
{
    /// <summary>
    /// Flaming skull heads to wear: the skeleton trophy's skull over the wearer's own head (hair and beard hidden, the head
    /// shrunk inside the skull), wrapped in the flames of the game's torches: orange (Hel Skull), blue (Wraith Skull) or green
    /// (Draugr Skull). Helmets with no armour, made at the workbench. Built from the game's leather helmet, skeleton trophy and
    /// torch effects once those are loaded (BikeVanillaParts).
    /// </summary>
    internal static class FlamingSkulls
    {
        private class Kind
        {
            public string Prefab, Name, Description, Effect, Fuel;
            public int FuelAmount; public Color Glow; public bool TintEyes;
        }

        private static readonly Kind[] Kinds =
        {
            new Kind { Prefab = "HelmetHelSkull", Name = "Hel Skull", Effect = "fx_Torch_Basic", Fuel = "SurtlingCore", FuelAmount = 1, Glow = new Color(1f, 0.55f, 0.2f),
                Description = "A skull to wear over your own, wrapped in the Hel Cycle's fire. Only for looks: it gives no armour." },
            new Kind { Prefab = "HelmetWraithSkull", Name = "Wraith Skull", Effect = "fx_Torch_Blue", Fuel = "GreydwarfEye", FuelAmount = 5, Glow = new Color(0.22f, 0.42f, 1f), TintEyes = true,
                Description = "A skull to wear over your own, wrapped in the Wraith Cycle's blue flames. Only for looks: it gives no armour." },
            new Kind { Prefab = "HelmetDraugrSkull", Name = "Draugr Skull", Effect = "fx_Torch_Green", Fuel = "Guck", FuelAmount = 5, Glow = new Color(0.2f, 0.9f, 0.3f), TintEyes = true,
                Description = "A skull to wear over your own, wrapped in the Draugr Cycle's green flames. Only for looks: it gives no armour." },
        };

        public static readonly List<string> Prefabs = new List<string>();

        public static void Create()
        {
            var leather = PrefabManager.Instance.GetPrefab("HelmetLeather");
            var trophy = PrefabManager.Instance.GetPrefab("TrophySkeleton");
            var skullModel = trophy != null ? trophy.transform.Find("attach/model") : null;
            if (leather == null || skullModel == null) { MotorModPlugin.Log.LogWarning("Flaming skulls: the game's leather helmet or skeleton trophy is missing"); return; }
            var trophyIcon = trophy.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons;
            foreach (var k in Kinds)
            {
                var item = PrefabManager.Instance.CreateClonedPrefab(k.Prefab, leather);
                if (item == null) continue;
                var shared = item.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_name = k.Name; shared.m_description = k.Description;
                shared.m_armor = 0f; shared.m_armorPerLevel = 0f; shared.m_maxQuality = 1; shared.m_weight = 1f; shared.m_useDurability = false;
                shared.m_helmetHideHair = ItemDrop.ItemData.HelmetHairType.Hidden; shared.m_helmetHideBeard = ItemDrop.ItemData.HelmetHairType.Hidden;
                shared.m_setName = ""; shared.m_setSize = 0; shared.m_setStatusEffect = null; shared.m_equipStatusEffect = null; shared.m_movementModifier = 0f;

                // the leather helmet's model and collider go; the skull, its flames and a glow take their place
                var attach = item.transform.Find("attach");
                var old = new List<GameObject>();
                foreach (Transform c in attach) old.Add(c.gameObject);
                foreach (var o in old) Object.DestroyImmediate(o);
                attach.localPosition = Vector3.zero; attach.localRotation = Quaternion.identity;
                var holder = new GameObject("skull");
                holder.transform.SetParent(attach, false);
                var skull = Object.Instantiate(skullModel.gameObject, holder.transform, false);
                skull.name = "model"; skull.transform.localPosition = Vector3.zero; skull.transform.localRotation = Quaternion.identity;
                foreach (var c in skull.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                if (k.TintEyes)                                       // the sockets glow in the flames' colour
                    foreach (var r in skull.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.gameObject.name.StartsWith("eye"))
                        {
                            var m = new Material(r.sharedMaterial); m.color = k.Glow;
                            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", k.Glow * 0.7f); }
                            r.sharedMaterial = m;
                        }
                AddFlames(holder.transform, k);
                var glow = new GameObject("glow").AddComponent<Light>();
                glow.transform.SetParent(holder.transform, false); glow.transform.localPosition = new Vector3(0f, 0.12f, 0.05f);
                glow.type = LightType.Point; glow.color = k.Glow; glow.range = 2.6f; glow.intensity = 0.9f; glow.shadows = LightShadows.None;
                var pickup = new GameObject("collider") { layer = item.layer };
                pickup.transform.SetParent(attach, false);
                var box = pickup.AddComponent<BoxCollider>(); box.size = new Vector3(0.24f, 0.3f, 0.3f);
                holder.AddComponent<SkullHead>();

                var icon = SkullIcon.Render(item, 128) ?? (trophyIcon != null && trophyIcon.Length > 0 ? trophyIcon[0] : null);
                if (icon != null) shared.m_icons = new[] { icon };
                ItemManager.Instance.AddItem(new CustomItem(item, false, new ItemConfig
                {
                    Name = k.Name, Description = k.Description, CraftingStation = CraftingStations.Workbench,
                    Requirements = new[] { new RequirementConfig("TrophySkeleton", 1, 0, true), new RequirementConfig(k.Fuel, k.FuelAmount, 0, true), new RequirementConfig("LeatherScraps", 2, 0, true) },
                    Icons = icon != null ? new[] { icon } : null,
                }));
                Prefabs.Add(k.Prefab);
            }
            MotorModPlugin.Log.LogInfo($"Flaming skulls: {string.Join(", ", Prefabs)}");
        }

        /// <summary>
        /// A torch's flames (the game's plain, blue or green torch) on the skull, in two layers: a crown that stays on the skull,
        /// and a short trail left in the air that streams behind a moving wearer. Purely local copies, only their particles.
        /// </summary>
        private static void AddFlames(Transform holder, Kind k)
        {
            var src = BikeTrail.Effect(k.Effect);
            if (src == null) { MotorModPlugin.Log.LogWarning($"{k.Name}: torch effect {k.Effect} missing"); return; }
            foreach (var trail in new[] { false, true })
            {
                ZNetView.m_forceDisableInit = true;
                GameObject go;
                try { go = Object.Instantiate(src); }
                finally { ZNetView.m_forceDisableInit = false; }
                var keep = go.GetComponent<ParticleSystem>();
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (!(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer)) Object.DestroyImmediate(c);
                var others = new List<GameObject>();
                foreach (Transform t in go.transform) others.Add(t.gameObject);       // the torch's glow sprite: the skull has its own light
                foreach (var o in others) Object.DestroyImmediate(o);
                go.name = trail ? "flames_trail" : "flames";
                go.transform.SetParent(holder, false);
                go.transform.localPosition = new Vector3(0f, 0.06f, -0.03f); go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                if (keep == null) continue;
                var main = keep.main; main.loop = true; main.playOnAwake = true;
                main.simulationSpace = trail ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                main.startSize = new ParticleSystem.MinMaxCurve(trail ? 0.2f : 0.26f, trail ? 0.34f : 0.46f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(trail ? 0.25f : 0.5f, trail ? 0.4f : 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.35f);
                var shape = keep.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.1f;
                var em = keep.emission; em.enabled = true; em.rateOverDistance = 0f; em.rateOverTime = trail ? 30f : 55f;
                var iv = keep.inheritVelocity; iv.enabled = false;
            }
        }

    }

    /// <summary>
    /// The hammer-menu and inventory icon of a flaming skull: the skull three-quarter from the front and a little from above, its
    /// flames burning, on a clear background. A camera of its own renders a copy on the game's spare layer 31 (the layer Jötunn's
    /// icon renderer uses), so nothing else of the world gets in. The harness can re-render with other settings (--icons).
    /// </summary>
    internal static class SkullIcon
    {
        public static float Yaw = 35f, Pitch = 15f, Fill = 0.56f, Flames = 2f;   // camera turn and rise (degrees), skull height as a share of the frame, seconds of flames simulated
        private const int Layer = 31;
        private const float Fov = 30f;

        public static Sprite Render(GameObject item, int size)
        {
            var holder = item.transform.Find("attach/skull");
            if (holder == null) return null;
            Camera cam = null; Light light = null; GameObject clone = null; RenderTexture rt = null;
            var was = RenderTexture.active; bool fog = RenderSettings.fog;
            try
            {
                clone = Object.Instantiate(holder.gameObject);
                clone.transform.position = new Vector3(0f, 1500f, 0f); clone.transform.rotation = Quaternion.identity; clone.transform.localScale = Vector3.one;
                foreach (var c in clone.GetComponentsInChildren<Component>(true)) if (c is MonoBehaviour || c is Collider) Object.DestroyImmediate(c);
                var trail = clone.transform.Find("flames_trail"); if (trail != null) Object.DestroyImmediate(trail.gameObject);   // the crown only
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                var b = new Bounds(); bool any = false;                                // frame the skull itself: the flames rise out of the top
                foreach (var r in clone.GetComponentsInChildren<MeshRenderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                if (!any) return null;
                float half = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
                float dist = half / Mathf.Clamp(Fill, 0.1f, 1.5f) / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                Vector3 look = b.center + Vector3.up * half * 0.3f;
                Vector3 dir = Quaternion.Euler(-Pitch, Yaw, 0f) * Vector3.forward;    // in front of the face (+z), turned by Yaw, raised by Pitch
                cam = new GameObject("SkullIconCamera").AddComponent<Camera>();
                cam.transform.position = look + dir * dist; cam.transform.LookAt(look);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << Layer; cam.fieldOfView = Fov; cam.nearClipPlane = 0.01f; cam.farClipPlane = 50f; cam.allowHDR = false; cam.allowMSAA = false;
                light = new GameObject("SkullIconLight").AddComponent<Light>();
                light.type = LightType.Directional; light.cullingMask = 1 << Layer; light.intensity = 1.1f; light.color = new Color(1f, 0.96f, 0.9f);
                light.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(20f, -35f, 0f) * (look - cam.transform.position));   // from the camera's upper left
                foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(Flames, true, true);
                rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; RenderSettings.fog = false;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (System.Exception e) { MotorModPlugin.Log.LogWarning("skull icon: " + e.Message); return null; }
            finally
            {
                RenderSettings.fog = fog; RenderTexture.active = was;
                if (cam != null) { cam.targetTexture = null; Object.DestroyImmediate(cam.gameObject); }
                if (light != null) Object.DestroyImmediate(light.gameObject);
                if (clone != null) Object.DestroyImmediate(clone);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }
    }

    /// <summary>
    /// On a worn skull: shrinks the wearer's head inside it (the face would poke through) and keeps the skull its own size;
    /// gives the head back when taken off. Size and place come from the [FlamingSkull] settings, so they can be tuned in game.
    /// Lying on the ground it does nothing.
    /// </summary>
    public class SkullHead : MonoBehaviour
    {
        private Transform m_head, m_wearer;
        public float HeadScale => m_head != null ? m_head.localScale.x : 1f;

        private void Start()
        {
            var vis = GetComponentInParent<VisEquipment>();
            if (vis == null) return;                                       // a dropped item, or the hammer's icon render
            m_wearer = vis.transform;
            var joint = transform.parent != null ? transform.parent.parent : null;   // skull -> attach copy -> the helmet joint
            m_head = joint != null ? joint.parent : null;                              // the head bone
            if (MotorModPlugin.Verbose) MotorModPlugin.Log.LogInfo($"Flaming skull worn on {(joint != null ? joint.name : "?")} under {(m_head != null ? m_head.name : "?")}");
        }

        private void LateUpdate()
        {
            if (m_head != null) m_head.localScale = Vector3.one * Mathf.Clamp(MotorModPlugin.SkullHeadShrink.Value, 0.2f, 1f);
            // the skull keeps its own size, the wearer's, whatever the bones above it are scaled to
            float wearer = m_wearer != null ? m_wearer.lossyScale.x : 1f, above = transform.parent != null ? Mathf.Max(1e-3f, transform.parent.lossyScale.x) : 1f;
            float k = wearer / above;
            transform.localScale = Vector3.one * Mathf.Clamp(MotorModPlugin.SkullScale.Value, 0.3f, 3f) * k;
            transform.localPosition = new Vector3(0f, MotorModPlugin.SkullUp.Value, MotorModPlugin.SkullForward.Value) * k;
            transform.localRotation = Quaternion.Euler(MotorModPlugin.SkullTilt.Value, MotorModPlugin.SkullTurn.Value, 0f);
        }

        private void OnDestroy()
        {
            if (m_head != null) m_head.localScale = Vector3.one;
        }
    }
}
