using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// The suburban background sound for the full-route runs and the tutorial: birds and the quiet
/// hum of a suburb in the morning. Plays for as long as its scene is loaded (RunSystem in every
/// module; Tutorial on EnvironmentAudio, where it replaced the wind-and-leaves loop), and
/// carries on under the pause menu, the help panels and the post-run survey.
///
/// SETUP: a RunAmbience on the RunSystem object, with the clip in its Clip slot. In the Editor an
/// empty slot fills itself with Assets/Audio/Ambience/Ambience_Suburb_Morning.mp3 - save the
/// scene afterwards so the build has it too. If RunSystem has no RunAmbience at all,
/// RunSystemController adds one, which can then only find the clip in
/// Resources/Ambience/Ambience_Suburb_Morning.
///
/// SEAMLESS LOOP: the recording is a field recording, not a made loop, so its end does not match
/// its start. Two sources take turns: a few seconds before one reaches the end, the other starts
/// from the beginning and they cross-fade, so there is never an audible jump.
///
/// SAME IN EVERY RUN: it always starts from the beginning of the recording at the same volume,
/// so every module (and every participant) hears the same background. That keeps the calm and
/// stress answers comparable between modules.
///
/// Leaving the run needs nothing here: SceneTransitionController fades the listener with the
/// picture, and the sound stops when the scene unloads.
/// </summary>
[DisallowMultipleComponent]
public class RunAmbience : MonoBehaviour
{
    public const string ResourcePath = "Ambience/Ambience_Suburb_Morning";
    public const string DefaultClipAssetPath = "Assets/Audio/Ambience/Ambience_Suburb_Morning.mp3";

    [Tooltip("The recording. Fills itself in the Editor with Assets/Audio/Ambience/Ambience_Suburb_Morning.mp3 " +
             "if left empty.")]
    [SerializeField] private AudioClip clip;

    [Tooltip("Kept low: it is background. Tune by ear against the footsteps in the headset.")]
    [Range(0f, 1f)] [SerializeField] private float volume = 0.25f;

    [Tooltip("Optional. Route to an Ambience mixer group when there is one, so the ambience volume " +
             "slider controls it. Empty plays straight to the listener, like the tutorial's " +
             "EnvironmentAudio.")]
    [SerializeField] private AudioMixerGroup output;

    [Tooltip("Seconds the end of the recording overlaps the start of the next time round.")]
    [SerializeField, Min(0.5f)] private float crossfadeSeconds = 4f;

    [Tooltip("Seconds to fade in when the run loads. The scene fade already covers most of it.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 1.5f;

    private readonly AudioSource[] _sources = new AudioSource[2];
    private int _current;               // index of the source that started most recently
    private double _currentStart;       // dspTime it started at
    private double _nextStart;          // dspTime the other source is scheduled to start
    private double _firstStart;
    private double _length;
    private bool _crossfading;
    private bool _playing;

    private void Start()
    {
        if (clip == null)
            clip = Resources.Load<AudioClip>(ResourcePath);
        if (clip == null)
        {
            Debug.LogWarning("[RunAmbience] No ambience clip, so the run is silent apart from footsteps. Drag " +
                             $"{DefaultClipAssetPath} into the Clip slot of Run Ambience on the RunSystem object " +
                             "and save the scene.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject($"Ambience_{(char)('A' + i)}");
            go.transform.SetParent(transform, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;        // all around the listener, not from a point
            s.priority = 64;            // above footsteps, so it is never the one dropped
            s.dopplerLevel = 0f;
            s.volume = 0f;
            if (output != null) s.outputAudioMixerGroup = output;
            _sources[i] = s;
        }

        _length = (double)clip.samples / clip.frequency;
        double fade = CrossfadeLength();

        _current = 0;
        _firstStart = _currentStart = AudioSettings.dspTime + 0.1;
        _sources[0].PlayScheduled(_currentStart);
        _nextStart = fade > 0 ? _currentStart + _length - fade : double.MaxValue;
        if (fade <= 0) _sources[0].loop = true;     // too short to cross-fade: plain loop
        _playing = true;
    }

    /// <summary>The overlap, or 0 if the clip is too short to overlap itself.</summary>
    private double CrossfadeLength() => _length > crossfadeSeconds * 2.5 ? crossfadeSeconds : 0;

    private void Update()
    {
        if (!_playing) return;
        double now = AudioSettings.dspTime;
        double fade = CrossfadeLength();

        // Schedule the next pass a little ahead of time, so it starts sample-accurately.
        int other = 1 - _current;
        if (!_crossfading && fade > 0 && now >= _nextStart - 1.0)
        {
            _sources[other].Stop();
            _sources[other].volume = 0f;
            _sources[other].PlayScheduled(_nextStart);
            _crossfading = true;
        }

        float master = volume * FadeIn(now);

        if (_crossfading && now >= _nextStart)
        {
            // Equal-power cross-fade: the overall level stays steady through the overlap.
            float k = Mathf.Clamp01((float)((now - _nextStart) / fade));
            _sources[_current].volume = master * Mathf.Cos(k * Mathf.PI * 0.5f);
            _sources[other].volume = master * Mathf.Sin(k * Mathf.PI * 0.5f);

            if (k >= 1f)
            {
                _sources[_current].Stop();
                _current = other;
                _currentStart = _nextStart;
                _nextStart = _currentStart + _length - fade;
                _crossfading = false;
            }
        }
        else
        {
            _sources[_current].volume = master;
        }
    }

    private float FadeIn(double now)
    {
        if (fadeInSeconds <= 0f) return 1f;
        return Mathf.Clamp01((float)((now - _firstStart) / fadeInSeconds));
    }

#if UNITY_EDITOR
    // Fill an empty Clip slot with the default recording, so it cannot be left empty by accident.
    // Marks the scene changed, so the next save stores it for the build.
    private void Reset() => FillDefaultClip();

    private void OnValidate()
    {
        if (clip == null) FillDefaultClip();
    }

    private void FillDefaultClip()
    {
        AudioClip found = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultClipAssetPath);
        if (found == null) return;
        clip = found;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void OnDisable()
    {
        _playing = false;
        foreach (AudioSource s in _sources)
            if (s != null) s.Stop();
    }

    private void OnEnable()
    {
        // Re-enabled after OnDisable (not on the first enable, which Start handles).
        if (_sources[0] == null || _playing) return;
        _crossfading = false;
        _current = 0;
        _sources[1].Stop();
        _firstStart = _currentStart = AudioSettings.dspTime + 0.1;
        _sources[0].volume = 0f;
        _sources[0].PlayScheduled(_currentStart);
        double fade = CrossfadeLength();
        _nextStart = fade > 0 ? _currentStart + _length - fade : double.MaxValue;
        _playing = true;
    }
}
