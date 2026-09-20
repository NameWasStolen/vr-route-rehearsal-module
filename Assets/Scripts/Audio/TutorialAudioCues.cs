using UnityEngine;
using UnityEngine.Events;

namespace VRTutorial
{
    /// <summary>
    /// Connects every tutorial lesson's events to the Bootstrap UiCuePlayer, in code.
    ///
    /// Why this rather than wiring each UnityEvent to a CueRelay by hand: there are around a
    /// dozen bindings across four lessons and the flow, every new lesson would need its own set,
    /// and a missed binding fails silently - the lesson just goes quiet. Subscribing here means a
    /// lesson that is added later gets its sounds for free as long as it is one of the task types
    /// below, and the Inspector bindings that already exist on those events are left untouched.
    ///
    /// The flip side, and the thing to remember: a task is hooked BY TYPE. Replacing a lesson's
    /// component with a new one - as TurnTask replaced SnapTurnTask - silently takes its sounds
    /// away until the new type is added to the list below. Nothing warns; the lesson simply goes
    /// quiet, which is precisely the failure this class was written to prevent for hand-wiring.
    ///
    /// What plays when:
    ///   - Action accepted (soft tick): a correct turn, the grip squeezed (and squeezed again
    ///     after letting go), the menu opened, the help button held, a practice request placed.
    ///   - Step complete (warm two-note chime): any lesson's onCompleted.
    ///   - Gentle retry (neutral): wrong-direction turn, grip let go mid-hold, help button let go
    ///     before the bar filled.
    ///   - Step advance (quiet): a new instruction appears.
    ///   - Flow complete: the end-zone panel appears.
    ///
    /// Put one in Tutorial.unity (on the tutorial panel object is fine). Everything is found in
    /// this component's own scene, including inactive steps. With Bootstrap not loaded, every
    /// cue is a silent no-op.
    /// </summary>
    // Runs before TutorialFlow, whose OnEnable shows the first step immediately - otherwise that
    // first onStepChanged could fire before anything is listening.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class TutorialAudioCues : MonoBehaviour
    {
        [Tooltip("Left empty, the TutorialFlow in this scene is found automatically.")]
        [SerializeField] private TutorialFlow flow;

        [Header("Which cues to use")]
        [SerializeField] private bool actionCues = true;
        [SerializeField] private bool completionCues = true;
        [SerializeField] private bool retryCues = true;
        [SerializeField] private bool advanceCues = true;

        [Tooltip("Play the advance cue for the very first instruction when the scene starts. " +
                 "Untick if it lands under a scene fade and is not heard anyway.")]
        [SerializeField] private bool cueInitialStep = true;

        [Tooltip("Log each cue as it fires. For checking wiring in the editor.")]
        [SerializeField] private bool logCues = false;

        private readonly System.Collections.Generic.List<System.Action> _unsubscribe =
            new System.Collections.Generic.List<System.Action>();

        private bool _sawFirstStep;

        private void Awake()
        {
            if (flow == null) flow = FindInScene<TutorialFlow>();

            if (flow != null)
            {
                UnityAction<int> onStep = OnStepChanged;
                flow.onStepChanged.AddListener(onStep);
                _unsubscribe.Add(() => flow.onStepChanged.RemoveListener(onStep));

                Hook(flow.onFlowCompleted, Flow);

                // Belt and braces, in case the flow enabled first anyway.
                if (flow.CurrentIndex >= 0) _sawFirstStep = true;
            }

            // The turning lesson. TurnTask is the live one - it teaches whichever of the three
            // turning modes the participant has selected. SnapTurnTask is its predecessor and is
            // kept here only so the rollback path stays audible; the two are never both on a
            // step, so this cannot double a cue.
            foreach (var t in FindAllInScene<TurnTask>())
            {
                Hook(t.onTurnRegistered, Action);
                Hook(t.onWrongDirection, Retry);
                Hook(t.onCompleted, Complete);
            }

            foreach (var t in FindAllInScene<SnapTurnTask>())
            {
                Hook(t.onTurnRegistered, Action);
                Hook(t.onWrongDirection, Retry);
                Hook(t.onCompleted, Complete);
            }

            foreach (var t in FindAllInScene<MovementTask>())
            {
                Hook(t.onHoldStarted, Action);
                Hook(t.onHoldResumed, Action);
                Hook(t.onHoldBroken, Retry);
                Hook(t.onCompleted, Complete);
            }

            foreach (var t in FindAllInScene<PauseTask>())
            {
                Hook(t.onMenuOpened, Action);
                Hook(t.onCompleted, Complete);
            }

            foreach (var t in FindAllInScene<AssistanceTask>())
            {
                Hook(t.onHoldStarted, Action);
                Hook(t.onHoldCancelled, Retry);
                Hook(t.onRequested, Action);
                Hook(t.onCompleted, Complete);
            }
        }

        private void OnDestroy()
        {
            foreach (var undo in _unsubscribe) undo();
            _unsubscribe.Clear();
        }

        // ------------------------------------------------------------------ cues

        private void Action()
        {
            if (!actionCues) return;
            Log("action accepted");
            UiCuePlayer.Instance?.PlayActionAccepted();
        }

        private void Complete()
        {
            if (!completionCues) return;
            Log("step complete");
            UiCuePlayer.Instance?.PlayStepComplete();
        }

        private void Retry()
        {
            if (!retryCues) return;
            Log("gentle retry");
            UiCuePlayer.Instance?.PlayGentleRetry();
        }

        private void Flow()
        {
            if (!completionCues) return;
            Log("flow complete");
            UiCuePlayer.Instance?.PlayFlowComplete();
        }

        private void OnStepChanged(int index)
        {
            bool first = !_sawFirstStep;
            _sawFirstStep = true;

            if (!advanceCues) return;
            if (first && !cueInitialStep) return;

            // The end-zone panel gets the flow-complete cue a moment later, once it has faded
            // in. Playing the advance cue as well would give two sounds for one event.
            if (flow != null && index == flow.StepCount - 1) return;

            Log("step advance");
            UiCuePlayer.Instance?.PlayStepAdvance();
        }

        // ------------------------------------------------------------------ plumbing

        private void Hook(UnityEvent evt, UnityAction call)
        {
            if (evt == null) return;
            evt.AddListener(call);
            _unsubscribe.Add(() => evt.RemoveListener(call));
        }

        private void Log(string what)
        {
            if (!logCues) return;
            if (UiCuePlayer.Instance == null)
                Debug.Log($"[TutorialAudioCues] {what} (no UiCuePlayer - is Bootstrap loaded?)", this);
            else
                Debug.Log($"[TutorialAudioCues] {what}", this);
        }

        private T FindInScene<T>() where T : Component
        {
            foreach (var c in FindAllInScene<T>()) return c;
            return null;
        }

        /// <summary>Only this scene's objects, so a second loaded scene cannot double the cues.</summary>
        private System.Collections.Generic.IEnumerable<T> FindAllInScene<T>() where T : Component
        {
            T[] all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].gameObject.scene == gameObject.scene)
                    yield return all[i];
        }
    }
}
