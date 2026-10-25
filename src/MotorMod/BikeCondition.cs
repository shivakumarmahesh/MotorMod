using System.Collections.Generic;
using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// What a damaged bike looks and sounds like, the way the game's ships and carts tell their owners: three states by health
    /// (fresh above 75 %, worn down to 25 %, wrecked below), each step down announced with a burst of smoke and a metal crunch
    /// (the WearNTear switch effect). The ships swap whole hull models; the bike keeps its one model and gets sootier instead:
    /// its materials are tinted darker and duller through property blocks (the shared materials stay untouched), and a wrecked
    /// bike smokes from the engine until it is repaired (a looping local copy of the Karve's damage smoke, like the trail's flames). The state follows the health every client reads from the network
    /// record, so everyone sees the same bike. The one-shot smoke and crunch are spawned by the owner only: they are network
    /// objects and would double up if every client spawned them.
    /// </summary>
    public class BikeCondition : MonoBehaviour
    {
        public enum State { Fresh, Worn, Wrecked }
        private static readonly int s_color = Shader.PropertyToID("_Color"), s_gloss = Shader.PropertyToID("_Glossiness"), s_glossMap = Shader.PropertyToID("_GlossMapScale");
        private static readonly MaterialPropertyBlock s_clear = new MaterialPropertyBlock();   // an empty block removes a slot's overrides (never pass null to the per-slot overload)
        private const float WornTint = 0.72f, WornGloss = 0.6f, WreckedTint = 0.45f, WreckedGloss = 0.35f, SmokeRate = 4f, SmokeScale = 0.5f;
        private ZNetView m_nview;
        private MotorcycleController m_bike;
        private WearNTear m_wear;
        private BikeHealth m_health;
        private readonly List<Renderer> m_renderers = new List<Renderer>();
        private ParticleSystem m_smoke;                  // the engine smoke of a wrecked bike, made on first need
        private State m_state = State.Fresh;
        private bool m_applied;
        private float m_nextPoll;

        public State Current => m_state;
        public bool Smoking => m_smoke != null && m_smoke.isEmitting;
        public int SmokeParticles => m_smoke != null ? m_smoke.particleCount : 0;
        public static bool Enabled => MotorModPlugin.DamageLook == null || MotorModPlugin.DamageLook.Value;
        public static State StateFor(float fraction) => fraction > 0.75f ? State.Fresh : fraction > 0.25f ? State.Worn : State.Wrecked;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>(); m_bike = GetComponent<MotorcycleController>(); m_wear = GetComponent<WearNTear>(); m_health = GetComponent<BikeHealth>();
        }

        private void Start()
        {
            // the bike's own body: not the saddlebags (cargo), not the headlight glow, not particle renderers (trail, wake)
            var root = m_bike != null && m_bike.m_visual != null ? m_bike.m_visual : transform;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool skip = false;
                for (var t = r.transform; t != null && t != root; t = t.parent) if (t.name == "Saddlebags" || t.name == "Headlight") { skip = true; break; }
                if (!skip) m_renderers.Add(r);
            }
            Apply(StateFor(m_health != null ? m_health.Fraction : 1f), silent: true);
        }

        private void Update()
        {
            if (m_nview == null || !m_nview.IsValid() || m_health == null) return;
            if (Time.time >= m_nextPoll)
            {
                m_nextPoll = Time.time + 0.2f;
                var s = StateFor(m_health.Fraction);
                if (s != m_state || !m_applied) Apply(s, silent: !m_applied);
            }
        }

        /// <summary>Console repair, the hammer, or a new state read from the network: tint the body, and on a step down the owner lets the smoke and crunch off.</summary>
        private void Apply(State s, bool silent)
        {
            bool down = s > m_state;
            m_state = s; m_applied = true;
            Tint(Enabled ? s : State.Fresh);
            SetSmoke(Enabled && s == State.Wrecked);
            if (!silent && down && m_wear != null && m_nview.IsOwner()) m_wear.m_switchEffect.Create(transform.position, transform.rotation);
        }

        /// <summary>Soot: every material's own colour and smoothness scaled down, per material slot, through a property block.</summary>
        private void Tint(State s)
        {
            float k = s == State.Fresh ? 1f : s == State.Worn ? WornTint : WreckedTint, g = s == State.Fresh ? 1f : s == State.Worn ? WornGloss : WreckedGloss;
            foreach (var r in m_renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    if (s == State.Fresh) { r.SetPropertyBlock(s_clear, i); continue; }
                    var b = new MaterialPropertyBlock();
                    if (m.HasProperty(s_color)) b.SetColor(s_color, m.GetColor(s_color) * new Color(k, k, k, 1f));
                    if (m.HasProperty(s_gloss)) b.SetFloat(s_gloss, m.GetFloat(s_gloss) * g);
                    if (m.HasProperty(s_glossMap)) b.SetFloat(s_glossMap, m.GetFloat(s_glossMap) * g);
                    r.SetPropertyBlock(b, i);
                }
            }
        }

        private void SetSmoke(bool on)
        {
            if (on && m_smoke == null) m_smoke = MakeSmoke();
            if (m_smoke == null) return;
            if (on && !m_smoke.isEmitting) m_smoke.Play(true);
            else if (!on && m_smoke.isEmitting) m_smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);   // what is in the air drifts off
        }

        /// <summary>A looping, half-size, world-space copy of the Karve's damage smoke at the engine: a steady trickle instead of one burst.
        /// Local on every client (no network view), nothing but the particle system kept, so it cannot time out or be saved.</summary>
        private ParticleSystem MakeSmoke()
        {
            var source = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("vfx_Damaged_Karve") : null;
            var ps0 = source != null ? source.GetComponentInChildren<ParticleSystem>(true) : null;
            if (ps0 == null) { MotorModPlugin.Log.LogWarning($"{gameObject.name}: no damage smoke to copy"); return null; }
            var go = BikeTrail.LocalCopy(ps0.gameObject);
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is ParticleSystem) && !(c is ParticleSystemRenderer)) Destroy(c);
            go.name = "EngineSmoke";
            go.transform.SetParent(m_bike != null && m_bike.m_visual != null ? m_bike.m_visual : transform, false);
            go.transform.position = EnginePoint; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one * SmokeScale;
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission; emission.enabled = true; emission.rateOverTime = SmokeRate; emission.rateOverDistance = 0f; emission.SetBursts(new ParticleSystem.Burst[0]);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>Between the axles, a little above the seat height: where the engine sits on every bike.</summary>
        public Vector3 EnginePoint
        {
            get
            {
                if (m_bike != null && m_bike.m_wheelFront != null && m_bike.m_wheelRear != null)
                    return (m_bike.m_wheelFront.position + m_bike.m_wheelRear.position) * 0.5f + Vector3.up * 0.45f;
                return transform.position + Vector3.up * 0.6f;
            }
        }
    }
}
