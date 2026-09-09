using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// Waits a moment, then hides one popup and shows the next. Call Begin() from a UnityEvent
    /// (e.g. SnapTurnTask.onCompleted) - UnityEvents can't delay on their own.
    ///
    /// Safe to put on the popup being hidden: the next popup is activated before this one is
    /// deactivated, so the coroutine isn't killed partway through.
    /// </summary>
    public class DelayedHandoff : MonoBehaviour
    {
        [Tooltip("Seconds to wait before the handoff. Long enough to read the completion " +
                 "message - 2 to 3 seconds for a short line.")]
        [SerializeField] private float delay = 2.5f;

        [Tooltip("Popup to hide. Leave empty to hide the GameObject this component is on.")]
        [SerializeField] private GameObject toHide;

        [Tooltip("Popup to show next. Leave empty to hide only.")]
        [SerializeField] private GameObject toShow;

        [Tooltip("Ignores Time.timeScale, so a paused game won't leave the handoff hanging.")]
        [SerializeField] private bool useUnscaledTime = true;

        [Tooltip("Fires after the delay, alongside the show/hide.")]
        public UnityEvent onHandoff;

        private bool _running;

        /// <summary>Starts the delayed handoff. Ignores repeat calls while already running.</summary>
        public void Begin()
        {
            if (_running) return;
            _running = true;
            StartCoroutine(Run());
        }

        /// <summary>Cancels a pending handoff, e.g. if the player leaves the zone.</summary>
        public void Cancel()
        {
            StopAllCoroutines();
            _running = false;
        }

        private IEnumerator Run()
        {
            if (useUnscaledTime) yield return new WaitForSecondsRealtime(delay);
            else                 yield return new WaitForSeconds(delay);

            // Show first, then hide - if 'toHide' is this GameObject, deactivating it stops
            // this coroutine, so anything after that point would never run.
            if (toShow != null) toShow.SetActive(true);

            onHandoff?.Invoke();

            GameObject target = toHide != null ? toHide : gameObject;
            target.SetActive(false);

            _running = false;
        }
    }
}
