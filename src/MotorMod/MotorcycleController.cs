using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// The bike as a vehicle. Mounting and inputs go through the game's own hooks (Interactable, IDoodadController,
    /// Player.AttachStart), modelled on the vanilla ship rudder and lox saddle. Physics runs on the rider's machine,
    /// which becomes the network owner of the bike; everyone else receives position, rotation and speed.
    /// </summary>
    public class MotorcycleController : MonoBehaviour, Interactable, Hoverable, IDoodadController
    {
        public string m_hoverText = "Motorcycle";
        public float m_maxUseRange = 4f;
        public Transform m_attachPoint;
        public Vector3 m_detachOffset = new Vector3(1.1f, 0.4f, 0f);
        public Transform m_visual, m_wheelFront, m_wheelRear, m_gripLeft, m_gripRight, m_headlight, m_seatMarker, m_footLeft, m_footRight;
        private Light m_lamp; private Renderer m_glow; private Material m_glowMat; private bool m_lampOn;
        private readonly System.Collections.Generic.List<Material> m_lampMats = new System.Collections.Generic.List<Material>();
        private static readonly Color LampColour = new Color(1f, 0.93f, 0.78f);
        private Vector3 m_lastNormal = Vector3.up;
        public float m_wheelRadius = 0.36f;
        public BikeDefinition m_definition;
        private BikeDefinition D => m_definition ?? (m_definition = BikeDefinition.ForObject(gameObject.name));

        private static readonly int s_speedHash = "mm_speed".GetStableHashCode();
        private static readonly int s_throttleHash = "mm_throttle".GetStableHashCode();
        private static readonly int s_waterHash = "mm_onwater".GetStableHashCode();
        private static readonly int s_engineHash = "mm_engine".GetStableHashCode();
        private static readonly int s_steerHash = "mm_steer".GetStableHashCode();   // the rider's eased steering, so everyone sees the bars turn and the lean
        internal static readonly int s_groundMask = LayerMask.GetMask("terrain", "Default", "static_solid", "piece", "Default_small");

        private ZNetView m_nview;
        private Rigidbody m_body;
        private float m_speed;          // signed, m/s, along the bike's forward
        private float m_yaw;            // heading in degrees
        private float m_throttle;       // -1..1 from the rider
        private float m_steer;          // -1..1 from the rider
        private bool m_boost, m_blip;
        private float m_lean;
        private float m_steerSmoothed;
        private float m_nextLog;
        private Quaternion m_visualBaseRotation = Quaternion.identity;
        private Quaternion m_frontBaseRot, m_rearBaseRot;
        private Vector3 m_frontAxle = Vector3.right, m_rearAxle = Vector3.right;
        private float m_wheelAngle;
        private float m_lastInputTime;
        private bool m_haveValidUser;
        private Player m_riderWithIK;
        private EngineAudio m_audio;
        private BikeFuel m_fuel;
        private Vector3 m_gripLeftBase, m_gripRightBase;
        public Transform m_bags;                     // the saddlebags (BikePrefabSetup)
        public Container Storage { get; private set; }
        /// <summary>Someone has the saddlebags open: on their machine the chest says so, everyone else reads the flag it shares.</summary>
        public bool BagsOpen => Storage != null && (Storage.IsInUse() || (m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1));
        public BikeFuel Fuel => m_fuel;

        public float SyncedSpeed => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetFloat(s_speedHash) : 0f;
        public bool OnWaterSynced => m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(s_waterHash);   // every client (the wake)
        public float SyncedThrottle => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetFloat(s_throttleHash) : 0f;
        public bool EngineOn => m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(s_engineHash);
        public bool BlipRequested { get; private set; }
        public bool HasRider => GetUser() != 0L;
        public bool IsOnWater { get; private set; }   // set each physics tick on the machine that simulates the bike (the rider's)
        public float m_forceThrottle = float.NaN;   // harness only: throttle applied after the player's own input each tick
        public int m_forceWheelie = -1;              // harness only: -1 the rider's key, 0 off, 1 held
        public bool m_forceBoost;                    // harness only: Shift held
        public float m_forceSteer = float.NaN;       // harness only: steering applied after the player's own input each tick

        // steering: the fork, bars and front wheel ("Steer" in the model) turn about the steering axis, most at walking pace
        private Transform m_steerPart;
        private Vector3 m_steerPivot, m_steerAxis, m_steerBasePos;
        private Quaternion m_steerBaseRot;
        private float m_barAngle, m_barLock;
        /// <summary>How far the rider's hands let the bars turn to the right and to the left, degrees: RiderPose stops an outside grip from swinging beyond reach.</summary>
        public float m_barReachRight = 90f, m_barReachLeft = 90f;
        public float BarAngle => m_barAngle;
        /// <summary>The lock at the current speed, degrees (before the rider's reach).</summary>
        public float BarLock => m_barLock;
        public Transform SteerPart => m_steerPart;
        /// <summary>The bars' turn as a rotation in the world (none for a bike without a steering part): the hands turn with it.</summary>
        public Quaternion BarTurn => m_steerPart != null && m_steerPart.parent != null ? Quaternion.AngleAxis(m_barAngle, m_steerPart.parent.TransformDirection(m_steerAxis)) : Quaternion.identity;
        /// <summary>Where a point carried by the bars would be with the bars at `angle` instead of their current angle (world space).</summary>
        public Vector3 BarPointAt(Vector3 point, float angle)
        {
            if (m_steerPart == null || m_steerPart.parent == null) return point;
            var parent = m_steerPart.parent; Vector3 pivot = parent.TransformPoint(m_steerPivot);
            return pivot + Quaternion.AngleAxis(angle - m_barAngle, parent.TransformDirection(m_steerAxis)) * (point - pivot);
        }

        // wheelie: the visible bike pitches up around the rear axle. Holding the key with throttle lifts the front, gravity pulls it
        // down (less the higher it is), so it is kept up by feathering the key. Past the top it is set back down: no fall, no damage.
        private const float WheelieLift = 560f, WheelieGravity = 430f, WheelieDamping = 3.2f, WheelieMax = 62f;
        private float m_pitch, m_pitchVel, m_wheelieUp;
        private bool m_wheelieHeld, m_wheelieRecover;
        private static float s_bestWheelie;
        private static readonly int s_pitchHash = "mm_pitch".GetStableHashCode();
        private Vector3 m_visualBasePos, m_pivot;
        public float Pitch => m_pitch;
        public static float BestWheelie => s_bestWheelie;   // the rider's longest timed wheelie this session (harness check)
        public float Speed => m_speed;                        // signed, m/s, along the bike's forward (the owner's own value)
        public bool Standing => Mathf.Abs(m_speed) < 0.5f && Mathf.Abs(SyncedSpeed) < 0.5f;   // at rest (filling the tank needs this), on any client

        // water: a bike has to keep cruising. Slower it settles lower (faster the slower it goes), cruising again it climbs back
        // up; swamped (the bike under the surface) the engine floods and it breaks apart. In shallow water it just sits on the bottom.
        public bool m_noSink;                                 // harness only: hold the bike up (the soundtrack test idles on water)
        private const float SwampDepth = 1.25f;               // metres under its riding height: the whole bike is under water
        private float m_sink, m_swampedAt = -1f, m_blockedTime;
        private float m_actualSpeed;                          // how fast the bike really moved along its forward, averaged over a few steps
        private Vector3 m_lastPos;
        public static float BlockedFraction = 0.4f;           // pressed against something and moving at less than this share of its speed = blocked
        private float m_frontContactAt = -10f;

        /// <summary>Something in the way: a contact whose normal pushes the bike back against its direction of travel (not the ground).</summary>
        private void OnCollisionStay(Collision info)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            Vector3 travel = transform.forward * (m_speed >= 0f ? 1f : -1f);
            for (int i = 0; i < info.contactCount; i++)
                if (Vector3.Dot(info.GetContact(i).normal, travel) < -0.5f) { m_frontContactAt = Time.time; return; }
        }
        public float SinkDepth => m_sink;
        public bool Swamped => m_swampedAt >= 0f;
        /// <summary>The bike's own axes (x right, y up, z forward, metres) carried by the visible bike: equal to the root at rest,
        /// tilted in a wheelie, leaned in turns. The model itself is not a good frame: it carries the -90 degree import rotation.</summary>
        public Transform BikeFrame { get; private set; }

        private Vector3 m_seatBase;

        /// <summary>Seat point = measured seat position + the config offsets, so the pose can be tuned with "motor reload".</summary>
        public void ApplySeatOffset()
        {
            var d = D; if (d == null) return;
            // the grips are placed in the bike's frame at rest: straighten the bars first (the next physics step turns them back)
            if (m_steerPart != null) { m_steerPart.localRotation = m_steerBaseRot; m_steerPart.localPosition = m_steerBasePos; }
            PlaceBags();
            Vector3 seat = m_seatBase + new Vector3(d.SeatOffsetSide.Value, d.SeatOffsetUp.Value, d.SeatOffsetForward.Value);
            if (m_seatMarker == null && m_visual != null) { m_seatMarker = new GameObject("SeatMarker").transform; m_seatMarker.SetParent(m_visual, false); }
            if (m_seatMarker != null) m_seatMarker.position = transform.TransformPoint(seat);
            if (m_attachPoint != null) m_attachPoint.position = transform.TransformPoint(seat);
            var g = new Vector3(MotorModPlugin.GripOffsetSide.Value + d.GripOffsetSide.Value, MotorModPlugin.GripOffsetUp.Value + d.GripOffsetUp.Value, MotorModPlugin.GripOffsetForward.Value + d.GripOffsetForward.Value);
            if (m_gripLeft != null) m_gripLeft.position = transform.TransformPoint(m_gripLeftBase + new Vector3(-g.x, g.y, g.z));
            if (m_gripRight != null) m_gripRight.position = transform.TransformPoint(m_gripRightBase + new Vector3(g.x, g.y, g.z));
            // footpegs relative to the seat, per bike
            if (m_visual != null)
            {
                if (m_footLeft == null) { m_footLeft = new GameObject("FootLeft").transform; m_footLeft.SetParent(m_visual, false); }
                if (m_footRight == null) { m_footRight = new GameObject("FootRight").transform; m_footRight.SetParent(m_visual, false); }
                var f = new Vector3(d.FootSide.Value, d.FootUp.Value, d.FootForward.Value);
                m_footLeft.position = transform.TransformPoint(new Vector3(seat.x - f.x, f.y, seat.z + f.z));
                m_footRight.position = transform.TransformPoint(new Vector3(seat.x + f.x, f.y, seat.z + f.z));
            }
        }

        /// <summary>
        /// Saddlebags: one each side of the rear wheel, their back just outside the bike at that spot (measured from the bike's
        /// own meshes, so pipes and fenders push them out), at the per-bike height, position and size from the config.
        /// The Saddlebags object sits in the bike's frame at rest (set up so), so these are bike-space positions.
        /// </summary>
        public void PlaceBags()
        {
            if (m_bags == null || BikePrefabSetup.BagSize == Vector3.zero) return;
            var d = D;
            float scale = d != null && d.BagScale != null ? Mathf.Clamp(d.BagScale.Value, 0.3f, 2f) : 0.85f;
            Vector3 size = BikePrefabSetup.BagSize * scale;                               // depth (x), height (y), width (z)
            Vector3 axle = m_wheelRear != null ? transform.InverseTransformPoint(m_wheelRear.position) : new Vector3(0f, 0.35f, -1f);
            float cy = axle.y + m_wheelRadius * 0.9f + (d != null && d.BagUp != null ? d.BagUp.Value : 0f);
            float cz = axle.z + (d != null && d.BagForward != null ? d.BagForward.Value : 0f);
            float side = BikePrefabSetup.HalfWidthAt(this, cz - size.z / 2f, cz + size.z / 2f, cy - size.y / 2f, cy + size.y / 2f) + 0.012f;
            var boxes = m_bags.GetComponents<BoxCollider>();
            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1f : 1f;
                var bag = m_bags.Find(i == 0 ? "BagLeft" : "BagRight");
                bool turn = (sign > 0f) == (BikePrefabSetup.BagBounds.center.x < 0f);          // the flap faces away from the bike
                if (bag != null) { bag.localPosition = new Vector3(sign * side, cy, cz); bag.localRotation = Quaternion.Euler(0f, turn ? 180f : 0f, 0f); bag.localScale = Vector3.one * scale; }
                if (boxes.Length > i) { boxes[i].center = new Vector3(sign * (side + size.x / 2f), cy, cz); boxes[i].size = size; }
            }
            BagsAt = new Vector3(side, cy, cz);
        }
        public Vector3 BagsAt { get; private set; }          // where the bags went: back face out from the centre line, height, along (test harness)

        private void SetLamp(bool on)
        {
            m_lampOn = on;
            bool lit = on && MotorModPlugin.Headlights.Value;
            if (m_lamp) { m_lamp.enabled = lit; m_lamp.intensity = MotorModPlugin.HeadlightIntensity.Value; m_lamp.range = MotorModPlugin.HeadlightRange.Value; }
            if (m_fill) m_fill.enabled = lit;
            if (m_glow) { m_glow.enabled = lit && MotorModPlugin.Glow.Value; m_glow.transform.localScale = Vector3.one * MotorModPlugin.GlowSize.Value; if (m_glowMat && m_glowMat.HasProperty("_TintColor")) m_glowMat.SetColor("_TintColor", new Color(LampColour.r, LampColour.g, LampColour.b, MotorModPlugin.GlowStrength.Value)); }
            foreach (var m in m_lampMats) { if (!m) continue; if (lit) m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", lit ? LampColour * 1.3f : Color.black); }
        }

        /// <summary>Materials that are obviously the lamp (glass, lens, lamp, light, reflektor, przod) get an emission when the engine runs; the pattern is a config entry.</summary>
        private void FindLampMaterials()
        {
            m_lampMats.Clear();
            if (m_visual == null) return;
            System.Text.RegularExpressions.Regex pat;
            try { pat = new System.Text.RegularExpressions.Regex(MotorModPlugin.LampMaterials.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase); } catch { return; }
            foreach (var r in m_visual.GetComponentsInChildren<Renderer>())
            {
                if (r == m_glow) continue;
                var shared = r.sharedMaterials; bool any = false;
                foreach (var m in shared) if (m && pat.IsMatch(m.name)) { any = true; break; }
                if (!any) continue;
                var mats = r.materials;                                   // instances for this bike only
                foreach (var m in mats) if (m && pat.IsMatch(m.name)) m_lampMats.Add(m);
            }
        }

        private Light m_fill;

        /// <summary>A real headlight: the lens faces forward (seen head-on, edge-on from the side, so never a ball), the beam points at the road
        /// a few metres ahead, and a short fill light lights the ground right in front of the wheel.</summary>
        private void AimHeadlight()
        {
            if (m_headlight == null) return;
            m_headlight.localRotation = Quaternion.identity;
            if (m_lamp)
            {
                m_lamp.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(9f, transform.right) * transform.forward, transform.up);
                m_lamp.type = LightType.Spot; m_lamp.spotAngle = 70f; m_lamp.innerSpotAngle = 35f;
                m_lamp.renderMode = LightRenderMode.ForcePixel; m_lamp.color = LampColour;
            }
            if (m_glow)
            {
                var g = m_glow.transform;
                g.rotation = Quaternion.LookRotation(-transform.forward, transform.up);   // quad front faces forward along the bike
                g.position = m_headlight.position + transform.forward * 0.04f;
            }
            if (m_fill == null)
            {
                var go = new GameObject("HeadlightFill"); go.transform.SetParent(m_headlight, false); go.transform.localPosition = new Vector3(0f, 0f, 0.6f);
                m_fill = go.AddComponent<Light>(); m_fill.type = LightType.Point; m_fill.range = 4f; m_fill.intensity = 1.2f; m_fill.color = LampColour;
                m_fill.renderMode = LightRenderMode.ForcePixel; m_fill.shadows = LightShadows.None; m_fill.enabled = false;
            }
        }

        /// <summary>Removes the bike without returning anything (console and test harness only; breaking goes through BikeHealth).</summary>
        public void Remove()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            m_nview.ClaimOwnership();
            ZNetScene.instance.Destroy(gameObject);
        }

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_audio = GetComponent<EngineAudio>();
            m_fuel = GetComponent<BikeFuel>();
            Storage = m_bags != null ? m_bags.GetComponent<Container>() : null;
            if (m_attachPoint != null) m_seatBase = transform.InverseTransformPoint(m_attachPoint.position);
            if (m_gripLeft != null) m_gripLeftBase = transform.InverseTransformPoint(m_gripLeft.position);
            if (m_gripLeft != null && m_gripRight != null)
            {
                // the two grips must mirror each other or the rider twists; the outer one is the real bar end
                var gr = transform.InverseTransformPoint(m_gripRight.position);
                if (Mathf.Abs(m_gripLeftBase.y - gr.y) > 0.02f || Mathf.Abs(m_gripLeftBase.z - gr.z) > 0.02f || Mathf.Abs(Mathf.Abs(m_gripLeftBase.x) - Mathf.Abs(gr.x)) > 0.02f)
                {
                    var best = Mathf.Abs(m_gripLeftBase.x) >= Mathf.Abs(gr.x) ? m_gripLeftBase : gr;
                    MotorModPlugin.Log.LogWarning($"{gameObject.name}: grips asymmetric (L {m_gripLeftBase} R {gr}); mirroring the outer end");
                    m_gripLeftBase = new Vector3(-Mathf.Abs(best.x), best.y, best.z);
                    m_gripRight.position = transform.TransformPoint(new Vector3(Mathf.Abs(best.x), best.y, best.z));
                }
            }
            if (m_gripRight != null) m_gripRightBase = transform.InverseTransformPoint(m_gripRight.position);
            if (m_headlight != null)
            {
                m_lamp = m_headlight.GetComponent<Light>();
                var glowT = m_headlight.Find("Glow"); m_glow = glowT ? glowT.GetComponent<Renderer>() : null;
                if (m_glow) m_glowMat = m_glow.material;
                FindLampMaterials();
                if (m_lamp) m_lamp.enabled = false; SetLamp(false);
            }
            var wheel = m_wheelFront ? m_wheelFront.GetComponent<Renderer>() : null;
            if (wheel != null) m_wheelRadius = Mathf.Max(0.1f, wheel.bounds.extents.y);
            m_body = GetComponent<Rigidbody>();
            m_yaw = transform.eulerAngles.y;
            if (m_visual)
            {
                m_visualBaseRotation = m_visual.localRotation; m_visualBasePos = m_visual.localPosition;
                var frame = new GameObject("BikeFrame").transform;
                frame.SetParent(m_visual, false);
                frame.SetPositionAndRotation(transform.position, transform.rotation);   // the visible bike is at rest here, so this is the root's frame
                BikeFrame = frame;
            }
            m_pivot = m_wheelRear != null ? transform.InverseTransformPoint(m_wheelRear.position) : new Vector3(0f, 0.35f, -1f);   // the rear axle
            SetupSteering();
            // each wheel's axle in its parent's space, taken before any lean: the bike's sideways axis as the wheel sits now
            if (m_wheelFront) { m_frontBaseRot = m_wheelFront.localRotation; m_frontAxle = m_wheelFront.parent ? m_wheelFront.parent.InverseTransformDirection(transform.right).normalized : Vector3.right; }
            if (m_wheelRear) { m_rearBaseRot = m_wheelRear.localRotation; m_rearAxle = m_wheelRear.parent ? m_wheelRear.parent.InverseTransformDirection(transform.right).normalized : Vector3.right; }
            AimHeadlight();
            PlaceBags();
            if (m_nview == null || !m_nview.IsValid()) return;
            m_nview.Register<long>("RequestControl", RPC_RequestControl);
            m_nview.Register<long>("ReleaseControl", RPC_ReleaseControl);
            m_nview.Register<bool>("RequestRespons", RPC_RequestRespons);
            m_nview.Register<Vector3>("Controls", RPC_Controls);
            m_nview.Register("Blip", RPC_Blip);
        }

        // ---------- Interactable / Hoverable ----------
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || m_nview == null || !m_nview.IsValid()) return false;
            if (!InUseDistance(user)) return false;
            var player = user as Player;
            if (player == null) return false;
            if (alt) { if (m_fuel != null && BikeFuel.Enabled) m_fuel.Fill(player); else m_nview.InvokeRPC("RequestControl", player.GetZDOID().UserID); return true; }   // Shift+E (the game's alternate use)
            if (BagsOpen) { player.Message(MessageHud.MessageType.Center, "Someone has the saddlebags open"); return false; }
            m_nview.InvokeRPC("RequestControl", player.GetZDOID().UserID);
            return false;
        }

        /// <summary>A hotbar item used on the bike: the bike's fuel fills the tank.</summary>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => m_fuel != null && user is Player p && InUseDistance(p) && m_fuel.UseItem(p, item);

        public string GetHoverText()
        {
            if (!InUseDistance(Player.m_localPlayer)) return Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
            var text = new System.Text.StringBuilder(m_hoverText);
            if (m_fuel != null && BikeFuel.Enabled) text.Append($"\nFuel {m_fuel.Percent}");
            var health = GetComponent<BikeHealth>();
            if (health != null) text.Append($"\nCondition {Mathf.CeilToInt(health.Fraction * 100f)}%");
            text.Append("\n[<color=yellow><b>$KEY_Use</b></color>] Ride");
            if (m_fuel != null && BikeFuel.Enabled && Standing) text.Append(m_fuel.FillHint).Append(m_fuel.HotbarHint);   // a rolling bike takes no fuel
            return Localization.instance.Localize(text.ToString());
        }

        public string GetHoverName() => m_hoverText;
        public float GetHoverOffset() => 0f;

        // ---------- IDoodadController ----------
        public bool IsValid() => this;
        public Component GetControlledComponent() => this;
        public Vector3 GetPosition() => m_attachPoint != null ? m_attachPoint.position : transform.position;

        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
        {
            bool wheelie = !Harness.HarnessMode.Enabled && MotorModPlugin.WheelieKey != null && ZInput.GetKey(MotorModPlugin.WheelieKey.Value, false);   // the harness drives it itself
            var packed = new Vector3(moveDir.x, moveDir.z, (run ? 1f : 0f) + (block ? 2f : 0f) + (wheelie ? 4f : 0f));
            if (m_nview.IsOwner()) SetInputs(packed);
            else m_nview.InvokeRPC("Controls", packed);
        }

        public void OnUseStop(Player player)
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            m_nview.InvokeRPC("ReleaseControl", player.GetZDOID().UserID);
            if (m_attachPoint != null) player.AttachStop();
        }

        // ---------- RPCs ----------
        private void RPC_RequestControl(long sender, long playerID)
        {
            if (!m_nview.IsOwner()) return;
            CalculateHaveValidUser();
            if ((GetUser() == playerID || !m_haveValidUser) && !BagsOpen)
            {
                var zdo = m_nview.GetZDO();
                zdo.Set(ZDOVars.s_user, playerID);
                zdo.Set(s_engineHash, m_fuel == null || !m_fuel.Empty);        // an empty tank: you can sit on it, the engine will not start
                ResetInputs();
                m_nview.InvokeRPC(sender, "RequestRespons", true);
                zdo.SetOwner(sender);          // the rider simulates the bike from now on
            }
            else m_nview.InvokeRPC(sender, "RequestRespons", false);
        }

        private void RPC_ReleaseControl(long sender, long playerID)
        {
            if (!m_nview.IsOwner() || GetUser() != playerID) return;
            var zdo = m_nview.GetZDO();
            zdo.Set(ZDOVars.s_user, 0L);
            zdo.Set(s_engineHash, false);
            ResetInputs();
        }

        private void RPC_RequestRespons(long sender, bool granted)
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            if (!granted) { player.Message(MessageHud.MessageType.Center, "$msg_inuse"); return; }
            player.StartDoodadControl(this);
            ApplySeatOffset();
            if (m_fuel != null && m_fuel.Empty) player.Message(MessageHud.MessageType.Center, "Out of fuel: get off and fill the tank (Shift+E)");
            if (m_attachPoint != null)
                player.AttachStart(m_attachPoint, gameObject, hideWeapons: true, isBed: false, onShip: false, (D != null ? D.CfgPose.Value : "attach_lox"), m_detachOffset);
        }

        private void RPC_Controls(long sender, Vector3 packed)
        {
            if (m_nview.IsOwner()) SetInputs(packed);
        }

        private void RPC_Blip(long sender) { BlipRequested = true; }

        private void SetInputs(Vector3 packed)
        {
            m_steer = Mathf.Clamp(packed.x, -1f, 1f);
            m_throttle = Mathf.Clamp(packed.y, -1f, 1f);
            int flags = Mathf.RoundToInt(packed.z);
            m_boost = (flags & 1) != 0;
            bool blip = (flags & 2) != 0;
            if (blip && !m_blip) m_nview.InvokeRPC(ZNetView.Everybody, "Blip");
            m_blip = blip;
            m_wheelieHeld = (flags & 4) != 0;
            m_lastInputTime = Time.time;
        }

        private void ResetInputs() { m_steer = 0f; m_throttle = 0f; m_boost = false; m_blip = false; m_wheelieHeld = false; }

        // ---------- simulation ----------
        private void FixedUpdate()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            float dt = Time.fixedDeltaTime;
            if (!m_nview.IsOwner())
            {
                m_speed = SyncedSpeed;
                m_pitch = Mathf.MoveTowards(m_pitch, m_nview.GetZDO().GetFloat(s_pitchHash), 400f * dt);
                UpdateVisuals(dt, m_nview.GetZDO().GetFloat(s_steerHash));
                return;
            }
            CalculateHaveValidUser();
            if (!m_haveValidUser || Time.time - m_lastInputTime > 1f) ResetInputs();
            if (!float.IsNaN(m_forceThrottle) && m_haveValidUser) { m_throttle = m_forceThrottle; m_lastInputTime = Time.time; }   // only with a rider: an empty bike never drives off
            if (m_forceBoost && m_haveValidUser) m_boost = true;
            if (!float.IsNaN(m_forceSteer) && m_haveValidUser) { m_steer = m_forceSteer; m_lastInputTime = Time.time; }

            // ground probe under the bike, and the water surface: the bike rides on water like a hovercraft
            bool grounded = Physics.Raycast(transform.position + Vector3.up * 0.6f, Vector3.down, out var hit, 1.4f, s_groundMask);
            Vector3 groundNormal = grounded ? hit.normal : Vector3.up;
            float water = Floating.GetLiquidLevel(transform.position + Vector3.up * 0.3f, 0.5f, LiquidType.Water);
            bool onWater = water > -9000f && (!grounded || water > hit.point.y + 0.3f);
            IsOnWater = onWater;
            if (onWater) { grounded = true; groundNormal = Vector3.up; }

            bool dry = m_fuel != null && m_fuel.Empty;
            if (dry)
            {
                m_throttle = 0f;                                   // no fuel: no throttle, so no wheelie and no planing either
                if (EngineOn) { m_nview.GetZDO().Set(s_engineHash, false); RiderMessage(MessageHud.MessageType.Center, "Out of fuel"); }
            }
            UpdateWheelie(dt, grounded);
            UpdateSinking(onWater, water);
            if (!m_nview.IsValid()) return;                        // it just sank and broke apart
            if (Swamped) m_throttle = 0f;                          // flooded: the engine is dead
            var d = D;
            float topSpeed = d != null ? d.CfgTopSpeed.Value : 16f, accelBase = d != null ? d.CfgAcceleration.Value : 5f, brake = d != null ? d.CfgBrakeForce.Value : 9f;
            float turnSlow = d != null ? d.CfgTurnRateSlow.Value : 60f, turnFast = d != null ? d.CfgTurnRateFast.Value : 28f, boostMul = d != null ? d.CfgBoostMultiplier.Value : 1.2f, terrain = d != null ? Mathf.Clamp01(d.CfgTerrain.Value) : 0.5f;

            // blocked (a wall, a creature in the way): the speed follows what the bike really does, so pushing against something
            // never builds a phantom speed (it made a creature right in front take full-speed rams)
            // measured from where the bike really got to, averaged over a few steps: ground friction makes the velocity of any
            // single physics step swing a lot (7.95 m/s asked, 3.2 measured), so only a sustained shortfall counts as blocked
            Vector3 along = m_body.rotation * Vector3.forward;
            if (m_lastPos == Vector3.zero) m_lastPos = m_body.position;
            float stepMoved = Vector3.Dot(m_body.position - m_lastPos, along) / dt;
            Vector3 stepFlat = m_body.position - m_lastPos; stepFlat.y = 0f;
            float stepDistance = Mathf.Min(stepFlat.magnitude, 40f * dt);         // ground really covered this step (a teleport is not driving)
            m_lastPos = m_body.position;
            m_actualSpeed = Mathf.Lerp(m_actualSpeed, Mathf.Clamp(stepMoved, -40f, 40f), 0.25f);
            // blocked = something touching the front (or the back when reversing) and the bike hardly getting anywhere. Friction
            // alone keeps a bike at walking pace far below its own speed (3.1 asked, 0.7 real), so the contact is what decides.
            float want = Mathf.Abs(m_speed);
            bool pressed = Time.time - m_frontContactAt < 0.1f;
            if (pressed && want > 3f && Mathf.Abs(m_actualSpeed) < want * BlockedFraction) m_blockedTime += dt; else m_blockedTime = 0f;
            if (m_blockedTime > 0.15f) m_speed = Mathf.MoveTowards(m_speed, m_actualSpeed, 40f * dt);

            // longitudinal speed; climbing costs the low-terrain bikes more
            float top = topSpeed * (m_boost ? boostMul : 1f);
            float accel;
            if (m_throttle > 0.5f) accel = accelBase * (m_boost ? 1.25f : 1f) * (1f - 0.6f * Mathf.Clamp01(m_sink / SwampDepth));   // a half-sunk bike drags
            else if (m_throttle < -0.5f) accel = m_speed > 0.3f ? -brake : -accelBase * 0.6f;
            else accel = -Mathf.Sign(m_speed) * Mathf.Min(MotorModPlugin.Coasting.Value, Mathf.Abs(m_speed) / dt);
            if (!grounded) accel = 0f;
            float slope = Mathf.Clamp01(Vector3.Dot(m_body.rotation * Vector3.forward, Vector3.up) * Mathf.Sign(m_speed));   // >0 when nose-up in the direction of travel
            accel -= slope * 6f * (1f - 0.7f * terrain) * Mathf.Sign(m_speed == 0f ? 1f : m_speed);
            // bumps: a sudden change of ground normal shakes low-terrain bikes and bleeds speed
            float bump = grounded && !onWater ? Vector3.Angle(m_lastNormal, groundNormal) : 0f; m_lastNormal = groundNormal;
            if (bump > 6f && Mathf.Abs(m_speed) > 3f) m_speed *= 1f - Mathf.Clamp01(bump / 40f) * (1f - terrain) * 0.35f;
            m_speed = Mathf.Clamp(m_speed + accel * dt, -MotorModPlugin.ReverseSpeed.Value, top);
            if (Mathf.Abs(m_speed) < 0.05f && Mathf.Abs(m_throttle) < 0.5f) m_speed = 0f;
            // fuel goes by the ground really covered under throttle (boost burns faster); coasting and standing still burn nothing
            if (m_fuel != null && m_haveValidUser && Mathf.Abs(m_throttle) > 0.1f)
                m_fuel.Burn(stepDistance * (m_boost ? Mathf.Max(1f, MotorModPlugin.BoostBurn.Value) : 1f));

            // heading: steering eases in and out, turns slower at speed, reversed when backing up
            m_steerSmoothed = Mathf.MoveTowards(m_steerSmoothed, m_steer, MotorModPlugin.SteerResponse.Value * dt);
            if (Mathf.Abs(m_speed) > 0.3f && grounded)
            {
                float t = Mathf.Clamp01(Mathf.Abs(m_speed) / topSpeed);
                float rate = Mathf.Lerp(turnSlow, turnFast, t);
                rate *= 1f - 0.6f * Mathf.Clamp01(m_pitch / 40f);        // front wheel in the air: steer by leaning only
                m_yaw += m_steerSmoothed * rate * dt * Mathf.Sign(m_speed);
            }
            if (MotorModPlugin.Verbose && Time.time >= m_nextLog && (Mathf.Abs(m_throttle) > 0.1f || Mathf.Abs(m_steer) > 0.1f || Mathf.Abs(m_speed) > 0.1f))
            {
                m_nextLog = Time.time + 0.5f;
                MotorModPlugin.Log.LogInfo($"ride[{(d != null ? d.Key : "?")}]: throttle {m_throttle:+0.00} steer {m_steer:+0.00} boost {m_boost} speed {m_speed:+0.00} (moved {m_actualSpeed:+0.00}) grounded {grounded} water {onWater} yaw {m_yaw:0} audio {(m_audio ? m_audio.Audible() : "-")}");
            }
            var heading = Quaternion.Euler(0f, m_yaw, 0f);
            var target = Quaternion.FromToRotation(Vector3.up, groundNormal) * heading;
            m_body.angularVelocity = Vector3.zero;
            m_body.MoveRotation(Quaternion.Slerp(m_body.rotation, target, Mathf.Lerp(5f, 12f, terrain) * dt));

            // velocity along the (slope-aligned) forward; let gravity act when airborne; a hop on sharp edges for the stiff bikes
            Vector3 fwd = m_body.rotation * Vector3.forward;
            Vector3 v = fwd * m_speed;
            if (onWater) v.y = Mathf.Clamp((water - 0.10f - m_sink - transform.position.y) * 5f, -3f, 4f);
            else if (grounded) v.y += bump > 10f && terrain < 0.6f && Mathf.Abs(m_speed) > 4f ? 0.6f * (1f - terrain) : -1.5f;
            else v.y = m_body.linearVelocity.y;
            m_body.linearVelocity = v;

            var zdo = m_nview.GetZDO();
            zdo.Set(s_speedHash, m_speed);
            zdo.Set(s_waterHash, IsOnWater);
            zdo.Set(s_pitchHash, m_pitch);
            zdo.Set(s_throttleHash, m_throttle);
            zdo.Set(s_steerHash, m_steerSmoothed);
            UpdateVisuals(dt, m_steerSmoothed);
        }

        private void UpdateSinking(bool onWater, float water)
        {
            float dt = Time.fixedDeltaTime;
            if (!onWater || m_noSink || !MotorModPlugin.Sinking.Value)
            {
                m_sink = Mathf.MoveTowards(m_sink, 0f, dt);        // out of the water (or sinking off): simply up again
                return;
            }
            if (!Swamped)
            {
                // it planes only at boost speed (Shift+W, past 60 % of its top speed) or with the front up in a wheelie;
                // anything else sinks: fastest standing still, slower when cruising without boost
                var d = D;
                float top = d != null ? d.CfgTopSpeed.Value : 16f, seconds = Mathf.Max(0.5f, MotorModPlugin.SinkSeconds.Value);
                float speed = Mathf.Abs(m_speed);
                bool planing = (m_boost && m_throttle > 0.5f && speed > 0.6f * top) || m_pitch > 8f;
                if (planing) m_sink -= 0.5f * dt;
                else m_sink += SwampDepth / seconds * (1f - (1f - Mathf.Clamp01(MotorModPlugin.CruiseSinkRate.Value)) * Mathf.Clamp01(speed / top)) * dt;
                m_sink = Mathf.Clamp(m_sink, 0f, SwampDepth);
                // swamped: sunk all the way and really under the surface (in shallow water it rests on the bottom instead)
                if (m_sink >= SwampDepth - 0.01f && water - transform.position.y >= SwampDepth - 0.1f)
                {
                    m_swampedAt = Time.time;
                    m_nview.GetZDO().Set(s_engineHash, false);     // the engine floods: sound, lights and trail go out
                    RiderMessage(MessageHud.MessageType.Center, "Your bike is swamped!");
                }
            }
            else if (Time.time - m_swampedAt > 1.5f)
            {
                m_swampedAt = float.MaxValue;                      // once
                var health = GetComponent<BikeHealth>();
                if (health != null) health.BreakApart("sank");
            }
        }

        /// <summary>A message for this bike's rider, shown only on the rider's own screen.</summary>
        internal void RiderMessage(MessageHud.MessageType type, string text)
        {
            var p = Player.m_localPlayer;
            if (p != null && GetUser() == p.GetZDOID().UserID) p.Message(type, text);
        }

        private void UpdateWheelie(float dt, bool grounded)
        {
            var d = D;
            float ease = d != null && d.CfgWheelieEase != null ? Mathf.Clamp(d.CfgWheelieEase.Value, 0.5f, 2f) : 1f, k = 1f / ease;
            bool want = m_forceWheelie >= 0 ? m_forceWheelie == 1 : m_wheelieHeld;
            bool lift = want && !m_wheelieRecover && grounded && m_throttle > 0.3f && m_haveValidUser;
            float acc = ((lift ? WheelieLift : 0f) - WheelieGravity * Mathf.Cos(m_pitch * Mathf.Deg2Rad) - (m_throttle < -0.5f ? 300f : 0f)) * k * k
                        - WheelieDamping * k * m_pitchVel;
            m_pitchVel += acc * dt;
            m_pitch += m_pitchVel * dt;
            if (m_pitch <= 0f) { m_pitch = 0f; if (m_pitchVel < 0f || !lift) m_pitchVel = 0f; }
            if (m_pitch > WheelieMax) { m_pitch = WheelieMax; m_pitchVel = -110f; m_wheelieRecover = true; }   // over the top: set it down, nothing else
            if (m_wheelieRecover && m_pitch < 5f) m_wheelieRecover = false;
            if (m_pitch > 8f) m_wheelieUp += dt;
            else if (m_pitch < 2f && m_wheelieUp > 0f)
            {
                float up = m_wheelieUp; m_wheelieUp = 0f;
                if (up >= 2f && MotorModPlugin.WheelieTimer.Value && Player.m_localPlayer != null && GetUser() == Player.m_localPlayer.GetZDOID().UserID)
                {
                    bool best = up > s_bestWheelie; if (best) s_bestWheelie = up;
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Wheelie {up:0.0} s{(best ? ", best yet" : "")}");
                }
            }
        }

        /// <summary>The model's steering part and its axis (two markers the build put at the front axle and the steering head), in
        /// the space the part moves in, taken at rest.</summary>
        private void SetupSteering()
        {
            if (m_visual == null) return;
            Transform part = null, bottom = null, top = null;
            foreach (var t in m_visual.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Steer") part = t; else if (t.name == "SteerAxisBottom") bottom = t; else if (t.name == "SteerAxisTop") top = t;
            }
            if (part == null || bottom == null || top == null || part.parent == null) return;
            var parent = part.parent;
            m_steerPivot = parent.InverseTransformPoint(bottom.position);
            m_steerAxis = (parent.InverseTransformPoint(top.position) - m_steerPivot).normalized;
            m_steerBasePos = part.localPosition; m_steerBaseRot = part.localRotation;
            m_steerPart = part;
        }

        private void UpdateVisuals(float dt, float steer)
        {
            m_wheelAngle = Mathf.Repeat(m_wheelAngle + m_speed / Mathf.Max(0.05f, m_wheelRadius) * Mathf.Rad2Deg * dt, 360f);
            if (m_steerPart != null)
            {
                var ds = D; float topSpeed = ds != null ? ds.CfgTopSpeed.Value : 16f;
                m_barLock = Mathf.Lerp(MotorModPlugin.BarTurn.Value, MotorModPlugin.BarTurnFast.Value, Mathf.Clamp01(Mathf.Abs(m_speed) / Mathf.Max(1f, topSpeed)));
                float want = Mathf.Clamp(Mathf.Clamp(steer, -1f, 1f) * m_barLock, -m_barReachLeft, m_barReachRight);
                m_barAngle = Mathf.MoveTowards(m_barAngle, want, 160f * dt);
                var turn = Quaternion.AngleAxis(m_barAngle, m_steerAxis);
                m_steerPart.localRotation = turn * m_steerBaseRot;
                m_steerPart.localPosition = m_steerPivot + turn * (m_steerBasePos - m_steerPivot);
            }
            if (m_wheelFront) m_wheelFront.localRotation = Quaternion.AngleAxis(m_wheelAngle, m_frontAxle) * m_frontBaseRot;
            if (m_wheelRear) m_wheelRear.localRotation = Quaternion.AngleAxis(m_wheelAngle, m_rearAxle) * m_rearBaseRot;
            if (m_visual)
            {
                var d = D; float topS = d != null ? d.CfgTopSpeed.Value : 16f, leanA = d != null ? d.CfgLeanAngle.Value : 16f;
                float speedFactor = Mathf.Clamp01(Mathf.Abs(m_speed) / Mathf.Max(1f, topS));
                float targetLean = -steer * leanA * Mathf.Sqrt(speedFactor);
                m_lean = Mathf.Lerp(m_lean, targetLean, 4f * dt);
                var pitch = Quaternion.AngleAxis(-m_pitch, Vector3.right);                     // nose up about the bike's sideways axis
                m_visual.localRotation = pitch * Quaternion.AngleAxis(m_lean, Vector3.forward) * m_visualBaseRotation;
                m_visual.localPosition = m_pivot + pitch * (m_visualBasePos - m_pivot);       // around the rear axle, so the rear wheel stays put
            }
        }

        public void ConsumeBlip() { BlipRequested = false; }

        private void Update()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            bool engine = EngineOn;
            if (engine != m_lampOn) { try { SetLamp(engine); } catch (System.Exception e) { m_lampOn = engine; MotorModPlugin.Log.LogWarning("headlight: " + e.Message); } }
            long user = GetUser();
            Player rider = null;
            if (user != 0L)
            {
                var local = Player.m_localPlayer;
                if (local && local.GetZDOID().UserID == user) rider = local;
                else foreach (var p in Player.GetAllPlayers()) if (p && p.GetZDOID().UserID == user) { rider = p; break; }
            }
            if (rider != m_riderWithIK)
            {
                if (m_riderWithIK) RiderPose.Detach(m_riderWithIK);
                m_riderWithIK = rider;
                if (rider) { ApplySeatOffset(); RiderPose.Attach(rider, this); }
            }
        }

        // ---------- helpers ----------
        private long GetUser() => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetLong(ZDOVars.s_user, 0L) : 0L;

        private void CalculateHaveValidUser()
        {
            m_haveValidUser = false;
            long user = GetUser();
            if (user == 0L) return;
            foreach (var zdo in ZNet.instance.GetAllCharacterZDOS())
            {
                if (zdo.m_uid.UserID != user) continue;
                m_haveValidUser = Vector3.Distance(zdo.GetPosition(), transform.position) < m_maxUseRange * 2f;
                break;
            }
        }

        private bool InUseDistance(Humanoid human) => human && Vector3.Distance(human.transform.position, GetPosition()) < m_maxUseRange;
    }
}
