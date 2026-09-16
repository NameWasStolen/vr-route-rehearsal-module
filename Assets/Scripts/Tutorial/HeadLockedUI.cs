using System.Collections;
using UnityEngine;

namespace VRTutorial
{
    /// <summary>
    /// Keeps a world-space UI canvas (or any object) positioned at a fixed offset from the
    /// player's head and facing them - a "body-locked" HUD popup.
    ///
    /// Attach to the root of a World Space Canvas. Assign the XR camera (the one under
    /// XR Origin -> Camera Offset -> Main Camera) as headTransform.
    ///
    /// Offset is defined in the head's local space: X = right, Y = up, Z = forward.
    /// "2 m forward, slightly left" is roughly (-0.3, 0, 2).
    /// </summary>
    public class HeadLockedUI : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The XR camera to follow. If left empty, uses Camera.main.")]
        [SerializeField] private Transform headTransform;

        [Header("Placement")]
        [Tooltip("Offset from the head, in the head's local space. Z is forward, X is right, Y is up.")]
        [SerializeField] private Vector3 localOffset = new Vector3(-0.3f, 0f, 2f);

        [Header("Rotation")]
        [Tooltip("If true, the panel always faces the player. If false, it keeps a fixed " +
                 "world rotation and only its position follows.")]
        [SerializeField] private bool billboardToPlayer = true;

        [Tooltip("Ignore head pitch/roll when billboarding, so the panel stays upright " +
                 "instead of tilting when the player looks up or down.")]
        [SerializeField] private bool lockUpright = true;

        [Tooltip("Extra rotation applied on top of the billboard, in degrees. Y yaws the panel " +
                 "left/right, X pitches it (negative tilts the top toward you - useful when the " +
                 "panel sits above eye level), Z rolls it. Leave at zero to face the player squarely.")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Header("Comfort")]
        [Tooltip("0 = instantly welded to the head (can feel nauseating). Higher values lag " +
                 "behind head movement, which reads as more comfortable and less 'stuck to your face'. " +
                 "0.12-0.20 is a good starting range for a tutorial popup.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float followSmoothTime = 0.15f;

        [Tooltip("Degrees per second cap on how fast the panel can turn to follow. Prevents a " +
                 "fast head-snap from spinning the panel instantly.")]
        [SerializeField] private float maxRotationSpeed = 180f;

        [Tooltip("Snap instantly instead of easing when the head yaws by a large amount in a " +
                 "single frame - i.e. a snap turn or a teleport. Without this the panel swings " +
                 "through an arc to catch up and faces the wrong way while it does.")]
        [SerializeField] private bool autoSnapOnLargeTurn = true;

        [Tooltip("Single-frame yaw change that counts as a snap rather than natural head " +
                 "movement. 20 is safely above anything a human neck produces in one frame.")]
        [SerializeField] private float snapYawThreshold = 20f;

        [Header("Obstacle avoidance")]
        [Tooltip("Pull the panel toward the participant when scenery would otherwise pass through " +
                 "it - a hedge, a fence, or the ground when they look down.")]
        [SerializeField] private bool avoidObstacles = true;

        [Tooltip("What counts as scenery. Set this to the environment layers only.\n\n" +
                 "If the player's own collider is included, the cast hits the body immediately and " +
                 "the panel sits at Min Distance permanently - a panel stuck too close is the " +
                 "symptom of this mask being wrong, not of the feature being broken.")]
        [SerializeField] private LayerMask obstacleLayers = ~0;

        [Tooltip("Half-width of the panel, in metres, used as the cast radius. A thin ray would " +
                 "let a fence post slide through a corner of the panel while the centre stayed " +
                 "clear, which looks worse than a panel that simply moved.")]
        [SerializeField] private float panelRadius = 0.3f;

        [Tooltip("Metres kept between the panel and whatever it found.")]
        [SerializeField] private float clearance = 0.15f;

        [Tooltip("The panel never comes nearer than this, however tight the space. Text at arm's " +
                 "length is uncomfortable, and for this cohort a panel that lunges is worse than " +
                 "one that clips.")]
        [SerializeField] private float minDistance = 0.55f;

        [Header("Freezing")]
        [Tooltip("Degrees the head may turn away from where the panel was frozen before it " +
                 "recentres. A frozen panel is a stable pointer target, which is the whole point, " +
                 "but one left behind the participant is a panel they cannot find.")]
        [SerializeField] private float refreezeYawThreshold = 50f;

        [Tooltip("Metres the participant may walk from where the panel was frozen before it " +
                 "recentres.")]
        [SerializeField] private float refreezeMoveDistance = 0.8f;

        [Tooltip("Seconds to ease the panel to its new resting place when it recentres. Long " +
                 "enough to read as the panel following them, not as a teleport.")]
        [SerializeField] private float refreezeDuration = 0.35f;

        private Vector3 _velocity; // used by SmoothDamp
        private float _lastHeadYaw;
        private float _frozenHeadYaw;
        private Vector3 _frozenHeadPos;
        private Coroutine _recentreRoutine;

        /// <summary>
        /// Time.unscaledTime of the last snap/teleport reposition. TutorialFlow waits for this
        /// to go quiet before starting a transition - a cross-fade beginning on the same frame
        /// as a teleport reads as two glitches at once.
        /// Initialised far in the past so nothing is gated during the first frames of the scene.
        /// </summary>
        public float LastSnapTimeUnscaled { get; private set; } = -999f;

        private void Reset()
        {
            if (Camera.main != null) headTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            TryResolveHead();

            // Snap to the correct spot immediately on enable, rather than smoothing in from
            // wherever the panel happened to be left in the editor.
            if (headTransform != null)
            {
                transform.position = TargetPosition();
                transform.rotation = TargetRotation();
            }
        }

        /// <summary>
        /// Resolves the head transform (and the Canvas's Event Camera) via Camera.main.
        /// Called from OnEnable and, until it succeeds, from every LateUpdate - multi-scene
        /// setups don't guarantee this scene's objects enable after the camera's scene has
        /// finished loading, so a single failed attempt at startup must not be permanent.
        /// </summary>
        private void TryResolveHead()
        {
            if (headTransform == null && Camera.main != null)
                headTransform = Camera.main.transform;

            var canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.worldCamera == null && headTransform != null)
                canvas.worldCamera = headTransform.GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (headTransform == null) TryResolveHead();
            if (headTransform == null) return;

            // A snap turn or teleport moves the head far enough in one frame that easing
            // toward the new target looks like the panel swinging around the player. Jump
            // instead, so the panel is simply already in the right place afterwards.
            float yaw = headTransform.eulerAngles.y;
            float yawDelta = Mathf.DeltaAngle(_lastHeadYaw, yaw);
            _lastHeadYaw = yaw;

            // Frozen: the panel holds its world pose so it can be pointed at. Yaw is still
            // tracked above, so unfreezing later does not see a huge delta and fire a snap.
            if (IsFrozen)
            {
                HandleFrozenDrift(yaw);
                return;
            }

            if (autoSnapOnLargeTurn && Mathf.Abs(yawDelta) >= snapYawThreshold)
            {
                SnapToTarget();
                return;
            }

            Vector3 targetPos = TargetPosition();
            Quaternion targetRot = TargetRotation();

            if (followSmoothTime <= 0f)
            {
                transform.position = targetPos;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position, targetPos, ref _velocity, followSmoothTime);
            }

            if (billboardToPlayer)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, maxRotationSpeed * Time.deltaTime);
            }
        }

        private Vector3 _nudge;

        /// <summary>Authored offset plus any temporary nudge.</summary>
        private Vector3 EffectiveOffset => localOffset + _nudge;

        private Vector3 TargetPosition()
        {
            Vector3 desired = headTransform.TransformPoint(EffectiveOffset);
            if (!avoidObstacles) return desired;

            Vector3 origin = headTransform.position;
            Vector3 to = desired - origin;
            float distance = to.magnitude;
            if (distance < 1e-4f) return desired;

            Vector3 direction = to / distance;

            // Start the cast at the closest the panel is ever allowed to be, so the sphere is
            // already clear of the participant's own body. Casting from the head itself would
            // start overlapping the character controller and report an immediate hit.
            float start = Mathf.Min(minDistance, distance);
            float length = distance - start;
            if (length <= 0f) return desired;

            if (Physics.SphereCast(origin + direction * start, panelRadius, direction,
                                   out RaycastHit hit, length, obstacleLayers,
                                   QueryTriggerInteraction.Ignore))
            {
                float allowed = Mathf.Max(minDistance, start + hit.distance - clearance);
                if (allowed < distance) return origin + direction * allowed;
            }

            return desired;
        }

        private Quaternion TargetRotation()
        {
            if (!billboardToPlayer) return transform.rotation;

            Vector3 toPlayer = transform.position - headTransform.position;
            if (lockUpright) toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude < 0.0001f) return transform.rotation;

            // Face the player: the canvas's +Z (front face) must point away from the head,
            // so that the readable side is what the head is looking at. The offset is applied
            // afterwards, in the panel's own space, so it reads as "tilt relative to facing".
            Quaternion facing = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            return facing * Quaternion.Euler(rotationOffset);
        }

        /// <summary>
        /// Where the panel sits relative to the head, in head-local space (X right, Y up,
        /// Z forward). Assigning EASES rather than jumps: the SmoothDamp follow below simply
        /// treats it as a new target, so a step change slides the panel into place.
        /// </summary>
        public Vector3 LocalOffset
        {
            get => localOffset;
            set => localOffset = value;
        }

        /// <summary>
        /// A temporary shift added on top of the authored offset, in the same head-local space.
        ///
        /// Deliberately separate from LocalOffset rather than something callers assign directly.
        /// A caller that wrote LocalOffset would have to remember the authored value to put it
        /// back, and the authored value is a serialised field somebody may well retune in the
        /// Inspector between the push and the pop. A nudge is additive and always undone by
        /// clearing it, so the two cannot drift apart.
        ///
        /// Not serialised: this is runtime-only state and should never be saved into the scene.
        /// </summary>
        public Vector3 Nudge
        {
            get => _nudge;
            set => _nudge = value;
        }

        /// <summary>
        /// Shifts the panel vertically. Metres, negative is down.
        ///
        /// A float UnityEvent target, so a step or a controller can lower a panel out of the way
        /// of another one without a glue script - and because assigning the offset only moves the
        /// SmoothDamp target, the panel slides rather than jumps.
        /// </summary>
        public void NudgeY(float metres) => _nudge = new Vector3(_nudge.x, metres, _nudge.z);

        /// <summary>Shifts the panel sideways. Metres, negative is left.</summary>
        public void NudgeX(float metres) => _nudge = new Vector3(metres, _nudge.y, _nudge.z);

        /// <summary>
        /// Shifts the panel nearer or further. Metres, positive is further away.
        ///
        /// Useful in combination with the other two: pushing a panel back shrinks how much of
        /// the view it covers, so it needs less sideways or downward travel to clear something.
        /// The cost is legibility, which for this cohort runs out quickly past about two metres.
        /// </summary>
        public void NudgeZ(float metres) => _nudge = new Vector3(_nudge.x, _nudge.y, metres);

        /// <summary>Returns the panel to its authored offset.</summary>
        public void ClearNudge() => _nudge = Vector3.zero;

        /// <summary>
        /// Extra rotation applied on top of the billboard, in degrees. X pitches (positive
        /// leans the top away from you, for a panel below eye level), Y yaws, Z rolls.
        /// </summary>
        public Vector3 RotationOffset
        {
            get => rotationOffset;
            set => rotationOffset = value;
        }

        /// <summary>
        /// True while the panel is holding a fixed world pose instead of following the head.
        /// </summary>
        public bool IsFrozen { get; private set; }

        /// <summary>
        /// Places the panel correctly, then leaves it there.
        ///
        /// For anything the participant has to aim at. The follow below smooth-damps with a lag
        /// that is comfortable for reading and wrong for pointing: a button that drifts as the
        /// head moves is a moving target, and a harder one than it looks for an older participant.
        /// Use this when a menu opens, and Unfreeze when it closes.
        /// </summary>
        public void FreezeAtCurrent()
        {
            TryResolveHead();
            SnapToTarget();
            IsFrozen = true;
            CaptureFrozenReference();
        }

        /// <summary>Returns the panel to following the head.</summary>
        public void Unfreeze()
        {
            if (_recentreRoutine != null)
            {
                StopCoroutine(_recentreRoutine);
                _recentreRoutine = null;
            }
            IsFrozen = false;
        }

        private void CaptureFrozenReference()
        {
            if (headTransform == null) return;
            _frozenHeadYaw = headTransform.eulerAngles.y;
            _frozenHeadPos = headTransform.position;
        }

        /// <summary>
        /// A frozen panel still has to come back if the participant turns around or walks off,
        /// otherwise the menu is simply lost behind them and the only way out is to guess.
        /// </summary>
        private void HandleFrozenDrift(float yaw)
        {
            if (_recentreRoutine != null) return;

            bool turnedAway = Mathf.Abs(Mathf.DeltaAngle(_frozenHeadYaw, yaw)) > refreezeYawThreshold;
            bool walkedAway = (headTransform.position - _frozenHeadPos).sqrMagnitude >
                              refreezeMoveDistance * refreezeMoveDistance;

            if (turnedAway || walkedAway) _recentreRoutine = StartCoroutine(Recentre());
        }

        private IEnumerator Recentre()
        {
            Vector3 fromPos = transform.position;
            Quaternion fromRot = transform.rotation;

            float t = 0f;
            while (t < refreezeDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / refreezeDuration);
                k = k * k * (3f - 2f * k);   // smoothstep, no velocity jump at either end

                transform.position = Vector3.Lerp(fromPos, TargetPosition(), k);
                transform.rotation = Quaternion.Slerp(fromRot, TargetRotation(), k);
                yield return null;
            }

            transform.position = TargetPosition();
            transform.rotation = TargetRotation();
            _velocity = Vector3.zero;

            CaptureFrozenReference();
            _recentreRoutine = null;
        }

        /// <summary>Call after teleporting the player to avoid a visible slide as the panel catches up.</summary>
        public void SnapToTarget()
        {
            if (headTransform == null) return;
            transform.position = TargetPosition();
            transform.rotation = TargetRotation();
            _velocity = Vector3.zero;
            _lastHeadYaw = headTransform.eulerAngles.y;
            LastSnapTimeUnscaled = Time.unscaledTime;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (headTransform == null) return;
            Gizmos.color = Color.cyan;
            Vector3 target = headTransform.TransformPoint(EffectiveOffset);
            Gizmos.DrawLine(headTransform.position, target);
            Gizmos.DrawWireSphere(target, 0.05f);
        }
#endif
    }
}
