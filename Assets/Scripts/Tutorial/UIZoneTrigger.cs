using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// Attach to the invisible trigger cube (mesh renderer off, BoxCollider with Is Trigger
    /// ticked). Detects the VR player entering and shows a UI popup.
    ///
    /// Player detection: checks the entering collider's tag. Make sure your XR rig has a
    /// collider tagged "Player" - usually on the CharacterController / capsule object under
    /// XR Origin, not the camera itself.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class EndZoneTrigger : MonoBehaviour
    {
        [Tooltip("Tag the entering collider must have. Your XR rig's collider object needs this tag set in the Inspector.")]
        [SerializeField] private string playerTag = "Player";

        [Tooltip("The popup to show. Can be a Canvas, a panel GameObject, or anything you want enabled on entry - it is just SetActive(true/false).")]
        [SerializeField] private GameObject popup;

        [Tooltip("If true, the popup is visible from scene load - use when the player spawns " +
                 "inside this volume. Don't rely on OnTriggerEnter firing for a collider that " +
                 "is already overlapping at startup; Unity is inconsistent about that.")]
        [SerializeField] private bool showOnStart = false;

        [Tooltip("If true, entering the volume shows the popup. Untick when something else " +
                 "owns showing it (e.g. a DelayedHandoff from a previous popup) and this " +
                 "trigger should only be responsible for hiding it on exit.")]
        [SerializeField] private bool showOnEnter = true;

        [Tooltip("If true, hides the popup again when the player leaves the trigger volume.")]
        [SerializeField] private bool hideOnExit = false;

        [Tooltip("If true, this only fires once - re-entering the volume after leaving does nothing.")]
        [SerializeField] private bool triggerOnce = true;

        [Tooltip("Extra hook for anything else you want to happen on entry (sound, scene event, etc).")]
        public UnityEvent onPlayerEntered;

        [Tooltip("Fires when the player leaves the volume, whether or not hideOnExit is set.")]
        public UnityEvent onPlayerExited;

        private bool _hasFired;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void Start()
        {
            if (popup == null) return;

            popup.SetActive(showOnStart);

            // Count the initial display as the one firing, so triggerOnce stops it coming
            // back if the player wanders out and returns.
            if (showOnStart) _hasFired = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasFired && triggerOnce) return;
            if (!other.CompareTag(playerTag)) return;

            _hasFired = true;

            if (popup != null && showOnEnter) popup.SetActive(true);
            onPlayerEntered?.Invoke();

            Debug.Log($"[EndZoneTrigger] Player entered '{name}'", this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;

            onPlayerExited?.Invoke();

            if (hideOnExit && popup != null) popup.SetActive(false);
        }
    }
}