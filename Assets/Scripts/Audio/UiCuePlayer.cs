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
    [Tooltip("A step was completed correctly. Keep it short, warm and low - a bright ping " +
             "reads as an alert, and an alert is the wrong message for 'you did that right'.")]
    [SerializeField] private AudioClip stepComplete;

    [Tooltip("A new instruction has appeared. Quieter than the completion cue; this one is " +
             "asking for attention, not rewarding anything.")]
    [SerializeField] private AudioClip stepAdvance;

    [Tooltip("Optional. Played when the participant does the right action the wrong way - the " +
             "wrong-direction turn. Must not sound like a buzzer.")]
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

    private AudioSource _source;
    private float _lastPlayTime = -99f;

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
    public void PlayStepComplete() => Play(stepComplete);
    public void PlayStepAdvance() => Play(stepAdvance);
    public void PlayGentleRetry() => Play(gentleRetry);
    public void PlayFlowComplete() => Play(flowComplete);

    /// <summary>
    /// Matches the signature of TutorialFlow.onStepChanged (UnityEvent&lt;int&gt;) so it can be
    /// wired directly to it. The index is not used; it is here purely so the Inspector will
    /// accept the binding.
    /// </summary>
    public void PlayStepAdvance(int _) => PlayStepAdvance();

    public void Play(AudioClip clip)
    {
        if (clip == null || _source == null) return;
        if (Time.unscaledTime - _lastPlayTime < minGap) return;

        _lastPlayTime = Time.unscaledTime;
        _source.PlayOneShot(clip, volume);
    }

    /// <summary>Safe to call from anywhere, including a scene with no Bootstrap loaded.</summary>
    public static void Cue(AudioClip clip)
    {
        if (Instance != null) Instance.Play(clip);
    }
}
