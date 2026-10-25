using System.Collections.Generic;
using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// The trail of fire behind every bike, made from the game's campfire flames and real against enemies: flames on both
    /// tyres, twice as many at top speed; whoever stands in them burns. The Wraith and Draugr Cycles burn the same fire in
    /// the colours of the game's blue and green torches. Visuals run on every client; hits run on the bike's owner (the
    /// rider) and only touch enemies: never players, tamed animals or buildings.
    /// </summary>
    public class BikeTrail : MonoBehaviour
    {
        /// <summary>fire (Hel Cycle), fire_blue (Wraith Cycle) or fire_green (Draugr Cycle).</summary>
        public string m_kind = "fire";

        private MotorcycleController m_bike;
        private ZNetView m_nview;
        private bool m_built;
        private readonly List<ParticleSystem> m_emitters = new List<ParticleSystem>();
        private const float BaseDensity = 10f;        // flames per metre at walking pace
        private Light m_glow;
        private const float GlowPeak = 1.4f;

        // hits (owner only)
        private struct Mark { public Vector3 pos; public float time; }
        private readonly List<Mark> m_marks = new List<Mark>();
        private Vector3 m_lastMark = new Vector3(float.MaxValue, 0f, 0f);
        private float m_nextScan;
        private readonly Dictionary<int, float> m_lastHit = new Dictionary<int, float>();
        private const float MarkLifetime = 2.5f;      // seconds a spot of the trail keeps burning
        private const float TrailHalfWidth = 1.1f;    // metres either side of the line the tyre drew; the creature's own size is added

        private void Awake()
        {
            m_bike = GetComponent<MotorcycleController>();
            m_nview = GetComponent<ZNetView>();
        }

        /// <summary>A game effect by name: networked ones are in the game's prefab list, plain effects (the torch flames) only in memory.</summary>
        internal static GameObject Effect(string name)
        {
            var p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            return p != null ? p : Jotunn.Managers.PrefabManager.Cache.GetPrefab<GameObject>(name);
        }

        private void Update()
        {
            if (m_bike == null || m_nview == null || !m_nview.IsValid()) return;      // also the hammer's placement ghost: no effects there
            if (!m_built && ZNetScene.instance != null) { m_built = true; Build(); }
            var def = m_bike.m_definition;
            float speed = Mathf.Abs(m_bike.SyncedSpeed), top = Mathf.Max(1f, def != null ? def.CfgTopSpeed.Value : 16f);
            bool on = MotorModPlugin.Trails.Value && m_bike.EngineOn && speed > 2f;
            float density = on ? BaseDensity * MotorModPlugin.TrailDensity.Value * (1f + Mathf.Clamp01(speed / top)) : 0f;   // twice as much at top speed
            foreach (var ps in m_emitters)
            {
                if (ps == null) continue;
                var em = ps.emission; em.rateOverDistance = density; em.rateOverTime = 0f;
            }
            if (m_glow != null) m_glow.intensity = Mathf.MoveTowards(m_glow.intensity, on ? GlowPeak : 0f, 3f * Time.deltaTime);
            if (!m_nview.IsOwner()) return;
            if (on) DropMark();
            if (Time.time >= m_nextScan) { m_nextScan = Time.time + 0.25f; ScanHits(); }
        }

        // ---------------- visuals ----------------
        private void Build()
        {
            AddEmitters(FindFlames("fire_pit", "flames (1)"));
            switch (m_kind)
            {
                case "fire_blue": Recolour("fx_Torch_Blue"); AddGlow(new Color(0.4f, 0.62f, 1f)); break;
                case "fire_green": Recolour("fx_Torch_Green"); AddGlow(new Color(0.4f, 1f, 0.45f)); break;
            }
            MotorModPlugin.Log.LogInfo($"{gameObject.name}: trail '{m_kind}' with {m_emitters.Count} emitters");
        }

        /// <summary>
        /// The flames take the colours of another of the game's fires. The campfire and the torches draw one grey flipbook through
        /// one gradient-mapping material; what makes a torch blue or green is the colour each particle carries in its custom
        /// vertex data (and its colour over lifetime), so those are copied, not the material.
        /// </summary>
        private void Recolour(string torch)
        {
            var src = Effect(torch); var from = src != null ? src.GetComponent<ParticleSystem>() : null;
            if (from == null) { MotorModPlugin.Log.LogWarning($"{gameObject.name}: no flames on {torch}"); return; }
            var fromMain = from.main; var fromColour = from.colorOverLifetime; var fromCustom = from.customData;
            foreach (var ps in m_emitters)
            {
                if (ps == null) continue;
                var main = ps.main; main.startColor = fromMain.startColor;
                var colour = ps.colorOverLifetime; colour.enabled = fromColour.enabled; colour.color = fromColour.color;
                var custom = ps.customData; custom.enabled = fromCustom.enabled;
                foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 })
                {
                    var mode = fromCustom.GetMode(stream); custom.SetMode(stream, mode);
                    if (mode == ParticleSystemCustomDataMode.Color) custom.SetColor(stream, fromCustom.GetColor(stream));
                    else if (mode == ParticleSystemCustomDataMode.Vector)
                    {
                        int n = fromCustom.GetVectorComponentCount(stream); custom.SetVectorComponentCount(stream, n);
                        for (int i = 0; i < n; i++) custom.SetVector(stream, i, fromCustom.GetVector(stream, i));
                    }
                }
            }
        }

        /// <summary>A soft coloured light at the rear wheel while the trail burns.</summary>
        private void AddGlow(Color colour)
        {
            if (m_bike.m_wheelRear == null) return;
            var go = new GameObject("TrailGlow");
            go.transform.SetParent(m_bike.m_visual != null ? m_bike.m_visual : transform, false);
            go.transform.position = m_bike.m_wheelRear.position + transform.up * 0.2f - transform.forward * 0.4f;
            m_glow = go.AddComponent<Light>(); m_glow.type = LightType.Point; m_glow.color = colour; m_glow.range = 4f;
            m_glow.intensity = 0f; m_glow.shadows = LightShadows.None;
        }

        private static GameObject FindFlames(string prefab, string child)
        {
            var p = ZNetScene.instance.GetPrefab(prefab);
            if (p == null) return null;
            foreach (var ps in p.GetComponentsInChildren<ParticleSystem>(true)) if (ps.gameObject.name == child) return ps.gameObject;
            return null;
        }

        /// <summary>A world-space copy of the flames at the bottom of each tyre; it emits per metre travelled.</summary>
        private void AddEmitters(GameObject source)
        {
            if (source == null) { MotorModPlugin.Log.LogWarning($"{gameObject.name}: the campfire flames are missing"); return; }
            foreach (var wheel in new[] { m_bike.m_wheelRear, m_bike.m_wheelFront })
            {
                if (wheel == null) continue;
                var go = LocalCopy(source);
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                    if (!(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer)) Destroy(c);
                go.name = "Trail_" + m_kind;
                // under the visible bike, so a lifted front wheel (wheelie) carries its flames up with it; the wheel itself spins
                go.transform.SetParent(m_bike.m_visual != null ? m_bike.m_visual : transform, false);
                go.transform.position = wheel.position - transform.up * Mathf.Max(0.2f, m_bike.m_wheelRadius) * 0.85f;
                go.transform.rotation = Quaternion.identity;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true;
                    main.startLifetimeMultiplier = Mathf.Max(0.8f, main.startLifetimeMultiplier) * 1.2f;   // a trail must linger
                    main.startSizeMultiplier *= 1.4f;
                    main.gravityModifierMultiplier = -0.15f;                      // the flames rise
                    var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.18f;
                    var iv = ps.inheritVelocity; iv.enabled = false;           // the flames stay where they fell
                    var em = ps.emission; em.rateOverTime = 0f; em.rateOverDistance = 0f;
                    ps.Play();
                    m_emitters.Add(ps);
                }
            }
        }

        /// <summary>
        /// A purely local copy of a game effect. Many effect prefabs carry a network view: copied normally, each copy would
        /// become a saved world object, and stripping its network view afterwards leaves a dead entry that makes the game's
        /// object loop throw every frame. The game makes its placement ghosts the same way (network setup switched off).
        /// </summary>
        internal static GameObject LocalCopy(GameObject source)
        {
            ZNetView.m_forceDisableInit = true;
            try { return Instantiate(source); }
            finally { ZNetView.m_forceDisableInit = false; }
        }

        // ---------------- hits (owner only) ----------------
        private void DropMark()
        {
            Vector3 p = m_bike.m_wheelRear != null ? m_bike.m_wheelRear.position : transform.position;
            if ((p - m_lastMark).sqrMagnitude < 1f) return;
            m_lastMark = p;
            m_marks.Add(new Mark { pos = p, time = Time.time });
            if (m_marks.Count > 64) m_marks.RemoveAt(0);
        }

        /// <summary>Is p (on the ground plane) within reach of the segment a-b, and roughly at its height?</summary>
        private static bool NearSegment(Vector3 p, Vector3 a, Vector3 b, float reach)
        {
            Vector2 P = new Vector2(p.x, p.z), A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), AB = B - A;
            float t = AB.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(P - A, AB) / AB.sqrMagnitude) : 0f;
            if ((A + AB * t - P).sqrMagnitude > reach * reach) return false;
            return Mathf.Abs(p.y - Mathf.Lerp(a.y, b.y, t)) < 2.5f;
        }

        private void ScanHits()
        {
            float now = Time.time;
            m_marks.RemoveAll(m => now - m.time > MarkLifetime);
            if (m_marks.Count == 0) return;
            var rider = RiderPlayer();
            foreach (var c in Character.GetAllCharacters())
            {
                if (!IsTarget(c)) continue;
                int id = c.GetInstanceID();
                if (m_lastHit.TryGetValue(id, out float last) && now - last < 1f) continue;   // once a second per creature
                Vector3 cp = c.transform.position;
                float reach = TrailHalfWidth + Mathf.Clamp(c.GetRadius(), 0.2f, 1.5f);
                for (int i = 0; i < m_marks.Count; i++)
                {
                    var m = m_marks[i];
                    Vector3 a = m.pos, b = i + 1 < m_marks.Count ? m_marks[i + 1].pos : m.pos;
                    if (!NearSegment(cp, a, b, reach)) continue;
                    var hit = new HitData();
                    hit.m_point = c.GetCenterPoint();
                    Vector3 dir = c.transform.position - m.pos; dir.y = 0f;
                    hit.m_dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                    if (rider != null) hit.m_attacker = rider.GetZDOID();
                    hit.m_damage.m_fire = MotorModPlugin.FireDamage.Value;         // the game adds Burning to fire damage
                    c.Damage(hit);
                    m_lastHit[id] = now;
                    break;
                }
            }
        }

        /// <summary>Enemies only: never players, tamed animals or the dead.</summary>
        private static bool IsTarget(Character c) => c != null && !c.IsPlayer() && !c.IsTamed() && !c.IsDead();

        private Player RiderPlayer()
        {
            var p = Player.m_localPlayer;
            return p != null && ReferenceEquals(p.GetDoodadController(), m_bike) ? p : null;
        }
    }
}
