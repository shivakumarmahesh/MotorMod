using System.Collections.Generic;
using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// The bike's health lives in the game's own durability part (WearNTear), set up like a boat's or a cart's: the hammer shows
    /// it and repairs it near a forge, weapons and monsters wear it down, and at 0 the game breaks the bike and drops everything
    /// it cost loose where it broke (on the ground, or down to the seabed; only the saddlebags' contents come up in a floating
    /// bag). Nothing repairs itself. This part adds what a
    /// vehicle needs on top: hard crashes cost health and damage what was hit (buildings and other breakable objects), ramming
    /// a creature hurts it like the game's cart does, by the bike's own speed into it, and a breaking bike throws its rider off.
    /// A crash hard enough to break it ends in a fireball. BikeCondition shows the damage in between (soot, smoke).
    /// </summary>
    public class BikeHealth : MonoBehaviour
    {
        private ZNetView m_nview;
        private MotorcycleController m_bike;
        private WearNTear m_wear;
        private string m_breakReason = "wrecked";
        private readonly Dictionary<int, float> m_lastImpact = new Dictionary<int, float>();   // creature or object -> last time it was hit

        private BikeDefinition D => m_bike != null ? m_bike.m_definition : null;
        public float Max => m_wear != null ? m_wear.m_health : MotorModPlugin.MaxHealth.Value;
        public float Health => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetFloat(ZDOVars.s_health, Max) : Max;
        public float Fraction => Mathf.Clamp01(Health / Mathf.Max(1f, Max));

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>(); m_bike = GetComponent<MotorcycleController>(); m_wear = GetComponent<WearNTear>();
            if (m_wear != null) m_wear.m_onDestroyed += OnBroken;
        }

        /// <summary>Health off the bike (the owner keeps it); at 0 the game's durability breaks it, refund and all.</summary>
        private void Hurt(float damage, string what)
        {
            if (m_wear == null || damage <= 0f || !m_nview.IsOwner()) return;
            if (MotorModPlugin.Verbose) MotorModPlugin.Log.LogInfo($"{gameObject.name}: {what} -{damage:0} health, {Mathf.Max(0f, Health - damage):0}/{Max:0}");
            m_wear.ApplyDamage(damage);
        }

        private void OnCollisionEnter(Collision info)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner() || info.contacts.Length == 0) return;
            var contact = info.contacts[0];
            var other = contact.otherCollider;
            var character = other ? other.GetComponentInParent<Character>() : null;
            if (character != null)
            {
                if (character.IsPlayer() || character.IsTamed()) return;
                // the bike's own speed toward the creature: a creature walking into a parked or crawling bike takes nothing,
                // and one pressed against the front takes at most one hit a second
                Vector3 to = character.transform.position - transform.position; to.y = 0f;
                float closing = (m_bike != null ? m_bike.Speed : 0f) * (to.sqrMagnitude > 1e-4f ? Vector3.Dot(transform.forward, to.normalized) : 1f);
                if (closing < MotorModPlugin.RamMinSpeed.Value || !Ready(character)) return;
                var hit = new HitData();
                hit.m_damage.m_blunt = closing * MotorModPlugin.RamDamage.Value;
                hit.m_point = contact.point; hit.m_dir = to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
                hit.m_pushForce = closing * 6f; hit.m_hitType = HitData.HitType.Impact;
                var rider = RiderHere(); if (rider != null) hit.m_attacker = rider.GetZDOID();
                character.Damage(hit);
                if (MotorModPlugin.Verbose) MotorModPlugin.Log.LogInfo($"{gameObject.name}: rammed {character.m_name} at {closing:0.0} m/s for {hit.m_damage.m_blunt:0}");
                Hurt(closing * 1.5f, "ram");                              // ramming costs the bike a little too
                return;
            }
            // the world: only a real crash counts (the speed into the surface, not along it)
            float into = Mathf.Abs(Vector3.Dot(info.relativeVelocity, contact.normal));
            // whatever was hit takes damage too: a wall, a door, a cart, another bike (trees and rocks need an axe or pickaxe)
            var target = other ? other.GetComponentInParent<IDestructible>() : null;
            if (target != null && !ReferenceEquals(target, m_wear) && into >= MotorModPlugin.ObjectMinSpeed.Value && target is Component tc && Ready(tc))
            {
                var hit = new HitData();
                hit.m_damage.m_blunt = into * MotorModPlugin.ObjectDamage.Value;
                hit.m_point = contact.point; hit.m_dir = transform.forward; hit.m_hitType = HitData.HitType.Impact;
                var rider = RiderHere(); if (rider != null) hit.m_attacker = rider.GetZDOID();
                target.Damage(hit);
                if (MotorModPlugin.Verbose) MotorModPlugin.Log.LogInfo($"{gameObject.name}: crashed into {tc.gameObject.name} at {into:0.0} m/s for {hit.m_damage.m_blunt:0}");
            }
            float limit = MotorModPlugin.CrashSpeed.Value;
            if (into > limit)
            {
                float damage = (into - limit) * MotorModPlugin.CrashDamage.Value;
                if (into >= MotorModPlugin.FatalCrashSpeed.Value) damage = Mathf.Max(damage, Health);   // a head-on at boost speed breaks it whatever its condition
                if (damage >= Health) m_breakReason = "crash";              // this one breaks it: OnBroken lets the crash effect off
                Hurt(damage, "crash");
                if (m_nview.IsValid()) m_breakReason = "wrecked";
            }
        }

        /// <summary>At most one impact a second on the same creature or object (a bike pressed against it would hit every bounce).</summary>
        private bool Ready(Component c)
        {
            int id = c.GetInstanceID(); float now = Time.time;
            if (m_lastImpact.TryGetValue(id, out float last) && now - last < 1f) return false;
            if (m_lastImpact.Count > 64) { var old = new List<int>(); foreach (var kv in m_lastImpact) if (now - kv.Value > 5f) old.Add(kv.Key); foreach (var k in old) m_lastImpact.Remove(k); }
            m_lastImpact[id] = now;
            return true;
        }

        /// <summary>The local player, when they are the one riding this bike.</summary>
        private Player RiderHere()
        {
            var p = Player.m_localPlayer;
            return p != null && m_bike != null && ReferenceEquals(p.GetDoodadController(), m_bike) ? p : null;
        }

        /// <summary>Console: full health at once.</summary>
        public void Repair()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            if (!m_nview.IsOwner()) { if (m_bike != null && m_bike.HasRider) return; m_nview.ClaimOwnership(); }
            m_nview.GetZDO().Set(ZDOVars.s_health, Max);
            m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", Max);
        }

        /// <summary>Breaks the bike now (it sank): the game's own destruction, so the rider comes off and everything it cost drops where it sank.</summary>
        public void BreakApart(string why)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner() || m_wear == null) return;
            m_breakReason = why;
            m_wear.Remove();
        }

        /// <summary>The game's durability just destroyed the bike (and dropped the refund): throw the rider off with a word why.</summary>
        private void OnBroken()
        {
            if (m_breakReason == "crash" && MotorModPlugin.CrashBreakEffect != null) MotorModPlugin.CrashBreakEffect.Create(transform.position + Vector3.up * 0.5f, transform.rotation);   // the owner spawns it; a network object, everyone sees it
            var rider = RiderHere();
            if (rider != null)
            {
                rider.StopDoodadControl();
                rider.Message(MessageHud.MessageType.Center, m_breakReason == "sank" ? "Your bike sank and broke apart." : m_breakReason == "crash" ? "Your bike blew apart." : "Your bike is wrecked.");
            }
            MotorModPlugin.Log.LogInfo($"{gameObject.name} broke ({m_breakReason})");
        }
    }
}
