using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// Invisible volume that reports the player entering or leaving. Replaces both of the
    /// existing trigger scripts:
    ///
    ///   - UIZoneTrigger.cs (which confusingly declared a class called EndZoneTrigger), whose
    ///     popup / showOnStart / showOnEnter / hideOnExit fields all became dead once the panel
    ///     stopped being switched on and off.
    ///   - EndZoneTrigger.cs, which only logged.
    ///
    /// Now it does one job - detect the player - and hands off through UnityEvents. Point
    /// onPlayerEntered at TutorialFlow.GoTo / Advance, or at anything else.
    ///
    /// Player detection: the XRPlayerRig instance in Bootstrap is tagged "Player" and carries
    /// the CharacterController, so that is the collider that enters. Note the tag is a scene
    /// override on the instance, not on the prefab asset - opening Tutorial.unity standalone
    /// gives you an untagged rig and this will never fire. See requirePlayerTag below.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class TutorialZoneTrigger : MonoBehaviour
    {
        [Header("Detection")]
        [Tooltip("Tag the entering collider must have.")]
        [SerializeField] private string playerTag = "Player";

        [Tooltip("Untick to accept ANY collider. Useful when testing this scene on its own, " +
                 "where the rig has no Player tag because the tag lives on the Bootstrap instance.")]
        [SerializeField] private bool requirePlayerTag = true;

        [Tooltip("If true, only fires once - re-entering after leaving does nothing.")]
        [SerializeField] private bool triggerOnce = true;

        [Header("Events")]
        [Tooltip("Player entered the volume. Wire to TutorialFlow.Advance or GoTo.")]
        public UnityEvent onPlayerEntered;

        [Tooltip("Player left the volume.")]
        public UnityEvent onPlayerExited;

        [Header("Debug")]
        [Tooltip("Log entries and exits to the console.")]
        [SerializeField] private bool logEvents = false;

        [Tooltip("Colour of the wire box drawn in the Scene view. The volume has no renderer, " +
                 "so without this it is invisible while authoring.")]
        [SerializeField] private Color gizmoColour = new Color(0.2f, 0.8f, 1f, 0.35f);

        private bool _hasFired;
        private BoxCollider _box;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();

            // A non-trigger collider here would block the player instead of detecting them,
            // and it is an easy thing to knock off by accident in the Inspector.
            if (_box != null && !_box.isTrigger)
            {
                Debug.LogWarning($"[TutorialZoneTrigger] '{name}' has Is Trigger unticked - " +
                                 "the player will collide with it instead of entering it.", this);
            }
        }

        /// <summary>Allows the zone to fire again after triggerOnce has consumed it.</summary>
        public void ResetTrigger() => _hasFired = false;

        private bool Matches(Collider other)
        {
            return !requirePlayerTag || other.CompareTag(playerTag);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasFired && triggerOnce) return;
            if (!Matches(other)) return;

            _hasFired = true;
            onPlayerEntered?.Invoke();

            if (logEvents) Debug.Log($"[TutorialZoneTrigger] entered '{name}'", this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!Matches(other)) return;

            onPlayerExited?.Invoke();

            if (logEvents) Debug.Log($"[TutorialZoneTrigger] exited '{name}'", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            BoxCollider box = _box != null ? _box : GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = gizmoColour;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(gizmoColour.r, gizmoColour.g, gizmoColour.b, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
#endif
    }
}
