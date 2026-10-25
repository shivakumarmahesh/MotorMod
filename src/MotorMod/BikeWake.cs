using System.Collections.Generic;
using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// Water on water: a foam wake behind the rear tyre, spray off both sides of the front tyre, a rooster tail behind the rear
    /// tyre and a foam patch around the bike, so riding on water looks like it. Looks only. The
    /// layers are the longship's own (Karve 'SurfaceEffects'), copied at bike size; they sit on the water surface and grow with
    /// speed. Every client shows them, from the on-water flag and speed the rider's machine shares.
    /// </summary>
    public class BikeWake : MonoBehaviour
    {
        private MotorcycleController m_bike;
        private ZNetView m_nview;
        private bool m_built;

        private enum Anchor { Front, Rear, Middle }
        private class Layer
        {
            public ParticleSystem ps; public Transform t; public Anchor at; public float back, side;
            public float perMetre, perSecondAtTop, perSecondIdle; public Quaternion turn;
        }
        private readonly List<Layer> m_layers = new List<Layer>();

        private void Awake()
        {
            m_bike = GetComponent<MotorcycleController>();
            m_nview = GetComponent<ZNetView>();
        }

        private void Update()
        {
            if (m_bike == null || m_nview == null || !m_nview.IsValid()) return;     // also the hammer's placement ghost
            if (!m_built && ZNetScene.instance != null) { m_built = true; Build(); }
            if (m_layers.Count == 0) return;
            var d = m_bike.m_definition;
            float speed = Mathf.Abs(m_bike.SyncedSpeed), top = Mathf.Max(1f, d != null ? d.CfgTopSpeed.Value : 16f), frac = Mathf.Clamp01(speed / top);
            bool on = MotorModPlugin.Wake.Value && m_bike.OnWaterSynced;
            float water = on ? Floating.GetLiquidLevel(transform.position + Vector3.up * 0.3f, 1f, LiquidType.Water) : 0f;
            if (water < -9000f) on = false;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            foreach (var l in m_layers)
            {
                if (l.ps == null) continue;
                if (on)
                {
                    Vector3 p = AnchorPoint(l.at) - fwd * l.back + right * l.side;
                    p.y = water + 0.03f;                                      // on the surface, even when the bike settles lower
                    l.t.SetPositionAndRotation(p, Quaternion.LookRotation(fwd) * l.turn);
                }
                var em = l.ps.emission;
                em.rateOverDistance = on && speed > 0.5f ? l.perMetre : 0f;
                em.rateOverTime = on ? l.perSecondIdle + l.perSecondAtTop * frac * frac : 0f;
            }
        }

        private Vector3 AnchorPoint(Anchor a)
        {
            switch (a)
            {
                case Anchor.Front: return m_bike.m_wheelFront != null ? m_bike.m_wheelFront.position : transform.position + transform.forward;
                case Anchor.Rear: return m_bike.m_wheelRear != null ? m_bike.m_wheelRear.position : transform.position - transform.forward;
                default: return transform.position;
            }
        }

        private void Build()
        {
            float s = Mathf.Clamp(MotorModPlugin.WakeSize.Value, 0.2f, 3f);
            // source effect, layer path, where, how far back / to the side (m), per metre travelled, per second at top speed,
            // per second always, size, aim (null = keep the layer's own orientation), speed
            // flat foam from the longship: the wake behind the rear tyre, foam patches at the tyre, a foam ring around the bike
            Add("Karve", "SurfaceEffects/SpeedWake/Trail", Anchor.Rear, 0.3f, 0f, 0.8f, 0f, 0f, 0.22f * s, null, 1f, colour: new Color(0.95f, 0.97f, 1f, 0.75f));
            Add("Karve", "SurfaceEffects/SpeedWake/aft_particles", Anchor.Rear, 0.3f, 0f, 0f, 16f, 0f, 0.12f * s, null, 1f);
            Add("Karve", "SurfaceEffects/WaterSurface/vfx_water_surface", Anchor.Middle, 0f, 0f, 0f, 0f, 3f, 0.25f * s, null, 1f);
            // spray: soft mist puffs (the fishing float's splash) and droplets stretched along their flight (the water-impact
            // splash), as a rooster tail behind the rear tyre and thrown off both sides of the front tyre
            Add("fx_float_hitwater", "", Anchor.Rear, 0.35f, 0f, 0f, 90f, 0f, 0.3f * s, Quaternion.Euler(-30f, 180f, 0f), 1f, 5.5f, 14f);
            Add("fx_WaterImpact_Big", "GameObject", Anchor.Rear, 0.35f, 0f, 0f, 200f, 0f, 0.75f * s, Quaternion.Euler(-32f, 180f, 0f), 1f, 7f, 10f, stretch: true, colour: new Color(0.92f, 0.96f, 1f, 0.6f));
            foreach (float sideSign in new[] { 1f, -1f })
            {
                Add("fx_WaterImpact_Big", "GameObject", Anchor.Front, 0f, 0.2f * sideSign, 0f, 90f, 0f, 0.65f * s, Quaternion.Euler(-28f, 110f * sideSign, 0f), 1f, 3.5f, 20f, stretch: true, colour: new Color(0.92f, 0.96f, 1f, 0.6f));
                Add("fx_float_hitwater", "", Anchor.Front, 0f, 0.2f * sideSign, 0f, 30f, 0f, 0.16f * s, Quaternion.Euler(-25f, 110f * sideSign, 0f), 1f, 2.5f, 20f);
            }
            MotorModPlugin.Log.LogInfo($"{gameObject.name}: water wake with {m_layers.Count} layers");
        }

        private void Add(string prefab, string path, Anchor at, float back, float side, float perMetre, float perSecondAtTop, float perSecondIdle, float size, Quaternion? aim, float speed,
                         float throwSpeed = 0f, float cone = 0f, bool stretch = false, Color? colour = null)
        {
            var srcRoot = ZNetScene.instance.GetPrefab(prefab);
            var src = srcRoot == null ? null : string.IsNullOrEmpty(path) ? srcRoot.transform : srcRoot.transform.Find(path);
            var ship = srcRoot != null ? srcRoot.transform : null;
            var srcPs = src != null ? src.GetComponent<ParticleSystem>() : null;
            if (srcPs == null) { MotorModPlugin.Log.LogWarning($"{gameObject.name}: wake layer {path} missing"); return; }
            ZNetView.m_forceDisableInit = true;
            GameObject go;
            try { go = Instantiate(src.gameObject); } finally { ZNetView.m_forceDisableInit = false; }
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer)) Destroy(c);
            for (int i = go.transform.childCount - 1; i >= 0; i--) Destroy(go.transform.GetChild(i).gameObject);   // one layer each
            go.name = "Wake_" + src.name;
            go.transform.SetParent(transform, false);
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.startSizeMultiplier *= size;
            main.startSpeedMultiplier *= speed;
            var shape = ps.shape;
            if (throwSpeed > 0f)
            {   // a spray: thrown along the aim in a narrow cone at a set speed, falling back under gravity
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = cone; shape.radius = 0.08f;
                shape.position = Vector3.zero; shape.rotation = Vector3.zero;
                main.startSpeed = new ParticleSystem.MinMaxCurve(throwSpeed * 0.7f, throwSpeed * 1.15f);
                if (main.gravityModifierMultiplier < 0.5f) main.gravityModifierMultiplier = 1f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            }
            else if (shape.enabled) { shape.radius *= Mathf.Min(1f, size * 1.2f); shape.position = Vector3.zero; }
            var iv = ps.inheritVelocity; iv.enabled = false;
            if (colour.HasValue) main.startColor = colour.Value;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            if (stretch && pr != null) { pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.05f; pr.lengthScale = 1f; pr.cameraVelocityScale = 0f; }
            var em = ps.emission; em.rateOverTime = 0f; em.rateOverDistance = 0f;
            ps.Play();
            var turn = aim ?? Quaternion.Inverse(ship.rotation) * src.rotation;   // its own orientation on the ship, or aimed: z is the spray direction
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (MotorModPlugin.Verbose) MotorModPlugin.Log.LogInfo($"wake layer {path}: turn {turn.eulerAngles}, render {(r ? r.renderMode.ToString() + "/" + r.alignment : "-")}");
            m_layers.Add(new Layer { ps = ps, t = go.transform, at = at, back = back, side = side, perMetre = perMetre, perSecondAtTop = perSecondAtTop, perSecondIdle = perSecondIdle, turn = turn });
        }
    }
}
