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

        private Vector3 _velocity; // used by SmoothDamp

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

        private Vector3 TargetPosition()
        {
            return headTransform.TransformPoint(localOffset);
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

        /// <summary>Call after teleporting the player to avoid a visible slide as the panel catches up.</summary>
        public void SnapToTarget()
        {
            if (headTransform == null) return;
            transform.position = TargetPosition();
            transform.rotation = TargetRotation();
            _velocity = Vector3.zero;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (headTransform == null) return;
            Gizmos.color = Color.cyan;
            Vector3 target = headTransform.TransformPoint(localOffset);
            Gizmos.DrawLine(headTransform.position, target);
            Gizmos.DrawWireSphere(target, 0.05f);
        }
#endif
    }
}
