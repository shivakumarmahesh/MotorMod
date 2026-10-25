using UnityEngine;

namespace MotorMod
{
    /// <summary>
    /// Procedural riding pose, applied after the animation each frame on the rider's Animator object:
    /// 1. the pelvis is pinned to the bike's seat marker (the game's attach pose puts it elsewhere per bike),
    /// 2. the torso leans forward, automatically more if the grips are out of reach,
    /// 3. hands go onto the grips (two-bone IK), 4. feet go onto the footpeg markers, or down onto the ground beside a
    /// standing bike (two-bone IK).
    /// Works for the local rider and for other players seen riding.
    /// </summary>
    public class RiderPose : MonoBehaviour
    {
        public MotorcycleController m_bike;
        private Player m_player;
        private Animator m_anim;
        private Transform m_hips, m_spine, m_chest, m_lUpper, m_lLower, m_lHand, m_lPalm, m_rUpper, m_rLower, m_rHand, m_rPalm;
        private Transform m_lThigh, m_lShin, m_lFoot, m_rThigh, m_rShin, m_rFoot, m_lShoulder, m_rShoulder, m_neck, m_head;
        private Transform[] m_lFingers, m_rFingers;   // proximal, intermediate, distal for index, middle, ring, little
        private Transform m_lToes, m_rToes;
        public float m_footSurfaceL = float.NaN, m_footSurfaceR = float.NaN;
        private float m_weight, m_autoLean, m_mountTime;
        private float m_feetDown, m_groundGapL = float.NaN, m_groundGapR = float.NaN;   // 0 feet on the pegs .. 1 both on the ground; cm each ankle sits higher than flat on the ground
        private float m_groundYL = float.NaN, m_groundYR = float.NaN;                   // the ground under each foot (world y), for the harness
        private bool m_seatAligned;
        private float m_seatTopLocal = float.NaN, m_sitDepth = 0.10f;      // seat surface (bike space y) under the hips, hips-bone height above the buttocks
        public string m_seatSource = "marker";

        public static RiderPose Attach(Player player, MotorcycleController bike)
        {
            var anim = player.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return null;
            var p = anim.GetComponent<RiderPose>() ?? anim.gameObject.AddComponent<RiderPose>();
            p.m_anim = anim; p.m_bike = bike; p.m_player = player; p.m_weight = 0f; p.m_autoLean = 0f; p.m_feetDown = 0f; p.m_mountTime = Time.time; p.m_seatAligned = false;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            p.m_hips = B(HumanBodyBones.Hips); p.m_spine = B(HumanBodyBones.Spine); p.m_chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            p.m_lUpper = B(HumanBodyBones.LeftUpperArm); p.m_lLower = B(HumanBodyBones.LeftLowerArm); p.m_lHand = B(HumanBodyBones.LeftHand); p.m_lPalm = B(HumanBodyBones.LeftMiddleProximal) ?? p.m_lHand;
            p.m_rUpper = B(HumanBodyBones.RightUpperArm); p.m_rLower = B(HumanBodyBones.RightLowerArm); p.m_rHand = B(HumanBodyBones.RightHand); p.m_rPalm = B(HumanBodyBones.RightMiddleProximal) ?? p.m_rHand;
            p.m_lThigh = B(HumanBodyBones.LeftUpperLeg); p.m_lShin = B(HumanBodyBones.LeftLowerLeg); p.m_lFoot = B(HumanBodyBones.LeftFoot);
            p.m_rThigh = B(HumanBodyBones.RightUpperLeg); p.m_rShin = B(HumanBodyBones.RightLowerLeg); p.m_rFoot = B(HumanBodyBones.RightFoot);
            p.m_lShoulder = B(HumanBodyBones.LeftShoulder); p.m_rShoulder = B(HumanBodyBones.RightShoulder);
            p.m_neck = B(HumanBodyBones.Neck); p.m_head = B(HumanBodyBones.Head);
            p.m_lToes = B(HumanBodyBones.LeftToes); p.m_rToes = B(HumanBodyBones.RightToes);
            p.m_lFingers = new[] { B(HumanBodyBones.LeftIndexProximal), B(HumanBodyBones.LeftIndexIntermediate), B(HumanBodyBones.LeftIndexDistal), B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftMiddleIntermediate), B(HumanBodyBones.LeftMiddleDistal), B(HumanBodyBones.LeftRingProximal), B(HumanBodyBones.LeftRingIntermediate), B(HumanBodyBones.LeftRingDistal), B(HumanBodyBones.LeftLittleProximal), B(HumanBodyBones.LeftLittleIntermediate), B(HumanBodyBones.LeftLittleDistal) };
            p.m_rFingers = new[] { B(HumanBodyBones.RightIndexProximal), B(HumanBodyBones.RightIndexIntermediate), B(HumanBodyBones.RightIndexDistal), B(HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.RightMiddleIntermediate), B(HumanBodyBones.RightMiddleDistal), B(HumanBodyBones.RightRingProximal), B(HumanBodyBones.RightRingIntermediate), B(HumanBodyBones.RightRingDistal), B(HumanBodyBones.RightLittleProximal), B(HumanBodyBones.RightLittleIntermediate), B(HumanBodyBones.RightLittleDistal) };
            // this rig maps no finger bones: the knuckle is the transform named *HandMiddle1 under the hand, the fingers are everything else below it
            if (p.m_lPalm == p.m_lHand) p.m_lPalm = FindChild(p.m_lHand, "Middle1") ?? p.m_lHand;
            if (p.m_rPalm == p.m_rHand) p.m_rPalm = FindChild(p.m_rHand, "Middle1") ?? p.m_rHand;
            if (System.Array.TrueForAll(p.m_lFingers, f => f == null)) p.m_lFingers = FingerTransforms(p.m_lHand, p.m_lPalm);
            if (System.Array.TrueForAll(p.m_rFingers, f => f == null)) p.m_rFingers = FingerTransforms(p.m_rHand, p.m_rPalm);
            p.enabled = true;
            return p;
        }

        public static void Detach(Player player)
        {
            var anim = player ? player.GetComponentInChildren<Animator>() : null;
            var p = anim ? anim.GetComponent<RiderPose>() : null;
            if (p) Destroy(p);
        }

        public Transform LeftPalm => m_lPalm;
        public Transform RightPalm => m_rPalm;
        private int m_appliedFrame = -1;
        private Transform[] m_touched = new Transform[0];
        private Quaternion[] m_rawRot = new Quaternion[0], m_setRot = new Quaternion[0];
        public int m_restoredFrames;   // diagnostics: frames where the animator had not re-sampled and the raw pose was restored by us

        private void CollectTouched()
        {
            var list = new System.Collections.Generic.List<Transform> { m_spine, m_chest, m_neck, m_head, m_lShoulder, m_rShoulder, m_lUpper, m_lLower, m_lHand, m_rUpper, m_rLower, m_rHand, m_lThigh, m_lShin, m_lFoot, m_rThigh, m_rShin, m_rFoot };
            if (m_lFingers != null) list.AddRange(m_lFingers);
            if (m_rFingers != null) list.AddRange(m_rFingers);
            list.RemoveAll(x => x == null);
            m_touched = list.ToArray(); m_rawRot = new Quaternion[m_touched.Length]; m_setRot = new Quaternion[m_touched.Length];
            for (int i = 0; i < m_touched.Length; i++) { m_rawRot[i] = m_touched[i].localRotation; m_setRot[i] = m_rawRot[i]; }
        }

        private void LateUpdate() => Apply();

        /// <summary>One line for logs: weight, lean and how far the hips sit from the seat marker.</summary>
        public string Status()
        {
            float hipsOff = m_hips && m_bike && m_bike.m_seatMarker ? Vector3.Distance(m_hips.position, m_bike.m_seatMarker.position) : -1f;
            return $"weight {m_weight:0.00} autoLean {m_autoLean:0.0} seatAligned {m_seatAligned} hipsToSeat {hipsOff:0.00} m sinceMount {Time.time - m_mountTime:0.0} s";
        }

        /// <summary>Applies the riding pose once per frame (safe to call again from a camera before it renders).</summary>
        public void Apply()
        {
            if (m_appliedFrame == Time.frameCount) return;
            m_appliedFrame = Time.frameCount;
            if (m_bike == null || m_hips == null) return;
            // the Animator re-samples the pose only on its own ticks; between ticks the bones still hold what we set last frame,
            // and applying our relative corrections again would stack them (the flicker). Restore the raw pose first in that case.
            if (m_touched.Length == 0) CollectTouched();
            bool animatorRan = m_touched.Length == 0 || m_touched[0].localRotation != m_setRot[0] || m_touched[m_touched.Length - 1].localRotation != m_setRot[m_touched.Length - 1];
            if (!animatorRan) { for (int i = 0; i < m_touched.Length; i++) m_touched[i].localRotation = m_rawRot[i]; m_restoredFrames++; }
            else for (int i = 0; i < m_touched.Length; i++) m_rawRot[i] = m_touched[i].localRotation;
            try { ApplyPose(); }
            finally { for (int i = 0; i < m_touched.Length; i++) m_setRot[i] = m_touched[i].localRotation; }
        }

        private void ApplyPose()
        {
            if (m_bike == null || m_hips == null) return;
            var d = m_bike.m_definition; var root = Frame;     // the bike's axes as the rider feels them: tilted in a wheelie, leaned in turns
            float dt = Time.deltaTime;
            if (m_player && m_bike.m_attachPoint && m_player.IsAttached())
                m_player.transform.SetPositionAndRotation(m_bike.m_attachPoint.position, m_bike.m_attachPoint.rotation);   // re-seat every frame, not only on physics ticks
            m_weight = Mathf.MoveTowards(m_weight, 1f, dt * 3f);
            Vector3 fwd = root.forward, up = root.up, right = root.right;

            // 1. pelvis on the seat: measured once the attach pose has blended in, then the attach point is moved so the hips land on the seat marker
            if (!m_seatAligned && Time.time - m_mountTime > 0.6f && m_bike.m_attachPoint != null && m_bike.m_seatMarker != null)
            {
                // the hips go to the seat surface measured on the bike's own mesh under the rider (the marker is the fallback), plus the rider's sit depth
                Vector3 local = root.InverseTransformPoint(m_bike.m_seatMarker.position);
                float top = SeatTop(root, local, 0.06f);                       // small radius: a tank edge or seat hump next to the hips must not count
                m_seatTopLocal = top; m_sitDepth = MotorModPlugin.SitDepth.Value; m_seatSource += SitDepthNote();
                float extra = d != null ? d.SeatOffsetUp.Value : 0f;
                Vector3 want = float.IsNaN(top) ? m_bike.m_seatMarker.position + up * (m_sitDepth + extra) : root.TransformPoint(new Vector3(local.x, top + m_sitDepth + extra, local.z));
                m_bike.m_attachPoint.position += want - m_hips.position;
                m_seatAligned = true;
            }

            // 2. torso lean: base lean per bike plus whatever is needed to reach the grips
            float lean = (d != null ? d.CfgLean.Value : 10f) + m_autoLean;
            if (m_spine) m_spine.rotation = Quaternion.AngleAxis(lean * 0.5f * m_weight, right) * m_spine.rotation;
            if (m_chest && m_chest != m_spine) m_chest.rotation = Quaternion.AngleAxis(lean * 0.5f * m_weight, right) * m_chest.rotation;
            // the shoulders follow the bars into a turn, which brings the outside shoulder forward to its grip
            float twist = Twist(m_bike.BarAngle);
            if (m_spine) m_spine.rotation = Quaternion.AngleAxis(twist * 0.4f, up) * m_spine.rotation;
            if (m_chest && m_chest != m_spine) m_chest.rotation = Quaternion.AngleAxis(twist * 0.6f, up) * m_chest.rotation;
            // the rider looks down the road, not at the tank: the neck and head take most of the lean back out
            float lookUp = Mathf.Min(lean * 0.8f, 40f) * m_weight;          // enough to look down the road without cranking the neck
            if (m_neck) m_neck.rotation = Quaternion.AngleAxis(-lookUp * 0.45f, right) * m_neck.rotation;
            if (m_head) m_head.rotation = Quaternion.AngleAxis(-lookUp * 0.55f, right) * m_head.rotation;

            // 3. hands on the grips; if a grip is beyond arm reach, lean more next frame
            if (MotorModPlugin.HandsOnBars.Value && m_bike.m_gripLeft && m_bike.m_gripRight)
            {
                float reach = ArmReach(m_lUpper, m_lLower, m_lPalm);
                float need = Mathf.Max((m_bike.m_gripLeft.position - m_lUpper.position).magnitude, (m_bike.m_gripRight.position - m_rUpper.position).magnitude);
                float target = need > reach * 0.97f ? m_autoLean + (need - reach * 0.97f) * 90f : m_autoLean - 20f * dt;
                float leanMax = d != null ? Mathf.Clamp(d.CfgLeanMax.Value, 0f, 70f) : 30f;
                m_autoLean = Mathf.Clamp(Mathf.MoveTowards(m_autoLean, target, 60f * dt), 0f, leanMax);
                // still short with the lean capped: roll the shoulders toward the grips (up to 25 degrees) before the arms stretch
                Shoulder(m_lShoulder, m_lUpper, m_bike.m_gripLeft.position, reach);
                Shoulder(m_rShoulder, m_rUpper, m_bike.m_gripRight.position, reach);
                // the knuckles sit half a hand inboard of the bar end and just above the tube, the hand lies over the bar, then the fingers close;
                // all of it in the bars' own axes, which turn with the steering
                var bar = m_bike.BarTurn; Vector3 bf = bar * fwd, br = bar * right, bu = bar * up;
                Grip(m_lHand, m_lPalm, bf, bu); Grip(m_rHand, m_rPalm, bf, bu);   // wrist first: the solve then aims the knuckle as it will be rendered
                // in a turn the outside grip swings forward: that hand may slide in along its grip, and the bars turn no further than it can follow
                Vector3 inward = (m_bike.m_gripRight.position - m_bike.m_gripLeft.position).normalized;
                float holdL = ArmReach(m_lUpper, m_lLower, m_lPalm), holdR = ArmReach(m_rUpper, m_rLower, m_rPalm);   // with the wrists bent over the bar
                Vector3 lt = Follow(m_lUpper.position, m_bike.m_gripLeft.position + br * 0.06f + bu * 0.02f, inward, holdL, 1f, out float reachRight);
                Vector3 rt = Follow(m_rUpper.position, m_bike.m_gripRight.position - br * 0.06f + bu * 0.02f, -inward, holdR, -1f, out float reachLeft);
                m_bike.m_barReachRight = reachRight; m_bike.m_barReachLeft = reachLeft;
                Solve(m_lUpper, m_lLower, m_lPalm, lt, m_bike.m_gripLeft.position - br * 0.6f - bu * 0.5f, m_weight);
                Solve(m_rUpper, m_rLower, m_rPalm, rt, m_bike.m_gripRight.position + br * 0.6f - bu * 0.5f, m_weight);
                Curl(m_lFingers, bu); Curl(m_rFingers, bu);                  // fists around the grips instead of open hands
            }
            else { m_bike.m_barReachRight = 90f; m_bike.m_barReachLeft = 90f; }

            // 4. feet: on the pegs while riding, both down on the ground beside the bike while it stands still; knees out and forward either way
            if (m_bike.m_footLeft && m_bike.m_footRight && m_lThigh && m_rThigh)
            {
                float ankle = MotorModPlugin.AnkleHeight.Value;
                Vector3 fl = FootTarget(m_bike.m_footLeft.position, ref m_footSurfaceL), fr = FootTarget(m_bike.m_footRight.position, ref m_footSurfaceR);
                // both feet rest at one height: the higher surface found (an exhaust or stay under one foot must not split the stance); one side found serves both
                bool hasL = !float.IsNaN(m_footSurfaceL) && !float.IsNegativeInfinity(m_footSurfaceL), hasR = !float.IsNaN(m_footSurfaceR) && !float.IsNegativeInfinity(m_footSurfaceR);
                if (hasL || hasR)
                {
                    float y = hasL && hasR ? Mathf.Max(m_footSurfaceL, m_footSurfaceR) : (hasL ? m_footSurfaceL : m_footSurfaceR);
                    Vector3 ll = root.InverseTransformPoint(fl), rr = root.InverseTransformPoint(fr); ll.y = y; rr.y = y;
                    fl = root.TransformPoint(ll); fr = root.TransformPoint(rr);
                }
                fl += up * ankle; fr += up * ankle;
                Vector3 toesL = fwd - up * 0.1f, toesR = toesL;                 // soles flat on the pegs, toes forward (the ankle itself stays put)
                bool standing = Mathf.Abs(m_bike.Speed) < 0.5f && !m_bike.IsOnWater && !m_bike.OnWaterSynced && Mathf.Abs(m_bike.Pitch) < 5f;
                m_feetDown = Mathf.MoveTowards(m_feetDown, standing ? 1f : 0f, dt * 2.5f);
                m_groundGapL = m_groundGapR = float.NaN;
                if (m_feetDown > 0f)
                {
                    float leg = (m_lShin.position - m_lThigh.position).magnitude + (m_lFoot.position - m_lShin.position).magnitude;
                    float toe = m_lToes ? (m_lToes.position - m_lFoot.position).magnitude : 0.15f;   // ankle to the ball of the foot
                    GroundFoot(m_lThigh.position, -right, fwd, ankle, leg, toe, ref fl, ref toesL, ref m_groundGapL, ref m_groundYL);
                    GroundFoot(m_rThigh.position, right, fwd, ankle, leg, toe, ref fr, ref toesR, ref m_groundGapR, ref m_groundYR);
                }
                Solve(m_lThigh, m_lShin, m_lFoot, fl, fl - right * 0.25f + fwd * 0.5f + up * 0.6f, m_weight);
                Solve(m_rThigh, m_rShin, m_rFoot, fr, fr + right * 0.25f + fwd * 0.5f + up * 0.6f, m_weight);
                Level(m_lFoot, m_lToes, toesL); Level(m_rFoot, m_rToes, toesR);
            }
        }

        /// <summary>The configured peg point, dropped or lifted onto the bike surface found under it (rail, peg, board) when one exists within 12 cm below or 6 cm above; measured once per mount.</summary>
        /// <summary>The bike's frame for the pose (falls back to the root on a bike without a visible model).</summary>
        private Transform Frame => m_bike.BikeFrame != null ? m_bike.BikeFrame : m_bike.transform;

        private Vector3 FootTarget(Vector3 peg, ref float surface)
        {
            var root = Frame;
            if (float.IsNaN(surface))
            {
                Vector3 local = root.InverseTransformPoint(peg);
                float top = SeatTop(root, new Vector3(local.x, local.y - 0.17f, local.z), 0.10f);   // SeatTop looks 0.35 below to 0.25 above the given y: here 0.52 below to 0.08 above the peg point
                surface = float.IsNaN(top) || top > local.y + 0.03f || top < local.y - 0.2f ? float.NegativeInfinity : top;   // snap down onto a rail or peg; never climb onto a plate above the peg point
            }
            if (float.IsNegativeInfinity(surface)) return peg;
            Vector3 l = root.InverseTransformPoint(peg); l.y = surface;
            return root.TransformPoint(l);
        }

        private static Transform FindChild(Transform root, string nameEnd)
        {
            if (!root) return null;
            foreach (var tr in root.GetComponentsInChildren<Transform>()) if (tr != root && tr.name.EndsWith(nameEnd)) return tr;
            return null;
        }

        /// <summary>Every transform below the hand bone except the thumb and the IK knuckle itself, in parent-first order; these curl as finger joints.</summary>
        private static Transform[] FingerTransforms(Transform hand, Transform knuckle)
        {
            var list = new System.Collections.Generic.List<Transform>();
            if (!hand) return list.ToArray();
            foreach (var tr in hand.GetComponentsInChildren<Transform>())
            {
                if (tr == hand) continue;
                string n = tr.name.ToLowerInvariant();
                if (n.EndsWith("_end") || n.Contains("thumb") || n.Contains("attach") || n.Contains("weapon") || n.Contains("shield") || n.Contains("tool")) continue;
                list.Add(tr);
            }
            return list.ToArray();
        }

        /// <summary>The ankle turns so the toes point the given way (along the bike and level on a peg, down to the ground on tiptoe) instead of hanging.</summary>
        private void Level(Transform foot, Transform toes, Vector3 toeDir)
        {
            if (!foot || !toes) return;
            Vector3 have = toes.position - foot.position; if (have.sqrMagnitude < 1e-8f || toeDir.sqrMagnitude < 1e-8f) return;
            foot.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(have, toeDir.normalized), m_weight) * foot.rotation;
        }

        /// <summary>
        /// A foot put down beside the standing bike: the ground under a spot 26 cm out from the hip (a ray that ignores the bike's
        /// own colliders), the ankle just above it, blended in by m_feetDown. Ground further than the straight leg reaches is
        /// tiptoed: the foot goes as far as it can and angles down until the ball of the foot rests on the ground. With no ground to
        /// find the foot stays on its peg.
        /// </summary>
        private void GroundFoot(Vector3 hip, Vector3 side, Vector3 fwd, float ankle, float leg, float toe, ref Vector3 target, ref Vector3 toes, ref float gap, ref float groundOut)
        {
            Vector3 from = hip + side * 0.26f + fwd * 0.08f + Vector3.up * 0.3f;
            float groundY = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 2.2f, MotorcycleController.s_groundMask))
                if (!h.collider.transform.IsChildOf(m_bike.transform) && h.point.y > groundY) groundY = h.point.y;
            if (float.IsNegativeInfinity(groundY)) return;
            Vector3 ground = new Vector3(from.x, groundY, from.z), want = ground + Vector3.up * ankle;
            Vector3 d = want - hip; float dist = d.magnitude;
            if (dist > leg) want = hip + d * (leg / dist);                                  // tiptoe: the leg straight, the ankle as low as it goes
            // the foot angles down from the ankle until the ball of the foot is 1 cm off the ground (flat when the ankle is at its height)
            float rise = Mathf.Max(0f, want.y - groundY - 0.01f);
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            Vector3 toeDir = rise < toe ? flat * Mathf.Sqrt(toe * toe - rise * rise) - Vector3.up * rise : -Vector3.up;
            target = Vector3.Lerp(target, want, m_feetDown);
            toes = Vector3.Slerp(toes.normalized, toeDir.normalized, m_feetDown);
            gap = (want.y - ankle - groundY) * 100f;                                       // 0 flat on the ground; more on tiptoe
            groundOut = groundY;
        }

        /// <summary>The hand lies over the bar: the wrist turns so the knuckles point forward and a little down, like a hand resting on a grip from above.</summary>
        private void Grip(Transform hand, Transform knuckle, Vector3 fwd, Vector3 up)
        {
            if (!hand || !knuckle || hand == knuckle) return;
            Vector3 have = knuckle.position - hand.position; if (have.sqrMagnitude < 1e-8f) return;
            Vector3 want = (fwd - up * 0.45f).normalized;
            hand.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(have, want), m_weight) * hand.rotation;
        }

        /// <summary>Fingers curl toward the ground around the bar: each finger bone turns about the axis that bends it downward.</summary>
        private static void Curl(Transform[] fingers, Vector3 up)
        {
            if (fingers == null) return;
            float[] deg = { 50f, 70f, 45f };
            bool humanoid = fingers.Length == 12;
            for (int i = 0; i < fingers.Length; i++)
            {
                var bone = fingers[i]; if (!bone) continue;
                Transform next = humanoid ? (i % 3 < 2 ? fingers[i + 1] : null) : (bone.childCount > 0 ? bone.GetChild(0) : null);
                Vector3 dir = next ? next.position - bone.position : bone.position - bone.parent.position;
                float angle = humanoid ? deg[i % 3] : 55f;
                if (dir.sqrMagnitude < 1e-8f) continue;
                Vector3 axis = Vector3.Cross(dir.normalized, -up);
                if (axis.sqrMagnitude < 1e-6f) continue;
                bone.rotation = Quaternion.AngleAxis(angle, axis.normalized) * bone.rotation;
            }
        }

        /// <summary>Highest bike-mesh vertex (bike space) within the radius of the seat point, horizontally. NaN when the meshes cannot be read.</summary>
        private float SeatTop(Transform root, Vector3 local, float radius)
        {
            float best = float.NaN; int seen = 0;
            foreach (var mf in (m_bike.m_visual ? m_bike.m_visual : root).GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh; if (mesh == null || !mesh.isReadable) continue;
                var verts = mesh.vertices; var l2w = mf.transform.localToWorldMatrix; var w2l = root.worldToLocalMatrix; float r2 = radius * radius;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 p = w2l.MultiplyPoint3x4(l2w.MultiplyPoint3x4(verts[i]));
                    float dx = p.x - local.x, dz = p.z - local.z;
                    if (dx * dx + dz * dz > r2 || p.y > local.y + 0.25f || p.y < local.y - 0.35f) continue;   // the saddle, not the tank or the frame below
                    if (float.IsNaN(best) || p.y > best) best = p.y; seen++;
                }
            }
            m_seatSource = float.IsNaN(best) ? "marker (mesh not readable)" : $"mesh ({seen} vertices)";
            return best;
        }

        /// <summary>Diagnostic only: what the baked rider mesh says about the hip joint's height above the buttocks (the config value SitDepth is what is used).</summary>
        private string SitDepthNote()
        {
            try
            {
                float lowest = float.MaxValue; Vector3 h = m_hips.position; int total = 0;
                foreach (var smr in m_anim.GetComponentsInChildren<SkinnedMeshRenderer>())    // body, clothes: the lowest point under the pelvis of any of them
                {
                    var mesh = new Mesh(); smr.BakeMesh(mesh); var verts = mesh.vertices; var l2w = smr.transform.localToWorldMatrix; total += verts.Length;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 p = l2w.MultiplyPoint3x4(verts[i]);
                        float dx = p.x - h.x, dz = p.z - h.z;
                        if (dx * dx + dz * dz > 0.15f * 0.15f || p.y > h.y || p.y < h.y - 0.3f) continue;
                        if (p.y < lowest) lowest = p.y;
                    }
                    Destroy(mesh);
                }
                return $"; rider meshes {total} verts, lowest {(lowest == float.MaxValue ? -1f : (h.y - lowest) * 100f):0.0} cm under the hips";
            }
            catch (System.Exception e) { return "; rider mesh unreadable: " + e.Message; }
        }

        /// <summary>Numbers for the harness, bike space (side, up, forward), metres: hand-to-grip and foot-to-peg gaps per side, hips above the seat surface, facing.</summary>
        public string Measure()
        {
            if (m_bike == null || m_hips == null) return "no pose";
            var root = Frame;
            string Gap(Transform bone, Transform target) { if (!bone || !target) return "n/a"; Vector3 g = root.InverseTransformVector(bone.position - target.position); return $"side {g.x * 100:+0.0} up {g.y * 100:+0.0} fwd {g.z * 100:+0.0} cm"; }
            float hipsLocalY = root.InverseTransformPoint(m_hips.position).y;
            string seat = float.IsNaN(m_seatTopLocal) ? "seat surface unknown" : $"hips {((hipsLocalY - m_seatTopLocal) * 100):0.0} cm above the seat surface (sit depth {m_sitDepth * 100:0.0} cm, {m_seatSource})";
            float yawHips = Vector3.SignedAngle(root.forward, Vector3.ProjectOnPlane(m_hips.forward, root.up), root.up);
            string bars = $"bars {m_bike.BarAngle:+0.0;-0.0} deg (lock {m_bike.BarLock:0.0}, the hands allow right {m_bike.m_barReachRight:0.0} left {m_bike.m_barReachLeft:0.0})";
            float yawChest = m_chest ? Vector3.SignedAngle(root.forward, Vector3.ProjectOnPlane(m_chest.forward, root.up), root.up) : 0f;
            float reach = ArmReach(m_lUpper, m_lLower, m_lPalm);
            float needL = m_bike.m_gripLeft ? (m_bike.m_gripLeft.position - m_lUpper.position).magnitude : 0f, needR = m_bike.m_gripRight ? (m_bike.m_gripRight.position - m_rUpper.position).magnitude : 0f;
            string FootNote(float s, Transform peg) => float.IsNaN(s) ? "" : float.IsNegativeInfinity(s) ? " (no surface under the peg point)" : $" (on surface {((s - root.InverseTransformPoint(peg.position).y) * 100):+0.0} cm from the peg point)";
            int fingers = 0; foreach (var f in m_lFingers ?? new Transform[0]) if (f) fingers++;
            string names = m_lHand ? string.Join(",", System.Array.ConvertAll(m_lHand.GetComponentsInChildren<Transform>(), x => x.name)) : "";
            return $"restoredFrames {m_restoredFrames} fingerBones {fingers} | handL {Gap(m_lPalm, m_bike.m_gripLeft)} | handR {Gap(m_rPalm, m_bike.m_gripRight)} | footL {Gap(m_lFoot, m_bike.m_footLeft)}{FootNote(m_footSurfaceL, m_bike.m_footLeft)} | footR {Gap(m_rFoot, m_bike.m_footRight)}{FootNote(m_footSurfaceR, m_bike.m_footRight)} | {seat} | yaw hips {yawHips:+0.0} chest {yawChest:+0.0} deg | reach {reach * 100:0.0} cm need L {needL * 100:0.0} R {needR * 100:0.0} | lean {m_autoLean:0.0} | {bars} | feet down {m_feetDown:0.00}{(float.IsNaN(m_groundGapL) ? "" : $", ankle higher than flat L {m_groundGapL:0.0} R {m_groundGapR:0.0} cm, toes above ground L {ToeAbove(m_lToes, m_lFoot, m_groundYL):0.0} R {ToeAbove(m_rToes, m_rFoot, m_groundYR):0.0} cm")}";
        }

        /// <summary>The toes bone's height above the ground found under that foot (the ball of the foot; a little below it is the toe tip), cm.</summary>
        private static float ToeAbove(Transform toes, Transform foot, float groundY) => ((toes ? toes : foot).position.y - groundY) * 100f;

        private void Shoulder(Transform shoulder, Transform upper, Vector3 grip, float reach)
        {
            if (!shoulder || !upper) return;
            float shortBy = (grip - upper.position).magnitude - reach * 0.99f;
            if (shortBy <= 0f) return;
            Vector3 axis = Vector3.Cross(upper.position - shoulder.position, grip - shoulder.position);
            if (axis.sqrMagnitude < 1e-6f) return;
            shoulder.rotation = Quaternion.AngleAxis(Mathf.Min(35f, shortBy * 150f) * m_weight, axis.normalized) * shoulder.rotation;
        }

        /// <summary>How far the chest turns with the bars, degrees.</summary>
        private float Twist(float barAngle) => Mathf.Clamp(barAngle, -40f, 40f) * 0.9f * m_weight;

        /// <summary>
        /// One hand through a turn: `inward` runs along the bar toward its middle, `sign` is the turn (+ right, - left) that swings this grip forward.
        /// Returns where the hand holds now: its spot, or up to 6 cm in along the grip when the arm falls short. `limit` is the furthest turn,
        /// degrees, at which the hand still holds as well as with the bars straight (1 cm of slack); the bike turns its bars no further.
        /// For each turn tried, the shoulder swings with the twist that turn would give the chest.
        /// </summary>
        private Vector3 Follow(Vector3 shoulder, Vector3 target, Vector3 inward, float reach, float sign, out float limit)
        {
            limit = 90f;
            if (m_bike.SteerPart == null) return target;
            float now = m_bike.BarAngle; Vector3 up = Frame.up;
            Vector3 spine = m_spine ? m_spine.position : shoulder, chest = m_chest ? m_chest.position : spine;
            Vector3 ShoulderAt(float turn)
            {
                float t = Twist(turn) - Twist(now); var q1 = Quaternion.AngleAxis(t * 0.4f, up); var q2 = Quaternion.AngleAxis(t * 0.6f, up);
                Vector3 c = spine + q1 * (chest - spine), s = spine + q1 * (shoulder - spine);
                return c + q2 * (s - c);
            }
            float Short(Vector3 s, Vector3 t) => (t - s).magnitude - reach;
            float straight = Short(ShoulderAt(0f), m_bike.BarPointAt(target, 0f)), slack = Mathf.Max(0f, straight) + 0.01f;
            Vector3 Slide(Vector3 s, Vector3 t, Vector3 dir) { for (int i = 0; i < 6 && Short(s, t) > slack; i++) t += dir * 0.01f; return t; }
            float prev = straight - slack;
            for (float a = 1f; a <= m_bike.BarLock + 0.5f; a += 1f)
            {
                float turn = sign * a;
                Vector3 s = ShoulderAt(turn), t = m_bike.BarPointAt(target, turn), dir = m_bike.BarPointAt(target + inward, turn) - t;
                float over = Short(s, Slide(s, t, dir)) - slack;
                if (over > 0f) { limit = a - 1f + Mathf.Clamp01(-prev / Mathf.Max(1e-4f, over - prev)); break; }
                prev = over;
            }
            return Slide(shoulder, target, inward);
        }

        private void OnDestroy()
        {
            if (m_bike != null) { m_bike.m_barReachRight = 90f; m_bike.m_barReachLeft = 90f; }
        }

        private static float ArmReach(Transform a, Transform b, Transform c) => a && b && c ? (b.position - a.position).magnitude + (c.position - b.position).magnitude : 0.6f;

        /// <summary>Exact two-bone IK: a=upper joint, b=middle joint, c=end effector. The middle joint is placed on the circle of solutions,
        /// on the pole's side, then the second bone is pointed at the target, so c lands on the target whenever it is within reach. No iteration.</summary>
        private static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float weight)
        {
            if (a == null || b == null || c == null) return;
            target = Vector3.Lerp(c.position, target, weight);
            Vector3 A = a.position, B = b.position;
            float lab = (B - A).magnitude, lcb = (c.position - B).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            Vector3 toT = target - A; float d = toT.magnitude;
            if (d < 1e-4f) return;
            d = Mathf.Clamp(d, Mathf.Abs(lab - lcb) + 0.001f, lab + lcb - 0.001f);
            Vector3 dir = toT.normalized;
            // elbow/knee direction: the pole, made perpendicular to the a->target line; fall back to the current bend, then to world up
            Vector3 n = Vector3.ProjectOnPlane(pole - A, dir);
            if (n.sqrMagnitude < 1e-4f) n = Vector3.ProjectOnPlane(B - A, dir);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.ProjectOnPlane(Vector3.up, dir);
            n.Normalize();
            float cosA = Mathf.Clamp((lab * lab + d * d - lcb * lcb) / (2f * lab * d), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 elbow = A + dir * (lab * cosA) + n * (lab * sinA);
            a.rotation = Quaternion.FromToRotation(B - A, elbow - A) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, target - b.position) * b.rotation;
        }
    }
}
