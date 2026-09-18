using UnityEngine;

/// <summary>
/// In-scene forwarder to the UiCuePlayer that lives on Bootstrap.
///
/// This exists for exactly the same reason the font-size list did not work: a UnityEvent in an
/// additively-loaded scene CANNOT hold a reference to an object in a different scene. Unity
/// will not serialise it, so dragging the Bootstrap UiCuePlayer into TutorialFlow.onStepChanged
/// either refuses outright or silently resolves to None at runtime.
///
/// So each content scene gets one of these instead. The UnityEvents point at this - a local
/// object, which serialises fine - and it forwards to the singleton through a static lookup at
/// runtime, which is the only kind of cross-scene reference that works.
///
/// Put one on any scene that fires progress cues, and point the scene's UnityEvents at it.
/// If Bootstrap is not loaded - opening a content scene standalone to test it - every call is
/// a silent no-op rather than a null reference.
/// </summary>
[DisallowMultipleComponent]
public class CueRelay : MonoBehaviour
{
    [Tooltip("Log when a cue is requested but no UiCuePlayer exists. Useful while testing a " +
             "scene standalone; noise in a real run, so it defaults off.")]
    [SerializeField] private bool warnWhenUnavailable = false;

    public void PlayActionAccepted()
    {
        if (Available()) UiCuePlayer.Instance.PlayActionAccepted();
    }

    public void PlayStepComplete()
    {
        if (Available()) UiCuePlayer.Instance.PlayStepComplete();
    }

    public void PlayStepAdvance()
    {
        if (Available()) UiCuePlayer.Instance.PlayStepAdvance();
    }

    /// <summary>
    /// Matches UnityEvent&lt;int&gt; so it can bind straight to TutorialFlow.onStepChanged.
    /// The index is ignored; it is here only so the Inspector accepts the binding.
    /// </summary>
    public void PlayStepAdvance(int _) => PlayStepAdvance();

    public void PlayGentleRetry()
    {
        if (Available()) UiCuePlayer.Instance.PlayGentleRetry();
    }

    public void PlayFlowComplete()
    {
        if (Available()) UiCuePlayer.Instance.PlayFlowComplete();
    }

    private bool Available()
    {
        if (UiCuePlayer.Instance != null) return true;

        if (warnWhenUnavailable)
            Debug.LogWarning("[CueRelay] No UiCuePlayer in the scene set. Is Bootstrap loaded?", this);

        return false;
    }
}
