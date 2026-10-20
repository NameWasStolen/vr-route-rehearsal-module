using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Short confirmation sounds for tutorial progress, on the UI bus.
///
/// This exists because of the specific failure mode with an older cohort: the risk is not that
/// a transition is harsh, it is that the participant does not register that anything happened
/// at all. A visual cross-fade that completes while someone is looking at their controller is
/// a cue they simply miss. Audio is not directional and does not need to be looked at, which
/// is exactly what is wanted here.
///
/// Lives on Bootstrap so it survives additive scene loads, and is reached through the static
/// Instance from wherever progress actually happens. Each cue is also exposed as a no-argument
/// method so it can be dropped straight onto a UnityEvent in the Inspector -
/// TutorialFlow.onStepChanged, TutorialStep.onStepEnter, TutorialZoneTrigger.onPlayerEntered -
/// without writing any glue.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class UiCuePlayer : MonoBehaviour
{
    public static UiCuePlayer Instance { get; private set; }

    [Header("Clips")]
    [Tooltip("One correct action inside a lesson - the first turn in the right direction, the " +
             "grip squeezed, the menu opened. The quietest and shortest cue: it says 'yes, that " +
             "one', and it will be heard many times in a session.")]
    [SerializeField] private AudioClip actionAccepted;

    [Tooltip("A step was completed correctly. Keep it short, warm and low - a bright ping " +
             "reads as an alert, and an alert is the wrong message for 'you did that right'.")]
    [SerializeField] private AudioClip stepComplete;

    [Tooltip("A new instruction has appeared. Quieter than the completion cue; this one is " +
             "asking for attention, not rewarding anything.")]
    [SerializeField] private AudioClip stepAdvance;

    [Tooltip("Neutral 'not quite - try again'. Played for the wrong-direction turn, letting go of " +
             "the grip mid-hold, and letting go of the help button before the bar fills. Must not " +
             "sound like a buzzer: same pitch repeated, never a falling interval.")]
    [SerializeField] private AudioClip gentleRetry;

    [Tooltip("The whole tutorial is finished.")]
    [SerializeField] private AudioClip flowComplete;

    [Header("Output")]
    [Tooltip("Route to the UI bus so the UI volume slider controls these.")]
    [SerializeField] private AudioMixerGroup output;

    [Range(0f, 1f)] [SerializeField] private float volume = 0.9f;

    [Tooltip("Minimum gap between any two cues. Without this, a step that both completes and " +
             "advances in the same frame fires two clips on top of each other, which sounds " +
             "like a fault rather than a confirmation.")]
    [SerializeField] private float minGap = 0.12f;

    [Tooltip("Minimum gap between two neutral cues. A participant who keeps turning the wrong " +
             "way should hear it once, not be nagged on every flick.")]
    [SerializeField] private float retryCooldown = 1.5f;

    /// <summary>
    /// Higher wins when two cues land inside minGap. The case that matters: the final correct
    /// turn raises both 'action accepted' and 'step complete' in the same frame, and it is the
    /// completion the participant needs to hear, not the tick.
    /// </summary>
    public enum CuePriority { Action = 0, Retry = 1, Advance = 1, Complete = 2, Flow = 3 }

    private AudioSource _source;
    private float _lastPlayTime = -99f;
    private float _lastRetryTime = -99f;
    private CuePriority _lastPriority = CuePriority.Action;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;

        // 2D. A confirmation that appears to come from a point in the room invites the
        // participant to turn and look for it, which is the opposite of what it is for.
        _source.spatialBlend = 0f;

        if (output != null) _source.outputAudioMixerGroup = output;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Inspector-friendly entry points for UnityEvents.
    public void PlayActionAccepted() => Play(actionAccepted, CuePriority.Action);
    public void PlayStepComplete() => Play(stepComplete, CuePriority.Complete);
    public void PlayStepAdvance() => Play(stepAdvance, CuePriority.Advance);
    public void PlayFlowComplete() => Play(flowComplete, CuePriority.Flow);

    public void PlayGentleRetry()
    {
        if (gentleRetry == null) return;
        if (Time.unscaledTime - _lastRetryTime < retryCooldown) return;
        if (Play(gentleRetry, CuePriority.Retry)) _lastRetryTime = Time.unscaledTime;
    }

    /// <summary>
    /// Matches the signature of TutorialFlow.onStepChanged (UnityEvent&lt;int&gt;) so it can be
    /// wired directly to it. The index is not used; it is here purely so the Inspector will
    /// accept the binding.
    /// </summary>
    public void PlayStepAdvance(int _) => PlayStepAdvance();

    public void Play(AudioClip clip) => Play(clip, CuePriority.Action);

    /// <returns>True if the clip was actually played.</returns>
    public bool Play(AudioClip clip, CuePriority priority)
    {
        if (clip == null || _source == null) return false;

        if (Time.unscaledTime - _lastPlayTime < minGap)
        {
            if (priority <= _lastPriority) return false;

            // A more important cue arrived on top of a lesser one. Cut the lesser one rather
            // than layering them - two clips at once reads as a glitch.
            _source.Stop();
        }

        _lastPlayTime = Time.unscaledTime;
        _lastPriority = priority;
        _source.PlayOneShot(clip, volume);
        return true;
    }

    /// <summary>Safe to call from anywhere, including a scene with no Bootstrap loaded.</summary>
    public static void Cue(AudioClip clip)
    {
        if (Instance != null) Instance.Play(clip);
    }
}
