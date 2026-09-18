using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Footstep sounds driven by how far the player has actually travelled, with the clip chosen
/// from the surface underneath them.
///
/// Distance, not a timer. In VR the player's speed varies constantly - thumbstick pressure,
/// brief stops to look around - and a timer produces steps that keep firing while standing
/// still or that fall out of sync the moment speed changes. Accumulating horizontal distance
/// and emitting a step every stride length stays correct at any speed and goes silent the
/// instant movement stops, with no special case.
///
/// Vertical motion is excluded from the accumulator so that riding a slope or the small
/// vertical drift of a tracked headset never manufactures a footstep.
///
/// Put this on the XRPlayerRig root. It needs the CharacterController that is already there.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class FootstepAudio : MonoBehaviour
{
    [Serializable]
    public class SurfaceClips
    {
        public SurfaceKind kind = SurfaceKind.Paving;

        [Tooltip("Two or more variations. One clip repeated is very quickly recognised as a " +
                 "loop, which is more distracting than no footsteps at all.")]
        public AudioClip[] clips;

        [Range(0f, 1f)] public float volume = 0.7f;

        [Tooltip("Keywords matched against the material name when no SurfaceMarker is present. " +
                 "Case-insensitive, matched as substrings. e.g. 'grass', 'lawn'.")]
        public string[] materialKeywords;
    }

    [Header("Movement")]
    [Tooltip("Metres of horizontal travel between footsteps. Around 0.7 m suits an older " +
             "cohort walking unhurriedly; shorten it and the walk starts to sound like a jog.")]
    [SerializeField] private float strideLength = 0.7f;

    [Tooltip("Below this speed no steps play at all. Stops tracked-headset jitter from " +
             "producing a slow drip of footsteps while the participant stands and reads.")]
    [SerializeField] private float minSpeed = 0.15f;

    [Tooltip("Speed ceiling, in m/s. A frame that moves the rig faster than this was not " +
             "walking - it was a teleport to a spawn point, a recentre, or a frame hitch - so " +
             "its distance is discarded rather than banked. Without this the metres jumped on " +
             "a scene reload sit in the accumulator until the participant next moves, then " +
             "drain as a burst of steps on one frame each. 3 m/s is a fast walk and is well " +
             "clear of anything XRI produces here.")]
    [SerializeField] private float maxSpeed = 3f;

    [Tooltip("Shortest gap, in seconds, between two footsteps. A hard floor under the stride " +
             "rhythm: even if something else banks distance, steps come out as steps rather " +
             "than as a rattle. 0.2 s is about twice the fastest plausible stride.")]
    [SerializeField] private float minStepInterval = 0.2f;

    [Tooltip("Only play steps while the CharacterController reports it is grounded. Off by " +
             "default, and deliberately so: CharacterController.isGrounded is only refreshed " +
             "by a Move() that actually pushes down into the ground, and XRI's body " +
             "transformer moves the rig horizontally. On flat ground isGrounded is therefore " +
             "false most frames, which silently suppresses every footstep. Turn this on only " +
             "if a GravityProvider is present and the rig genuinely leaves the ground.")]
    [SerializeField] private bool requireGrounded = false;

    [Header("Surfaces")]
    [SerializeField] private SurfaceClips[] surfaces = new SurfaceClips[0];

    [Tooltip("Used when the raycast hits nothing, or hits something with no marker and no " +
             "matching material name.")]
    [SerializeField] private SurfaceKind fallbackSurface = SurfaceKind.Paving;

    [Header("Ground probe")]
    [Tooltip("How far down to look for ground, from just above the rig's feet.")]
    [SerializeField] private float probeDistance = 1.6f;

    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Audio")]
    [Tooltip("Route to the Movement bus so the movement volume slider controls it.")]
    [SerializeField] private AudioMixerGroup output;

    [Tooltip("Random pitch spread. A little variation stops repeated clips reading as a loop.")]
    [Range(0f, 0.3f)]
    [SerializeField] private float pitchJitter = 0.08f;

    private AudioSource _source;
    private CharacterController _controller;
    private Vector3 _lastPosition;
    private float _accumulated;
    private float _lastStepTime = float.NegativeInfinity;
    private int _lastClipIndex = -1;
    private readonly System.Collections.Generic.HashSet<SurfaceKind> _warnedSurfaces =
        new System.Collections.Generic.HashSet<SurfaceKind>();

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _controller = GetComponent<CharacterController>();

        _source.playOnAwake = false;
        _source.loop = false;
        if (output != null) _source.outputAudioMixerGroup = output;

        _lastPosition = transform.position;

        if (surfaces == null || surfaces.Length == 0)
            Debug.LogWarning("[FootstepAudio] No surfaces configured - no footsteps will play.", this);
    }

    private void OnEnable()
    {
        // Reset on enable so a scene transition does not bank a large phantom distance and
        // fire a burst of steps on the first frame back.
        ResetStride();
    }

    /// <summary>
    /// Forget where the rig was and how far it had travelled since the last step. Call this
    /// immediately after moving the rig by anything other than walking - a spawn-point
    /// placement, a teleport, a recentre.
    ///
    /// The rig persists across scene loads and this component is never disabled with it, so
    /// OnEnable does not fire on a repeat run. Without an explicit reset the jump from wherever
    /// the participant had walked to back to the spawn pad is measured as travel, banked, and
    /// then spent one step per frame the moment they next move.
    /// </summary>
    public void ResetStride()
    {
        _lastPosition = transform.position;
        _accumulated = 0f;
        _lastStepTime = Time.time;
    }

    private void Update()
    {
        Vector3 now = transform.position;

        Vector3 delta = now - _lastPosition;
        delta.y = 0f;
        _lastPosition = now;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float distance = delta.magnitude;
        float speed = distance / dt;
        if (speed < minSpeed) return;

        // Anything above a fast walk is not a stride. Discard the frame outright instead of
        // banking it: a teleport or a hitch otherwise contributes metres that have to come back
        // out as footsteps later. The position is already re-synced above, so the next frame
        // measures from where the rig actually is.
        if (maxSpeed > 0f && speed > maxSpeed)
        {
            _accumulated = 0f;
            return;
        }

        // Only count travel while actually on the ground, where there is a surface to hear.
        // Opt-in: see requireGrounded for why this is not the default.
        if (requireGrounded && _controller != null && !_controller.isGrounded) return;

        _accumulated += distance;
        if (_accumulated < strideLength) return;

        // Never let more than one step be owed. Subtracting a single stride keeps the rhythm
        // honest on a fast frame, but leaves any surplus banked - and a large surplus is what
        // turns into a burst. Clamp what is carried forward to less than one stride.
        _accumulated = Mathf.Min(_accumulated - strideLength, strideLength * 0.999f);

        // Last line of defence on cadence, whatever fed the accumulator.
        if (Time.time - _lastStepTime < minStepInterval) return;

        _lastStepTime = Time.time;
        PlayStep();
    }

    private void PlayStep()
    {
        SurfaceKind kind = ProbeSurface();
        SurfaceClips set = Resolve(kind);

        if (set == null || set.clips == null || set.clips.Length == 0)
        {
            // Warn once per surface. A footstep that resolves to an empty clip set is the
            // single most common reason this looks broken, and returning quietly hides it.
            if (_warnedSurfaces.Add(kind))
                Debug.LogWarning($"[FootstepAudio] Surface '{kind}' has no clips assigned " +
                                 "(and neither does the fallback) - no footstep played.", this);
            return;
        }

        _source.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
        _source.PlayOneShot(PickClip(set.clips), set.volume);
    }

    /// <summary>Avoids playing the same variation twice running, which is what the ear notices.</summary>
    private AudioClip PickClip(AudioClip[] clips)
    {
        if (clips.Length == 1) return clips[0];

        int index = UnityEngine.Random.Range(0, clips.Length);
        if (index == _lastClipIndex) index = (index + 1) % clips.Length;
        _lastClipIndex = index;
        return clips[index];
    }

    private SurfaceKind ProbeSurface()
    {
        Vector3 origin = transform.position + Vector3.up * 0.3f;

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeDistance,
                             groundLayers, QueryTriggerInteraction.Ignore))
            return fallbackSurface;

        // Explicit label wins whenever it is there.
        var marker = hit.collider.GetComponentInParent<SurfaceMarker>();
        if (marker != null) return marker.Kind;

        // Otherwise fall back to the material name. The generated environment names its
        // materials M_Grass, M_Stone, M_Asphalt and so on, so this works today without the
        // scene needing to be rebuilt.
        var renderer = hit.collider.GetComponentInParent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            string materialName = renderer.sharedMaterial.name.ToLowerInvariant();

            for (int i = 0; i < surfaces.Length; i++)
            {
                var s = surfaces[i];
                if (s == null || s.materialKeywords == null) continue;

                for (int k = 0; k < s.materialKeywords.Length; k++)
                {
                    string keyword = s.materialKeywords[k];
                    if (!string.IsNullOrEmpty(keyword) &&
                        materialName.Contains(keyword.ToLowerInvariant()))
                        return s.kind;
                }
            }
        }

        return fallbackSurface;
    }

    private SurfaceClips Resolve(SurfaceKind kind)
    {
        for (int i = 0; i < surfaces.Length; i++)
            if (surfaces[i] != null && surfaces[i].kind == kind) return surfaces[i];

        // Asked for a surface with no clips assigned - better to play the fallback than nothing.
        for (int i = 0; i < surfaces.Length; i++)
            if (surfaces[i] != null && surfaces[i].kind == fallbackSurface) return surfaces[i];

        return null;
    }
}
