using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// Engine sound driven by the bike's synced engine state, speed and throttle. Runs on every client.
    /// Rules: idle only while slower than 3 km/h, one cruise loop at a time with narrow
    /// crossfades that are quicker on the way down, the acceleration and rev clips stop the moment their cause
    /// stops, and getting off cuts everything and plays the shut-off once.
    /// </summary>
    public class EngineAudio : MonoBehaviour
    {
        public AudioClip m_startupShort, m_startupFull, m_idle, m_cruiseLow, m_cruiseHigh, m_cruiseFast, m_accel, m_revBlips, m_shutoff;

        private const float IdleSpeed = 0.83f;          // 3 km/h: below this the engine idles
        private const float IdleLeave = 1.15f;          // and it leaves idle only above this (no flicker at the edge)
        private const float FadeUp = 5f;                // volume units per second going up (0.2 s)
        private const float FadeDown = 9f;              // going down (0.11 s)
        private const float Cut = 10f;                  // one-shots cut (0.1 s)

        private MotorcycleController m_bike;
        private AudioSource m_idleSrc, m_lowSrc, m_highSrc, m_fastSrc, m_accelSrc, m_blipSrc, m_oneShot;
        private enum State { Off, Starting, Running, Stopping }
        private enum Band { Idle, Low, High, Fast }
        private State m_state = State.Off;
        private Band m_band = Band.Idle;
        private float m_stateTime, m_lastThrottle, m_lastSpeed, m_accelTime;
        private bool m_accelWanted;

        private void Awake()
        {
            m_bike = GetComponent<MotorcycleController>();
            m_idleSrc = MakeSource("idle", m_idle, true);
            m_lowSrc = MakeSource("cruise_low", m_cruiseLow, true);
            m_highSrc = MakeSource("cruise_high", m_cruiseHigh, true);
            m_fastSrc = MakeSource("cruise_fast", m_cruiseFast, true);
            m_accelSrc = MakeSource("accel", m_accel, false);
            m_blipSrc = MakeSource("blips", m_revBlips, false);
            m_oneShot = MakeSource("oneshot", null, false);
        }

        private AudioSource MakeSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject("audio_" + name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.loop = loop; src.playOnAwake = false;
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 4f; src.maxDistance = 70f;
            src.dopplerLevel = 0.2f; src.volume = 0f;
            var mixer = AudioMan.instance != null ? AudioMan.instance.m_masterMixer : null;
            if (mixer != null) { var groups = mixer.FindMatchingGroups("SFX"); if (groups != null && groups.Length > 0) src.outputAudioMixerGroup = groups[0]; }
            return src;
        }

        private void Update()
        {
            if (m_bike == null) return;
            float master = MotorModPlugin.EngineVolume.Value;
            bool on = m_bike.EngineOn;
            float signedSpeed = m_bike.SyncedSpeed;
            float speed = Mathf.Max(0f, signedSpeed);            // reversing sounds like idle
            float throttle = m_bike.SyncedThrottle;
            float dt = Time.deltaTime;
            m_stateTime += dt;

            switch (m_state)
            {
                case State.Off:
                    if (on) { m_oneShot.Stop(); PlayOnce(m_oneShot, StartClip(), master); Enter(State.Starting); }
                    break;
                case State.Starting:
                    if (!on) { Shutdown(master); break; }
                    var startClip = StartClip();
                    if (startClip == null || m_stateTime >= startClip.length - 0.35f) { StartLoops(master); Enter(State.Running); }
                    break;
                case State.Running:
                    if (!on) { Shutdown(master); break; }
                    MixLoops(speed, master, dt);
                    UpdateAccel(throttle, signedSpeed, speed, master, dt);
                    if (m_bike.BlipRequested) { m_bike.ConsumeBlip(); m_blipSrc.Stop(); PlayOnce(m_blipSrc, m_revBlips, master); }
                    break;
                case State.Stopping:
                    FadeAll(dt * Cut);
                    if (m_stateTime > 0.25f) { StopLoops(); Enter(State.Off); }
                    break;
            }
            if (m_state != State.Running) { m_accelWanted = false; Fade(m_accelSrc, 0f, dt * Cut); Fade(m_blipSrc, 0f, dt * Cut); }
            m_lastThrottle = throttle; m_lastSpeed = signedSpeed;
        }

        private AudioClip StartClip() => MotorModPlugin.UseFullStartup.Value && m_startupFull ? m_startupFull : m_startupShort;

        private void Enter(State s) { m_state = s; m_stateTime = 0f; }

        /// <summary>Getting off (or the engine dying mid start-up): every loop and one-shot is cut within a tenth of a second and the shut-off plays once.</summary>
        private void Shutdown(float master)
        {
            m_oneShot.Stop();
            PlayOnce(m_oneShot, m_shutoff, master);
            Enter(State.Stopping);
        }

        private void StartLoops(float master)
        {
            foreach (var s in new[] { m_idleSrc, m_lowSrc, m_highSrc, m_fastSrc }) if (s.clip != null && !s.isPlaying) s.Play();
            m_band = Band.Idle;
            m_idleSrc.volume = master;
        }

        private void StopLoops()
        {
            foreach (var s in new[] { m_idleSrc, m_lowSrc, m_highSrc, m_fastSrc, m_accelSrc, m_blipSrc }) { s.Stop(); s.volume = 0f; }
        }

        private void FadeAll(float step) { foreach (var s in new[] { m_idleSrc, m_lowSrc, m_highSrc, m_fastSrc, m_accelSrc, m_blipSrc }) s.volume = Mathf.MoveTowards(s.volume, 0f, step); }

        /// <summary>One loop at a time. The band changes only when the speed crosses its edge with a little margin, then the old loop fades fast and the new one fades in.</summary>
        private void MixLoops(float speed, float master, float dt)
        {
            float top = Mathf.Max(1f, m_bike.m_definition != null ? m_bike.m_definition.CfgTopSpeed.Value : 16f);
            bool hasLow = m_cruiseLow != null;
            float e1 = (hasLow ? 0.38f : 0.55f) * top;     // low -> high (or idle -> high without a low loop)
            float e2 = (hasLow ? 0.68f : 0.90f) * top;     // high -> fast
            float m = 0.04f * top;                          // hysteresis margin
            Band want = m_band;
            switch (m_band)
            {
                case Band.Idle: if (speed > IdleLeave) want = hasLow ? Band.Low : (speed > e1 ? Band.High : Band.High); break;
                case Band.Low: if (speed < IdleSpeed) want = Band.Idle; else if (speed > e1 + m) want = Band.High; break;
                case Band.High: if (speed < IdleSpeed) want = Band.Idle; else if (hasLow && speed < e1 - m) want = Band.Low; else if (speed > e2 + m) want = Band.Fast; break;
                case Band.Fast: if (speed < IdleSpeed) want = Band.Idle; else if (speed < e2 - m) want = Band.High; break;
            }
            if (!hasLow && want == Band.Low) want = Band.High;
            m_band = want;
            float lowBand = hasLow ? Mathf.InverseLerp(IdleSpeed, e1, speed) : 0f;
            Set(m_idleSrc, m_band == Band.Idle ? master : 0f, 1f, dt);
            if (hasLow) Set(m_lowSrc, m_band == Band.Low ? master : 0f, Mathf.Lerp(0.92f, 1.12f, lowBand), dt);
            Set(m_highSrc, m_band == Band.High ? master : 0f, Mathf.Lerp(0.9f, 1.15f, Mathf.InverseLerp(hasLow ? e1 : IdleSpeed, e2, speed)), dt);
            Set(m_fastSrc, m_band == Band.Fast ? master : 0f, Mathf.Lerp(0.95f, 1.2f, Mathf.InverseLerp(e2, top * 1.2f, speed)), dt);
        }

        /// <summary>The acceleration clip starts when the throttle opens from a slow speed and keeps playing only while the throttle is held and the bike is still gaining speed.</summary>
        private void UpdateAccel(float throttle, float signedSpeed, float speed, float master, float dt)
        {
            if (m_accel != null && throttle > 0.5f && m_lastThrottle <= 0.5f && signedSpeed > -0.5f && speed < 6f)
            {
                m_accelSrc.Stop(); m_accelSrc.clip = m_accel; m_accelSrc.volume = 0f; m_accelSrc.Play();
                m_accelWanted = true; m_accelTime = 0f;
            }
            m_accelTime += dt;
            bool gaining = signedSpeed >= m_lastSpeed - 0.05f;
            if (m_accelWanted && (throttle <= 0.5f || !gaining || m_band == Band.Fast || !m_accelSrc.isPlaying || m_accelTime > 5f)) m_accelWanted = false;
            Fade(m_accelSrc, m_accelWanted ? master * 0.9f : 0f, dt * (m_accelWanted ? FadeUp * 2f : Cut));
            if (!m_accelWanted && m_accelSrc.volume <= 0f && m_accelSrc.isPlaying) m_accelSrc.Stop();
            if (m_blipSrc.isPlaying && m_blipSrc.volume <= 0f) m_blipSrc.Stop();
        }

        private static void Set(AudioSource s, float vol, float pitch, float dt)
        {
            Fade(s, vol, dt * (vol < s.volume ? FadeDown : FadeUp));
            s.pitch = Mathf.MoveTowards(s.pitch, pitch, dt * 4f);
        }

        private static void Fade(AudioSource s, float vol, float step) => s.volume = Mathf.MoveTowards(s.volume, vol, step);

        private static void PlayOnce(AudioSource s, AudioClip clip, float vol)
        {
            if (clip == null) return;
            s.clip = clip; s.volume = vol; s.Play();
        }

        /// <summary>For the ride log: what is audible right now.</summary>
        public string Audible()
        {
            string Part(string n, AudioSource s) => s != null && s.isPlaying && s.volume > 0.02f ? $"{n}:{s.volume:0.00} " : "";
            return (m_state + " " + Part("idle", m_idleSrc) + Part("low", m_lowSrc) + Part("high", m_highSrc) + Part("fast", m_fastSrc) + Part("accel", m_accelSrc) + Part("blip", m_blipSrc) + Part("once", m_oneShot)).Trim();
        }
    }
}
